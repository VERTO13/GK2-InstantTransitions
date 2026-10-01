using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LazyBearTechnology;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace InstantTransitions;

// Aviso corto arriba al centro (al prender o apagar el mod con su tecla): un recuadro oscuro como los del panel de
// Crafting Queue, con la letra de la interfaz del juego, que se desvanece solo.
internal static class Notice
{
    private static readonly FieldInfo GuiElements = AccessTools.Field(typeof(LazyUI), "guiElementsDictionary");

    private static GameObject root;
    private static Canvas canvas;
    private static CanvasGroup group;
    private static RectTransform box;
    private static TextMeshProUGUI text;
    private static float shownAt, hideAt;

    internal static void Show(string message, float seconds = 2.5f)
    {
        try
        {
            if (root == null)
                Create();
            text.text = message;
            float scale = LazyUI.ScaleFactor > 0.001f ? Mathf.Round(LazyUI.ScaleFactor) : 1f;
            if (canvas.scaleFactor != scale)
                canvas.scaleFactor = scale;
            box.anchoredPosition = new Vector2(0f, -Mathf.Round(Screen.height / scale * 0.12f));
            group.alpha = 1f;
            root.SetActive(true);
            shownAt = Time.unscaledTime;
            hideAt = shownAt + seconds;
        }
        catch (Exception e)
        {
            Plugin.Log.LogDebug("Notice: " + e.Message);
        }
    }

    // Al descargar el mod (recarga en caliente): su objeto no se queda huérfano.
    internal static void Shutdown()
    {
        if (root != null)
            UnityEngine.Object.Destroy(root);
        root = null;
    }

    // Cada cuadro (DoorWatch): el último medio segundo se desvanece.
    internal static void Tick()
    {
        if (root == null || !root.activeSelf)
            return;
        float left = hideAt - Time.unscaledTime;
        if (left <= 0f)
            root.SetActive(false);
        else if (left < 0.5f)
            group.alpha = left / 0.5f;
    }

    private static void Create()
    {
        root = new GameObject("Instant Transitions notice");
        UnityEngine.Object.DontDestroyOnLoad(root);
        canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        canvas.pixelPerfect = true;
        group = root.AddComponent<CanvasGroup>();
        group.blocksRaycasts = group.interactable = false;

        GameObject b = new GameObject("Box", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
        b.transform.SetParent(root.transform, false);
        box = (RectTransform)b.transform;
        box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 1f);
        Image bg = b.GetComponent<Image>();
        bg.color = new Color(0.1f, 0.08f, 0.07f, 0.85f);
        bg.raycastTarget = false;
        HorizontalLayoutGroup h = b.GetComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(8, 8, 3, 4);
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        ContentSizeFitter f = b.GetComponent<ContentSizeFitter>();
        f.horizontalFit = f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject t = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        t.transform.SetParent(box, false);
        text = t.GetComponent<TextMeshProUGUI>();
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        text.color = new Color(0.93f, 0.86f, 0.74f);
        text.fontSize = 16f;
        // La letra del HUD del juego, si ya existe.
        TMP_Text sample = null;
        if (GuiElements?.GetValue(null) is Dictionary<Type, ILazyGUIElement> elements
            && elements.TryGetValue(typeof(HUD), out ILazyGUIElement hud) && hud is Component c && c != null)
            sample = c.GetComponentInChildren<TMP_Text>(true);
        if (sample != null && sample.font != null)
        {
            text.font = sample.font;
            text.fontSharedMaterial = sample.fontSharedMaterial;
            text.fontSize = sample.fontSize > 0f ? sample.fontSize : 16f;
        }
    }
}
