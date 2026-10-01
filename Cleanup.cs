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
        public string why;      // por qué ("10 min since the last one", "memory +312 MB", "loading")
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
        return $"next unload in {minutes:0.0} min or +{mb} MB";
    }

    private const string Loading = "loading";

    private static string DueReason()
    {
        // Fuera de una puerta = al cargar una partida (la pantalla de carga ya lo tapa). Un teletransporte que el
        // vigilante todavía no vio (la cortina ya estaba negra) sigue siendo puerta: lo dice el parche de Teleport.
        if (!DoorWatch.InDoor && Time.realtimeSinceStartup - QuickDoors.LastTeleportAt > 10f)
            return Loading;
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

    // En una puerta con corte, la vista de antes queda congelada mientras se limpia: liberar los recursos sin usar (~0.3 s)
    // se vería como un tirón cada 10 minutos. Ahí solo se libera si la memoria creció el doble de lo normal; si no, espera
    // a una puerta por negro, un viaje por el mapa o cargar una partida (las únicas veces que el juego limpia).
    private static string DueAtCut()
    {
        if (!anyFull)
            return null; // recién recargado el mod: no se sabe cuánto creció
        long grown = MemoryMB() - memoryAtLastFull;
        return grown >= 2L * Plugin.FullWhenGrownMB.Value ? $"memory +{grown} MB" : null;
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
    // HiddenOptimization. Aquí va nuestra parte, igual que en una puerta normal (un tipo de componente de editor cada
    // tercera puerta; liberar recursos sin usar cuando toca, sin esperar a que termine: Unity lo hace en segundo plano).
    internal static void AtSilentDoor()
    {
        if (!Plugin.Enabled.Value)
            return;
        try
        {
            string why = DueAtCut();
            Run run = new Run { why = why };
            if (++doorsSinceStrip >= 3 || why != null)
            {
                doorsSinceStrip = 0;
                Tidy.StripNextType(run);
            }
            if (why != null)
            {
                run.unloadOnly = true;
                Timed(run); // termina sola (FinishUnload anota en el log)
            }
            else if (run.oneType != null)
                Plugin.Log.LogInfo($"[Clean-up] at a cut door: {run.oneType}: {run.oneTypeFound} removed in {run.oneTypeMs:0} ms; {NextFull()}");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Clean-up at a cut door: " + e.Message);
        }
    }

    private static double Ms(long from, long to) => (to - from) * 1000.0 / Stopwatch.Frequency;

    private static UniTask Timed(Run run)
    {
        var done = new UniTaskCompletionSource();
        // Al cargar una partida aquí NO se puede alargar nada: el juego ya disparó "después de dormir" y sus escenas ya
        // corren (ver Preload). La precarga del mapa va antes, en Preload.BeforeAfterLoad.
        Action after = () => done.TrySetResult();
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
                done.TrySetResult(); // la puerta sigue pase lo que pase
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
