using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace InstantTransitions;

// Precarga del otro lado de la puerta, para que el corte no se trabe. Al cruzar, el juego no devuelve el control hasta
// cargar las piezas de lo que hay del otro lado (PlayerController.Teleport espera a WsoConstructorPartsLoadManager,
// WsoOptimizedStagesLoadManager y WgoPartLoadManager); la primera vez en un rato eso tarda ~0.4 s y el corte deja la vista
// de antes congelada todo ese tiempo ("hay veces que se traba la transición"). Las veces siguientes es al instante: al
// esconderse un objeto, sus piezas vuelven a un depósito (Wgo.ReleaseWgoPartsToPool) y de ahí salen enseguida.
//
// Así que al acercarse a una puerta, lo que se ve del otro lado se muestra un momento fuera de cámara, con la marca con la
// que el modo construcción muestra una zona entera (ChunkedObjectUtility.UpdateFlag; aquí con una clave propia), hasta que
// sus piezas cargan; luego se esconde y sus piezas quedan en el depósito. Por partes en varios cuadros, para que caminar
// no se trabe. Solo puertas a la misma escena (las del corte); el mismo lugar, como mucho cada Again segundos.
internal static class DoorPrewarm
{
    private const float Near = 5f;              // a esta distancia de una puerta empieza (~1 s caminando)
    private const float LookEvery = 0.2f;
    private const float Again = 300f;
    private const float MaxLoading = 4f;        // espera máxima a que carguen (s)
    private const float FarAway = 60f;          // destinos más cerca que esto no se precargan
    private const double ShowBudgetMs = 1.5;    // por cuadro, para mostrar
    private const int HidePerFrame = 20;        // por cuadro, para esconder (lo hace la cámara al cuadro siguiente)
    private static readonly ChunkingIgnoreType Key = (ChunkingIgnoreType)3; // las del juego: Building 0, Fighting 1, Animation 2
    // Lo que se ve alrededor del punto de llegada (cámara ortográfica de 6.75 inclinada 53°: unas 24 x 17 unidades del
    // suelo), con margen, y un poco más hacia el fondo por lo alto que asoma desde atrás.
    private static readonly Vector3 Around = new Vector3(30f, 40f, 26f);
    private static readonly Vector3 AroundShift = new Vector3(0f, 0f, 2f);

    private enum Stage { Idle, Showing, Loading, Hiding }

    private static Stage stage;
    private static readonly Queue<IChunkableObject> toShow = new Queue<IChunkableObject>();
    private static readonly List<IChunkableObject> shown = new List<IChunkableObject>();
    private static readonly Dictionary<string, float> doneAt = new Dictionary<string, float>();
    private static readonly Collider[] hits = new Collider[32];
    private static int hideIndex, frames, interactable = -2;
    private static float lookAt, startedAt, loadingFrom, loadSeconds;
    private static double queryMs, showMs;
    private static bool timedOut, broken;
    private static string door, place;

    // Cada cuadro (DoorWatch.Update).
    internal static void Tick()
    {
        if (broken)
            return;
        try
        {
            switch (stage)
            {
                case Stage.Idle:
                    Look();
                    break;
                case Stage.Showing:
                    Show();
                    break;
                case Stage.Loading:
                    WaitForLoads();
                    break;
                case Stage.Hiding:
                    Hide();
                    break;
            }
        }
        catch (Exception e)
        {
            broken = true;
            Plugin.Log.LogWarning("[Prewarm] off for this session: " + e);
            Shutdown();
        }
    }

    // ¿Hay una puerta cerca cuyo otro lado no se haya precargado hace poco?
    private static void Look()
    {
        float now = Time.unscaledTime;
        if (now < lookAt || !Plugin.Enabled.Value || !Plugin.HardCut.Value)
            return;
        lookAt = now + LookEvery;
        PlayerController player = MainGame.Instance != null ? MainGame.PlayerController : null;
        if (player == null || MainGame.PlayerData == null || DoorWatch.InDoor || Cut.Holding || WalkIn.InFight())
            return;
        if (interactable == -2)
            interactable = LayerMask.NameToLayer("Interactable");
        if (interactable < 0)
            return;
        Vector3 pos = player.MovablePosition;
        int n = Physics.OverlapSphereNonAlloc(pos, Near, hits, 1 << interactable, QueryTriggerInteraction.Collide);
        Wgo best = null;
        float bestDist = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            Wgo w = hits[i] != null ? hits[i].GetComponentInParent<Wgo>() : null;
            if (w == null || !w.HasData || !WalkIn.IsDoor(w.Data))
                continue;
            Vector3 d = w.transform.position - pos;
            float dist = new Vector2(d.x, d.z).magnitude;
            if (dist < bestDist)
            {
                best = w;
                bestDist = dist;
            }
        }
        if (best == null)
            return;
        string to = best.Data.Definition.teleportDestinationWgoIds[0];
        if (doneAt.TryGetValue(to, out float at) && now - at < Again)
            return;
        if (!Destination(to, out Vector3 dest))
        {
            doneAt[to] = float.MaxValue / 2f; // a otra escena (va por negro) o sin datos: nunca
            return;
        }
        // Un destino a la vista (o casi) ya está cargado, y mostrar lo de alrededor se vería: solo lugares lejanos (los
        // interiores están a cientos de unidades).
        if ((dest - pos).magnitude < FarAway)
        {
            doneAt[to] = float.MaxValue / 2f;
            return;
        }
        var sw = Stopwatch.StartNew();
        toShow.Clear();
        shown.Clear();
        Collect(new Bounds(dest + AroundShift, Around));
        queryMs = sw.Elapsed.TotalMilliseconds;
        door = best.Data.id;
        place = to;
        startedAt = now;
        frames = 0;
        showMs = 0;
        stage = Stage.Showing;
    }

    // Los objetos con piezas (Wgo, Wso) dentro de la caja. Solo se recorren los pedazos del mapa (chunks) que la tocan:
    // ChunkManager.GetAllChunkableObjectsInBounds recorre todos los objetos del mundo y tardaba 22 ms en un cuadro.
    private static readonly FieldInfo LayersField = AccessTools.Field(typeof(ChunkManager), "layers");
    private static readonly HashSet<IChunkableObject> seen = new HashSet<IChunkableObject>();

    private static void Collect(Bounds box)
    {
        var found = new List<IChunkableObject>();
        ObjectsIn(box, found, false);
        foreach (IChunkableObject o in found)
            toShow.Enqueue(o);
    }

    // Los objetos con piezas (Wgo, Wso) dentro de la caja; con baked, también las piezas fijas horneadas (muros, cercas:
    // BakedChunkableObjectComponentData), que no respetan la marca pero se muestran al ponerla.
    internal static void ObjectsIn(Bounds box, ICollection<IChunkableObject> into, bool baked)
    {
        var area = new BurstableBounds { center = box.center, size = box.size };
        seen.Clear();
        foreach (ChunkManagerLayer layer in (List<ChunkManagerLayer>)LayersField.GetValue(ChunkManager.Instance))
        {
            Chunk[] chunks = layer?.chunks;
            int count = chunks != null ? Math.Min(layer.Count, chunks.Length) : 0;
            for (int i = 0; i < count; i++)
            {
                Chunk chunk = chunks[i];
                if (chunk?.chunkableObjects == null || !chunk.chunkBounds.Intersects(area))
                    continue;
                foreach (IChunkableObject o in chunk.chunkableObjects)
                {
                    bool alive = (o is Wgo wgo && wgo != null) || (o is Wso wso && wso != null) || (baked && o is BakedChunkableObjectComponentData);
                    if (alive && seen.Add(o) && o.GetChunkableData().Intersects(area))
                        into.Add(o);
                }
            }
        }
        seen.Clear();
    }

    // Dónde aparece uno al cruzar hacia ese destino, si es en esta misma escena.
    private static bool Destination(string id, out Vector3 dest)
    {
        dest = default;
        WorldData world = MainGame.Instance.GameSave?.worldData;
        if (world == null || !world.TryGetWgoData(id, out WgoData data, out GameSceneData scene) || data == null || scene == null
            || scene.id != MainGame.PlayerData.currentGameSceneId)
            return false;
        dest = data.GetTeleportPointPosition();
        return true;
    }

    private static void Show()
    {
        frames++;
        var sw = Stopwatch.StartNew();
        while (toShow.Count > 0 && sw.Elapsed.TotalMilliseconds < ShowBudgetMs)
        {
            IChunkableObject o = toShow.Dequeue();
            if (o is UnityEngine.Object u && u == null)
                continue;
            ChunkedObjectUtility.UpdateFlag(o, Key, true);
            shown.Add(o);
        }
        showMs += sw.Elapsed.TotalMilliseconds;
        if (toShow.Count > 0)
            return;
        loadingFrom = Time.unscaledTime;
        stage = Stage.Loading;
    }

    private static void WaitForLoads()
    {
        frames++;
        bool busy = WgoPartLoadManager.Instance.HasActiveRequests || WsoOptimizedStagesLoadManager.Instance.HasActiveRequests
                    || WsoConstructorPartsLoadManager.Instance.HasActiveRequests;
        float waited = Time.unscaledTime - loadingFrom;
        if (busy && waited < MaxLoading)
            return;
        loadSeconds = waited;
        timedOut = busy;
        hideIndex = 0;
        stage = Stage.Hiding;
    }

    private static void Hide()
    {
        frames++;
        int end = Math.Min(shown.Count, hideIndex + HidePerFrame);
        for (; hideIndex < end; hideIndex++)
        {
            IChunkableObject o = shown[hideIndex];
            if (o is UnityEngine.Object u && u == null)
                continue;
            ChunkedObjectUtility.UpdateFlag(o, Key, false);
        }
        if (hideIndex < shown.Count)
            return;
        float now = Time.unscaledTime;
        doneAt[place] = now;
        Plugin.Log.LogInfo($"[Prewarm] {door} -> {place}: {shown.Count} objects on the other side shown off camera " +
                           $"(found in {queryMs:0.0} ms, shown in {showMs:0.0} ms), " +
                           $"{(timedOut ? $"still loading after {loadSeconds:0.00} s" : $"loaded in {loadSeconds:0.00} s")}, hidden again; " +
                           $"{now - startedAt:0.00} s and {frames} frames in all");
        shown.Clear();
        stage = Stage.Idle;
    }

    // Al descargar el mod (recarga en caliente) o si algo falla: todo lo mostrado vuelve a esconderse.
    internal static void Shutdown()
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
                // un objeto que ya no está: nada que esconder
            }
        }
        shown.Clear();
        toShow.Clear();
        stage = Stage.Idle;
    }
}
