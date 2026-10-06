using System;
using System.Reflection;
using HarmonyLib;
using LazyBearTechnology;

namespace InstantTransitions;

// Reja de la sala de resurrección cerrada (reporte de Nexus, 2026-10-05, y el mismo caso en la partida del autor).
//
// Qué pasa en el juego. Un objeto con animación guardada (una reja que ya se abrió: customAnimationTrigger = force_open)
// recibe esa orden cada vez que se le enlaza el dibujo (Wgo.TryBindAnimationComponent). Al recibirla se pone la marca de
// animación (ChunkedObjectUtility.UpdateFlag con ChunkingIgnoreType.Animation): con ella el sistema de zonas ya no lo
// esconde nunca más, y la propia marca lo muestra (UpdateChunkVisibility(true)). Todo eso va bien porque el dibujo se
// carga de forma asíncrona, varios cuadros después de que ChunkManager.DispatchChunkVisibilityState lo pidió.
//
// Qué cambia la precarga del mod. Con PreloadPlaces las piezas ya están en las reservas, y la carga (UniTask) termina en
// el mismo cuadro, DENTRO de esa llamada del sistema de zonas. Cuando el objeto está en "Prewarm" (se acerca el jugador, lo
// carga pero no lo muestra), la secuencia es: el sistema de zonas pide cargar → el dibujo se enlaza → la orden de abrir
// llega, se pone la marca y se muestra (todo bien) → el sistema de zonas sigue con lo que le quedaba, que es decirle
// "no visible" (todavía iba con el permiso de antes de la marca) → el dibujo se apaga. Queda con la marca puesta (nunca más
// lo toca nadie), enlazado, pero apagado, y el animador perdió la orden. Más tarde, cuando algo lo enciende (la precarga de
// puertas del propio mod, una escena, lo que sea), el animador arranca en su pose de fábrica: reja cerrada, con una
// colisión que no deja pasar, y la partida dice «abierta» (así que el juego ni ofrece abrirla otra vez).
//
// Qué hace esto. Después de cada paso del sistema de zonas sobre un objeto, si ese objeto lleva una marca (que por diseño
// del juego significa "siempre visible") y quedó escondido, se muestra de nuevo y, si ya tenía el dibujo enlazado, se le
// repite su orden guardada (lo mismo que hace el juego cada vez que enlaza un dibujo). Es lo que el juego habría dejado
// con la carga asíncrona. Reproducido y comprobado con la partida del autor: sin el mod la reja queda abierta; con la 0.10.1
// quedaba cerrada; con esto, abierta.
internal static class ChunkGuard
{
    private static readonly FieldInfo VisibleField = AccessTools.Field(typeof(Wgo), "isVisible");
    private static readonly FieldInfo BoundField = AccessTools.Field(typeof(Wgo), "animationBindingsBound");
    private static int fixes;
    private static bool broken;

    internal static void Apply(Harmony harmony)
    {
        MethodInfo target = AccessTools.Method(typeof(ChunkManager), "DispatchChunkVisibilityState");
        if (target == null || VisibleField == null || BoundField == null)
        {
            Plugin.Log.LogWarning("Chunk guard: the game's chunk system was not found (game update?); animated objects are left as the game has them.");
            return;
        }
        // Seguro de parchar: su cuerpo solo llama a métodos y a la interfaz del objeto, no lee campos estáticos de clases del
        // juego (parcharlo no adelanta ningún constructor estático).
        harmony.Patch(target, postfix: new HarmonyMethod(typeof(ChunkGuard), nameof(AfterDispatch)));
    }

    // Se llama por cada objeto cada vez que el sistema de zonas decide algo: lo de siempre es un tipo y una bandera.
    private static void AfterDispatch(IChunkableObject chunkableObject)
    {
        if (broken || !(chunkableObject is Wgo w))
            return;
        try
        {
            MultiFlagOR<ChunkingIgnoreType> flags = w != null ? w.IgnoreMultiFlag : null;
            if (flags == null || !flags.ResultFlag)
                return; // sin marca: el sistema de zonas hizo lo suyo
            if (!w.HasData || (bool)VisibleField.GetValue(w))
                return; // con marca y visible: como debe ser
            Repair(w);
        }
        catch (Exception e)
        {
            broken = true; // un solo aviso y se apaga: nunca debe estorbar al sistema de zonas
            Plugin.Log.LogWarning("Chunk guard off for this session: " + e);
        }
    }

    private static void Repair(Wgo w)
    {
        bool wasActive = w.gameObject.activeSelf;
        w.UpdateChunkVisibility(true); // la marca pide que se vea: lo mismo que hace el juego al ponerla
        bool bound = (bool)BoundField.GetValue(w);
        bool fired = false;
        if (bound && w.gameObject.activeInHierarchy)
        {
            w.Data.TryFireSerializedTrigger(); // lo mismo que el juego al enlazar el dibujo
            fired = true;
        }
        if (++fixes <= 30)
            Plugin.Log.LogInfo($"[Chunk guard] {w.Data.id}: the zone system hid it right after it was marked as always visible; " +
                               $"shown again{(fired ? " and its saved animation sent again" : "")} (was {(wasActive ? "on" : "off")}).");
    }
}
