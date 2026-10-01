using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace InstantTransitions;

// La casa que se abre (prototipo, solo tu casa): en vez de pasar por negro, al entrar el cuarto aparece chico dentro de
// la silueta de la casa y crece hasta llenar la pantalla mientras el patio se oscurece; al salir, al revés.
//
// Cómo: el juego mueve al jugador sin fundido (TeleportDataBase.donNotFade, lo que usan sus escenas). Antes de moverlo se
// dibuja la cámara del mundo en dos texturas (con y sin el jugador). Mientras el juego lo mueve, se ve la foto con el
// jugador. Al llegar, la cámara del mundo dibuja a una textura en vez de a la pantalla y una capa encima de todo lo del
// mundo (pero debajo de los menús del juego) arma la animación: fondo negro, la foto de afuera oscureciéndose, y el lado
// de adentro escalado alrededor del jugador y recortado al rectángulo de la casa, que crece hasta la pantalla entera.
// Al terminar, la cámara vuelve a dibujar a la pantalla y la capa se esconde: el último cuadro es igual al juego.
//
// Seguros: la cámara del mundo siempre vuelve a la pantalla (al terminar, si algo falla y a los 3 s pase lo que pase);
// si algo falla una vez, esto se apaga en la sesión y las puertas siguen como antes.
internal static class HouseOpen
{
    private sealed class Building
    {
        public string enterDoor, exitDoor; // el objeto puerta de afuera y el de adentro
        public string model;               // parte del nombre del modelo de afuera (para su rectángulo en pantalla)
    }

    private static readonly Building[] Buildings =
    {
        new Building { enterDoor = "tp_RT_home_enter", exitDoor = "tp_RT_home_exit", model = "ruined_temple_house_T2" },
    };

    private const float Seconds = 0.45f;
    private const float GiveUpAfter = 3f;

    private enum State { Off, Waiting, Animating }
    private static State state;
    private static bool entering;
    private static Building building;
    private static float stateFrom;
    private static bool broken;

    // Para saber cuándo llegó: sin fundido el juego lo mueve en el mismo cuadro y DoorWatch no alcanza a ver la puerta.
    private static Vector3 startPos, cameraOffset;
    private static float arrivedAt;
    private static int arrivedFrames;

    private static Camera world;
    private static RenderTexture withPlayer, withoutPlayer, live;
    private static Rect houseRect, roomRect; // en la pantalla, de 0 a 1 desde abajo a la izquierda
    private static Vector2 pivot;            // el jugador adentro, en la pantalla (0 a 1)
    private static float s0;                 // tamaño del cuarto dentro de la casa (0 a 1)

    private static GameObject root;
    private static RawImage outside, inside;
    private static RectMask2D mask;

    internal static bool LastUsed;

    // QuickDoors.BeforeTeleport, para una puerta del jugador: ¿es la de un edificio conocido? Si sí, prepara todo y
    // devuelve true (el juego debe moverlo sin fundido).
    internal static bool Begin(TeleportDataBase data)
    {
        LastUsed = false;
        if (broken || !Plugin.OpenHouses.Value || state != State.Off || !(data is WgoTeleportData w))
            return false;
        string to = w.wgoIdTeleportTo;
        building = Buildings.FirstOrDefault(b => b.exitDoor == to || b.enterDoor == to);
        if (building == null)
            return false;
        try
        {
            world = WorldCamera();
            if (world == null || world.targetTexture != null)
                return false;
            entering = building.exitDoor == to;
            int wpx = Screen.width, hpx = Screen.height;
            withPlayer = Make(withPlayer, wpx, hpx, "with player");
            withoutPlayer = Make(withoutPlayer, wpx, hpx, "without player");
            live = Make(live, wpx, hpx, "live");
            if (entering)
                houseRect = ModelRect(building.model);
            else
            {
                roomRect = ZoneRect();
                pivot = PlayerOnScreen();
            }
            startPos = MainGame.PlayerController.MovablePosition;
            cameraOffset = world.transform.position - startPos;
            arrivedAt = -1f;
            arrivedFrames = 0;
            Render(withPlayer, true);
            Render(withoutPlayer, false);
            Show();
            if (entering)
                ShowOnly(outsideTexture: withPlayer);
            else
                ShowOnly(insideTexture: withPlayer);
            state = State.Waiting;
            stateFrom = Time.unscaledTime;
            LastUsed = true;
            return true;
        }
        catch (Exception e)
        {
            Fail("preparing", e);
            return false;
        }
    }

    // DoorWatch: la puerta terminó (si alcanzó a verla).
    internal static void DoorEnded()
    {
        if (state == State.Waiting)
            Start();
    }

    // ¿Ya llegó? Se movió lejos, el juego le devolvió el control y la cámara ya está sobre él (o pasaron 0.4 s).
    private static bool Arrived()
    {
        PlayerController player = MainGame.PlayerController;
        if (player == null)
            return false;
        Vector3 pos = player.MovablePosition;
        if ((pos - startPos).sqrMagnitude < 25f || !player.IsControlEnabledByType(TakenControlType.ByTeleport))
            return false;
        if (arrivedAt < 0f)
            arrivedAt = Time.unscaledTime;
        arrivedFrames++;
        bool settled = (world.transform.position - (pos + cameraOffset)).sqrMagnitude < 0.25f;
        return arrivedFrames >= 2 && (settled || Time.unscaledTime - arrivedAt > 0.4f);
    }

    private static void Start()
    {
        if (state != State.Waiting)
            return;
        try
        {
            if (entering)
            {
                roomRect = ZoneRect();
                pivot = PlayerOnScreen();
            }
            else
                houseRect = ModelRect(building.model);
            s0 = Mathf.Clamp(Mathf.Min(houseRect.width / Mathf.Max(0.01f, roomRect.width), houseRect.height / Mathf.Max(0.01f, roomRect.height)), 0.2f, 0.9f);
            world.targetTexture = live;
            if (entering)
            {
                outside.texture = withoutPlayer;
                inside.texture = live;
            }
            else
            {
                outside.texture = live;
                inside.texture = withoutPlayer;
            }
            outside.enabled = inside.enabled = true;
            state = State.Animating;
            stateFrom = Time.unscaledTime;
            Plugin.Log.LogInfo($"[House] {(entering ? "entering" : "leaving")} {building.enterDoor}: arrived after {Time.unscaledTime - stateFrom:0.00} s; " +
                               $"house on screen {houseRect}, room {roomRect}, room starts at {s0:0.00} of its size around {pivot}");
            Apply(0f);
        }
        catch (Exception e)
        {
            Fail("starting", e);
        }
    }

    // Cada cuadro (DoorWatch.Update).
    internal static void Tick()
    {
        if (state == State.Off)
            return;
        try
        {
            float t = Time.unscaledTime - stateFrom;
            if (t > GiveUpAfter)
            {
                Plugin.Log.LogWarning($"[House] gave up after {GiveUpAfter} s ({state}); back to normal.");
                Restore();
                return;
            }
            if (state == State.Waiting)
            {
                if (Arrived())
                    Start();
                return;
            }
            float u = Mathf.Clamp01(t / Seconds);
            Apply(u * u * (3f - 2f * u));
            if (u >= 1f)
                Restore();
        }
        catch (Exception e)
        {
            Fail("animating", e);
        }
    }

    // e = 0..1 del avance. Entrando: el cuarto crece de s0 a 1 y el recorte va de la casa a la pantalla entera; saliendo,
    // al revés (y el cuarto se desvanece al final, ya chico dentro de la casa).
    private static void Apply(float e)
    {
        float k = entering ? e : 1f - e; // 0 = el cuarto chico dentro de la casa, 1 = el cuarto a pantalla completa
        float scale = Mathf.Lerp(s0, 1f, k);
        Rect clip = new Rect(Mathf.Lerp(houseRect.x, 0f, k), Mathf.Lerp(houseRect.y, 0f, k),
            Mathf.Lerp(houseRect.width, 1f, k), Mathf.Lerp(houseRect.height, 1f, k));
        float w = Screen.width, h = Screen.height;
        mask.padding = new Vector4(clip.xMin * w, clip.yMin * h, (1f - clip.xMax) * w, (1f - clip.yMax) * h);
        var rt = inside.rectTransform;
        rt.pivot = pivot;
        rt.localScale = new Vector3(scale, scale, 1f);
        float light = Mathf.Lerp(1f, 0.2f, k); // afuera se oscurece mientras entras
        outside.color = new Color(light, light, light, 1f);
        inside.color = new Color(1f, 1f, 1f, entering ? 1f : Mathf.Clamp01((1f - e) / 0.25f));
    }

    private static void Restore()
    {
        if (world != null && world.targetTexture == live)
            world.targetTexture = null;
        if (root != null)
            root.SetActive(false);
        state = State.Off;
    }

    // Al descargar el mod (recarga en caliente): la cámara vuelve a la pantalla y no queda nada suyo.
    internal static void Shutdown()
    {
        Restore();
        if (root != null)
            UnityEngine.Object.Destroy(root);
        root = null;
        foreach (RenderTexture rt in new[] { withPlayer, withoutPlayer, live })
            if (rt != null)
            {
                rt.Release();
                UnityEngine.Object.Destroy(rt);
            }
        withPlayer = withoutPlayer = live = null;
    }

    private static void Fail(string what, Exception e)
    {
        broken = true;
        try
        {
            Restore();
        }
        catch
        {
            // la cámara ya se intentó devolver: nada más que hacer
        }
        Plugin.Log.LogWarning($"[House] off for this session ({what}): {e}");
    }

    // La cámara que dibuja el mundo: la de Cinemachine (la "principal" de Unity es otra, que dibuja una sola capa).
    private static Camera WorldCamera()
    {
        if (world != null && world.isActiveAndEnabled)
            return world;
        return UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)
            .FirstOrDefault(c => c.isActiveAndEnabled && c.GetComponent("CinemachineBrain") != null);
    }

    // Sin canal alfa: la imagen del mundo no es opaca en todos lados (se vio con el fundido cruzado).
    private static RenderTexture Make(RenderTexture old, int w, int h, string name)
    {
        if (old != null && old.width == w && old.height == h)
            return old;
        if (old != null)
        {
            old.Release();
            UnityEngine.Object.Destroy(old);
        }
        RenderTextureFormat format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGB111110Float)
            ? RenderTextureFormat.RGB111110Float : RenderTextureFormat.ARGB32;
        var rt = new RenderTexture(w, h, 24, format) { name = "Instant Transitions house " + name, filterMode = FilterMode.Point };
        rt.Create();
        return rt;
    }

    // Dibuja la cámara del mundo en la textura, con o sin el jugador (sus renderers apagados solo durante este dibujo).
    private static void Render(RenderTexture into, bool player)
    {
        var hidden = new List<Renderer>();
        if (!player)
            foreach (Renderer r in MainGame.PlayerController.GetComponentsInChildren<Renderer>())
                if (r.enabled)
                {
                    r.enabled = false;
                    hidden.Add(r);
                }
        RenderTexture before = world.targetTexture;
        world.targetTexture = into;
        try
        {
            world.Render();
        }
        finally
        {
            world.targetTexture = before;
            foreach (Renderer r in hidden)
                r.enabled = true;
        }
    }

    private static Vector2 PlayerOnScreen()
    {
        Vector3 p = world.WorldToViewportPoint(MainGame.PlayerController.MovablePosition);
        return new Vector2(p.x, p.y);
    }

    // El rectángulo en pantalla de las partes del modelo de afuera (las que tienen altura: la casa, no su sombra ni el piso).
    private static Rect ModelRect(string model)
    {
        Vector3 p = MainGame.PlayerController.MovablePosition;
        var corners = new List<Vector3>();
        foreach (Renderer r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            Bounds b = r.bounds;
            if (b.size.y < 1f || Mathf.Abs(b.center.x - p.x) > 8f || Mathf.Abs(b.center.z - p.z) > 8f || !r.name.Contains(model))
                continue;
            corners.AddRange(Corners(b));
        }
        return corners.Count > 0 ? OnScreen(corners) : new Rect(0.4f, 0.5f, 0.2f, 0.4f);
    }

    // El rectángulo en pantalla de la zona donde está el jugador (el cuarto).
    private static Rect ZoneRect()
    {
        string zone = MainGame.PlayerData?.CurrentWorldZoneData?.id;
        foreach (WorldZone z in UnityEngine.Object.FindObjectsByType<WorldZone>(FindObjectsSortMode.None))
            if (z.ZoneCollider != null && z.Data?.id == zone)
                return OnScreen(Corners(z.ZoneCollider.bounds).ToList());
        return new Rect(0.2f, 0.4f, 0.6f, 0.5f);
    }

    private static IEnumerable<Vector3> Corners(Bounds b)
    {
        for (int i = 0; i < 8; i++)
            yield return new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
    }

    private static Rect OnScreen(List<Vector3> corners)
    {
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
        foreach (Vector3 c in corners)
        {
            Vector3 v = world.WorldToViewportPoint(c);
            min = Vector2.Min(min, v);
            max = Vector2.Max(max, v);
        }
        min = Vector2.Max(min, Vector2.zero);
        max = Vector2.Min(max, Vector2.one);
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    // La capa: fondo negro, la foto o el mundo de afuera, y el recorte con el lado de adentro. Encima del mundo y
    // debajo de todos los menús del juego (su orden de dibujo más bajo, menos uno).
    private static void Show()
    {
        if (root == null)
        {
            root = new GameObject("Instant Transitions house");
            UnityEngine.Object.DontDestroyOnLoad(root);
            root.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            Full(new GameObject("Black", typeof(RectTransform)), root.transform).AddComponent<Image>().color = Color.black;
            outside = Full(new GameObject("Outside", typeof(RectTransform)), root.transform).AddComponent<RawImage>();
            GameObject clip = Full(new GameObject("House", typeof(RectTransform)), root.transform);
            mask = clip.AddComponent<RectMask2D>();
            inside = Full(new GameObject("Inside", typeof(RectTransform)), clip.transform).AddComponent<RawImage>();
            foreach (Graphic g in root.GetComponentsInChildren<Graphic>(true))
                g.raycastTarget = false;
        }
        var canvas = root.GetComponent<Canvas>();
        int lowest = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
            .Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay && c.gameObject != root)
            .Select(c => c.sortingOrder).DefaultIfEmpty(0).Min();
        canvas.sortingOrder = Mathf.Max(short.MinValue, lowest - 1);
        root.SetActive(true);
    }

    private static void ShowOnly(Texture outsideTexture = null, Texture insideTexture = null)
    {
        outside.texture = outsideTexture;
        outside.enabled = outsideTexture != null;
        outside.color = Color.white;
        inside.texture = insideTexture;
        inside.enabled = insideTexture != null;
        inside.color = Color.white;
        inside.rectTransform.localScale = Vector3.one;
        mask.padding = Vector4.zero;
    }

    private static GameObject Full(GameObject go, Transform parent)
    {
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return go;
    }
}
