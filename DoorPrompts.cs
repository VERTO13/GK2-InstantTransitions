using System;
using System.Reflection;
using HarmonyLib;

namespace InstantTransitions;

// El aviso "[E] Entrar / Salir" sobre las puertas a las que se entra caminando: se oculta (HideDoorPrompts), para que
// la puerta se sienta como parte del mapa. Las trampillas y las escaleras de mano para escalar (que no llevan a otro
// lado) conservan el suyo, y la tecla sigue funcionando en todas. PromptsKey lo cambia jugando.
//
// El aviso sale de WGOInteractionHandlerBase.GetInteractionInfos: Wgo.GetWidgetData arma con esa lista el letrero y,
// si viene vacía, no lo dibuja. La tecla de interactuar no la usa (va por HasInteraction e Interact). Ese método no lee
// datos estáticos del juego (parcharlo no adelanta ningún constructor estático) y las puertas lo heredan sin cambiarlo.
internal static class DoorPrompts
{
    private static readonly MethodInfo DrawWidgets = AccessTools.Method(typeof(Wgo), "DrawWidgets");

    internal static void Apply(Harmony harmony)
    {
        MethodInfo target = AccessTools.Method(typeof(WGOInteractionHandlerBase), "GetInteractionInfos");
        if (target == null)
        {
            Plugin.Log.LogWarning("Door prompts: the game's method was not found (game update?); prompts stay as they are.");
            return;
        }
        harmony.Patch(target, postfix: new HarmonyMethod(typeof(DoorPrompts), nameof(HideOnDoors)));
    }

    private static void HideOnDoors(Wgo ___assignedWgo, InteractionInfos __result)
    {
        try
        {
            if (__result?.list == null || __result.list.Count == 0 || !Plugin.HideDoorPrompts.Value)
                return;
            if (___assignedWgo != null && ___assignedWgo.HasData && WalkIn.WalksInto(___assignedWgo.Data))
                __result.list.Clear();
        }
        catch (Exception e)
        {
            Plugin.Log.LogDebug("Door prompts: " + e.Message);
        }
    }

    // Tras cambiar el ajuste (o prender/apagar el mod): el letrero de la puerta enfocada se vuelve a armar en ese momento.
    internal static void Refresh()
    {
        try
        {
            Wgo focused = WalkIn.Focused();
            if (focused != null && DrawWidgets != null)
                DrawWidgets.Invoke(focused, null);
        }
        catch (Exception e)
        {
            Plugin.Log.LogDebug("Door prompts refresh: " + e.Message);
        }
    }
}
