using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace PathingPlus.PathingPlusCode.Map;

/// <summary>
/// Slows the drawing quill in step with the zoom.
///
/// The cursor moves a fixed 700 px per second in *screen* space. That is right at
/// normal zoom, but the mod's zoomed views shrink the map under it, so the same
/// screen speed crosses proportionally more map — at a third scale the quill flies
/// three times as fast across the nodes it is meant to trace.
///
/// Rather than replicate the game's movement (which reads the stick, the d-pad and
/// the drawing state), this measures how far the cursor actually travelled during
/// the frame and rescales that step by the map's own scale. Every input source is
/// covered at once, and nothing here knows or cares which one moved it.
/// </summary>
[HarmonyPatch(typeof(NControllerMapDrawingInput), "_Process", typeof(double))]
internal static class QuillSpeedPatch
{
    private static Vector2 _before;
    private static bool _measured;

    [HarmonyPrefix]
    private static void BeforeProcess(NControllerMapDrawingInput __instance) =>
        Guard.Run("Measuring the quill's step", () =>
        {
            _measured = false;
            if (Cursor(__instance) is not { } cursor)
                return;
            _before = cursor.GlobalPosition;
            _measured = true;
        });

    [HarmonyPostfix]
    private static void AfterProcess(NControllerMapDrawingInput __instance) =>
        Guard.Run("Slowing the quill to the zoom", () =>
        {
            if (!_measured || Cursor(__instance) is not { } cursor)
                return;
            var scale = MapScale();
            if (scale >= 0.999f)
                return;
            cursor.GlobalPosition = _before + (cursor.GlobalPosition - _before) * scale;
        });

    private static Control? Cursor(Node input) => input.GetNodeOrNull<Control>("%Cursor");

    /// <summary>The map's current scale, which is 1 in the normal view.</summary>
    private static float MapScale()
    {
        var theMap = NMapScreen.Instance?.GetNodeOrNull<Control>("TheMap");
        return theMap is null ? 1f : Mathf.Max(0.05f, theMap.Scale.X);
    }
}
