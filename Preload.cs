using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

namespace InstantTransitions;

// Primera visita rápida. Los objetos de un lugar se dibujan con piezas que el juego saca de tres reservas: WgoPartPool
// (objetos: estaciones, muebles, lo que construyes), ConstructorPartPool (piezas de edificios) y
// BakedChunkableObjectPool (decorado fijo). Mientras carga la partida, el juego llena esas reservas con una lista
// fija de sus desarrolladores (MainGame.RegisterBackgroundPreloadTasks → *.InitAsync). Lo que no está en esa lista
// se carga del disco la primera vez que lo ves, todo en un cuadro: medido, hasta 1.3 s al entrar al patio.
//
// Aquí, con el mapa ya cargado y ANTES de que el juego haga su trabajo de después de cargar (MainGame.AfterSceneHasLoaded,
// así la pantalla de carga espera), se revisan TODAS las piezas del mapa donde estás (las mismas que el juego
// registra en GameScene.CollectScenePoolPaths) y se cargan las que falten con las funciones asíncronas del propio
// juego (WgoPartPool.LoadPrefab, *.CreatePoolByIdAsync), hasta 32 a la vez: cargadas una por una tardaban 15 s
// en una partida a medias (836 piezas), cada una esperando al disco. A cada reserva le queda una copia, como en la
// precarga del juego. El trabajo de cada cuadro se limita para que la pantalla de carga siga animada, un texto bajo
// la barra dice cuánto falta, y nunca pasa de PreloadMaxSeconds. Pase lo que pase, al final la carga sigue.
// Los componentes de editor que traen esas copias los quita la limpieza de carga, que va justo después.
//
// Por qué antes y no dentro de la limpieza (como hasta la 0.10.0): AfterSceneHasLoaded dispara el evento "después de
// dormir" (GlobalEventsSystem AfterSleep) ANTES de su limpieza, y con él arrancan las escenas de "al despertar" (Jack
// yéndose al barco, cartas, etc.). El juego cuenta con que la limpieza dura un instante: al terminar la carga,
// MainGame.OnGameStarted borra todos los globos de diálogo (Bubble.OnGameStarted → UISpeechBubble.ForceRemoveAll) sin
// avisar a nadie. Con la precarga metida ahí, la escena llevaba varios segundos corriendo detrás de la pantalla de
// carga, el globo que estuviera puesto se borraba y el guion se quedaba esperándolo para siempre: pantalla en negro
// al cargar, y Jack nunca llegaba al barco (reportes de Nexus, reproducido el 2026-10-02 con una partida guardada la
// noche antes de esa escena). Antes de AfterSceneHasLoaded todavía no ha empezado nada: es igual que un disco más lento.
internal static class Preload
{
    private const int MaxInFlight = 32;
    private const int FrameBudgetMs = 20;

    private static MethodInfo afterLoad;
    private static bool passThrough; // la llamada es la nuestra, ya con la precarga hecha: que corra el juego

    public static void Apply(Harmony harmony)
    {
        MethodInfo target = AccessTools.Method(typeof(MainGame), "AfterSceneHasLoaded", Type.EmptyTypes);
        if (target == null || target.IsStatic || target.ReturnType != typeof(UniTask))
        {
            Plugin.Log.LogWarning("MainGame.AfterSceneHasLoaded not found (game update?): the map preload is off.");
            return;
        }
        try
        {
            // Seguro de parchar: su cuerpo solo crea su máquina de estados (como HiddenOptimization), sin leer estáticos del juego.
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(Preload), nameof(BeforeAfterLoad)));
            afterLoad = target;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Could not hook the map preload: " + e.Message);
        }
    }

    // El mapa ya cargó y el juego va a empezar su trabajo de después: primero la precarga, y luego ese trabajo tal cual.
    // La tarea que se devuelve SIEMPRE termina (con lo que diga el juego): si no, la pantalla de carga no se quitaría.
    private static bool BeforeAfterLoad(MainGame __instance, ref UniTask __result)
    {
        if (passThrough || afterLoad == null || !Plugin.Enabled.Value || !Plugin.PreloadPlaces.Value)
            return true;
        try
        {
            var done = new UniTaskCompletionSource();
            MainGame game = __instance;
            DoorWatch.LoadPreloadStartAt = Stopwatch.GetTimestamp();
            LoadingLabel.Show(0, 1);
            Start(() => Continue(game, done));
            __result = done.Task;
            return false;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[Preload] off this time: " + e.Message);
            try { LoadingLabel.Hide(); }
            catch (Exception ex) { Plugin.Log.LogDebug("Loading label: " + ex.Message); }
            return true;
        }
    }

    private static void Continue(MainGame game, UniTaskCompletionSource done)
    {
        UniTask rest;
        try
        {
            passThrough = true;
            try
            {
                rest = (UniTask)afterLoad.Invoke(game, null);
            }
            finally
            {
                passThrough = false;
            }
        }
        catch (Exception e)
        {
            done.TrySetException(e.InnerException ?? e); // lo mismo que habría visto el juego
            return;
        }
        Forward(rest, done).Forget();
    }

    private static async UniTaskVoid Forward(UniTask rest, UniTaskCompletionSource done)
    {
        try
        {
            await rest;
            done.TrySetResult();
        }
        catch (Exception e)
        {
            done.TrySetException(e);
        }
    }

    private static readonly FieldInfo WgoPoolInstance = AccessTools.Field(typeof(LazySingleton<WgoPartPool>), "instance");
    private static readonly FieldInfo CtorPoolInstance = AccessTools.Field(typeof(LazySingleton<ConstructorPartPool>), "instance");
    private static readonly FieldInfo BakedPoolInstance = AccessTools.Field(typeof(LazySingleton<BakedChunkableObjectPool>), "instance");
    private static readonly FieldInfo WgoPools = AccessTools.Field(typeof(WgoPartPool), "pools");
    private static readonly MethodInfo WgoLoadPrefab = Async(typeof(WgoPartPool), "LoadPrefab", typeof(UniTask<GameObject>));
    private static readonly MethodInfo WgoLoadPrefabSync = AccessTools.Method(typeof(WgoPartPool), "LoadPrefabSync", new[] { typeof(string) });
    private static readonly FieldInfo CtorPools = AccessTools.Field(typeof(ConstructorPartPool), "poolsDict");
    private static readonly MethodInfo CtorCreateAsync = Async(typeof(ConstructorPartPool), "CreatePoolByIdAsync", typeof(UniTask<Pool>));
    private static readonly MethodInfo CtorCreate = AccessTools.Method(typeof(ConstructorPartPool), "CreatePoolById", new[] { typeof(string) });
    private static readonly FieldInfo BakedPools = AccessTools.Field(typeof(BakedChunkableObjectPool), "poolsDict");
    private static readonly MethodInfo BakedCreateAsync = Async(typeof(BakedChunkableObjectPool), "CreatePoolByIdAsync", typeof(UniTask<Pool>));
    private static readonly MethodInfo BakedCreate = AccessTools.Method(typeof(BakedChunkableObjectPool), "CreatePoolById", new[] { typeof(string) });
    private static readonly FieldInfo SceneData = AccessTools.Field(typeof(GameScene), "gameSceneData");
    private static readonly FieldInfo SceneConstructorParts = AccessTools.Field(typeof(GameScene), "constructorParts");

    // La versión asíncrona del juego, solo si devuelve lo esperado (si una actualización la cambia, se usa la síncrona).
    private static MethodInfo Async(Type type, string name, Type returns)
    {
        MethodInfo m = AccessTools.Method(type, name, new[] { typeof(string) });
        return m != null && m.ReturnType == returns ? m : null;
    }

    private enum Kind { Object, ConstructorPart, Baked }

    private sealed class Job
    {
        public Kind kind;
        public string path;
        public object task; // UniTask<GameObject> o UniTask<Pool> en caja (null = síncrona)
    }

    private sealed class Counts
    {
        public int total, missing, loaded, failed;
        public override string ToString() => $"{loaded}/{missing} missing of {total}" + (failed > 0 ? $" ({failed} failed)" : "");
    }

    private static Action pending;      // quien espera a que termine (la carga de la partida)
    private static float giveUpAt;
    internal static double LastSeconds; // para la línea de la pantalla de carga

    // Empieza la precarga; `finished` se llama una sola vez, al terminar o si algo falla (y a más tardar, al tope).
    internal static void Start(Action finished)
    {
        bool called = false;
        Action once = null;
        once = () =>
        {
            if (called)
                return;
            called = true;
            if (ReferenceEquals(pending, once))
                pending = null; // solo si sigue siendo la suya: otra precarga pudo empezar después
            try
            {
                LoadingLabel.Hide();
            }
            finally
            {
                finished(); // la carga sigue aunque el texto no se pudiera quitar
            }
        };
        try
        {
            MonoBehaviour host = DoorWatch.Host;
            if (host == null)
            {
                once();
                return;
            }
            if (SystemInfo.systemMemorySize > 0 && SystemInfo.systemMemorySize < 7000)
            {
                Plugin.Log.LogInfo($"[Preload] skipped: {SystemInfo.systemMemorySize} MB of RAM is not enough to keep the whole map loaded.");
                once();
                return;
            }
            pending = once;
            giveUpAt = Time.realtimeSinceStartup + Mathf.Max(1f, Plugin.PreloadMaxSeconds.Value) + 10f;
            host.StartCoroutine(Run(once));
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[Preload] could not start: " + e.Message);
            once();
        }
    }

    // Cada cuadro (DoorWatch): si la corrutina se hubiera detenido sin avisar, la carga no se queda esperando.
    internal static void Watch()
    {
        if (pending != null && Time.realtimeSinceStartup > giveUpAt)
            GiveUp("did not finish in time");
    }

    // La carga sigue sin esperar a la precarga; la corrutina, si todavía corre, lo nota y se detiene sin tocar la pantalla.
    // También si su componente se apaga o se destruye: Unity detiene la corrutina sin correr su finally.
    internal static void GiveUp(string why)
    {
        Action waiting = pending;
        if (waiting == null)
            return;
        Plugin.Log.LogWarning($"[Preload] {why}; the game goes on.");
        waiting();
    }

    private static IEnumerator Run(Action finished)
    {
        try
        {
            Stopwatch total = Stopwatch.StartNew(), frame = new Stopwatch();
            long memoryBefore = Cleanup.MemoryMB(), processBefore = DoorWatch.ProcessMB();
            var counts = new Dictionary<Kind, Counts> { [Kind.Object] = new Counts(), [Kind.ConstructorPart] = new Counts(), [Kind.Baked] = new Counts() };
            List<Job> jobs = Collect(counts, out string scene);
            var inFlight = new List<Job>();
            int next = 0, done = 0, frames = 0;
            long workMs = 0;
            bool stopped = false;
            while (next < jobs.Count || inFlight.Count > 0)
            {
                if (!ReferenceEquals(pending, finished))
                {
                    // Ya se dio por terminada (GiveUp) y el juego siguió: nada de congelar ni de volver a mostrar el texto.
                    Plugin.Log.LogInfo($"[Preload] map {scene ?? "?"}: given up after {total.Elapsed.TotalSeconds:0.00} s · objects {counts[Kind.Object]}" +
                                       $" · building parts {counts[Kind.ConstructorPart]} · scenery {counts[Kind.Baked]}");
                    yield break;
                }
                if (total.Elapsed.TotalSeconds > Plugin.PreloadMaxSeconds.Value)
                {
                    stopped = true; // lo que sigue en camino termina solo; su copia de reserva ya no se agrega
                    break;
                }
                frame.Restart();
                while (next < jobs.Count && inFlight.Count < MaxInFlight && frame.ElapsedMilliseconds < FrameBudgetMs)
                {
                    Job job = jobs[next++];
                    if (Begin(job))
                        inFlight.Add(job);
                    else
                    {
                        counts[job.kind].failed++;
                        done++;
                    }
                }
                for (int i = inFlight.Count - 1; i >= 0 && frame.ElapsedMilliseconds < FrameBudgetMs; i--)
                {
                    bool? ok = TryFinish(inFlight[i]);
                    if (ok == null)
                        continue; // todavía cargando
                    if (ok == true)
                        counts[inFlight[i].kind].loaded++;
                    else
                        counts[inFlight[i].kind].failed++;
                    inFlight.RemoveAt(i);
                    done++;
                }
                LoadingLabel.Show(done, jobs.Count);
                workMs += frame.ElapsedMilliseconds;
                frames++;
                yield return null; // que la pantalla de carga siga animada
            }
            // Los componentes de editor de las copias nuevas y la referencia de memoria: la limpieza de carga, que va enseguida.
            LastSeconds = total.Elapsed.TotalSeconds;
            DoorWatch.LoadPreloadDoneAt = Stopwatch.GetTimestamp();
            Plugin.Log.LogInfo($"[Preload] map {scene ?? "?"}: {LastSeconds:0.00} s ({frames} frames, main-thread work {workMs / 1000.0:0.00} s)" +
                               (stopped ? $" (stopped at the {Plugin.PreloadMaxSeconds.Value:0} s limit)" : "") +
                               $" · objects {counts[Kind.Object]} · building parts {counts[Kind.ConstructorPart]} · scenery {counts[Kind.Baked]}" +
                               $" · memory {memoryBefore} -> {Cleanup.MemoryMB()} MB · game process {processBefore} -> {DoorWatch.ProcessMB()} MB");
        }
        finally
        {
            finished(); // la carga de la partida sigue pase lo que pase
        }
    }

    // Las piezas del mapa donde estás que todavía no están en su reserva.
    private static List<Job> Collect(Dictionary<Kind, Counts> counts, out string scene)
    {
        var jobs = new List<Job>();
        scene = null;
        try
        {
            GameScene gameScene = MainGame.Instance != null ? MainGame.PlayerController?.CurrentGameScene : null;
            if (gameScene == null)
                return jobs;
            GameSceneData data = SceneData?.GetValue(gameScene) as GameSceneData;
            scene = data?.id;

            WgoPartPool wgoPool = WgoPoolInstance?.GetValue(null) as WgoPartPool;
            if (wgoPool != null && data?.wgoDataList != null && WgoPools?.GetValue(wgoPool) is IDictionary wgoStacks)
            {
                var keys = new HashSet<string>();
                foreach (WgoData wgo in data.wgoDataList)
                    if (wgo != null)
                        WgoPartPool.CollectAddressableKeysForWgoData(wgo, keys);
                foreach (string key in keys)
                {
                    counts[Kind.Object].total++;
                    if (!HasPooledCopy(wgoStacks, key))
                        jobs.Add(new Job { kind = Kind.Object, path = key });
                }
            }

            ConstructorPartPool ctorPool = CtorPoolInstance?.GetValue(null) as ConstructorPartPool;
            if (ctorPool != null && SceneConstructorParts?.GetValue(gameScene) is List<ConstructorPart> parts && CtorPools?.GetValue(ctorPool) is IDictionary ctorDict)
            {
                var paths = new HashSet<string>();
                foreach (ConstructorPart part in parts)
                    if (part != null && part.HasChildPath && !string.IsNullOrEmpty(part.constructorPartChildData?.pathToObject))
                        paths.Add(part.constructorPartChildData.pathToObject);
                foreach (string path in paths)
                {
                    counts[Kind.ConstructorPart].total++;
                    if (!ctorDict.Contains(path))
                        jobs.Add(new Job { kind = Kind.ConstructorPart, path = path });
                }
            }

            BakedChunkableObjectPool bakedPool = BakedPoolInstance?.GetValue(null) as BakedChunkableObjectPool;
            if (bakedPool != null && gameScene.bakedChunkableObjectComponentDatas != null && BakedPools?.GetValue(bakedPool) is IDictionary bakedDict)
            {
                var paths = new HashSet<string>();
                foreach (BakedChunkableObjectComponentData baked in gameScene.bakedChunkableObjectComponentDatas)
                    if (baked != null && !string.IsNullOrEmpty(baked.pathToObject))
                        paths.Add(baked.pathToObject);
                foreach (string path in paths)
                {
                    counts[Kind.Baked].total++;
                    if (!bakedDict.Contains(path))
                        jobs.Add(new Job { kind = Kind.Baked, path = path });
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[Preload] could not list the map's pieces: " + e.Message);
        }
        foreach (Job job in jobs)
            counts[job.kind].missing++;
        return jobs;
    }

    private static bool HasPooledCopy(IDictionary stacks, string key) =>
        stacks.Contains(key) && stacks[key] is ICollection stack && stack.Count > 0;

    // Pide la pieza con la función asíncrona del juego (sin esperar). false = no se pudo ni pedir.
    private static bool Begin(Job job)
    {
        try
        {
            switch (job.kind)
            {
                case Kind.Object:
                {
                    WgoPartPool pool = WgoPoolInstance?.GetValue(null) as WgoPartPool;
                    if (pool == null)
                        return false;
                    job.task = WgoLoadPrefab?.Invoke(pool, new object[] { job.path });
                    return true; // sin la asíncrona, TryFinish la carga síncrona
                }
                case Kind.ConstructorPart:
                {
                    ConstructorPartPool pool = CtorPoolInstance?.GetValue(null) as ConstructorPartPool;
                    if (pool == null)
                        return false;
                    job.task = CtorCreateAsync?.Invoke(pool, new object[] { job.path });
                    return true;
                }
                default:
                {
                    BakedChunkableObjectPool pool = BakedPoolInstance?.GetValue(null) as BakedChunkableObjectPool;
                    if (pool == null)
                        return false;
                    job.task = BakedCreateAsync?.Invoke(pool, new object[] { job.path });
                    return true;
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogDebug($"[Preload] {job.path}: {(e.InnerException ?? e).Message}");
            return false;
        }
    }

    // null = sigue cargando; true = quedó en su reserva con una copia; false = falló.
    private static bool? TryFinish(Job job)
    {
        try
        {
            switch (job.kind)
            {
                case Kind.Object:
                {
                    GameObject prefab;
                    if (job.task is UniTask<GameObject> load)
                    {
                        if (load.Status == UniTaskStatus.Pending)
                            return null;
                        prefab = load.GetAwaiter().GetResult();
                    }
                    else
                    {
                        WgoPartPool syncPool = WgoPoolInstance?.GetValue(null) as WgoPartPool;
                        prefab = syncPool != null ? WgoLoadPrefabSync?.Invoke(syncPool, new object[] { job.path }) as GameObject : null;
                    }
                    WgoPartPool pool = WgoPoolInstance?.GetValue(null) as WgoPartPool;
                    WgoPart template = prefab != null ? prefab.GetComponent<WgoPart>() : null;
                    if (pool == null || template == null)
                        return false;
                    // Como WgoPartPool.InitAsync: una copia limpia, marcada con su llave, a la reserva.
                    if (!(WgoPools?.GetValue(pool) is IDictionary stacks) || !HasPooledCopy(stacks, job.path))
                    {
                        WgoPart copy = UnityEngine.Object.Instantiate(template);
                        copy.CleanupChunkableComponents();
                        copy.PooledAddressableKey = job.path;
                        pool.Release(job.path, copy);
                    }
                    return true;
                }
                case Kind.ConstructorPart:
                    return FinishPool(job, CtorPoolInstance, CtorCreate);
                default:
                    return FinishPool(job, BakedPoolInstance, BakedCreate);
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogDebug($"[Preload] {job.path}: {(e.InnerException ?? e).Message}");
            return false;
        }
    }

    private static bool? FinishPool(Job job, FieldInfo instance, MethodInfo createSync)
    {
        Pool created;
        if (job.task is UniTask<Pool> create)
        {
            if (create.Status == UniTaskStatus.Pending)
                return null;
            created = create.GetAwaiter().GetResult();
        }
        else
        {
            object pool = instance?.GetValue(null);
            created = pool != null ? createSync?.Invoke(pool, new object[] { job.path }) as Pool : null;
        }
        return WithOneCopy(created);
    }

    // Una reserva recién creada puede quedar vacía (su tamaño de fábrica es 0 si no está en la lista del juego): se le
    // deja una copia, para que la primera vez tampoco haya que crearla.
    private static bool WithOneCopy(Pool pool)
    {
        if (pool == null)
            return false;
        if (pool.Objects == null || pool.Objects.Count == 0)
            pool.AddObjectToPool();
        return true;
    }
}
