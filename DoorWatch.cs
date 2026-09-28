using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx.Bootstrap;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Scripting;

namespace SmoothDoors;

// Sigue cada puerta de principio a fin sin tocar nada del juego: solo mira, cuadro a cuadro, lo mismo que
// el juego usa. Empieza cuando el juego le quita el control al jugador por teletransporte
// (TakenControlType.ByTeleport: PlayerController.Teleport lo hace antes del fundido) y termina cuando se lo
// devuelve y la pantalla ya no está negra. En medio, la opacidad de la cortina negra del juego (UIFade)
// dice cuándo terminó de oscurecer y cuándo empezó a aclarar.
//
// Una línea por puerta en el log, p. ej.:
//   [Door] home -> yard (same scene): 0.83 s = fade in 0.28 + black 0.52 (clean-up skipped, next full one in
//   7.5 min or +280 MB; then until the fade back 0.51) + fade out 0.03 · longest frame 0.21 s (1 over 0.1 s) ·
//   GCs 0 · managed 413 -> 415 MB · Unity 958 -> 962 MB · game process 2210 -> 2215 MB
internal class DoorWatch : MonoBehaviour
{
    private const float Black = 0.99f;

    private static readonly FieldInfo GuiElements = AccessTools.Field(typeof(LazyUI), "guiElementsDictionary");
    private static readonly FieldInfo Blackout = AccessTools.Field(typeof(UIBasicFade), "blackoutCanvas");

    private UIFade fade;
    private CanvasGroup curtain;
    private float nextLookup;
    private string lastError;

    // La puerta en curso (null = ninguna).
    private sealed class Door
    {
        public long start, blackAt, clearAt;
        public string fromScene, fromZone;
        public long managedBefore, unityBefore, processBefore;
        public int gcBefore;
        public float longestFrame;
        public int slowFrames;          // cuadros de más de 100 ms
        public Cleanup.Run cleanup;
        public long cleanupEnd;
    }

    private Door door;
    private static DoorWatch instance;

    // ¿Hay una puerta en curso? (la limpieza fuera de una puerta es la de cargar una partida)
    internal static bool InDoor => instance != null && instance.door != null;

    private void Start()
    {
        instance = this;
        // Aquí y no en Awake: para entonces BepInEx ya cargó todos los mods (para los reportes: cuántos hay).
        Plugin.Log.LogInfo($"{Plugin.Name} {Plugin.Version}: " +
                           (Plugin.Enabled.Value
                               ? $"doors skip the full clean-up unless one is due (every {Plugin.FullEveryMinutes.Value:0.#} min or +{Plugin.FullWhenGrownMB.Value} MB); " +
                                 $"door fades {Plugin.FadeSeconds.Value:0.##} s, pause in black {Plugin.BlackPauseSeconds.Value:0.##} s. "
                               : "off: every door does the full clean-up, like the unmodded game. ") +
                           $"Unity {Application.unityVersion}, incremental GC {GarbageCollector.isIncremental}, " +
                           $"system RAM {SystemInfo.systemMemorySize} MB, {Chainloader.PluginInfos.Count} BepInEx plugins: " +
                           string.Join(", ", Chainloader.PluginInfos.Values.Select(p => p.Metadata.Name)));
    }

    private static long Now() => Stopwatch.GetTimestamp();

    private static double Seconds(long from, long to) => (to - from) / (double)Stopwatch.Frequency;

    internal static long ManagedMB() => GC.GetTotalMemory(false) / (1024 * 1024);

    internal static long UnityMB() => Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024);

    // La memoria privada del proceso entero (la columna "Memoria" del Administrador de tareas): de eso se quejan
    // los jugadores. Process.PrivateMemorySize64 da 0 en el Mono del juego; se pregunta directo a Windows.
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryCounters
    {
        public uint cb, PageFaultCount;
        public UIntPtr PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage,
            QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage, PrivateUsage;
    }

    [DllImport("kernel32.dll", EntryPoint = "K32GetProcessMemoryInfo")]
    private static extern bool GetProcessMemoryInfo(IntPtr process, out MemoryCounters counters, uint size);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    private static bool noProcessMemory;

    private static long ProcessMB()
    {
        if (noProcessMemory)
            return -1;
        try
        {
            if (GetProcessMemoryInfo(GetCurrentProcess(), out MemoryCounters c, (uint)Marshal.SizeOf(typeof(MemoryCounters))))
                return (long)(c.PrivateUsage.ToUInt64() / (1024 * 1024));
        }
        catch
        {
            noProcessMemory = true; // sin Windows (o sin esa función): no se vuelve a intentar
        }
        return -1;
    }

    private void Update()
    {
        try
        {
            Tick();
        }
        catch (Exception e)
        {
            if (e.ToString() != lastError)
            {
                lastError = e.ToString();
                Plugin.Log.LogError("Door watch: " + e);
            }
            door = null;
        }
    }

    private void Tick()
    {
        // En el menú principal todavía no hay juego: MainGame.PlayerController fallaría.
        PlayerController player = MainGame.Instance != null ? MainGame.PlayerController : null;
        if (player == null || MainGame.PlayerData == null)
        {
            door = null; // menú principal o cargando: aquí no hay puertas
            return;
        }
        bool teleporting = !player.IsControlEnabledByType(TakenControlType.ByTeleport);
        float alpha = CurtainAlpha();

        if (door == null)
        {
            if (!teleporting)
                return;
            door = new Door
            {
                start = Now(),
                fromScene = MainGame.PlayerData.currentGameSceneId,
                fromZone = MainGame.PlayerData.CurrentWorldZoneData?.id,
                managedBefore = ManagedMB(),
                unityBefore = UnityMB(),
                processBefore = ProcessMB(),
                gcBefore = GC.CollectionCount(0),
            };
            return;
        }

        float dt = Time.unscaledDeltaTime;
        if (dt > door.longestFrame)
            door.longestFrame = dt;
        if (dt > 0.1f)
            door.slowFrames++;
        if (door.blackAt == 0 && alpha >= Black)
            door.blackAt = Now();
        else if (door.blackAt != 0 && door.clearAt == 0 && alpha < Black)
            door.clearAt = Now();

        if (teleporting || alpha > 0.01f)
            return; // sigue la puerta (o el fundido de vuelta)
        Report(door);
        door = null;
        QuickDoors.DoorEnded();
    }

    // La limpieza terminó (o se saltó): si fue durante una puerta, cuenta para esa puerta; si no (cargar una
    // partida), va sola.
    internal static void CleanupFinished(Cleanup.Run run)
    {
        Door d = instance != null ? instance.door : null;
        if (d != null && d.cleanup == null)
        {
            d.cleanup = run;
            d.cleanupEnd = Now();
            return;
        }
        Plugin.Log.LogInfo($"[Clean-up] outside a door (loading a save?): {Describe(run)} · managed {ManagedMB()} MB · Unity {UnityMB()} MB · " +
                           $"game process {ProcessMB()} MB");
    }

    private static string Describe(Cleanup.Run r)
    {
        string errors = r.failed ? " (with errors)" : "";
        if (r.full)
            return $"full clean-up {r.TotalMs / 1000:0.00} s ({r.why}): unload {r.unloadMs / 1000:0.00}, GC {r.gcMs / 1000:0.00}, " +
                   $"strip {r.stripMs / 1000:0.00}" + (r.stripped >= 0 ? $" ({r.stripped} editor components)" : "") + errors;
        if (r.gcMs > 0)
            return $"GC only {r.gcMs / 1000:0.00} s (the game has no incremental GC){errors}";
        return "clean-up skipped, " + Cleanup.NextFull();
    }

    private void Report(Door d)
    {
        long end = Now();
        string toScene = MainGame.PlayerData?.currentGameSceneId;
        string toZone = MainGame.PlayerData?.CurrentWorldZoneData?.id;
        bool sameScene = toScene == d.fromScene;
        string parts;
        if (d.blackAt != 0 && d.clearAt != 0)
        {
            double fadeIn = Seconds(d.start, d.blackAt), black = Seconds(d.blackAt, d.clearAt), fadeOut = Seconds(d.clearAt, end);
            string inside = d.cleanup != null
                ? $" ({Describe(d.cleanup)}; then until the fade back {Seconds(d.cleanupEnd, d.clearAt):0.00})"
                : " (no clean-up)";
            parts = $"fade in {fadeIn:0.00} + black {black:0.00}{inside} + fade out {fadeOut:0.00}";
        }
        else
            parts = d.cleanup != null ? $"no fade; {Describe(d.cleanup)}" : "no fade, no clean-up";
        Plugin.Log.LogInfo($"[Door] {d.fromZone ?? "?"} -> {toZone ?? "?"} ({(sameScene ? "same scene" : $"scene {d.fromScene} -> {toScene}")}" +
                           $"{(QuickDoors.LastWasQuick ? ", quick fades" : "")}): " +
                           $"{Seconds(d.start, end):0.00} s = {parts} · longest frame {d.longestFrame:0.00} s ({d.slowFrames} over 0.1 s) · " +
                           $"GCs {GC.CollectionCount(0) - d.gcBefore} · managed {d.managedBefore} -> {ManagedMB()} MB · Unity {d.unityBefore} -> {UnityMB()} MB · " +
                           $"game process {d.processBefore} -> {ProcessMB()} MB");
    }

    // Opacidad de la cortina negra del juego (0 = se ve el juego, 1 = negro). La cortina se toma de la lista de
    // LazyUI, como hace el juego (LazyUI.Get<UIFade> lanzaría una excepción si todavía no existe).
    private float CurtainAlpha()
    {
        if (curtain == null && Time.unscaledTime >= nextLookup)
        {
            nextLookup = Time.unscaledTime + 2f;
            fade = GuiElements?.GetValue(null) is Dictionary<Type, ILazyGUIElement> elements
                   && elements.TryGetValue(typeof(UIFade), out ILazyGUIElement e) ? e as UIFade : null;
            curtain = fade != null ? Blackout?.GetValue(fade) as CanvasGroup : null;
        }
        return curtain != null && curtain.gameObject.activeInHierarchy ? curtain.alpha : 0f;
    }
}
