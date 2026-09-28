using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace SmoothDoors;

// Puertas sin congelones. En cada puerta y teletransporte, con la pantalla en negro, el juego hace una limpieza
// completa (liberar recursos sin usar, recolección de basura y 24 búsquedas de "componentes de editor" por todo
// lo cargado) que congela el juego unos 0.75 s. La recolección de basura del juego ya es por partes (Unity con
// GC incremental), así que casi nunca hace falta en la puerta: aquí la limpieza completa solo se hace cuando toca
// (cada tantos minutos o cuando la memoria creció) y siempre al cargar una partida. Cada puerta queda medida en
// el log (cuánto tardó cada parte y cuánta memoria usa el juego), para comprobar que no sale más lenta ni gasta
// más memoria.
[BepInPlugin(Guid, Name, Version)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Guid = "verto13.gk2.smoothdoors";
    public const string Name = "Smooth Doors";
    public const string Version = "0.1.0";

    internal static ManualLogSource Log;
    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<KeyboardShortcut> ToggleKey;
    internal static ConfigEntry<float> FullEveryMinutes;
    internal static ConfigEntry<int> FullWhenGrownMB;
    internal static ConfigEntry<float> FadeSeconds;
    internal static ConfigEntry<float> BlackPauseSeconds;
    internal static ConfigEntry<bool> PreloadPlaces;
    internal static ConfigEntry<float> PreloadMaxSeconds;

    private void Awake()
    {
        Log = Logger;
        Enabled = Config.Bind("Doors", "Enabled", true,
            "Doors skip the game's full memory clean-up (the freeze while the screen is black) unless one is due, fade faster " +
            "and preload places. false = everything as in the unmodded game; the mod only measures doors in the log. " +
            "Can be switched while playing with ToggleKey.");
        ToggleKey = Config.Bind("Doors", "ToggleKey", new KeyboardShortcut(KeyCode.O, KeyCode.LeftControl, KeyCode.LeftShift),
            "Turns Smooth Doors on and off while playing, to compare doors with and without it. A notice shows the new state.");
        FullEveryMinutes = Config.Bind("Doors", "FullCleanupEveryMinutes", 10f,
            "A door still gets the game's full clean-up once this many minutes have passed since the last one.");
        FullWhenGrownMB = Config.Bind("Doors", "FullCleanupWhenMemoryGrowsMB", 300,
            "Or sooner: once the game's memory has grown this much (MB) since the last full clean-up.");
        FadeSeconds = Config.Bind("Doors", "FadeSeconds", 0.15f,
            "Length of each fade (to black and back) on doors you use and on map travel. The game: 0.3.");
        BlackPauseSeconds = Config.Bind("Doors", "BlackPauseSeconds", 0f,
            "Pause with the screen fully black before you are moved, on doors you use and on map travel. The game: 0.3. " +
            "Fights and story scenes keep the game's own fades and pause.");
        PreloadPlaces = Config.Bind("Loading", "PreloadPlaces", true,
            "While a save loads, also load the pieces of the whole map that the game's own preload leaves out, so the first " +
            "visit to each place is as quick as the next ones. The loading screen takes a little longer and the game uses more memory.");
        PreloadMaxSeconds = Config.Bind("Loading", "PreloadMaxSeconds", 20f,
            "The extra preload never makes the loading screen longer than this (seconds); what is left loads as usual.");
        // Los parches se ponen siempre, para poder prenderlo jugando; apagado, cada uno deja pasar al juego tal cual
        // (la limpieza original, los fundidos y la pausa de fábrica, sin precarga), y otro mod de puertas funciona igual.
        Harmony harmony = new Harmony(Guid);
        Cleanup.Apply(harmony);
        QuickDoors.Apply(harmony);
        gameObject.AddComponent<DoorWatch>();
    }
}
