using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace InstantTransitions;

// Revisión de todas las puertas sin ir a ellas (prueba). Por turnos, el lugar de cada puerta se muestra fuera de cámara
// (la marca de la precarga, con otra clave) y se simula al personaje acercándose desde cada dirección, con su cuerpo, la
// física y la orilla de piso del juego (PlayerInvisibleWalls: el piso se mira un poco adelante en cada dirección). Se
// anota dónde se frenaría y si entraría caminando, con las mismas reglas que WalkIn.
internal static class DoorCheck
{
    private static readonly ChunkingIgnoreType Key = (ChunkingIgnoreType)4; // la precarga usa 3
    private enum Stage { Waiting, Showing, Loading, Done }

    private static Stage stage;
    private static List<Wgo> doors;
    private static int index, frames;
    private static float stageAt;
    private static readonly List<IChunkableObject> shown = new List<IChunkableObject>();
    private static readonly List<string> problems = new List<string>();
    private static int checkedDoors, hatches;

    // Dónde mira el piso el juego, por dirección (PlayerInvisibleWalls.edgeCheckDistance*).
    private static float ex = 0.75f, ezp = 0.7f, ezm = 0.6f, edx = 0.6f, edzp = 0.6f, edzm = 0.6f;

    // Medidas del cuerpo (PlayerPhysicalBody: 0.50 x 1.07 x 0.30, su centro 0.54 arriba de los pies y 0.05 adelante).
    private static readonly Vector3 BodyHalf = new Vector3(0.25f, 0.53f, 0.15f);
    private static readonly Vector3 BodyCenter = new Vector3(0f, 0.54f, 0.05f);
    private const float Step = 0.05f;
    private const float InReach = 1.8f; // el juego enfoca la puerta (el aviso "[E]") a esta distancia, más o menos

    // Con Simulate apagado solo se anota qué puertas son trampillas (datos, al instante); prendido, además se cargan los
    // lugares y se simulan los acercamientos (unos 13 s con tirones).
    private static readonly bool Simulate = false;

    internal static void Tick()
    {
        if (stage == Stage.Done)
            return;
        if (!Simulate)
        {
            ListHatches();
            return;
        }
        try
        {
            Next();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[Check] stopped: " + e);
            Hide();
            stage = Stage.Done;
        }
    }

    private static void Next()
    {
        PlayerController player = MainGame.Instance != null ? MainGame.PlayerController : null;
        if (player == null || MainGame.PlayerData == null || DoorWatch.InDoor || Cut.Holding)
            return;
        switch (stage)
        {
            case Stage.Waiting:
                doors = UnityEngine.Object.FindObjectsByType<Wgo>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Where(w => w != null && w.HasData && WalkIn.IsDoor(w.Data)).OrderBy(w => w.Data.id).ToList();
                ReadEdges(player);
                Plugin.Log.LogInfo($"[Check] checking {doors.Count} doors, one by one (floor looked at {ex:0.00} sideways, {ezp:0.00} up, " +
                                   $"{ezm:0.00} down, diagonals {edx:0.00}/{edzp:0.00}/{edzm:0.00})");
                index = 0;
                stage = Stage.Showing;
                return;
            case Stage.Showing:
                if (index >= doors.Count)
                {
                    Plugin.Log.LogInfo($"[Check] done: {checkedDoors} doors, {hatches} hatches (key only), {problems.Count} to look at" +
                                       (problems.Count > 0 ? ":\n   " + string.Join("\n   ", problems) : ""));
                    stage = Stage.Done;
                    return;
                }
                Wgo door = doors[index];
                if (door == null || !door.HasData)
                {
                    index++;
                    return;
                }
                shown.Clear();
                DoorPrewarm.ObjectsIn(new Bounds(door.transform.position, new Vector3(10f, 30f, 10f)), shown, true);
                foreach (IChunkableObject o in shown)
                    ChunkedObjectUtility.UpdateFlag(o, Key, true);
                stageAt = Time.unscaledTime;
                frames = 0;
                stage = Stage.Loading;
                return;
            case Stage.Loading:
                frames++;
                bool busy = WgoPartLoadManager.Instance.HasActiveRequests || WsoOptimizedStagesLoadManager.Instance.HasActiveRequests
                            || WsoConstructorPartsLoadManager.Instance.HasActiveRequests;
                if (frames < 3 || (busy && Time.unscaledTime - stageAt < 3f))
                    return;
                // Las piezas horneadas no respetan la marca: la cámara las vuelve a esconder. Se muestran justo antes de mirar.
                foreach (IChunkableObject o in shown)
                    if (o is BakedChunkableObjectComponentData)
                        o.UpdateChunkVisibility(true);
                Physics.SyncTransforms();
                Analyze(doors[index], player);
                Hide();
                index++;
                stage = Stage.Showing;
                return;
        }
    }

    private static void ListHatches()
    {
        if (MainGame.Instance == null || MainGame.PlayerData == null || MainGame.PlayerController == null)
            return;
        stage = Stage.Done;
        var all = UnityEngine.Object.FindObjectsByType<Wgo>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(w => w != null && w.HasData && WalkIn.IsDoor(w.Data)).OrderBy(w => w.Data.id).ToList();
        var key = all.Where(w => WalkIn.HatchByData(w.Data, out _)).Select(w => w.Data.id).ToList();
        Plugin.Log.LogInfo($"[Check] {all.Count} doors; {key.Count} hatches, only with the key: {string.Join(", ", key)}");
        // En qué zona queda cada puerta de los exploradores (el usuario no sabe cuál es).
        int zones = LayerMask.NameToLayer("WorldZone");
        foreach (Wgo w in all.Where(w => w.Data.id.Contains("scout")))
        {
            var names = Physics.OverlapSphere(w.transform.position, 0.3f, zones >= 0 ? 1 << zones : ~0, QueryTriggerInteraction.Collide)
                .Select(c => c.transform.parent != null ? c.transform.parent.name : c.name).Distinct();
            string to = w.Data.Definition.teleportDestinationWgoIds.FirstOrDefault();
            WorldData world = MainGame.Instance.GameSave?.worldData;
            string where = world != null && to != null && world.TryGetWgoData(to, out WgoData d, out GameSceneData scene)
                ? $"{to} exists, scene {scene?.id}, at {d?.GetTeleportPointPosition()}" : $"{to} not in the world data";
            Plugin.Log.LogInfo($"[Check] {w.Data.id} at {w.transform.position}: zone {string.Join(", ", names)}; leads to {where}; " +
                               $"this scene {MainGame.PlayerData.currentGameSceneId}");
        }
    }

    private static void Hide()
    {
        foreach (IChunkableObject o in shown)
        {
            try
            {
                if (!(o is UnityEngine.Object u && u == null))
                    ChunkedObjectUtility.UpdateFlag(o, Key, false);
            }
            catch
            {
                // ya no está: nada que esconder
            }
        }
        shown.Clear();
    }

    internal static void Shutdown() => Hide();

    private static void ReadEdges(PlayerController player)
    {
        if (!(WalkIn.WallsField?.GetValue(player.PhysicalBody) is Component walls))
            return;
        float F(string name, float fallback) => AccessTools.Field(walls.GetType(), name)?.GetValue(walls) is float v ? v : fallback;
        ex = F("edgeCheckDistanceX", ex);
        ezp = F("edgeCheckDistanceZPlus", ezp);
        ezm = F("edgeCheckDistanceZMinus", ezm);
        edx = F("edgeCheckDistanceDiagonalX", edx);
        edzp = F("edgeCheckDistanceDiagonalZPlus", edzp);
        edzm = F("edgeCheckDistanceDiagonalZMinus", edzm);
    }

    private static readonly (string name, Vector2 dir)[] Directions =
    {
        ("down", new Vector2(0f, -1f)), ("up", new Vector2(0f, 1f)), ("left", new Vector2(-1f, 0f)), ("right", new Vector2(1f, 0f)),
        ("down-left", new Vector2(-1f, -1f)), ("down-right", new Vector2(1f, -1f)), ("up-left", new Vector2(-1f, 1f)), ("up-right", new Vector2(1f, 1f)),
    };

    private static void Analyze(Wgo door, PlayerController player)
    {
        if (!WalkIn.Body(player, out PlayerPhysicalBody physical, out _, out _))
            return;
        string id = door.Data.id;
        Vector3 p = door.transform.position;
        checkedDoors++;
        int solids = Physics.OverlapSphere(p + Vector3.up * 0.6f, 3f, WalkIn.Solid(WalkIn.BodyLayer), QueryTriggerInteraction.Ignore)
            .Count(c => !WalkIn.Mine(c, player, physical));
        if (WalkIn.HatchByData(door.Data, out string hatchWhy))
        {
            hatches++;
            Plugin.Log.LogInfo($"[Check] {id}: hatch, key only ({hatchWhy}; {solids} solid things within 3)");
            return;
        }
        var parts = new List<string>();
        int reach = 0, enter = 0;
        var failed = new List<string>();
        foreach ((string name, Vector2 dir) in Directions)
        {
            string best = null;
            bool bestEnters = false, any = false;
            foreach (float off in new[] { 0f, -0.25f, 0.25f })
            {
                string r = Approach(p, dir.normalized, off, player, physical, out bool reached, out bool enters);
                if (!reached)
                    continue;
                any = true;
                if (best == null || (enters && !bestEnters))
                {
                    best = r;
                    bestEnters = enters;
                }
            }
            if (!any)
                continue;
            reach++;
            if (bestEnters)
                enter++;
            else
                failed.Add($"{name}: {best}");
            parts.Add($"{name}: {(bestEnters ? "enters" : "NO")} ({best})");
        }
        Plugin.Log.LogInfo($"[Check] {id}: door ({hatchWhy}; {solids} solid things within 3)\n      " +
                           (parts.Count > 0 ? string.Join("\n      ", parts) : "can't get near it from any side"));
        if (solids == 0)
            problems.Add($"{id}: nothing solid loaded around it, the check can't tell");
        else if (reach == 0)
            problems.Add($"{id}: the check couldn't get near it from any side");
        else if (enter == 0)
            problems.Add($"{id}: no side enters ({string.Join("; ", failed)})");
        else if (failed.Count > 0)
            problems.Add($"{id}: enters from {enter} of {reach} sides; not from {string.Join("; ", failed)}");
    }

    // Un acercamiento: desde 2.5 antes de la puerta (corrido off a un lado), caminando en dir hasta que algo lo frene.
    private static string Approach(Vector3 p, Vector2 dir, float off, PlayerController player, PlayerPhysicalBody physical,
                                   out bool reached, out bool enters)
    {
        reached = enters = false;
        Vector3 d = new Vector3(dir.x, 0f, dir.y);
        Vector3 side = new Vector3(-d.z, 0f, d.x);
        Vector3 pos = p - d * 2.5f + side * off;
        pos.y = WalkIn.FloorAt(pos, p.y);
        if (!WalkIn.Floor(pos, pos.y, physical) || Blocking(pos, player, physical) != null)
            return null; // ahí no se puede estar parado
        Collider wall = null;
        bool edge = false;
        for (int i = 0; i < 120; i++)
        {
            if (!EdgeAllows(pos, dir, physical))
            {
                edge = true;
                break;
            }
            Vector3 next = pos + d * Step;
            next.y = WalkIn.FloorAt(next, pos.y);
            wall = Blocking(next, player, physical);
            if (wall != null)
                break;
            pos = next;
        }
        Vector3 to = p - pos;
        to.y = 0f;
        var flat = new Vector2(to.x, to.z);
        float dist = flat.magnitude;
        if (dist > InReach)
            return null; // no llega a enfocarla por este lado
        reached = true;
        float dot = dist > 0.001f ? Vector2.Dot(dir, flat / dist) : 1f;
        float along = Vector2.Dot(flat, dir);
        float lateral = Mathf.Sqrt(Mathf.Max(0f, flat.sqrMagnitude - along * along));
        bool facing = dot >= WalkIn.Toward || (along > 0.05f && lateral < WalkIn.SideReach);
        bool onItsSpot = !facing && along > -0.35f && lateral < WalkIn.SideReach && dist < 0.7f;
        string where = $"stops {dist:0.00} from it, {along:+0.00;-0.00} ahead, {lateral:0.00} aside";
        if (wall != null)
        {
            Vector3 center = pos + d * Step + BodyCenter;
            Vector3 contact = wall.ClosestPoint(center);
            float atDoor = new Vector2(contact.x - p.x, contact.z - p.z).magnitude;
            enters = (facing || onItsSpot) && atDoor < WalkIn.PushedAtTheDoor;
            return $"{where}, against {wall.name} [{LayerMask.LayerToName(wall.gameObject.layer)}] {atDoor:0.00} from the door" +
                   (!facing && !onItsSpot ? ", the door off to the side" : "");
        }
        if (edge)
        {
            if (!facing && !onItsSpot)
                return $"{where}, at the edge of the floor, the door off to the side";
            float walk = Mathf.Max(0f, along) - WalkIn.WalkTo;
            enters = walk <= WalkIn.MaxWalk;
            return $"{where}, at the edge of the floor{(enters ? "" : ", too far to walk to it")}";
        }
        return $"{where}, walks on without stopping";
    }

    // Lo sólido que tocaría el cuerpo parado ahí (no lo propio).
    private static Collider Blocking(Vector3 feet, PlayerController player, PlayerPhysicalBody physical)
    {
        foreach (Collider c in Physics.OverlapBox(feet + BodyCenter, BodyHalf, Quaternion.identity, WalkIn.Solid(WalkIn.BodyLayer),
                     QueryTriggerInteraction.Ignore))
            if (!WalkIn.Mine(c, player, physical))
                return c;
        return null;
    }

    // ¿El juego lo deja seguir en esa dirección? Sus paredes invisibles se prenden si no hay piso un poco adelante.
    private static bool EdgeAllows(Vector3 pos, Vector2 dir, PlayerPhysicalBody physical)
    {
        bool Ok(float x, float z) => WalkIn.Floor(pos + new Vector3(x, 0f, z), pos.y, physical);
        float sx = Mathf.Sign(dir.x), sz = Mathf.Sign(dir.y);
        bool movesX = Mathf.Abs(dir.x) > 0.1f, movesZ = Mathf.Abs(dir.y) > 0.1f;
        if (movesX && !Ok(sx * ex, 0f))
            return false;
        if (movesZ && !Ok(0f, sz > 0 ? ezp : -ezm))
            return false;
        if (movesX && movesZ && !Ok(sx * edx, sz > 0 ? edzp : -edzm))
            return false;
        return true;
    }
}
