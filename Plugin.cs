using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace SmoothDoors;

// Puertas sin congelones. Primera etapa: solo MIDE. Cada puerta (y cada teletransporte) queda en el log con
// cuánto tardó cada parte: el fundido a negro, la limpieza de memoria que el juego hace con la pantalla en
// negro (liberar recursos sin usar + recolección de basura), la carga del lugar nuevo y el fundido de vuelta.
// Con esos números se decide qué acortar. El juego hace exactamente lo mismo que sin el mod.
[BepInPlugin(Guid, Name, Version)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Guid = "verto13.gk2.smoothdoors";
    public const string Name = "Smooth Doors";
    public const string Version = "0.1.0";

    internal static ManualLogSource Log;

    private void Awake()
    {
        Log = Logger;
        Cleanup.Apply(new Harmony(Guid));
        gameObject.AddComponent<DoorWatch>();
    }
}
