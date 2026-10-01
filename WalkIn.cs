using System;
using System.Collections.Generic;
using System.Linq;
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
    internal static readonly FieldInfo WallsField = AccessTools.Field(typeof(PlayerPhysicalBody), "invisibleWalls");
    private static readonly FieldInfo PixelSizeField = AccessTools.Field(typeof(PlayerView), "pixelSizeForPosRounding");

    // Que la puerta no frene nunca, como un pasillo (lo pidió el usuario): se entra justo ANTES de chocar. Cada cuadro, una
    // esfera del tamaño del personaje se lanza hacia donde camina, lo que avanzaría en Lookahead segundos; si va a chocar y
    // la dirección apunta a la puerta enfocada, se cruza en ese cuadro y el jugador sigue caminando del otro lado.
    // Caminar cerca o de lado no cuenta (no va a chocar contra la puerta). Antes: entrar al acercarse derecho a menos de
    // 1.2 era "muy fácil y muy lejos"; entrar al chocar se sentía como un tope.
    // Respaldo: si la esfera no lo vio y algo ya lo frenó, se mira qué fue en cuanto se nota el freno (Blocked).
    internal const float Toward = 0.5f;         // coseno: la dirección apunta a la puerta (hasta 60° de lado)
    internal const float SideReach = 0.6f;      // o, muy cerca, la puerta delante y a menos de esto de lado
    // Segundos de camino que se miran por delante: ~2-3 cuadros, lo justo para no sentir el tope. Con 0.12 s (hasta 0.6
    // unidades) se sentía "como si entraras desde mucho antes al edificio".
    private const float Lookahead = 0.05f;
    private const float PushSeconds = 0.04f;   // respaldo: empujar contra la puerta este tiempo…
    private const float StillUnder = 0.03f;    // …avanzando menos que esto (unidades del mundo)

    private static Collider body;              // el cuerpo físico del personaje
    internal static int BodyLayer => body != null ? body.gameObject.layer : 0;
    private static Vector3 speedFrom;
    private static float speedAt, speed;       // rapidez reciente (unidades por segundo)
    private static float lastWalkSpeed = 4f;   // la última rapidez caminando libre
    // Tras cruzar, una pausa corta antes de otra puerta (contada desde que empezó la puerta; el corte dura ~0.07 s). Con
    // 0.8 s, salir y volver a entrar enseguida chocaba con la puerta. Al llegar se sigue caminando hacia el otro lado,
    // así que la puerta de vuelta no se toma sola.
    private const float AfterDoor = 0.3f;

    private static Wgo pushing;
    private static float pushStart;
    private static Vector3 pushFrom;
    private static readonly HashSet<string> described = new HashSet<string>();
    private static bool broken;
    private static float notDoorLoggedAt = -10f;

    // La caminata hasta la puerta. Frente a algunas puertas hay algo que el juego no deja pisar: en la taberna, un porche
    // de madera; las paredes invisibles del personaje (la orilla de lo caminable) lo frenan en su orilla, a 1 unidad de
    // la puerta ("los pies apenas tocan el tapete y ya me mete; no es lógico que desde ahí entre"). Ahí, solo el dibujo
    // del personaje sigue caminando hasta la puerta, a su paso (con la animación de caminar: el juego la pone mientras
    // haya tecla, aunque esté frenado), y al llegar se cruza. El cuerpo, la física y la partida no se mueven: el dibujo se
    // corre únicamente mientras las cámaras dibujan (Camera.onPreCull / onPostRender).
    // Si lo que lo frena es la puerta o su pared, ya llegó y se cruza en ese momento; si es otra cosa (un barril), no.
    internal const float WalkTo = 0.1f;         // el dibujo camina hasta quedar a esto del objeto puerta (en la taberna, la
                                               // pared de la puerta queda 0.25 más allá: los pies llegan al umbral)
    internal const float MaxWalk = 1.5f;        // más lejos que esto de la puerta, no se entra
    private const float SlowestWalk = 4f;      // unidades/s si no se vio caminar
    private const float LongestWalk = 1f;      // lo más que camina el dibujo
    private const float LetGo = 0.35f;         // si el cuerpo se mueve esto mientras el dibujo camina, se cancela
    // Tras cruzar por negro (otra escena), el dibujo se queda en la puerta hasta que la pantalla esté negra (y el cuerpo
    // se vaya), o este tiempo como mucho.
    private const float HoldAtDoor = 1f;
    private static bool approaching, hooked;
    private static float approachFrom, approachSeconds, holdUntil;
    private static Vector3 approachStart, approachDelta, viewOffset, viewSaved;
    private static Wgo approachDoor;
    private static object approachHandler;
    private static string approachHow;
    private static Transform viewMoved;

    internal static void Tick()
    {
        RestoreView(null); // por si una cámara no avisó al terminar de dibujar
        if (holdUntil > 0f && Time.unscaledTime > holdUntil)
        {
            holdUntil = 0f;
            viewOffset = Vector3.zero;
        }
        if (broken || !Plugin.Enabled.Value || !Plugin.WalkIntoDoors.Value)
        {
            pushing = null;
            StopApproach();
            return;
        }
        PlayerController player = MainGame.Instance != null ? MainGame.PlayerController : null;
        if (player == null || MainGame.PlayerData == null || DoorWatch.InDoor || Cut.Holding
            || Time.realtimeSinceStartup - QuickDoors.LastTeleportAt < AfterDoor
            || LazyWindowsStackController.ActiveWindow != null || InFight()
            || !player.IsControlEnabledByType(TakenControlType.ByCinematics)
            || !player.IsControlEnabledByType(TakenControlType.ByTeleport))
        {
            pushing = null;
            StopApproach();
            return;
        }
        if (InputHandlerField == null || InteractionField == null || HandlerField == null || FireEvents == null)
        {
            broken = true;
            Plugin.Log.LogWarning("Walk into doors: off, this version of the game is missing something it needs.");
            return;
        }
        Rearm(player.MovablePosition);
        if (!hooked)
        {
            hooked = true;
            Camera.onPreCull += ShiftView;
            Camera.onPostRender += RestoreView;
        }
        object handler = InputHandlerField.GetValue(player);
        var interaction = handler != null ? InteractionField.GetValue(handler) as PlayerInteractionComponent : null;
        Wgo wgo = interaction != null && interaction.HasWgoUnderInteraction ? interaction.WgoUnderInteraction : null;
        if (wgo == null || !wgo.HasData || !IsDoor(wgo.Data))
        {
            pushing = null;
            StopApproach();
            return;
        }
        Vector3 pos = player.MovablePosition;
        float now = Time.unscaledTime;
        if (now - speedAt >= 0.05f) // rapidez en ventanas de 0.05 s (el personaje se mueve en FixedUpdate)
        {
            float step = speedAt > 0f ? new Vector2(pos.x - speedFrom.x, pos.z - speedFrom.z).magnitude : 0f;
            speed = step > 3f ? 0f : step / (now - speedAt); // un salto de varias unidades fue una puerta, no un paso
            if (speed > 1f)
                lastWalkSpeed = speed;
            speedFrom = pos;
            speedAt = now;
        }
        if (described.Add(wgo.Data.id ?? "?"))
        {
            Plugin.Log.LogInfo($"[Walk-in] door in reach: {wgo.Data.id}, to {string.Join(", ", wgo.Data.Definition.teleportDestinationWgoIds)}; " +
                               $"you at {pos}, the door object at {wgo.transform.position}");
        }
        Vector2 input = LazyInput.GetDirection();
        if (input.sqrMagnitude <= 0.04f)
        {
            pushing = null; // sin tecla de movimiento: quedarse frente a la puerta no cuenta
            StopApproach();
            return;
        }
        if (wgo.Data.id == cameThrough)
        {
            pushing = null; // la puerta por la que acaba de llegar: no lo regresa mientras siga caminando igual
            StopApproach();
            if (!cameThroughLogged)
            {
                cameThroughLogged = true;
                Plugin.Log.LogInfo($"[Walk-in] {wgo.Data.id}: you just came through it; it takes you back once you let go of " +
                                   $"the movement key, turn around or walk {RearmAway:0.0} away");
            }
            return;
        }
        if (IsHatch(wgo))
        {
            pushing = null; // trampilla en el piso: solo con la tecla
            StopApproach();
            return;
        }
        Vector3 to = wgo.transform.position - pos;
        var flat = new Vector2(to.x, to.z);
        float dist = flat.magnitude;
        float dot = dist > 0.001f ? Vector2.Dot(input.normalized, flat / dist) : 1f;
        // De frente a la puerta: apuntando hacia ella (hasta 60°) o, ya muy cerca, con ella delante y a menos de SideReach
        // de lado. Arriba de las escaleras del cuartel, parado 0.4 a un lado de su punto y a 0.2 de él, el ángulo pasaba
        // de 60° y no salía.
        float along = Vector2.Dot(flat, input.normalized);
        float lateral = Mathf.Sqrt(Mathf.Max(0f, flat.sqrMagnitude - along * along));
        bool facing = dot >= Toward || (along > 0.05f && lateral < SideReach);
        // O parado casi sobre el punto de la puerta: en el cuartel se puede pisar un poco más allá de él (el primer
        // escalón) y la orilla frena con la puerta 0.2 a la espalda. Así solo cuenta si algo lo frena (la orilla o algo
        // sólido junto a la puerta), no al pasar.
        bool onItsSpot = !facing && along > -0.35f && lateral < SideReach && dist < 0.7f;
        if (!facing && !onItsSpot)
        {
            pushing = null; // pasando de lado o alejándose
            StopApproach();
            return;
        }
        if (approaching)
        {
            Approach(player, wgo, pos, now);
            return;
        }
        if (facing && AboutToHit(player, wgo, input, out float ahead, out string what))
        {
            pushing = null;
            Use(handler, wgo, player, $"reached the door ({ahead:0.00} before touching {what}; you {dist:0.0} from it, moving {speed:0.0}/s)");
            return;
        }
        // Va a llegar a la orilla de lo caminable: el dibujo sigue caminando sin detenerse ni un instante en ella.
        if (EdgeAhead(player, input, out float edge))
        {
            pushing = null;
            Blocked(handler, wgo, player, pos, input, $"reaching the edge of where you can walk ({edge:0.00} ahead) {dist:0.0} from the door, " +
                                                      $"direction {dot:0.00} toward it, moving {speed:0.0}/s");
            return;
        }
        if (wgo != pushing)
        {
            pushing = wgo;
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
        Blocked(handler, wgo, player, pos, input, $"stopped {dist:0.0} from the door, direction {dot:0.00} toward it");
    }

    // Algo lo frenó mientras camina hacia la puerta: ¿qué?
    private static void Blocked(object handler, Wgo wgo, PlayerController player, Vector3 pos, Vector2 input, string how)
    {
        string what = SolidAhead(player, wgo, input, PushedAtTheDoor, out bool atDoor);
        if (what != null)
        {
            if (atDoor)
                Use(handler, wgo, player, $"{how}, against {what}"); // la puerta o su pared: ya llegó
            else if (Time.unscaledTime - notDoorLoggedAt > 2f)
            {
                notDoorLoggedAt = Time.unscaledTime;
                Plugin.Log.LogInfo($"[Walk-in] {wgo.Data.id}: {how}, but against {what}, not the door: not going in");
            }
            return;
        }
        // Nada sólido enfrente: lo frena la orilla de lo caminable. El dibujo sigue caminando hacia donde camina el jugador
        // (no derecho al punto de la puerta: en el arco del laboratorio ese punto queda en diagonal y lo metía en la pared)
        // hasta la altura de la puerta y, si del otro lado sigue el paso (escaleras que bajan, como las del cuartel:
        // "tendría que hacerse casi tocando las escaleras que bajan"), hasta Past más, siguiendo la altura del piso. Lo
        // único que lo para es una pared a la altura de la puerta o más allá (en la taberna, el borde del porche); lo que hay
        // antes lo pasa por encima.
        Vector3 dir = new Vector3(input.x, 0f, input.y).normalized;
        Vector3 to = wgo.transform.position - pos;
        to.y = 0f;
        float dist = Mathf.Max(0f, Vector3.Dot(to, dir)); // hasta la altura de la puerta, en la dirección en que camina
        if (dist - WalkTo > MaxWalk)
        {
            if (Time.unscaledTime - notDoorLoggedAt > 2f)
            {
                notDoorLoggedAt = Time.unscaledTime;
                Plugin.Log.LogInfo($"[Walk-in] {wgo.Data.id}: {how}, too far to walk to it: not going in");
            }
            return;
        }
        // Como mucho LongestWalk: más allá, en lo oscuro, ya no se ve y solo retrasaba el corte (llegó a medio segundo).
        float walk = Mathf.Min(WallBeyond(player, dir, dist, dist + Past, out string wall), LongestWalk);
        if (walk < 0.1f)
        {
            Use(handler, wgo, player, how + ", at the door");
            return;
        }
        Vector3 end = pos + dir * walk;
        float dy = FloorAt(end, pos.y) - pos.y;
        approachStart = pos;
        approachDelta = dir * walk + Vector3.up * dy;
        how += $"; the door {dist:0.00} ahead, {(wall != null ? $"a wall {wall}" : $"open {Past:0.0} past it")}, floor {dy:+0.00;-0.00}";
        approachSeconds = walk / Mathf.Max(SlowestWalk, lastWalkSpeed);
        approachFrom = Time.unscaledTime;
        approachDoor = wgo;
        approachHandler = handler;
        approachHow = how + ", at the edge of where you can walk";
        approaching = true;
        holdUntil = 0f;
        viewOffset = Vector3.zero;
    }

    // Cada cuadro mientras el dibujo camina a la puerta; al llegar, se cruza.
    private static void Approach(PlayerController player, Wgo wgo, Vector3 pos, float now)
    {
        Vector3 moved = pos - approachStart;
        moved.y = 0f;
        if (wgo != approachDoor || moved.sqrMagnitude > LetGo * LetGo)
        {
            StopApproach();
            return;
        }
        // El dibujo va por su camino (desde donde empezó, a paso parejo) aunque el cuerpo todavía avance unos centímetros
        // hasta la orilla.
        float u = Mathf.Min(1f, (now - approachFrom) / approachSeconds);
        viewOffset = approachStart + approachDelta * u - pos;
        viewOffset.y = approachDelta.y * u; // baja (o sube) con el piso
        if (u < 1f)
            return;
        approaching = false; // la vista congelada del corte lo dibuja ya en la puerta
        bool used = Use(approachHandler, approachDoor, player, approachHow + $"; walked {approachDelta.magnitude:0.00} to it in {approachSeconds:0.00} s");
        // Con corte (misma escena) el juego ya lo movió y ShiftView deja de correr el dibujo (el cuerpo se fue). Con
        // negro (otra escena), el dibujo se queda en la puerta mientras llega el negro.
        holdUntil = used ? Time.unscaledTime + HoldAtDoor : 0f;
        if (!used)
            viewOffset = Vector3.zero;
    }

    // Escaleras que bajan: el dibujo sigue hasta esto más allá de la puerta, si no hay pared.
    private const float Past = 0.5f;

    // Cuánto puede caminar el dibujo hacia la puerta: hasta la primera pared (lo que frena de lado; pisos y escalones no)
    // o hasta reach, pero siempre al menos hasta WalkTo antes de la puerta (lo que hay antes, como el porche de la
    // taberna, lo pasa por encima).
    private static float WallBeyond(PlayerController player, Vector3 dir, float dist, float reach, out string wall)
    {
        wall = null;
        float stop = reach;
        if (Body(player, out PlayerPhysicalBody physical, out Bounds b, out float radius))
        {
            foreach (RaycastHit hit in Physics.SphereCastAll(b.center, radius, dir, reach, Solid(body.gameObject.layer), QueryTriggerInteraction.Ignore))
            {
                // Solo paredes (lo que frena de lado, no pisos ni escalones), a la altura de la puerta o más allá.
                if (hit.distance <= 0f || hit.distance < dist - 0.3f || Mine(hit.collider, player, physical) || Mathf.Abs(hit.normal.y) > 0.5f
                    || hit.distance >= stop)
                    continue;
                stop = hit.distance;
                wall = $"{stop - dist:+0.00;-0.00} from it ({hit.collider.name})";
            }
        }
        return Mathf.Max(dist - WalkTo, stop);
    }

    // La altura del piso o escalón en ese punto (capas Ground y Step, las que el juego cuenta como caminables); la de
    // ahora si no hay o queda muy lejos.
    internal static float FloorAt(Vector3 at, float y)
    {
        int mask = FloorMask();
        if (mask != 0 && Physics.Raycast(new Vector3(at.x, y + 1f, at.z), Vector3.down, out RaycastHit hit, 2.5f, mask, QueryTriggerInteraction.Ignore)
            && hit.point.y - y > -1.2f && hit.point.y - y < 0.4f)
            return hit.point.y;
        return y;
    }

    private static int floorMask = -1;

    internal static int FloorMask()
    {
        if (floorMask < 0)
        {
            int g = LayerMask.NameToLayer("Ground"), s = LayerMask.NameToLayer("Step");
            floorMask = (g >= 0 ? 1 << g : 0) | (s >= 0 ? 1 << s : 0);
        }
        return floorMask;
    }

    // Trampillas en el piso: solo con la tecla (lo pidió el usuario: "hay una justo en medio en la torre; si la
    // detectamos como puerta es como si siempre estuviera tratando de entrar en esa").
    private static readonly Dictionary<string, bool> hatches = new Dictionary<string, bool>();

    // En una pelea (preparándola o peleando) nada se toma solo: moviéndose cerca de una puerta se saldría sin querer.
    // Se lee el campo del juego y no FightingGameController.Instance, que crea el controlador si no existe.
    private static readonly FieldInfo FightField = AccessTools.Field(typeof(LazySingleton<FightingGameController>), "instance");

    // Solo la pelea en curso: el cuartel deja al juego "preparando pelea" todo el rato que uno está dentro (ahí se manejan
    // los guardias), y con eso no se podía salir caminando del cuartel.
    private static FightState lastFight = (FightState)(-1);

    internal static bool InFight()
    {
        var fight = FightField?.GetValue(null) as FightingGameController;
        FightState state = fight != null ? fight.CurrentFightState : FightState.Disabled;
        if (state != lastFight)
        {
            lastFight = state;
            Plugin.Log.LogInfo($"[Walk-in] fight state: {state}{(state == FightState.ActiveFight ? " (doors only with the key)" : "")}");
        }
        return state == FightState.ActiveFight;
    }

    private static bool IsHatch(Wgo door) => IsHatch(door.Data);

    private static bool IsHatch(WgoData door)
    {
        string id = door.id ?? "?";
        if (hatches.TryGetValue(id, out bool known))
            return known;
        bool isHatch = HatchByData(door, out string detail);
        hatches[id] = isHatch;
        Plugin.Log.LogInfo($"[Walk-in] {id}: {(isHatch ? "a hatch in the floor, only with the key" : "a door")} ({detail})");
        return isHatch;
    }

    // Una trampilla: se baja por ella y se regresa subiendo una escalera de mano. Su pareja (el otro lado) dice "Subir"
    // (ui_hint_climb) y ella no. Así quedan exactamente la del laboratorio de alquimia, la de la casa y la del patio; las
    // escaleras de mano (ellas mismas "Subir") y las puertas siguen entrando caminando. Antes se miraba el lugar (piso
    // libre alrededor), pero dependía de qué paredes estaban cargadas y confundía la taberna o la morgue.
    internal static bool HatchByData(WgoData data, out string why)
    {
        string own = data.Definition?.customInteraction?.hint;
        string to = data.Definition?.teleportDestinationWgoIds?.FirstOrDefault();
        string other = null;
        WorldData world = MainGame.Instance != null ? MainGame.Instance.GameSave?.worldData : null;
        if (to != null && world != null && world.TryGetWgoData(to, out WgoData partner, out _))
            other = partner?.Definition?.customInteraction?.hint;
        why = $"its hint '{own}', the other side's '{other}'";
        return own != "ui_hint_climb" && other == "ui_hint_climb";
    }

    // ¿Hay piso ahí, según el juego? Lo mismo que PlayerInvisibleWalls.IsRaycastHitWalkableGround: una esfera de 0.01
    // desde raycastYOffset arriba, hacia abajo edgeRaycastLength, contra las capas de piso (Ground y Step).
    private static float wallsYOffset = float.NaN, wallsRayLength;

    internal static bool Floor(Vector3 at, float y, PlayerPhysicalBody physical)
    {
        if (float.IsNaN(wallsYOffset))
        {
            wallsYOffset = 0.5f;
            wallsRayLength = 1f;
            if (WallsField?.GetValue(physical) is Component walls)
            {
                if (AccessTools.Field(walls.GetType(), "raycastYOffset")?.GetValue(walls) is float yo)
                    wallsYOffset = yo;
                if (AccessTools.Field(walls.GetType(), "edgeRaycastLength")?.GetValue(walls) is float len)
                    wallsRayLength = len;
            }
            Plugin.Log.LogInfo($"[Walk-in] floor test like the game's: {wallsRayLength:0.00} down from {wallsYOffset:0.00} up");
        }
        int mask = FloorMask();
        return mask != 0 && Physics.SphereCast(new Vector3(at.x, y + wallsYOffset, at.z), 0.01f, Vector3.down, out _, wallsRayLength, mask);
    }

    // La puerta por la que uno acaba de llegar no lo regresa mientras siga caminando igual: al entrar se puede seguir
    // caminando y, en escaleras de mano y alcantarillas, la de regreso queda justo enfrente ("el jugador puede que salga o
    // entre de nuevo sin querer"). Se vuelve a poder usar al soltar la tecla de caminar, al darse la vuelta o al alejarse
    // RearmAway del punto de llegada. Sin tiempo fijo: con 0.8 s, salir y volver a entrar enseguida se trababa.
    private const float RearmAway = 1.5f;
    private static string cameThrough;
    private static Vector2 cameThroughDir;
    private static Vector3 cameThroughAt;
    private static bool cameThroughArrived, cameThroughLogged;

    // QuickDoors, al cruzar una puerta del jugador (caminando o con la tecla): id del objeto en el que aparece.
    internal static void CameThrough(string arrivalDoorId)
    {
        cameThrough = arrivalDoorId;
        cameThroughDir = LazyInput.GetDirection().normalized;
        cameThroughArrived = false;
        cameThroughLogged = false;
    }

    private static void Rearm(Vector3 pos)
    {
        if (cameThrough == null)
            return;
        if (!cameThroughArrived)
        {
            cameThroughArrived = true; // primer cuadro ya del otro lado
            cameThroughAt = pos;
        }
        Vector2 input = LazyInput.GetDirection();
        if (input.sqrMagnitude <= 0.04f || Vector2.Dot(input.normalized, cameThroughDir) < 0f
            || new Vector2(pos.x - cameThroughAt.x, pos.z - cameThroughAt.z).sqrMagnitude > RearmAway * RearmAway)
            cameThrough = null;
    }

    private static void StopApproach()
    {
        approaching = false;
        if (holdUntil <= 0f)
            viewOffset = Vector3.zero;
    }

    // Antes de que cada cámara dibuje: el dibujo del personaje, corrido (en la cuadrícula de píxeles del juego, como
    // PlayerView.LateUpdate); después: de vuelta en su lugar. Solo mientras el cuerpo siga donde empezó la caminata.
    private static int pixelSize = -1;

    private static void ShiftView(Camera cam)
    {
        if (viewOffset == Vector3.zero || viewMoved != null || MainGame.Instance == null)
            return;
        PlayerController player = MainGame.PlayerController;
        PlayerView view = player != null ? player.View : null;
        if (view == null)
            return;
        Vector3 moved = player.MovablePosition - approachStart;
        moved.y = 0f;
        if (moved.sqrMagnitude > 0.25f)
        {
            viewOffset = Vector3.zero; // el juego ya lo movió (cruzó la puerta)
            holdUntil = 0f;
            return;
        }
        viewMoved = view.transform;
        viewSaved = viewMoved.position;
        Vector3 shifted = viewSaved + viewOffset;
        try
        {
            if (pixelSize < 0)
                pixelSize = PixelSizeField != null ? (int)PixelSizeField.GetValue(view) : 0;
            if (pixelSize > 0 && viewMoved.parent != null)
                shifted = VisualConsts.GetRoundedPosXYZ(viewMoved.parent.position + viewOffset, pixelSize, 1);
        }
        catch
        {
            pixelSize = 0; // sin redondeo
        }
        viewMoved.position = shifted;
    }

    private static void RestoreView(Camera cam)
    {
        if (viewMoved == null)
            return;
        viewMoved.position = viewSaved;
        viewMoved = null;
    }

    // Al descargar el mod (recarga en caliente): soltar las cámaras y dejar el dibujo en su lugar.
    internal static void Shutdown()
    {
        holdUntil = 0f;
        StopApproach();
        RestoreView(null);
        if (hooked)
        {
            Camera.onPreCull -= ShiftView;
            Camera.onPostRender -= RestoreView;
            hooked = false;
        }
    }

    // Solo cuenta chocar con la puerta misma: el punto del choque a menos de esto (XZ) del objeto puerta. Sin esto, los
    // escalones, un barril o la mesita junto a la taberna hacían entrar desde antes ("casi casi desde aquí").
    internal const float AtTheDoor = 1f;
    // Ya frenado y empujando hacia la puerta: un poco más de margen. En el arco de salida del laboratorio de alquimia el
    // marco del arco queda justo a 1.0 del punto de la puerta y, según dónde se parara uno, no salía.
    internal const float PushedAtTheDoor = 1.3f;

    // ¿Va a chocar con la puerta en los próximos Lookahead segundos? Una esfera del ancho del personaje, desde su centro,
    // hacia donde camina (en pantalla arriba es +Z y derecha +X). Solo cosas sólidas del mundo: las paredes invisibles
    // del personaje son la orilla de lo caminable, no la puerta (en la taberna lo frenan en la orilla del porche).
    private static bool AboutToHit(PlayerController player, Wgo door, Vector2 input, out float distance, out string what)
    {
        distance = 0f;
        what = null;
        if (!Body(player, out PlayerPhysicalBody physical, out Bounds b, out float radius))
            return false;
        Vector3 dir = new Vector3(input.x, 0f, input.y).normalized;
        float look = Mathf.Clamp(speed * Lookahead, 0.08f, 0.3f);
        int mask = Solid(body.gameObject.layer);
        Vector3 doorPos = door.transform.position;
        if (Physics.SphereCast(b.center, radius, dir, out RaycastHit hit, look, mask, QueryTriggerInteraction.Ignore)
            && !Mine(hit.collider, player, physical) && AtDoor(hit.point, doorPos))
        {
            distance = hit.distance;
            what = $"{Describe(hit.collider)} at {Flat(hit.point - doorPos):0.0} from the door object";
            return true;
        }
        // Si la esfera ya empieza tocando algo (llegaste pegado a la puerta y te diste la vuelta), el lanzamiento no lo
        // cuenta: se mira si un poco más adelante, hacia donde camina, ya está tocando la puerta.
        string touching = SolidAhead(player, door, input, AtTheDoor, out bool atDoor);
        if (touching == null || !atDoor)
            return false;
        what = "touching " + touching;
        return true;
    }

    // ¿Va a chocar con sus paredes invisibles (la orilla de lo caminable) en los próximos Lookahead segundos?
    private static bool EdgeAhead(PlayerController player, Vector2 input, out float distance)
    {
        distance = 0f;
        int walls = LayerMask.NameToLayer("PlayerInvisibleWalls");
        if (walls < 0 || !Body(player, out _, out Bounds b, out float radius))
            return false;
        Vector3 dir = new Vector3(input.x, 0f, input.y).normalized;
        float look = Mathf.Clamp(speed * Lookahead, 0.08f, 0.3f);
        if (!Physics.SphereCast(b.center, radius, dir, out RaycastHit hit, look, 1 << walls, QueryTriggerInteraction.Ignore))
            return false;
        distance = hit.distance;
        return true;
    }

    // Lo sólido (no las paredes invisibles ni lo propio) que toca el personaje un poco más adelante, hacia donde camina;
    // null si nada. atDoor: el punto de contacto está junto a la puerta.
    private static string SolidAhead(PlayerController player, Wgo door, Vector2 input, float within, out bool atDoor)
    {
        atDoor = false;
        if (!Body(player, out PlayerPhysicalBody physical, out Bounds b, out float radius))
            return null;
        Vector3 dir = new Vector3(input.x, 0f, input.y).normalized;
        Vector3 ahead = b.center + dir * 0.08f;
        Vector3 doorPos = door.transform.position;
        string found = null;
        foreach (Collider c in Physics.OverlapSphere(ahead, radius, Solid(body.gameObject.layer), QueryTriggerInteraction.Ignore))
        {
            if (Mine(c, player, physical))
                continue;
            Vector3 contact = c.ClosestPoint(ahead);
            string it = $"{Describe(c)} at {Flat(contact - doorPos):0.0} from the door object";
            if (Flat(contact - doorPos) < within)
            {
                atDoor = true;
                return it;
            }
            found ??= it;
        }
        return found;
    }

    internal static bool Body(PlayerController player, out PlayerPhysicalBody physical, out Bounds b, out float radius)
    {
        // El cuerpo físico del personaje es otro objeto (PlayerController.PhysicalBody, "PhysicalBody"), no un hijo del
        // personaje: sin excluirlo, la esfera se tocaba a sí misma y entraba en cuanto la puerta se enfocaba.
        physical = player.PhysicalBody;
        b = default;
        radius = 0f;
        if (physical == null)
            return false;
        if (body == null || !body.enabled)
            body = physical.GetComponentsInChildren<Collider>().FirstOrDefault(c => c.enabled && !c.isTrigger);
        if (body == null)
            return false;
        b = body.bounds;
        radius = Mathf.Max(0.05f, Mathf.Min(b.extents.x, b.extents.z) * 0.9f);
        return true;
    }

    internal static bool Mine(Collider c, PlayerController player, PlayerPhysicalBody physical) =>
        c.transform.IsChildOf(physical.transform) || c.transform.IsChildOf(player.transform)
        || (c.attachedRigidbody != null && c.attachedRigidbody.transform.IsChildOf(physical.transform));

    private static bool AtDoor(Vector3 p, Vector3 doorPos) => Flat(p - doorPos) < AtTheDoor;

    private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

    // Las capas que de verdad pueden frenar al personaje, según la tabla de choques del juego, menos sus paredes
    // invisibles. Sin esto, la esfera tocaba el colisionador del sonido de la cámara (capa SoundZone, una caja de
    // 8 x 84 x 66 que envuelve todo) y se entraba en cuanto la puerta quedaba a una unidad.
    private static int solidFor = -1, solidMask;

    internal static int Solid(int layer)
    {
        if (layer != solidFor)
        {
            int walls = LayerMask.NameToLayer("PlayerInvisibleWalls");
            solidMask = 0;
            for (int i = 0; i < 32; i++)
                if (i != layer && i != walls && !Physics.GetIgnoreLayerCollision(layer, i))
                    solidMask |= 1 << i;
            solidFor = layer;
            var names = new List<string>();
            for (int i = 0; i < 32; i++)
                if ((solidMask & (1 << i)) != 0)
                    names.Add(string.IsNullOrEmpty(LayerMask.LayerToName(i)) ? i.ToString() : LayerMask.LayerToName(i));
            Plugin.Log.LogInfo($"[Walk-in] the player ({LayerMask.LayerToName(layer)}) is stopped by: {string.Join(", ", names)}; " +
                               $"and the edge of where it can walk ({(walls >= 0 ? "PlayerInvisibleWalls" : "no such layer")})");
        }
        return solidMask;
    }

    // Para el log: qué es ese colisionador (ruta, capa, tamaño, cuerpo rígido).
    private static string Describe(Collider c)
    {
        if (c == null)
            return "nothing";
        var names = new List<string>();
        for (Transform t = c.transform; t != null && names.Count < 4; t = t.parent)
            names.Add(t.name);
        names.Reverse();
        return $"[{string.Join("/", names)}, layer {LayerMask.LayerToName(c.gameObject.layer)}, {c.GetType().Name} size {c.bounds.size}, " +
               $"rigidbody {(c.attachedRigidbody != null ? c.attachedRigidbody.name : "none")}]";
    }

    // Lo mismo que hace la tecla de interactuar con un objeto (PlayerInputHandler.UpdateInput). true: el juego la tomó.
    private static bool Use(object handler, Wgo wgo, PlayerController player, string how)
    {
        Plugin.Log.LogInfo($"[Walk-in] {wgo.Data.id}: {how}");
        IWGOInteractionHandler h = wgo.InteractionHandler;
        HandlerField.SetValue(handler, h);
        // El valor que pasa UpdateInput con la tecla de interactuar (su primera llamada: Interaction1 = 0).
        if (FireEvents.Invoke(handler, new[] { Enum.ToObject(FireEvents.GetParameters()[0].ParameterType, 0) }) is true)
        {
            Plugin.Log.LogInfo($"[Walk-in] walked into {wgo.Data.id}: its events ran");
            return true;
        }
        bool used = h != null && ((IInteractionHandler)h).HasInteraction() && h.Interact(player);
        Plugin.Log.LogInfo($"[Walk-in] walked into {wgo.Data.id}: {(used ? "going through" : "the game didn't take it")}");
        return used;
    }

    // DoorPrompts: ¿a esta se entra caminando ahora? (puerta, no trampilla, el mod y la opción prendidos, sin pelea)
    internal static bool WalksInto(WgoData data) =>
        Plugin.Enabled.Value && Plugin.WalkIntoDoors.Value && !broken && IsDoor(data) && !IsHatch(data) && !InFight();

    // El objeto que el juego tiene enfocado (el del aviso), o null.
    internal static Wgo Focused()
    {
        PlayerController player = MainGame.Instance != null ? MainGame.PlayerController : null;
        object handler = player != null ? InputHandlerField?.GetValue(player) : null;
        var interaction = handler != null ? InteractionField?.GetValue(handler) as PlayerInteractionComponent : null;
        return interaction != null && interaction.HasWgoUnderInteraction ? interaction.WgoUnderInteraction : null;
    }

    internal static bool IsDoor(WgoData data)
    {
        WGODef def = data.Definition;
        return def != null && def.interactionType != WGODef.InteractionType.TeleportMilestone
               && def.teleportDestinationWgoIds != null && def.teleportDestinationWgoIds.Count > 0;
    }
}
