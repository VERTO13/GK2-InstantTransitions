using System;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;

namespace InstantTransitions;

// Los "componentes de editor" del juego (24 tipos: RoundPos, UniqueId, WorldZonePrebuiltWgo…) se quitan buscándolos
// en todo lo cargado (EditorOnlyComponentStripper.StripAll). El juego lo hace en cada puerta, los 24 de golpe
// (~0.4 s congelado). Aquí cada puerta busca UNO solo, por turnos (~30 ms, con la pantalla en negro): así nada se
// acumula y la puerta no se congela.
// Lo que NO se debe hacer: quitarlos a cada pieza nueva en cualquier cuadro mientras se juega. Se probó (parchando
// WgoPart.ReInitFromPool y Pool.AddObjectToPool) y el driver de la tarjeta de video se cayó dos veces seguidas
// (d3d11 887a0005, device removed). El juego solo los quita con la pantalla en negro.
internal static class Tidy
{
    private static readonly Type Stripper = AccessTools.TypeByName("EditorOnlyComponentStripper");
    private static readonly FieldInfo StripTypes = Stripper != null ? AccessTools.Field(Stripper, "TypesToStrip") : null;
    private static int nextType;

    // En una puerta, con la pantalla negra: busca un solo tipo en todo lo cargado (se van turnando los 24).
    internal static void StripNextType(Cleanup.Run run)
    {
        try
        {
            if (!(StripTypes?.GetValue(null) is Type[] types) || types.Length == 0)
                return;
            Type type = types[nextType % types.Length];
            nextType = (nextType + 1) % types.Length;
            if (type == null)
                return;
            long t = Stopwatch.GetTimestamp();
            run.oneTypeFound = Cleanup.StripType(type);
            run.oneTypeMs = (Stopwatch.GetTimestamp() - t) * 1000.0 / Stopwatch.Frequency;
            run.oneType = type.Name;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Clean-up (one type): " + e.Message);
        }
    }
}
