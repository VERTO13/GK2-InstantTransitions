using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

namespace InstantTransitions;

// Entrar caminando: si el objeto que el juego tiene enfocado (el del aviso "[E] Entrar") es una puerta y el jugador
// camina contra ella, se usa como con la tecla de interactuar, en el mismo orden que PlayerInputHandler.UpdateInput:
// primero sus eventos (TryCallFireEvent: hablar, rezar…; las puertas no tienen) y si no, la acción del objeto
// (IWGOInteractionHandler.Interact, que es la que mueve de lugar). QuickDoors cuenta esta llamada como del jugador.
//
// Puerta = su definición trae destinos de teletransporte (WGODef.teleportDestinationWgoIds), menos las estatuas del
// mapa (abren el mapa). "Caminar contra ella" = la entrada de movimiento del juego (LazyInput.GetDirection, la misma
// que mueve al personaje; cero sin tecla) sostenida PushSeconds mientras el personaje casi no avanza: la pared de la
// puerta lo frena. Pasar caminando al lado no cuenta (avanza), ni quedarse quieto frente a ella (sin tecla).
// La posición se mide en una ventana de tiempo y no por cuadro: el personaje se mueve en FixedUpdate.
internal static class WalkIn
{
    private static readonly FieldInfo InputHandlerField = AccessTools.Field(typeof(PlayerController), "playerInputHandler");
    private static readonly FieldInfo InteractionField = AccessTools.Field(typeof(PlayerInputHandler), "interactionComponent");
    private static readonly FieldInfo HandlerField = AccessTools.Field(typeof(PlayerInputHandler), "curInteractionHandler");
    private static readonly MethodInfo FireEvents = AccessTools.Method(typeof(PlayerInputHandler), "TryCallFireEvent");

    private const float PushSeconds = 0.15f; // empujar contra la puerta este tiempo…
    private const float StillUnder = 0.12f;  // …avanzando menos que esto (unidades del mundo)
    private const float AfterDoor = 0.8f;    // tras cruzar una puerta, esperar antes de otra (llegas junto a la de vuelta)

    private static Wgo pushing;
    private static float pushStart;
    private static Vector3 pushFrom;
    private static readonly HashSet<string> described = new HashSet<string>();
    private static bool broken;

    internal static void Tick()
    {
        if (broken || !Plugin.Enabled.Value || !Plugin.WalkIntoDoors.Value)
        {
            pushing = null;
            return;
        }
        PlayerController player = MainGame.Instance != null ? MainGame.PlayerController : null;
        if (player == null || MainGame.PlayerData == null || DoorWatch.InDoor
            || Time.realtimeSinceStartup - QuickDoors.LastTeleportAt < AfterDoor
            || LazyWindowsStackController.ActiveWindow != null
            || !player.IsControlEnabledByType(TakenControlType.ByCinematics)
            || !player.IsControlEnabledByType(TakenControlType.ByTeleport))
        {
            pushing = null;
            return;
        }
        if (InputHandlerField == null || InteractionField == null || HandlerField == null || FireEvents == null)
        {
            broken = true;
            Plugin.Log.LogWarning("Walk into doors: off, this version of the game is missing something it needs.");
            return;
        }
        object handler = InputHandlerField.GetValue(player);
        var interaction = handler != null ? InteractionField.GetValue(handler) as PlayerInteractionComponent : null;
        Wgo wgo = interaction != null && interaction.HasWgoUnderInteraction ? interaction.WgoUnderInteraction : null;
        if (wgo == null || !wgo.HasData || !IsDoor(wgo.Data))
        {
            pushing = null;
            return;
        }
        Vector3 pos = player.MovablePosition;
        float now = Time.unscaledTime;
        if (described.Add(wgo.Data.id ?? "?"))
            Plugin.Log.LogInfo($"[Walk-in] door in reach: {wgo.Data.id}, to {string.Join(", ", wgo.Data.Definition.teleportDestinationWgoIds)}; " +
                               $"you at {pos}, the door object at {wgo.transform.position}");
        bool pressing = LazyInput.GetDirection().sqrMagnitude > 0.04f;
        if (!pressing || wgo != pushing)
        {
            pushing = pressing ? wgo : null;
            pushStart = now;
            pushFrom = pos;
            return;
        }
        if (now - pushStart < PushSeconds)
            return;
        float moved = new Vector2(pos.x - pushFrom.x, pos.z - pushFrom.z).magnitude;
        if (moved > StillUnder)
        {
            pushStart = now; // todavía camina libre: se vuelve a medir desde aquí
            pushFrom = pos;
            return;
        }
        pushing = null;
        Use(handler, wgo, player);
    }

    // Lo mismo que hace la tecla de interactuar con un objeto (PlayerInputHandler.UpdateInput).
    private static void Use(object handler, Wgo wgo, PlayerController player)
    {
        IWGOInteractionHandler h = wgo.InteractionHandler;
        HandlerField.SetValue(handler, h);
        // El valor que pasa UpdateInput con la tecla de interactuar (su primera llamada: Interaction1 = 0).
        if (FireEvents.Invoke(handler, new[] { Enum.ToObject(FireEvents.GetParameters()[0].ParameterType, 0) }) is true)
        {
            Plugin.Log.LogInfo($"[Walk-in] walked into {wgo.Data.id}: its events ran");
            return;
        }
        bool used = h != null && ((IInteractionHandler)h).HasInteraction() && h.Interact(player);
        Plugin.Log.LogInfo($"[Walk-in] walked into {wgo.Data.id}: {(used ? "going through" : "the game didn't take it")}");
    }

    private static bool IsDoor(WgoData data)
    {
        WGODef def = data.Definition;
        return def != null && def.interactionType != WGODef.InteractionType.TeleportMilestone
               && def.teleportDestinationWgoIds != null && def.teleportDestinationWgoIds.Count > 0;
    }
}
