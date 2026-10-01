using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace InstantTransitions;

// Corte directo en las puertas del jugador dentro de la misma escena: sin negro. Un cuadro estás afuera y al siguiente
// adentro, como un cambio de toma.
//
// Cómo: antes de moverlo, la cámara del mundo se dibuja en una textura y esa imagen queda quieta en pantalla (encima del
// mundo, debajo de los menús del juego). El juego lo mueve sin fundido (TeleportDataBase.donNotFade, lo que usan sus
// escenas: lo hace en el mismo cuadro). Mientras la imagen tapa, la cámara llega a su lugar, el lugar nuevo se dibuja y
// se hace nuestra parte de la limpieza de memoria (sin fundido el juego no llama a la suya). Unos cuadros después, la
// imagen se quita de golpe: corte limpio, sin ver piezas aparecer.
//
// Seguros: si algo falla, la imagen se quita y esto se apaga en la sesión (las puertas siguen con el corte por negro);
// la imagen nunca tapa más de 3 s.
internal static class Cut
{
    private const float GiveUpAfter = 3f;
    private const int FramesToDraw = 3; // cuadros dibujados debajo de la imagen antes del corte

    private static bool holding, broken, cleaned;
    private static float since, arrivedAt;
    private static int arrivedFrames, heldFrames;
    private static Vector3 startPos, cameraOffset;
    private static string fromZone;

    private static Camera world;
    private static RenderTexture frozen;
    private static GameObject root;
    private static RawImage image;

    internal static bool LastUsed;

    // La vista de antes todavía tapa (el cambio no ha terminado): WalkIn no toma otra puerta mientras.
    internal static bool Holding => holding;

    // QuickDoors.BeforeTeleport, para una puerta del jugador en la misma escena: congela la vista y devuelve true (el
    // juego debe moverlo sin fundido).
    internal static bool Begin()
    {
        LastUsed = false;
        if (broken || holding || !Plugin.HardCut.Value)
            return false;
        try
        {
            world = WorldCamera();
            if (world == null || world.targetTexture != null)
                return false;
            frozen = Make(frozen, Screen.width, Screen.height);
            RenderTexture before = world.targetTexture;
            world.targetTexture = frozen;
            try
            {
                world.Render();
            }
            finally
            {
                world.targetTexture = before;
            }
            Show();
            startPos = MainGame.PlayerController.MovablePosition;
            cameraOffset = world.transform.position - startPos;
            fromZone = MainGame.PlayerData?.CurrentWorldZoneData?.id;
            arrivedAt = -1f;
            arrivedFrames = 0;
            cameraLate = false;
            heldFrames = 0;
            cleaned = false;
            since = Time.unscaledTime;
            holding = true;
            LastUsed = true;
            return true;
        }
        catch (Exception e)
        {
            Fail("holding the view", e);
            return false;
        }
    }

    // Cada cuadro (DoorWatch.Update).
    internal static void Tick()
    {
        if (!holding)
            return;
        try
        {
            heldFrames++;
            if (Time.unscaledTime - since > GiveUpAfter)
            {
                Plugin.Log.LogWarning($"[Cut] gave up after {GiveUpAfter} s; back to normal.");
                Release();
                return;
            }
            if (!Arrived())
                return;
            if (!cleaned)
            {
                cleaned = true;
                Cleanup.AtSilentDoor(); // debajo de la imagen: si tarda, se ve la vista de antes un instante más
                return;
            }
            if (arrivedFrames < FramesToDraw)
                return;
            float held = Time.unscaledTime - since;
            Release();
            Plugin.Log.LogInfo($"[Door] {fromZone ?? "?"} -> {MainGame.PlayerData?.CurrentWorldZoneData?.id ?? "?"} (same scene, cut: {QuickDoors.LastWhy}): " +
                               $"{held:0.00} s and {heldFrames} frames holding the old view (the game loaded the other side in {arrivedAt - since:0.00} s" +
                               $"{(cameraLate ? "; the camera still wasn't on you after 0.4 s" : "")}), then cut · " +
                               $"managed {DoorWatch.ManagedMB()} MB · Unity {DoorWatch.UnityMB()} MB · " +
                               $"game process {DoorWatch.ProcessMB()} MB");
            Settle.Start($"{fromZone ?? "?"} -> {MainGame.PlayerData?.CurrentWorldZoneData?.id ?? "?"} (cut)");
        }
        catch (Exception e)
        {
            Fail("cutting", e);
        }
    }

    // ¿Ya llegó? Se movió lejos, el juego le devolvió el control y la cámara ya lo mira (o pasaron 0.4 s).
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
        if (Watching(pos))
            return true;
        cameraLate = Time.unscaledTime - arrivedAt > 0.4f;
        return cameraLate;
    }

    private static bool cameraLate;

    // La cámara mira al personaje: el centro de la pantalla cae en el suelo a menos de CameraNear de él. Antes se pedía la
    // misma distancia que antes de cruzar, y como uno sigue caminando y la cámara lo sigue con un poco de retraso, casi
    // nunca se cumplía: se esperaban los 0.4 s completos con la vista de antes congelada mientras el personaje entraba
    // caminando sin verse ("el corte no deja ver al personaje"). Sirve para no mostrar la cámara todavía en el lugar de
    // antes o cruzando el mapa (los interiores están a cientos de unidades).
    private const float CameraNear = 12f;

    private static bool Watching(Vector3 pos)
    {
        Transform cam = world.transform;
        Vector3 f = cam.forward;
        if (f.y > -0.1f)
            return (cam.position - (pos + cameraOffset)).sqrMagnitude < 0.25f; // no mira hacia abajo: como antes
        Vector3 ground = cam.position + f * ((cam.position.y - pos.y) / -f.y);
        return new Vector2(ground.x - pos.x, ground.z - pos.z).magnitude < CameraNear;
    }

    private static void Release()
    {
        holding = false;
        if (root != null)
            root.SetActive(false);
    }

    private static void Fail(string what, Exception e)
    {
        broken = true;
        try
        {
            Release();
        }
        catch
        {
            // ya se intentó quitar la imagen: nada más que hacer
        }
        Plugin.Log.LogWarning($"[Cut] off for this session ({what}), doors go through black: {e}");
    }

    // Al descargar el mod (recarga en caliente): no queda nada suyo.
    internal static void Shutdown()
    {
        Release();
        if (root != null)
            UnityEngine.Object.Destroy(root);
        root = null;
        if (frozen != null)
        {
            frozen.Release();
            UnityEngine.Object.Destroy(frozen);
        }
        frozen = null;
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
    private static RenderTexture Make(RenderTexture old, int w, int h)
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
        var rt = new RenderTexture(w, h, 24, format) { name = "Instant Transitions cut", filterMode = FilterMode.Point };
        rt.Create();
        return rt;
    }

    // La imagen quieta: encima del mundo y debajo de todos los menús del juego (su orden de dibujo más bajo, menos uno).
    private static void Show()
    {
        if (root == null)
        {
            root = new GameObject("Instant Transitions cut");
            UnityEngine.Object.DontDestroyOnLoad(root);
            root.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var go = new GameObject("View", typeof(RectTransform));
            go.transform.SetParent(root.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            image = go.AddComponent<RawImage>();
            image.raycastTarget = false;
        }
        int lowest = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
            .Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay && c.gameObject != root)
            .Select(c => c.sortingOrder).DefaultIfEmpty(0).Min();
        root.GetComponent<Canvas>().sortingOrder = Mathf.Max(short.MinValue, lowest - 1);
        image.texture = frozen;
        root.SetActive(true);
    }
}
