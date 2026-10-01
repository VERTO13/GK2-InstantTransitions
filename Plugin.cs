using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace InstantTransitions;

// Puertas sin congelones. En cada puerta y teletransporte, con la pantalla en negro, el juego hace una limpieza
// completa (liberar recursos sin usar, recolección de basura y 24 búsquedas de "componentes de editor" por todo
// lo cargado) que congela el juego unos 0.75 s. La recolección de basura del juego ya es por partes (Unity con
// GC incremental), así que en la puerta no hace falta: aquí una puerta nunca hace la limpieza completa. Cada tercera
// puerta quita un solo tipo de componente de editor (unos 30 ms) y, cuando toca (cada tantos minutos o cuando la
// memoria creció), una puerta libera los recursos sin usar (unos 0.3 s). Al cargar una partida, la limpieza completa
// se hace como siempre. Cada puerta queda medida en el log (cuánto tardó cada parte y cuánta memoria usa el juego),
// para comprobar que no sale más lenta ni gasta más memoria.
[BepInPlugin(Guid, Name, Version)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Guid = "verto13.gk2.instanttransitions";
    public const string Name = "Instant Transitions";
    public const string Version = "0.10.0";

    internal static ManualLogSource Log;
    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<KeyboardShortcut> ToggleKey;
    internal static ConfigEntry<float> FullEveryMinutes;
    internal static ConfigEntry<int> FullWhenGrownMB;
    internal static ConfigEntry<float> FadeSeconds;
    internal static ConfigEntry<float> BlackPauseSeconds;
    internal static ConfigEntry<bool> WalkIntoDoors;
    internal static ConfigEntry<bool> HardCut;
    internal static ConfigEntry<KeyboardShortcut> WalkInKey;
    internal static ConfigEntry<bool> HideDoorPrompts;
    internal static ConfigEntry<KeyboardShortcut> PromptsKey;
    internal static ConfigEntry<bool> PreloadPlaces;
    internal static ConfigEntry<float> PreloadMaxSeconds;

    private void Awake()
    {
        Log = Logger;
        Enabled = Config.Bind("Doors", "Enabled", true,
            "Doors cut straight to the other side (no black, no freeze), you can walk into them, and places load ahead of time. " +
            "false = everything as in the unmodded game; the mod only measures doors in the log. " +
            "Can be switched while playing with ToggleKey.");
        ToggleKey = Config.Bind("Doors", "ToggleKey", new KeyboardShortcut(KeyCode.O, KeyCode.LeftControl, KeyCode.LeftShift),
            "Turns Instant Transitions on and off while playing, to compare doors with and without it. A notice shows the new state.");
        FullEveryMinutes = Config.Bind("Doors", "FullCleanupEveryMinutes", 10f,
            "Doors never do the game's full clean-up: every third door removes one kind of its editor-only components instead. " +
            "Unloading unused assets (the part that frees memory, about 0.3 s) happens at a door that goes through black " +
            "once this many minutes have passed since the last time. Doors with the cut (no black) only do it when memory " +
            "grew twice FullCleanupWhenMemoryGrowsMB, so it doesn't show; loading a save always does it.");
        FullWhenGrownMB = Config.Bind("Doors", "FullCleanupWhenMemoryGrowsMB", 300,
            "Or sooner: once the game's memory has grown this much (MB) since the last time unused assets were unloaded.");
        FadeSeconds = Config.Bind("Doors", "FadeSeconds", 0f,
            "Length of each fade (to black and back) on the doors that still go through black: doors to another scene, and " +
            "every door and map trip when HardCut is false. 0 = no fade (about 0.1 s). The game: 0.3.");
        // 0.9.0 traía 0.15 de fábrica: quien lo tiene así nunca lo cambió y pasa al de ahora (sin fundido). Cualquier
        // otro valor lo eligió el jugador y se respeta.
        if (Mathf.Approximately(FadeSeconds.Value, 0.15f))
            FadeSeconds.Value = 0f;
        BlackPauseSeconds = Config.Bind("Doors", "BlackPauseSeconds", 0f,
            "Pause with the screen fully black before you are moved, on the doors that still go through black. The game: 0.3. " +
            "Fights and story scenes keep the game's own fades and pause.");
        WalkIntoDoors = Config.Bind("Doors", "WalkIntoDoors", true,
            "Go through a door by walking into it, without the interact key: you go in right as you reach it. Hatches in the " +
            "floor keep the key. The door you just came through waits until you let go of the movement key, turn around or " +
            "walk away from it, so you don't bounce back. Off during fights. false = doors only with the key, as in the game.");
        HardCut = Config.Bind("Doors", "HardCut", true,
            "Doors and map travel inside the same scene (home, yard, tavern, morgue... the whole map is one scene): no black " +
            "at all. You are on the other side right away, like a cut in a film (the old view holds a few frames while the " +
            "new place is drawn). Near a door, its other side loads ahead of time so the cut never waits. " +
            "false = the quick fade through black (FadeSeconds). Doors to another scene always go through black.");
        WalkInKey = Config.Bind("Doors", "WalkInKey", new KeyboardShortcut(KeyCode.C, KeyCode.LeftControl, KeyCode.LeftShift),
            "Turns walking into doors (WalkIntoDoors) on and off while playing. Off, every door works with the interact key " +
            "and shows its prompt again. A notice shows the new state.");
        HideDoorPrompts = Config.Bind("Doors", "HideDoorPrompts", true,
            "Hides the \"[E] Enter / Exit\" prompt over the doors you can walk into, so they feel like part of the map. " +
            "Hatches in the floor and ladders you climb keep theirs, and the interact key still works on every door. " +
            "Can be switched while playing with PromptsKey.");
        PromptsKey = Config.Bind("Doors", "PromptsKey", new KeyboardShortcut(KeyCode.H, KeyCode.LeftControl, KeyCode.LeftShift),
            "Shows or hides the door prompts while playing. A notice shows the new state.");
        PreloadPlaces = Config.Bind("Loading", "PreloadPlaces", true,
            "While a save loads, also load the pieces of the whole map that the game's own preload leaves out, so the first " +
            "visit to each place is as quick as the next ones. The loading screen takes a little longer and the game uses more memory.");
        PreloadMaxSeconds = Config.Bind("Loading", "PreloadMaxSeconds", 20f,
            "The extra preload never makes the loading screen longer than this (seconds); what is left loads as usual.");
        // Los parches se ponen siempre, para poder prenderlo jugando; apagado, cada uno deja pasar al juego tal cual
        // (la limpieza original, los fundidos y la pausa de fábrica, sin precarga), y otro mod de puertas funciona igual.
        harmony = new Harmony(Guid);
        // Recarga en caliente (ScriptEngine): si quedaron parches de la copia anterior, fuera antes de poner los nuevos.
        harmony.UnpatchSelf();
        // Cada parte arranca por su lado: si a una le falta algo del juego (otra versión, como la 1.004.2), esa se
        // queda apagada y el log dice por qué, y las demás siguen. Antes una sola clase faltante tumbaba el mod entero.
        StartPart("the memory clean-up on doors", () => Cleanup.Apply(harmony));
        StartPart("the quick fades", () => QuickDoors.Apply(harmony));
        StartPart("hiding the door prompts", () => DoorPrompts.Apply(harmony));
        StartPart("the map preload while a save loads", () => Preload.Apply(harmony));
        StartPart("the door log, loading line and preload", () => gameObject.AddComponent<DoorWatch>());
    }

    private Harmony harmony;

    // Al descargar el mod sin cerrar el juego (ScriptEngine, para probar cambios): quitar sus parches y lo que creó. Al
    // cerrar el juego no hace falta, pero no estorba.
    private void OnDestroy()
    {
        try
        {
            harmony?.UnpatchSelf();
            Cut.Shutdown();
            WalkIn.Shutdown();
            DoorPrewarm.Shutdown();
            LoadingLabel.Shutdown();
            Notice.Shutdown();
        }
        catch (System.Exception e)
        {
            Log.LogWarning("Unloading: " + e.Message);
        }
    }

    private static void StartPart(string what, System.Action start)
    {
        try
        {
            start();
        }
        catch (System.Exception e)
        {
            System.Exception cause = e is System.TypeInitializationException && e.InnerException != null ? e.InnerException : e;
            Log.LogWarning($"Off: {what}. This version of the game ({Application.version}) is missing something it needs: {cause.Message}");
        }
    }
}
