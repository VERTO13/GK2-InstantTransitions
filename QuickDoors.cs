using System;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

namespace InstantTransitions;

// Puertas más cortas: sin la limpieza, lo que queda de una puerta es animación fija del juego. Medido:
// fundido a negro 0.3 s, una pausa en negro de 0.3 s (TeleportDataBase.delayInFade, el valor de fábrica de
// todos sus teletransportes) y fundido de vuelta 0.3 s. Aquí se acortan los dos fundidos (FadeSeconds) y la
// pausa (BlackPauseSeconds), pero solo en las puertas que usa el jugador y en los viajes desde el mapa. Las
// peleas y las escenas de historia (nodos Flow_*) se quedan como el juego: ahí la historia puede estar
// acomodando cosas mientras la pantalla está negra.
//
// Cómo se sabe qué teletransporte es: quién llamó a PlayerController.Teleport. Casi todas las puertas son
// funciones de LazyExpression (teleport a un objeto o a un punto); el mapa, MapPageWidget. Algunas puertas son
// objetos con su propio script (p. ej. la salida de la torre del astrólogo, tp_RT_astrologer_tower_exit): su
// nodo Flow_TeleportPlayer es el mismo que usan las escenas, así que cuenta como puerta solo si más arriba en la
// misma llamada está PlayerInputHandler (el jugador la usó). Una expresión o un script también podrían venir de
// un diálogo o de una escena: esos se dejan como el juego si hay una ventana abierta o si el juego tiene tomado
// el control por una escena.
//
// Parches seguros (ver la lección de Crafting Queue): PlayerController.Teleport y UIBasicFade.FadeIn/FadeOut
// no leen campos estáticos de clases del juego. Solo se cambian argumentos; el juego sigue haciendo lo suyo.
internal static class QuickDoors
{
    private static bool active;        // puerta del jugador en curso: sus fundidos se acortan
    private static float activeUntil;  // por si algo falla: nunca más de 15 s
    internal static bool LastWasQuick; // para el log de la puerta
    internal static string LastWhy = ""; // qué teletransporte era, o por qué se dejó como el juego (para el log)
    internal static float LastTeleportAt = -100f; // cualquier teletransporte (la limpieza de después es de puerta, no de carga)

    // Quién llamó a Teleport, por nombre: si una versión del juego no tiene alguna de estas clases, ese caso
    // simplemente no se reconoce (se queda como el juego) en vez de fallar en cada teletransporte.
    private static readonly Type MapPage = AccessTools.TypeByName("MapPageWidget");
    private static readonly Type Expression = AccessTools.TypeByName("LazyExpression");
    private static readonly Type Fights = AccessTools.TypeByName("FightingGameController");
    private static readonly Type ScriptTeleport = AccessTools.TypeByName("GK2.FlowCanvasNodes.Flow_TeleportPlayer");
    private static readonly Type PlayerInput = AccessTools.TypeByName("PlayerInputHandler");

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
    internal static void DoorEnded()
    {
        active = false;
        pendingCleanup = null;
    }

    private static string pendingCleanup; // esta puerta pasa por negro para liberar recursos sin usar debajo (Cleanup.Request)

    private static void BeforeTeleport(TeleportDataBase teleportData)
    {
        LastTeleportAt = Time.realtimeSinceStartup;
        Cut.LastUsed = false;
        pendingCleanup = null;
        active = false;
        LastWasQuick = false;
        LastWhy = "";
        try
        {
            if (!Plugin.Enabled.Value || teleportData == null || teleportData.donNotFade || !PlayerInitiated(out LastWhy))
                return;
            active = true;
            activeUntil = Time.realtimeSinceStartup + 15f;
            LastWasQuick = true;
            WalkIn.CameThrough(teleportData.GetDestinationId());
            teleportData.delayInFade = Mathf.Min(teleportData.delayInFade, Mathf.Max(0f, Plugin.BlackPauseSeconds.Value));
            if (SafeSameScene(teleportData))
            {
                // Liberar recursos sin usar tarda 1 a 3 s y no puede hacerse a la vista: si toca (con corte, solo cuando ya pasó
                // mucho sin dormir ni cargar; sin corte, la puerta ya pasa por negro), esta puerta pasa por negro y va debajo.
                string due = Plugin.HardCut.Value ? Cleanup.EmergencyDue() : Cleanup.DueAtBlack();
                if (due != null)
                    pendingCleanup = due;
                // Corte directo: en la misma escena, sin negro; el juego lo mueve sin fundido y Cut tapa los cuadros del cambio.
                else if (Cut.Begin())
                    teleportData.donNotFade = true;
            }
        }
        catch (Exception e)
        {
            active = false;
            Plugin.Log.LogWarning("Quick doors: " + e.Message);
        }
    }

    // ¿El destino está en la escena de ahora? (a otra escena hay pantalla de carga: ahí sigue el negro). En su propio
    // método: si una versión del juego no tiene GetDestinationSceneData, falla solo esto y la puerta sigue con el negro.
    private static bool SafeSameScene(TeleportDataBase teleportData)
    {
        try
        {
            return SameScene(teleportData);
        }
        catch
        {
            return false;
        }
    }

    private static bool SameScene(TeleportDataBase teleportData)
    {
        string here = MainGame.PlayerData?.currentGameSceneId;
        string there = teleportData.GetDestinationSceneData()?.id;
        return !string.IsNullOrEmpty(here) && here == there;
    }

    // Un fundido del juego nunca debe fallar por culpa del mod: si algo sale mal, el fundido queda como el del juego.
    private static void ShortFadeIn(UIBasicFade __instance, ref float fadeTime, ref Action onComplete)
    {
        try
        {
            Shorten(__instance, ref fadeTime, last: false);
            HoldForCleanup(__instance, ref onComplete);
        }
        catch (Exception e)
        {
            active = false;
            Plugin.Log.LogDebug("Quick fades: " + e.Message);
        }
    }

    private static void ShortFadeOut(UIBasicFade __instance, ref float fadeTime)
    {
        try
        {
            Shorten(__instance, ref fadeTime, last: true);
        }
        catch (Exception e)
        {
            active = false;
            Plugin.Log.LogDebug("Quick fades: " + e.Message);
        }
    }

    // Una puerta que pasa por negro para liberar recursos: cuando la cortina termina de oscurecer, lo que el juego seguía haciendo
    // (mover al jugador, aclarar) espera a que la limpieza, ya con la pantalla negra, termine.
    private static void HoldForCleanup(UIBasicFade fade, ref Action onComplete)
    {
        if (pendingCleanup == null || !Active || !(fade is UIFade))
            return;
        string why = pendingCleanup;
        pendingCleanup = null;
        Action original = onComplete;
        onComplete = () => Cleanup.Request(why, "door", original);
    }

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

    // ¿Una puerta que usó el jugador, o un viaje desde el mapa? Se mira quién llamó a Teleport, del más cercano al más
    // lejano: el primero que se reconozca decide. `why` dice qué era (o por qué no), para el log de la puerta.
    private static bool PlayerInitiated(out string why)
    {
        why = "not a door or the map";
        StackFrame[] frames = new StackTrace(2, false).GetFrames();
        if (frames == null)
            return false;
        // Una expresión (puertas normales) o el nodo de teletransporte de un script (salidas como la de la torre) cuentan
        // como puerta solo si más arriba, en la misma llamada, está PlayerInputHandler: el jugador la usó. Si no, es el
        // juego moviéndolo por su cuenta (una misión, una escena) y se queda con los tiempos del juego.
        string needsInput = null;
        foreach (StackFrame frame in frames)
        {
            for (Type t = frame.GetMethod()?.DeclaringType; t != null; t = t.DeclaringType) // las lambdas viven en tipos anidados
            {
                if (needsInput != null)
                {
                    if (t == PlayerInput)
                        return NothingElseGoingOn(needsInput, out why);
                    if (t == typeof(WalkIn)) // el jugador caminó contra la puerta (WalkIn usa la puerta como la tecla)
                        return NothingElseGoingOn(needsInput + ", walked in", out why);
                    continue;
                }
                if (t == MapPage)
                {
                    why = "map";
                    return true;
                }
                if (t == Fights)
                {
                    why = "fight";
                    return false;
                }
                if (t == Expression)
                {
                    needsInput = "door";
                    why = "a game expression the player didn't start";
                    continue;
                }
                if (t == ScriptTeleport)
                {
                    needsInput = "scripted door";
                    why = "a script the player didn't start";
                    continue;
                }
                if (t.Namespace == "GK2.FlowCanvasNodes")
                {
                    why = "script " + t.Name; // peleas (Flow_StartFightById…) y escenas
                    return false;
                }
            }
        }
        return false;
    }

    // Una puerta se usa jugando: sin ventanas abiertas (un diálogo) y sin una escena con el control tomado.
    private static bool NothingElseGoingOn(string what, out string why)
    {
        if (LazyWindowsStackController.ActiveWindow != null)
        {
            why = what + " with a window open";
            return false;
        }
        PlayerController player = MainGame.Instance != null ? MainGame.PlayerController : null;
        if (player != null && !player.IsControlEnabledByType(TakenControlType.ByCinematics))
        {
            why = what + " during a cinematic";
            return false;
        }
        why = what;
        return true;
    }
}
