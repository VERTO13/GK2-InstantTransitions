using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LazyBearTechnology;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SmoothDoors;

// Un renglón bajo la barra de la pantalla de carga mientras corre la precarga: la barra del juego ya llegó al final y,
// sin esto, parecía que la carga se había trabado. Con la letra de la misma pantalla y en el idioma del juego
// (español o inglés). La barra del juego no se toca: la mueve el juego por fases y se pelearían.
internal static class LoadingLabel
{
    private static readonly FieldInfo GuiElements = AccessTools.Field(typeof(LazyUI), "guiElementsDictionary");
    private static readonly FieldInfo Slider = AccessTools.Field(typeof(UILoadingOverlay), "progressSlider");

    private static GameObject root;
    private static Canvas canvas;
    private static TextMeshProUGUI text;
    private static int shownPercent = -1;
    private static readonly Vector3[] corners = new Vector3[4];

    internal static void Show(int done, int total)
    {
        try
        {
            if (root == null)
                Create();
            if (root == null)
                return;
            int percent = total > 0 ? Mathf.Clamp(Mathf.FloorToInt(100f * done / total), 0, 100) : 100;
            if (percent != shownPercent)
            {
                shownPercent = percent;
                text.text = string.Format(Message(), percent);
            }
            Place();
            if (!root.activeSelf)
                root.SetActive(true);
        }
        catch (Exception e)
        {
            Plugin.Log.LogDebug("Loading label: " + e.Message);
        }
    }

    internal static void Hide()
    {
        shownPercent = -1;
        if (root != null && root.activeSelf)
            root.SetActive(false);
    }

    private static string Message()
    {
        string lang = LLBase.CurrentLang ?? "";
        return lang.StartsWith("es", StringComparison.OrdinalIgnoreCase)
            ? "Preparando los lugares para que las puertas sean rápidas… {0}%"
            : "Preparing places so doors are quick… {0}%";
    }

    private static UILoadingOverlay Overlay() =>
        GuiElements?.GetValue(null) is Dictionary<Type, ILazyGUIElement> elements
        && elements.TryGetValue(typeof(UILoadingOverlay), out ILazyGUIElement e) ? e as UILoadingOverlay : null;

    private static void Create()
    {
        root = new GameObject("Smooth Doors loading label");
        UnityEngine.Object.DontDestroyOnLoad(root);
        canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32767; // el máximo: encima de la pantalla de carga
        canvas.pixelPerfect = true;

        GameObject t = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        t.transform.SetParent(root.transform, false);
        text = t.GetComponent<TextMeshProUGUI>();
        text.alignment = TextAlignmentOptions.Top;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        text.color = new Color(0.93f, 0.86f, 0.74f);
        text.fontSize = 16f;
        // La letra de la propia pantalla de carga (o, si no tiene texto, la de su interfaz).
        TMP_Text sample = Overlay() != null ? Overlay().GetComponentInChildren<TMP_Text>(true) : null;
        if (sample != null && sample.font != null)
        {
            text.font = sample.font;
            text.fontSharedMaterial = sample.fontSharedMaterial;
            text.fontSize = sample.fontSize > 0f ? sample.fontSize : 16f;
        }
        RectTransform rt = (RectTransform)t.transform;
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(1200f, 40f);
    }

    // Debajo de la barra del juego (en unidades de este lienzo, a la escala de la interfaz del juego); sin barra, abajo al centro.
    private static void Place()
    {
        float scale = LazyUI.ScaleFactor > 0.001f ? Mathf.Round(LazyUI.ScaleFactor) : 1f;
        if (canvas.scaleFactor != scale)
            canvas.scaleFactor = scale;
        Vector2 at = new Vector2(Screen.width * 0.5f, Screen.height * 0.12f);
        if (Slider?.GetValue(Overlay()) is UnityEngine.UI.Slider bar && bar != null && bar.gameObject.activeInHierarchy)
        {
            Canvas owner = bar.GetComponentInParent<Canvas>();
            Camera cam = owner != null && owner.renderMode != RenderMode.ScreenSpaceOverlay ? owner.worldCamera : null;
            ((RectTransform)bar.transform).GetWorldCorners(corners);
            Vector2 left = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
            Vector2 right = RectTransformUtility.WorldToScreenPoint(cam, corners[3]);
            at = new Vector2((left.x + right.x) * 0.5f, Mathf.Min(left.y, right.y) - 6f * scale);
        }
        ((RectTransform)text.transform).anchoredPosition = new Vector2(Mathf.Round(at.x / scale), Mathf.Round(at.y / scale));
    }
}
