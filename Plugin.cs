using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

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
    internal static ConfigEntry<float> FullEveryMinutes;
    internal static ConfigEntry<int> FullWhenGrownMB;

    private void Awake()
    {
        Log = Logger;
        Enabled = Config.Bind("Doors", "Enabled", true,
            "Doors skip the game's full memory clean-up (the freeze while the screen is black) unless one is due. " +
            "false = every door does it, like the unmodded game. Doors are measured in the log either way.");
        FullEveryMinutes = Config.Bind("Doors", "FullCleanupEveryMinutes", 10f,
            "A door still gets the game's full clean-up once this many minutes have passed since the last one.");
        FullWhenGrownMB = Config.Bind("Doors", "FullCleanupWhenMemoryGrowsMB", 300,
            "Or sooner: once the game's memory has grown this much (MB) since the last full clean-up.");
        Cleanup.Apply(new Harmony(Guid));
        gameObject.AddComponent<DoorWatch>();
    }
}
