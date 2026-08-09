using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace PathingPlus.PathingPlusCode.Map;

/// <summary>
/// Makes the left stick drive the map's drawing quill.
///
/// The quill already asks for it — <c>NControllerMapDrawingInput</c> moves the cursor
/// by <c>GetLeftAnalogStickDirection</c> and only falls back to the d-pad when that
/// reads near zero. The gap is underneath: with Steam Input active the strategy
/// reports a Steam *analog action* rather than the physical stick, so a controller
/// config that does not bind the left stick to that action reports zero forever. The
/// d-pad keeps working because those are digital actions bound separately.
///
/// Filling a zero reading in from Godot's own raw left-stick axes costs nothing when
/// the strategy already has an answer, and is scoped to the one moment it matters:
/// the map open with a quill or eraser in hand. Everywhere else the game's input is
/// left exactly as it was.
/// </summary>
[HarmonyPatch(typeof(NControllerManager), nameof(NControllerManager.GetLeftAnalogStickDirection))]
internal static class LeftStickQuillPatch
{
    private static readonly StringName RawLeft = "raw_l_stick_left";
    private static readonly StringName RawRight = "raw_l_stick_right";
    private static readonly StringName RawUp = "raw_l_stick_up";
    private static readonly StringName RawDown = "raw_l_stick_down";

    /// <summary>Matches the quill's own dead zone, so the two agree on "not moving".</summary>
    private const float DeadZone = 0.1f;

    [HarmonyPostfix]
    private static void AfterGetLeftAnalogStickDirection(ref Vector2 __result)
    {
        // __result cannot be captured by the guarded lambda, so the reading is taken
        // first and assigned outside it.
        var reported = __result;
        var filled = Guard.Run("Reading the left stick for the quill",
            () => PhysicalStickWhileDrawing(reported), Vector2.Zero);
        if (filled != Vector2.Zero)
            __result = filled;
    }

    private static Vector2 PhysicalStickWhileDrawing(Vector2 reported)
    {
        if (reported.Length() >= DeadZone)
            return Vector2.Zero;
        if (NMapScreen.Instance is not { IsOpen: true } screen)
            return Vector2.Zero;
        if (screen.Drawings.GetLocalDrawingMode() == DrawingMode.None)
            return Vector2.Zero;
        var raw = Input.GetVector(RawLeft, RawRight, RawUp, RawDown);
        return raw.Length() >= DeadZone ? raw : Vector2.Zero;
    }
}
