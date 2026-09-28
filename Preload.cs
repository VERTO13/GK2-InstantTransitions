using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

namespace SmoothDoors;

// Primera visita rápida. Los objetos de un lugar se dibujan con piezas que el juego saca de tres reservas: WgoPartPool
// (objetos: estaciones, muebles, lo que construyes), ConstructorPartPool (piezas de edificios) y
// BakedChunkableObjectPool (decorado fijo). Mientras carga la partida, el juego llena esas reservas con una lista
// fija de sus desarrolladores (MainGame.RegisterBackgroundPreloadTasks → *.InitAsync). Lo que no está en esa lista
// se carga del disco la primera vez que lo ves, todo en un cuadro: medido, hasta 1.3 s al entrar al patio.
//
// Aquí, al final de la carga de la partida (dentro de la limpieza que el juego hace justo antes de quitar la pantalla
// de carga, así la pantalla espera), se revisan TODAS las piezas del mapa donde estás (las mismas que el juego
// registra en GameScene.CollectScenePoolPaths) y se cargan las que falten, igual que su precarga: WgoPartPool carga
// el prefab y deja una copia en la reserva (como WgoPartPool.InitAsync); las otras dos crean su reserva
// (CreatePoolById, lo mismo que harían la primera vez) y le dejan una copia. Cede un cuadro cada ~25 ms para que la
// pantalla de carga siga animada, y nunca pasa de PreloadMaxSeconds. Pase lo que pase, al final la carga sigue.
internal static class Preload
{
    private static readonly FieldInfo WgoPoolInstance = AccessTools.Field(typeof(LazySingleton<WgoPartPool>), "instance");
    private static readonly FieldInfo CtorPoolInstance = AccessTools.Field(typeof(LazySingleton<ConstructorPartPool>), "instance");
    private static readonly FieldInfo BakedPoolInstance = AccessTools.Field(typeof(LazySingleton<BakedChunkableObjectPool>), "instance");
    private static readonly FieldInfo WgoPools = AccessTools.Field(typeof(WgoPartPool), "pools");
    private static readonly MethodInfo WgoLoadPrefabSync = AccessTools.Method(typeof(WgoPartPool), "LoadPrefabSync", new[] { typeof(string) });
    private static readonly FieldInfo CtorPools = AccessTools.Field(typeof(ConstructorPartPool), "poolsDict");
    private static readonly MethodInfo CtorCreate = AccessTools.Method(typeof(ConstructorPartPool), "CreatePoolById", new[] { typeof(string) });
    private static readonly FieldInfo BakedPools = AccessTools.Field(typeof(BakedChunkableObjectPool), "poolsDict");
    private static readonly MethodInfo BakedCreate = AccessTools.Method(typeof(BakedChunkableObjectPool), "CreatePoolById", new[] { typeof(string) });
    private static readonly FieldInfo SceneData = AccessTools.Field(typeof(GameScene), "gameSceneData");
    private static readonly FieldInfo SceneConstructorParts = AccessTools.Field(typeof(GameScene), "constructorParts");

    private enum Kind { Object, ConstructorPart, Baked }

    private sealed class Counts
    {
        public int total, missing, loaded, failed;
        public override string ToString() => $"{loaded}/{missing} missing of {total}" + (failed > 0 ? $" ({failed} failed)" : "");
    }

    private static Action pending;      // quien espera a que termine (la carga de la partida)
    private static float giveUpAt;

    // Empieza la precarga; `finished` se llama una sola vez, al terminar o si algo falla (y a más tardar, al tope).
    internal static void Start(Action finished)
    {
        bool called = false;
        Action once = () =>
        {
            if (called)
                return;
            called = true;
            pending = null;
            finished();
        };
        MonoBehaviour host = DoorWatch.Host;
        if (host == null)
        {
            once();
            return;
        }
        pending = once;
        giveUpAt = Time.realtimeSinceStartup + Mathf.Max(1f, Plugin.PreloadMaxSeconds.Value) + 10f;
        host.StartCoroutine(Run(once));
    }

    // Cada cuadro (DoorWatch): si la corrutina se hubiera detenido sin avisar, la carga no se queda esperando.
    internal static void Watch()
    {
        if (pending != null && Time.realtimeSinceStartup > giveUpAt)
        {
            Plugin.Log.LogWarning("[Preload] did not finish in time; the game goes on.");
            pending();
        }
    }

    private static IEnumerator Run(Action finished)
    {
        try
        {
            Stopwatch total = Stopwatch.StartNew(), frame = Stopwatch.StartNew();
            long memoryBefore = Cleanup.MemoryMB(), processBefore = DoorWatch.ProcessMB();
            var counts = new Dictionary<Kind, Counts> { [Kind.Object] = new Counts(), [Kind.ConstructorPart] = new Counts(), [Kind.Baked] = new Counts() };
            List<(Kind kind, string path)> jobs = Collect(counts, out string scene);
            bool stopped = false;
            foreach ((Kind kind, string path) in jobs)
            {
                if (total.Elapsed.TotalSeconds > Plugin.PreloadMaxSeconds.Value)
                {
                    stopped = true;
                    break;
                }
                if (Load(kind, path))
                    counts[kind].loaded++;
                else
                    counts[kind].failed++;
                if (frame.ElapsedMilliseconds > 25)
                {
                    yield return null; // que la pantalla de carga siga animada
                    frame.Restart();
                }
            }
            Plugin.Log.LogInfo($"[Preload] map {scene ?? "?"}: {total.Elapsed.TotalSeconds:0.00} s" + (stopped ? $" (stopped at the {Plugin.PreloadMaxSeconds.Value:0} s limit)" : "") +
                               $" · objects {counts[Kind.Object]} · building parts {counts[Kind.ConstructorPart]} · scenery {counts[Kind.Baked]}" +
                               $" · memory {memoryBefore} -> {Cleanup.MemoryMB()} MB · game process {processBefore} -> {DoorWatch.ProcessMB()} MB");
        }
        finally
        {
            finished(); // la carga de la partida sigue pase lo que pase
        }
    }

    // Las piezas del mapa donde estás que todavía no están en su reserva.
    private static List<(Kind, string)> Collect(Dictionary<Kind, Counts> counts, out string scene)
    {
        var jobs = new List<(Kind, string)>();
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
                    if (!(wgoStacks.Contains(key) && wgoStacks[key] is ICollection stack && stack.Count > 0))
                        jobs.Add((Kind.Object, key));
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
                        jobs.Add((Kind.ConstructorPart, path));
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
                        jobs.Add((Kind.Baked, path));
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[Preload] could not list the map's pieces: " + e.Message);
        }
        foreach (Counts c in counts.Values)
            c.missing = 0;
        foreach ((Kind kind, string _) in jobs)
            counts[kind].missing++;
        return jobs;
    }

    // Una pieza, igual que la precarga del juego. true = quedó en su reserva.
    private static bool Load(Kind kind, string path)
    {
        try
        {
            switch (kind)
            {
                case Kind.Object:
                {
                    WgoPartPool pool = WgoPoolInstance?.GetValue(null) as WgoPartPool;
                    GameObject prefab = pool != null ? WgoLoadPrefabSync?.Invoke(pool, new object[] { path }) as GameObject : null;
                    WgoPart template = prefab != null ? prefab.GetComponent<WgoPart>() : null;
                    if (template == null)
                        return false;
                    WgoPart copy = UnityEngine.Object.Instantiate(template);
                    copy.CleanupChunkableComponents();
                    copy.PooledAddressableKey = path;
                    pool.Release(path, copy);
                    return true;
                }
                case Kind.ConstructorPart:
                {
                    ConstructorPartPool pool = CtorPoolInstance?.GetValue(null) as ConstructorPartPool;
                    return pool != null && WithOneCopy(CtorCreate?.Invoke(pool, new object[] { path }) as Pool);
                }
                default:
                {
                    BakedChunkableObjectPool pool = BakedPoolInstance?.GetValue(null) as BakedChunkableObjectPool;
                    return pool != null && WithOneCopy(BakedCreate?.Invoke(pool, new object[] { path }) as Pool);
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogDebug($"[Preload] {path}: {(e.InnerException ?? e).Message}");
            return false;
        }
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
