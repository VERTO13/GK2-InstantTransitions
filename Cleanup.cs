using System;
using System.Diagnostics;
using System.Reflection;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Scripting;

namespace InstantTransitions;

// La limpieza que el juego hace con la pantalla en negro (MainGame.HiddenOptimization): en cada puerta y
// teletransporte (PlayerController.Teleport, después del fundido) y al terminar de cargar una partida
// (MainGame.AfterSceneHasLoaded). Son tres pasos:
//   1. Resources.UnloadUnusedAssets: recorre todo lo cargado y libera los recursos que ya nadie usa;
//   2. GC.Collect: recolección de basura completa;
//   3. EditorOnlyComponentStripper.StripAll: 24 búsquedas por TODO lo cargado (también lo inactivo) de
//      componentes que solo sirven en el editor, para destruirlos. Medido: la mitad del tiempo de la limpieza.
// En una partida a medias: unos 0.2 + 0.17 + 0.37 s, todo en un solo cuadro congelado.
//
// Aquí se reemplaza. En una puerta nunca se hace la búsqueda grande: los componentes de editor se van quitando
// mientras se juega (Tidy: cada pieza nueva, y en cada puerta un solo tipo), y la basura la sigue recogiendo por
// partes el recolector incremental del juego. Solo liberar los recursos sin usar (paso 1) se hace en una puerta, y
// solo cuando toca: pasaron N minutos o la memoria creció M MB desde la última vez. Al cargar una partida (ahí ya se
// está esperando) y con el mod apagado se hacen los tres pasos como el juego, en el mismo orden y con la misma
// espera, cronometrados. Si el juego no tuviera recolección incremental, la de basura se sigue haciendo en cada puerta.
//
// Por qué es seguro parcharlo: el cuerpo original solo crea su máquina de estados (no lee campos estáticos de
// clases del juego: ver la lección de Crafting Queue sobre MainGame y PlayerSkinHelper). Y la tarea que se
// devuelve SIEMPRE termina, aunque un paso falle: si no, la puerta se quedaría en negro.
internal static class Cleanup
{
    internal sealed class Run
    {
        public bool full;       // los tres pasos (al cargar una partida)
        public bool unloadOnly; // en una puerta, cuando toca: solo liberar los recursos sin usar
        public string why;      // por qué ("10 min since the last one", "memory +312 MB", "loading"; debajo de negro, "sleep: ..." o "door: ...")
        public double unloadMs, gcMs, stripMs;
        public int stripped = -1; // componentes de editor destruidos (-1 = no se contaron)
        public string oneType;    // en una puerta: el tipo de componente de editor que se buscó esta vez (Tidy)
        public int oneTypeFound;
        public double oneTypeMs;
        public bool failed;
        public double TotalMs => unloadMs + gcMs + stripMs;
    }

    // Por nombre y no con typeof: en versiones viejas del juego (1.004.2) estas clases no existen, y con typeof el mod
    // entero no cargaba. Ahí el juego tampoco quita componentes de editor, así que ese paso simplemente no hace nada.
    private static readonly Type Stripper = AccessTools.TypeByName("EditorOnlyComponentStripper");
    private static readonly FieldInfo StripTypes = Stripper != null ? AccessTools.Field(Stripper, "TypesToStrip") : null;
    private static readonly MethodInfo StripAll = Stripper != null ? AccessTools.Method(Stripper, "StripAll") : null;
    private static readonly Type BoxMarker = AccessTools.TypeByName("WgoPartGraphUpdateSceneBoxMarker");

    private static float lastFullAt;          // Time.realtimeSinceStartup de la última limpieza completa
    private static long memoryAtLastFull;     // MB (Unity + basura administrada) justo después de ella
    private static bool anyFull;
    private static int doorsSinceStrip;

    public static void Apply(Harmony harmony)
    {
        MethodInfo target = AccessTools.Method(typeof(MainGame), "HiddenOptimization");
        if (target == null || target.ReturnType != typeof(UniTask))
        {
            Plugin.Log.LogWarning("MainGame.HiddenOptimization not found (game update?): doors are left as they are.");
            return;
        }
        try
        {
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(Cleanup), nameof(Instead)));
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Could not hook the clean-up: " + e.Message);
        }
    }

    internal static long MemoryMB() => DoorWatch.UnityMB() + DoorWatch.ManagedMB();

    // Para el log de la puerta: cuánto falta para la siguiente limpieza completa.
    internal static string NextFull()
    {
        float minutes = Math.Max(0f, Plugin.FullEveryMinutes.Value - (Time.realtimeSinceStartup - lastFullAt) / 60f);
        long mb = Math.Max(0L, Plugin.FullWhenGrownMB.Value - (MemoryMB() - memoryAtLastFull));
        return $"next unload, at the next sleep or black screen, after {minutes:0.0} min or +{mb} MB";
    }

    private const string Loading = "loading";

    private static string DueReason()
    {
        // Fuera de una puerta = al cargar una partida (la pantalla de carga ya lo tapa). Un teletransporte que el
        // vigilante todavía no vio (la cortina ya estaba negra) sigue siendo puerta: lo dice el parche de Teleport.
        if (!DoorWatch.InDoor && Time.realtimeSinceStartup - QuickDoors.LastTeleportAt > 10f)
            return Loading;
        return DueAtBlack();
    }

    // ¿Toca liberar recursos sin usar en un momento de pantalla negra (dormir, una puerta por negro)? Pasaron N minutos o la
    // memoria creció M MB desde la última vez.
    internal static string DueAtBlack()
    {
        if (!anyFull)
            return "first one";
        float minutes = (Time.realtimeSinceStartup - lastFullAt) / 60f;
        if (minutes >= Plugin.FullEveryMinutes.Value)
            return $"{minutes:0} min since the last one";
        long grown = MemoryMB() - memoryAtLastFull;
        if (grown >= Plugin.FullWhenGrownMB.Value)
            return $"memory +{grown} MB";
        return null;
    }

    // En una puerta con corte, la vista de antes queda congelada mientras se limpia, y liberar los recursos sin usar tarda
    // 1.3 a 2.9 s en una partida larga (medido: nueve veces en 9.5 h, y cada una se veía como un tirón en la puerta). Por eso
    // ahí no se libera nunca: espera a un momento con la pantalla negra (dormir, una puerta por negro, cargar). Las puertas y
    // los viajes por el mapa son justo lo que este mod vuelve instantáneo: solo si de verdad hace falta (la memoria creció
    // EmergencyMB desde la última vez Y a la PC le falta memoria) esa puerta no corta, pasa por negro y la limpieza va debajo
    // (QuickDoors.BeforeTeleport). Sin falta de memoria no se hace: el juego sin mod tampoco libera nada dentro del mapa.
    internal const long TightFreeMB = 1500;

    internal static string EmergencyDue()
    {
        if (!anyFull)
            return null; // recién recargado el mod: no se sabe cuánto creció
        long grown = MemoryMB() - memoryAtLastFull;
        if (grown < Plugin.EmergencyMB.Value)
            return null;
        long ram = SystemInfo.systemMemorySize, game = DoorWatch.ProcessMB(), free = DoorWatch.FreeMB();
        bool bigShare = ram > 0 && game > 0 && game * 100 >= ram * Plugin.EmergencyRamPercent.Value;
        bool littleFree = free >= 0 && free < TightFreeMB;
        if (!bigShare && !littleFree)
            return null; // la PC tiene memoria de sobra
        return $"memory +{grown} MB, the game uses {game} of {ram} MB and {free} MB are free, no black moment since";
    }

    private static bool Instead(ref UniTask __result)
    {
        // Apagado (se puede cambiar jugando): la limpieza original del juego, tal cual.
        if (!Plugin.Enabled.Value)
            return true;
        try
        {
            string why;
            try
            {
                why = DueReason();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Clean-up decision: " + e.Message);
                why = "error deciding";
            }
            if (why == Loading)
            {
                __result = Timed(new Run { full = true, why = why });
                return false;
            }
            // Una puerta: si toca, solo liberar los recursos sin usar; y cada tercera puerta (o cuando se libera), un tipo
            // de componente de editor por turno (~30 ms). Esos componentes se destruyen solos al activarse su objeto
            // (Destroy(this) en Awake): la búsqueda solo recoge los que quedaron en objetos inactivos, así que no corre prisa.
            Run run = new Run { why = why };
            if (++doorsSinceStrip >= 3 || why != null)
            {
                doorsSinceStrip = 0;
                Tidy.StripNextType(run);
            }
            if (why != null)
            {
                run.unloadOnly = true;
                __result = Timed(run);
            }
            else if (!GarbageCollector.isIncremental)
                __result = Timed(run); // sin recolección por partes: al menos la de basura
            else
            {
                try { DoorWatch.CleanupFinished(run); }
                catch (Exception e) { Plugin.Log.LogWarning("Clean-up: " + e.Message); }
                __result = UniTask.CompletedTask; // la puerta sigue sin congelarse
            }
            return false;
        }
        catch (Exception e)
        {
            // Algo inesperado: la limpieza del propio juego, para que ni la puerta ni la carga se queden esperando. Solo
            // puede llegar aquí antes de que empiece nada de lo nuestro, porque Timed atrapa cada paso que hace algo: así
            // nunca se limpia dos veces. Todo paso nuevo en Timed necesita su propio catch.
            Plugin.Log.LogWarning("Clean-up: " + e.Message + " (the game's own clean-up runs instead)");
            return true;
        }
    }

    // Una puerta sin la limpieza del juego: con el corte directo el juego mueve al jugador sin fundido y no llama a
    // HiddenOptimization. Aquí va nuestra parte, igual que en una puerta normal: un tipo de componente de editor cada tercera
    // puerta (unos 40 ms). Liberar recursos sin usar NO se hace en un corte (congelaría la vista de antes): ver EmergencyDue.
    internal static void AtSilentDoor()
    {
        if (!Plugin.Enabled.Value)
            return;
        try
        {
            Run run = new Run();
            if (++doorsSinceStrip >= 3)
            {
                doorsSinceStrip = 0;
                Tidy.StripNextType(run);
            }
            if (run.oneType != null)
                Plugin.Log.LogInfo($"[Clean-up] at a cut door: {run.oneType}: {run.oneTypeFound} removed in {run.oneTypeMs:0} ms; {NextFull()}");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Clean-up at a cut door: " + e.Message);
        }
    }

    // ---- Liberar recursos sin usar debajo de una pantalla negra ----------------------------------------------------------
    // Resources.UnloadUnusedAssets recorre todo lo cargado en un solo cuadro (1.3 a 2.9 s en una partida larga). Con la
    // pantalla ya negra no se nota: dormir y las puertas que pasan por negro. Se pide (Request) y DoorWatch avisa cada cuadro
    // si hay negro (BlackTick); con dos cuadros ya dibujados en negro, se hace. Si lo que se pidió fue desde una puerta, lo que
    // el juego seguía haciendo (volver a aclarar) espera a que termine.
    private const float WaitForBlackSeconds = 8f;
    private static string requested;
    private static float requestedAt;
    private static Action held;
    private static int blackFrames;

    internal static bool Waiting => requested != null;

    internal static void Request(string why, string where, Action hold = null)
    {
        if (DoorWatch.Host == null)
        {
            hold?.Invoke(); // nadie va a mirar si hay negro: que el juego siga como siempre
            return;
        }
        if (requested == null)
        {
            requested = $"{where}: {why}";
            requestedAt = Time.unscaledTime;
            blackFrames = 0;
        }
        if (hold != null)
            held += hold;
    }

    // Cada cuadro (DoorWatch.Update). black = la cortina negra del juego (o la de dormir) ya está del todo negra.
    internal static void BlackTick(bool black)
    {
        if (requested == null)
            return;
        try
        {
            if (Time.unscaledTime - requestedAt > WaitForBlackSeconds)
            {
                Plugin.Log.LogInfo($"[Clean-up] no black screen within {WaitForBlackSeconds:0} s for the request ({requested}); not done now.");
                Release(null);
                return;
            }
            blackFrames = black ? blackFrames + 1 : 0;
            if (blackFrames < 2)
                return;
            string why = requested;
            Release(new Run { unloadOnly = true, why = why });
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Clean-up under black: " + e.Message);
            Release(null);
        }
    }

    // Termina la petición: con una corrida, la hace y suelta lo que esperaba cuando acabe; sin ella, lo suelta ya.
    private static void Release(Run run)
    {
        Action after = held;
        requested = null;
        held = null;
        blackFrames = 0;
        if (run == null)
        {
            after?.Invoke();
            return;
        }
        Timed(run, after);
    }

    // Al dormir (el fundido de dormir ya terminó: EnergySystem.IsSleeping pasa a verdadero) se pide la limpieza si toca.
    internal static void ApplySleep(Harmony harmony)
    {
        MethodInfo setter = AccessTools.PropertySetter(typeof(EnergySystem), "IsSleeping");
        if (setter == null)
        {
            Plugin.Log.LogWarning("EnergySystem.IsSleeping not found (game update?): nothing is cleaned while you sleep.");
            return;
        }
        harmony.Patch(setter, postfix: new HarmonyMethod(typeof(Cleanup), nameof(SleepChanged)));
    }

    private static void SleepChanged(bool value)
    {
        if (!value || !Plugin.Enabled.Value)
            return;
        try
        {
            string why = DueAtBlack();
            if (why != null)
                Request(why, "sleep");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Clean-up at sleep: " + e.Message);
        }
    }

    private static double Ms(long from, long to) => (to - from) * 1000.0 / Stopwatch.Frequency;

    private static UniTask Timed(Run run, Action extra = null)
    {
        var done = new UniTaskCompletionSource();
        // Al cargar una partida aquí NO se puede alargar nada: el juego ya disparó "después de dormir" y sus escenas ya
        // corren (ver Preload). La precarga del mapa va antes, en Preload.BeforeAfterLoad.
        // extra: lo que esperaba a esta limpieza (el final del fundido de una puerta), siempre se suelta, pase lo que pase.
        Action after = () =>
        {
            done.TrySetResult();
            try
            {
                extra?.Invoke();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Clean-up (what was waiting): " + e.Message);
            }
        };
        if (run.why == Loading)
            DoorWatch.LoadCleanupAt = Stopwatch.GetTimestamp(); // para el desglose de la pantalla de carga
        long t0 = Stopwatch.GetTimestamp();
        Action<Run, long> finish = run.unloadOnly ? FinishUnload : Finish;
        if (!run.full && !run.unloadOnly)
        {
            try
            {
                GcOnly(run, t0);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Clean-up: " + e.Message);
            }
            finally
            {
                after(); // la puerta sigue pase lo que pase
            }
            return done.Task;
        }
        AsyncOperation unload;
        try
        {
            unload = Resources.UnloadUnusedAssets();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Clean-up (unload): " + e.Message);
            run.failed = true;
            try
            {
                finish(run, t0);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Clean-up: " + ex.Message);
            }
            finally
            {
                after();
            }
            return done.Task;
        }
        // Como el juego: se sigue cuando Unity termina de liberar (el evento llega aunque ya hubiera terminado).
        unload.completed += _ =>
        {
            try
            {
                finish(run, t0);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Clean-up: " + ex.Message);
            }
            finally
            {
                after(); // la puerta (o la carga) sigue pase lo que pase
            }
        };
        return done.Task;
    }

    private static void GcOnly(Run run, long t0)
    {
        try { GC.Collect(); }
        catch (Exception e) { run.failed = true; Plugin.Log.LogWarning("Clean-up (GC): " + e.Message); }
        run.gcMs = Ms(t0, Stopwatch.GetTimestamp());
        DoorWatch.CleanupFinished(run);
    }

    private static void Finish(Run run, long t0)
    {
        long t1 = Stopwatch.GetTimestamp();
        run.unloadMs = Ms(t0, t1);
        try { GC.Collect(); }
        catch (Exception e) { run.failed = true; Plugin.Log.LogWarning("Clean-up (GC): " + e.Message); }
        long t2 = Stopwatch.GetTimestamp();
        run.gcMs = Ms(t1, t2);
        try { run.stripped = Strip(); }
        catch (Exception e) { run.failed = true; Plugin.Log.LogWarning("Clean-up (strip): " + e.Message); }
        run.stripMs = Ms(t2, Stopwatch.GetTimestamp());
        anyFull = true;
        lastFullAt = Time.realtimeSinceStartup;
        memoryAtLastFull = MemoryMB();
        DoorWatch.CleanupFinished(run);
    }

    // En una puerta, cuando toca: solo liberar los recursos sin usar (la parte que de verdad libera memoria). La basura
    // la recoge el recolector incremental, y los componentes de editor se van quitando mientras se juega (Tidy).
    private static void FinishUnload(Run run, long t0)
    {
        long t1 = Stopwatch.GetTimestamp();
        run.unloadMs = Ms(t0, t1);
        if (!GarbageCollector.isIncremental)
        {
            try { GC.Collect(); }
            catch (Exception e) { run.failed = true; Plugin.Log.LogWarning("Clean-up (GC): " + e.Message); }
            run.gcMs = Ms(t1, Stopwatch.GetTimestamp());
        }
        anyFull = true;
        lastFullAt = Time.realtimeSinceStartup;
        memoryAtLastFull = MemoryMB();
        DoorWatch.CleanupFinished(run);
    }

    // Al prender el mod jugando: la memoria de ahora es la referencia (apagado, el juego limpió en cada puerta; si no, la
    // siguiente puerta la tomaba como crecimiento y liberaba todo).
    internal static void ResetBaseline()
    {
        anyFull = true;
        lastFullAt = Time.realtimeSinceStartup;
        memoryAtLastFull = MemoryMB();
    }

    // Lo mismo que EditorOnlyComponentStripper.StripAll (buscar cada tipo en todo lo cargado, también lo
    // inactivo, y destruirlo; el marcador de actualización de grafo se desactiva antes), pero contando lo que
    // encuentra: así el log dice cuántos se juntan entre una limpieza completa y otra. Si el juego cambiara ese
    // campo, se llama a la del juego tal cual.
    private static int Strip()
    {
        if (Stripper == null)
            return 0; // esta versión del juego no tiene componentes de editor que quitar
        if (!(StripTypes?.GetValue(null) is Type[] types))
        {
            StripAll?.Invoke(null, null);
            return -1;
        }
        int found = 0;
        foreach (Type type in types)
            if (type != null)
                found += StripType(type);
        return found;
    }

    // Un tipo, en todo lo cargado (también lo inactivo): como EditorOnlyComponentStripper.StripType del juego.
    internal static int StripType(Type type)
    {
        int found = 0;
        foreach (UnityEngine.Object o in UnityEngine.Object.FindObjectsByType(type, FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!(o is Component c) || c == null)
                continue;
            found++;
            if (BoxMarker != null && BoxMarker.IsInstanceOfType(c) && c.gameObject.activeSelf)
                c.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(c);
        }
        return found;
    }
}
