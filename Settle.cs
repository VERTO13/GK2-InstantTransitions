using UnityEngine;

namespace InstantTransitions;

// Medición para el log: cómo va el juego el segundo y medio después de llegar por una puerta. Al llegar, el juego
// enciende lo que hay del otro lado y puede ir a tirones un rato; con la pantalla negra eso casi no se veía, con el
// corte sí, así que cada puerta deja anotado cuántos cuadros salieron lentos.
internal static class Settle
{
    private const float Window = 1.5f;
    private static string label;
    private static float from, worst;
    private static int frames, slow;

    internal static void Start(string what)
    {
        label = what;
        from = Time.unscaledTime;
        worst = 0f;
        frames = slow = 0;
    }

    // Cada cuadro (DoorWatch.Update).
    internal static void Tick()
    {
        if (label == null)
            return;
        float dt = Time.unscaledDeltaTime;
        frames++;
        if (dt > 0.034f)
            slow++;
        if (dt > worst)
            worst = dt;
        if (Time.unscaledTime - from < Window)
            return;
        Plugin.Log.LogInfo($"[Arrival] {label}: {frames} frames in the next {Window:0.0} s, {slow} slower than 34 ms, worst {worst * 1000f:0} ms");
        label = null;
    }
}
