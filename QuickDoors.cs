using System;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

namespace SmoothDoors;

// Puertas más cortas: sin la limpieza, lo que queda de una puerta es animación fija del juego. Medido:
// fundido a negro 0.3 s, una pausa en negro de 0.3 s (TeleportDataBase.delayInFade, el valor de fábrica de
// todos sus teletransportes) y fundido de vuelta 0.3 s. Aquí se acortan los dos fundidos (FadeSeconds) y la
// pausa (BlackPauseSeconds), pero solo en las puertas que usa el jugador y en los viajes desde el mapa. Las
// peleas y las escenas de historia (nodos Flow_*) se quedan como el juego: ahí la historia puede estar
// acomodando cosas mientras la pantalla está negra.
//
// Cómo se sabe qué teletransporte es: quién llamó a PlayerController.Teleport. Las puertas son funciones de
// LazyExpression (teleport a un objeto o a un punto); el mapa, MapPageWidget. Una expresión también podría
// venir de un diálogo o de una escena: esas se dejan como el juego si hay una ventana abierta o si el juego
// tiene tomado el control por una escena.
//
// Parches seguros (ver la lección de Crafting Queue): PlayerController.Teleport y UIBasicFade.FadeIn/FadeOut
// no leen campos estáticos de clases del juego. Solo se cambian argumentos; el juego sigue haciendo lo suyo.
internal static class QuickDoors
{
    private static bool active;        // puerta del jugador en curso: sus fundidos se acortan
    private static float activeUntil;  // por si algo falla: nunca más de 15 s
    internal static bool LastWasQuick; // para el log de la puerta
    internal static float LastTeleportAt = -100f; // cualquier teletransporte (la limpieza de después es de puerta, no de carga)

    public static void Apply(Harmony harmony)
    {
        TryPatch(harmony, AccessTools.Method(typeof(PlayerController), nameof(PlayerController.Teleport), new[] { typeof(TeleportDataBase) }),
            nameof(BeforeTeleport));
        TryPatch(harmony, AccessTools.Method(typeof(UIBasicFade), nameof(UIBasicFade.FadeIn),
            new[] { typeof(float), typeof(Action), typeof(FadeFlag), typeof(bool) }), nameof(ShortFadeIn));
        TryPatch(harmony, AccessTools.Method(typeof(UIBasicFade), nameof(UIBasicFade.FadeOut),
            new[] { typeof(float), typeof(Action), typeof(FadeFlag) }), nameof(ShortFadeOut));
    }

    private static void TryPatch(Harmony harmony, MethodInfo target, string prefix)
    {
        if (target == null)
        {
            Plugin.Log.LogWarning($"Quick doors: the game's method for {prefix} was not found (game update?); fades stay as they are.");
            return;
        }
        try
        {
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(QuickDoors), prefix));
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Quick doors ({prefix}): {e.Message}");
        }
    }

    private static bool Active => active && Time.realtimeSinceStartup < activeUntil;

    // Terminó la puerta (DoorWatch): los fundidos que vengan después ya son del juego.
    internal static void DoorEnded() => active = false;

    private static void BeforeTeleport(TeleportDataBase teleportData)
    {
        LastTeleportAt = Time.realtimeSinceStartup;
        active = false;
        LastWasQuick = false;
        try
        {
            if (!Plugin.Enabled.Value || teleportData == null || teleportData.donNotFade || !PlayerInitiated())
                return;
            active = true;
            activeUntil = Time.realtimeSinceStartup + 15f;
            LastWasQuick = true;
            teleportData.delayInFade = Mathf.Min(teleportData.delayInFade, Mathf.Max(0f, Plugin.BlackPauseSeconds.Value));
        }
        catch (Exception e)
        {
            active = false;
            Plugin.Log.LogWarning("Quick doors: " + e.Message);
        }
    }

    private static void ShortFadeIn(UIBasicFade __instance, ref float fadeTime) => Shorten(__instance, ref fadeTime, last: false);

    private static void ShortFadeOut(UIBasicFade __instance, ref float fadeTime) => Shorten(__instance, ref fadeTime, last: true);

    // Solo la cortina negra de las puertas (UIFade); dormir y los textos en negro son otras.
    private static void Shorten(UIBasicFade fade, ref float fadeTime, bool last)
    {
        if (!Active || !(fade is UIFade))
            return;
        if (!Plugin.Enabled.Value)
        {
            active = false; // se apagó a media puerta: el resto de esta puerta ya es del juego
            return;
        }
        float quick = Mathf.Max(0f, Plugin.FadeSeconds.Value);
        if (fadeTime > quick)
            fadeTime = quick;
        if (last)
            active = false; // el fundido de vuelta es lo último de la puerta
    }

    // ¿Una puerta que usó el jugador, o un viaje desde el mapa? Se mira quién llamó a Teleport: el primero de la
    // pila que se reconozca decide.
    private static bool PlayerInitiated()
    {
        StackFrame[] frames = new StackTrace(2, false).GetFrames();
        if (frames == null)
            return false;
        foreach (StackFrame frame in frames)
        {
            for (Type t = frame.GetMethod()?.DeclaringType; t != null; t = t.DeclaringType) // las lambdas viven en tipos anidados
            {
                if (t == typeof(MapPageWidget))
                    return true;
                if (t == typeof(LazyExpression))
                    return NothingElseGoingOn();
                if (t == typeof(FightingGameController) || t.Namespace == "GK2.FlowCanvasNodes")
                    return false;
            }
        }
        return false;
    }

    // Una puerta se usa jugando: sin ventanas abiertas (un diálogo) y sin una escena con el control tomado.
    private static bool NothingElseGoingOn()
    {
        if (LazyWindowsStackController.ActiveWindow != null)
            return false;
        PlayerController player = MainGame.Instance != null ? MainGame.PlayerController : null;
        return player == null || player.IsControlEnabledByType(TakenControlType.ByCinematics);
    }
}
