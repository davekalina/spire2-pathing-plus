using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using System.Reflection;

namespace PathingPlus.PathingPlusCode.Map;

/// <summary>
/// The right stick scrolls the map, the way it did in the first game.
///
/// Without it the normal map view is close to unusable on a controller: the d-pad and
/// the left stick are spent navigating the legend and the nodes, and nothing is left
/// to move the view, so anything off screen stays off screen. Splitting the two — left
/// for selection, right for the view — is the arrangement players already know.
///
/// <c>_targetDragPos</c> is what the screen lerps toward every frame, and nudging it
/// before <c>UpdateScrollPosition</c> runs means the game's own easing and its
/// rubber-band clamp back into [-600, 1800] apply unchanged. Writing the container's
/// position directly would fight both.
/// </summary>
[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen._Process))]
internal static class RightStickScrollPatch
{
    private static readonly FieldInfo? TargetDragPosField =
        AccessTools.Field(typeof(NMapScreen), "_targetDragPos");

    /// <summary>Pixels per second at full deflection.</summary>
    private const float Speed = 1400f;

    private const float DeadZone = 0.15f;

    /// <summary>
    /// Steam Input can leave the game's own stick actions unbound, exactly as it does
    /// for the left stick and the quill, so Godot's raw axes are the fallback.
    /// </summary>
    private static readonly StringName RawLeft = "raw_r_stick_left";
    private static readonly StringName RawRight = "raw_r_stick_right";
    private static readonly StringName RawUp = "raw_r_stick_up";
    private static readonly StringName RawDown = "raw_r_stick_down";

    [HarmonyPrefix]
    private static void BeforeProcess(NMapScreen __instance, double __0) =>
        Guard.Run("Scrolling the map with the right stick", () =>
        {
            if (!__instance.IsOpen || !__instance.IsVisibleInTree())
                return;
            // Zoomed out the whole act is on screen and all scrolling is suspended;
            // scrolling there would only fight the framing.
            if (MapScreenPatches.ZoomedOut(__instance))
                return;
            if (TargetDragPosField?.GetValue(__instance) is not Vector2 target)
                return;

            var push = Read();
            if (push == Vector2.Zero)
                return;

            // Subtracted, not added: a *larger* _targetDragPos.Y slides the map down
            // and shows earlier floors, so pushing the stick up has to lower it.
            TargetDragPosField.SetValue(
                __instance, target with { Y = target.Y - push.Y * Speed * (float)__0 });
        });

    private static Vector2 Read()
    {
        var direction = Input.GetVector(
            Controller.rStickLeft, Controller.rStickRight, Controller.rStickUp, Controller.rStickDown);
        if (direction.Length() < DeadZone)
            direction = Input.GetVector(RawLeft, RawRight, RawUp, RawDown);
        if (direction.Length() < DeadZone)
            direction = FromJoypad();
        return direction.Length() >= DeadZone ? direction : Vector2.Zero;
    }

    /// <summary>
    /// The stick as the event stream reports it.
    ///
    /// <c>Input.GetConnectedJoypads()</c> comes back **empty** under Steam Input even
    /// while joypad motion events arrive perfectly well carrying <c>device=0</c>, so
    /// enumerating devices finds nothing to ask and every per-device read is dead on
    /// arrival. Keeping the axis values as they go past is the one route that does not
    /// depend on the InputMap, on action bindings, or on the device list.
    /// </summary>
    private static Vector2 _fromEvents;

    internal static void NoteJoypadMotion(InputEventJoypadMotion motion)
    {
        if (motion.Axis == JoyAxis.RightX)
            _fromEvents.X = motion.AxisValue;
        else if (motion.Axis == JoyAxis.RightY)
            _fromEvents.Y = motion.AxisValue;
    }

    private static Vector2 FromJoypad()
    {
        if (_fromEvents.Length() >= DeadZone)
            return _fromEvents;
        // Device 0 explicitly: the connected-joypad list is empty under Steam Input
        // even when that device is plainly sending events.
        var axes = new Vector2(
            Input.GetJoyAxis(0, JoyAxis.RightX),
            Input.GetJoyAxis(0, JoyAxis.RightY));
        return axes.Length() >= DeadZone ? axes : Vector2.Zero;
    }
}
