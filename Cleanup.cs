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
// Aquí se reemplaza. En una puerta, si no toca limpieza completa, no se hace ninguno de los tres pasos: la
// basura la sigue recogiendo por partes el recolector incremental del juego mientras juegas, y lo demás espera a
// la siguiente limpieza completa. Toca cuando pasaron N minutos o la memoria creció M MB desde la última, al
// cargar una partida (ahí ya se está esperando) o con el mod apagado; entonces se hacen los tres pasos como el
// juego, en el mismo orden y con la misma espera, cronometrados. Si el juego no tuviera recolección incremental,
// la de basura se sigue haciendo en cada puerta.
//
// Por qué es seguro parcharlo: el cuerpo original solo crea su máquina de estados (no lee campos estáticos de
// clases del juego: ver la lección de Crafting Queue sobre MainGame y PlayerSkinHelper). Y la tarea que se
// devuelve SIEMPRE termina, aunque un paso falle: si no, la puerta se quedaría en negro.
internal static class Cleanup
{
    internal sealed class Run
    {
        public bool full;       // los tres pasos (o solo la recolección de basura, si no es incremental)
        public string why;      // por qué se hizo completa ("10 min", "+312 MB", "loading", "mod off")
        public double unloadMs, gcMs, stripMs;
        public int stripped = -1; // componentes de editor destruidos (-1 = no se contaron)
        public bool failed;
        public double TotalMs => unloadMs + gcMs + stripMs;
    }

    private static readonly FieldInfo StripTypes = AccessTools.Field(typeof(EditorOnlyComponentStripper), "TypesToStrip");

    private static float lastFullAt;          // Time.realtimeSinceStartup de la última limpieza completa
    private static long memoryAtLastFull;     // MB (Unity + basura administrada) justo después de ella
    private static bool anyFull;

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
        return $"next full one in {minutes:0.0} min or +{mb} MB";
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

    private static bool Instead(ref UniTask __result)
    {
        // Apagado (se puede cambiar jugando): la limpieza original del juego, tal cual.
        if (!Plugin.Enabled.Value)
            return true;
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
        if (why != null)
            __result = Timed(new Run { full = true, why = why });
        else if (!GarbageCollector.isIncremental)
            __result = Timed(new Run { full = false }); // sin recolección por partes: al menos la de basura
        else
        {
            try { DoorWatch.CleanupFinished(new Run { full = false }); }
            catch (Exception e) { Plugin.Log.LogWarning("Clean-up: " + e.Message); }
            __result = UniTask.CompletedTask; // la puerta sigue sin congelarse
        }
        return false;
    }

    private static double Ms(long from, long to) => (to - from) * 1000.0 / Stopwatch.Frequency;

    private static UniTask Timed(Run run)
    {
        var done = new UniTaskCompletionSource();
        Action complete = () => done.TrySetResult();
        // Al cargar una partida, después de la limpieza va la precarga de las piezas del mapa (la carga la espera).
        bool preload = run.why == Loading && Plugin.PreloadPlaces.Value;
        Action after = preload ? () => StartPreload(complete) : complete;
        if (run.why == Loading)
            DoorWatch.LoadCleanupAt = Stopwatch.GetTimestamp(); // para el desglose de la pantalla de carga
        if (preload)
            LoadingLabel.Show(0, 1); // desde ya: la barra del juego ya está llena y la limpieza tarda casi 1 s
        long t0 = Stopwatch.GetTimestamp();
        if (!run.full)
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
                Finish(run, t0);
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
                Finish(run, t0);
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

    private static void StartPreload(Action complete)
    {
        try
        {
            Preload.Start(complete);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[Preload] " + e.Message);
            complete();
        }
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

    // Para la precarga: quitar ya los componentes de editor de lo recién cargado (cronometrado).
    internal static int StripNow(out double ms)
    {
        long t = Stopwatch.GetTimestamp();
        int stripped;
        try { stripped = Strip(); }
        catch (Exception e) { Plugin.Log.LogWarning("[Preload] strip: " + e.Message); stripped = -1; }
        ms = Ms(t, Stopwatch.GetTimestamp());
        return stripped;
    }

    // Después de la limpieza y la precarga de una carga de partida: lo que la precarga dejó en memoria es a propósito,
    // así que esa memoria es la referencia (si no, la siguiente puerta la tomaba como crecimiento y limpiaba todo).
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
        if (!(StripTypes?.GetValue(null) is Type[] types))
        {
            EditorOnlyComponentStripper.StripAll();
            return -1;
        }
        int found = 0;
        foreach (Type type in types)
        {
            if (type == null)
                continue;
            foreach (UnityEngine.Object o in UnityEngine.Object.FindObjectsByType(type, FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!(o is Component c) || c == null)
                    continue;
                found++;
                if (c is WgoPartGraphUpdateSceneBoxMarker && c.gameObject.activeSelf)
                    c.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(c);
            }
        }
        return found;
    }
}
