using System;
using System.Diagnostics;
using System.Reflection;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using UnityEngine;

namespace SmoothDoors;

// La limpieza que el juego hace con la pantalla en negro (MainGame.HiddenOptimization): en cada puerta y
// teletransporte (PlayerController.Teleport, después del fundido) y al terminar de cargar una partida
// (MainGame.AfterSceneHasLoaded). Son tres pasos:
//   1. Resources.UnloadUnusedAssets: recorre todo lo cargado y libera los recursos que ya nadie usa;
//   2. GC.Collect: recolección de basura completa;
//   3. EditorOnlyComponentStripper.StripAll: quita componentes que solo sirven en el editor.
// Aquí se reemplaza por los MISMOS tres pasos, en el mismo orden y con la misma espera, pero cronometrados.
//
// Por qué el reemplazo es seguro de parchar: el cuerpo original solo crea su máquina de estados (no lee
// campos estáticos de clases del juego: ver la lección de Crafting Queue sobre MainGame y PlayerSkinHelper).
// Y la tarea que se devuelve SIEMPRE termina, aunque un paso falle: si no, la puerta se quedaría en negro.
internal static class Cleanup
{
    internal sealed class Run
    {
        public double unloadMs, gcMs, stripMs;
        public int frame;       // cuadro en que empezó
        public bool failed;
    }

    // La última limpieza que terminó (la lee DoorWatch para la línea de la puerta).
    internal static Run Last;
    internal static int Count; // cuántas han terminado desde que arrancó el juego

    public static void Apply(Harmony harmony)
    {
        MethodInfo target = AccessTools.Method(typeof(MainGame), "HiddenOptimization");
        if (target == null || target.ReturnType != typeof(UniTask))
        {
            Plugin.Log.LogWarning("MainGame.HiddenOptimization not found (game update?): doors are measured without the clean-up breakdown.");
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

    private static bool Instead(ref UniTask __result)
    {
        __result = Timed();
        return false;
    }

    private static double Ms(long from, long to) => (to - from) * 1000.0 / Stopwatch.Frequency;

    private static UniTask Timed()
    {
        var done = new UniTaskCompletionSource();
        var run = new Run { frame = Time.frameCount };
        long t0 = Stopwatch.GetTimestamp();
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
                done.TrySetResult(); // la puerta sigue pase lo que pase
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
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Clean-up: " + e.Message);
            }
            finally
            {
                done.TrySetResult(); // la puerta sigue pase lo que pase
            }
        };
        return done.Task;
    }

    private static void Finish(Run run, long t0)
    {
        long t1 = Stopwatch.GetTimestamp();
        run.unloadMs = Ms(t0, t1);
        try { GC.Collect(); }
        catch (Exception e) { run.failed = true; Plugin.Log.LogWarning("Clean-up (GC): " + e.Message); }
        long t2 = Stopwatch.GetTimestamp();
        run.gcMs = Ms(t1, t2);
        try { EditorOnlyComponentStripper.StripAll(); }
        catch (Exception e) { run.failed = true; Plugin.Log.LogWarning("Clean-up (strip): " + e.Message); }
        run.stripMs = Ms(t2, Stopwatch.GetTimestamp());
        Last = run;
        Count++;
        DoorWatch.CleanupFinished(run);
    }
}
