using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;

namespace PathingPlus.PathingPlusCode.Map;

[HarmonyPatch(typeof(NMapScreen))]
internal static class MapScreenPatches
{
    private static readonly Dictionary<NMapScreen, PathingView> Views = [];

    [HarmonyPostfix]
    [HarmonyPatch(nameof(NMapScreen.Open))]
    private static void AfterOpen(NMapScreen __instance) =>
        Guard.Run("Attaching the pathing view", () =>
        {
            if (!Views.TryGetValue(__instance, out var view))
            {
                view = new PathingView(__instance);
                Views.Add(__instance, view);
                __instance.TreeExiting += () => Detach(__instance);
            }
            view.Refresh();
        });

    [HarmonyPostfix]
    [HarmonyPatch(nameof(NMapScreen.SetMap))]
    private static void AfterSetMap(NMapScreen __instance) =>
        Guard.Run("Resetting pathing after a map change", () =>
        {
            if (Views.TryGetValue(__instance, out var view))
                view.OnMapChanged();
        });

    /// <summary>
    /// The game re-evaluates which nodes are travelable whenever position or travel
    /// permission changes — exactly the moments the routes need recomputing.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch("RecalculateTravelability")]
    private static void AfterRecalculateTravelability(NMapScreen __instance) =>
        Guard.Run("Refreshing routes after travelability change", () =>
        {
            if (__instance.IsOpen && Views.TryGetValue(__instance, out var view))
                view.Refresh();
        });

    /// <summary>
    /// Natively the vertical d-pad scrolls the map and left/right/select snap the view
    /// back to the current row. In plan mode focus travel does the navigating and the
    /// view follows focus; zoomed out, the whole map is visible and scrolling is
    /// pointless. Either way the native handler sits out. Falling back to true leaves
    /// the game exactly native.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch("ProcessControllerEvent")]
    private static bool BeforeProcessControllerEvent(NMapScreen __instance) =>
        Guard.Run("Suspending native map scroll",
            () => !ScrollSuspended(__instance), true);

    /// <summary>
    /// Zoomed out, the screen's own mouse handling — drag-to-pan, the wheel, and
    /// quill drawing starts — is frozen too. Node clicks are unaffected: map points
    /// receive their own gui input.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(nameof(NMapScreen._GuiInput))]
    private static bool BeforeScreenGuiInput(NMapScreen __instance) =>
        Guard.Run("Freezing map input while zoomed out",
            () => !(Views.TryGetValue(__instance, out var view) && view.ZoomActive), true);

    internal static bool PlanModeActive(NMapScreen screen) =>
        Views.TryGetValue(screen, out var view) && view.PlanModeActive;

    private static bool ScrollSuspended(NMapScreen screen) =>
        Views.TryGetValue(screen, out var view) && (view.PlanModeActive || view.ZoomActive);

    /// <summary>
    /// Right Trigger toggles plan mode while the map screen is the active context.
    /// The trigger is an axis, so a press produces a stream of motion events past the
    /// threshold; the held latch turns that into one toggle per pull.
    /// </summary>
    private static bool _rightTriggerHeld;

    [HarmonyPostfix]
    [HarmonyPatch(nameof(NMapScreen._Input))]
    private static void AfterInput(NMapScreen __instance, InputEvent __0) =>
        Guard.Run("Plan mode hotkey", () =>
        {
            if (__0.IsActionReleased(Controller.rightTrigger))
            {
                _rightTriggerHeld = false;
                return;
            }
            if (!__0.IsActionPressed(Controller.rightTrigger) || _rightTriggerHeld)
                return;
            _rightTriggerHeld = true;
            if (!__instance.IsOpen || !ActiveScreenContext.Instance.IsCurrent(__instance))
                return;
            if (Views.TryGetValue(__instance, out var view))
            {
                view.TogglePlanMode();
                __instance.GetViewport().SetInputAsHandled();
            }
        });

    internal static void Detach(NMapScreen screen)
    {
        if (Views.Remove(screen, out var view))
            view.Dispose();
    }

    internal static void RouteMapPointClick(NMapPoint point)
    {
        foreach (var view in Views.Values)
        {
            if (view.Owns(point))
            {
                view.OnMapPointClicked(point);
                return;
            }
        }
    }

    internal static void RouteClearDrawings(NMapDrawings drawings)
    {
        foreach (var (screen, view) in Views)
        {
            if (GodotObject.IsInstanceValid(screen) && screen.IsAncestorOf(drawings))
            {
                view.ClearPins();
                return;
            }
        }
    }
}

/// <summary>
/// The Clear drawings button resets the player's plan; the pins are part of that plan,
/// so they go with it. <c>ClearDrawnLinesLocal</c> is the local-player clear action —
/// remote players' clears arrive by another path and do not touch local pins.
/// </summary>
[HarmonyPatch(typeof(NMapDrawings), nameof(NMapDrawings.ClearDrawnLinesLocal))]
internal static class MapClearDrawingsPatch
{
    [HarmonyPostfix]
    private static void AfterClear(NMapDrawings __instance) =>
        Guard.Run("Clearing pins with the drawings",
            () => MapScreenPatches.RouteClearDrawings(__instance));
}

/// <summary>
/// Pin presses. A non-travelable map node is disabled, which suppresses its own click
/// signals but not its <c>_GuiInput</c> — the one place a press on it can still be
/// seen. Mouse: a left-click release. Controller: the select action, which only ever
/// reaches a disabled node while plan mode has focused it. Travelable nodes are left
/// entirely to the game: this only ever acts on disabled nodes, so a failure here can
/// never cost the player a movement click.
/// </summary>
[HarmonyPatch(typeof(NClickableControl), nameof(NClickableControl._GuiInput))]
internal static class MapPointPinPatch
{
    [HarmonyPrefix]
    private static void BeforeGuiInput(NClickableControl __instance, InputEvent __0)
    {
        if (__instance is not NMapPoint point)
            return;
        if (point.IsEnabled)
            return;
        var pressed = __0 is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } ||
            __0.IsActionPressed(MegaInput.select);
        if (!pressed)
            return;

        Guard.Run("Handling a pin press", () => MapScreenPatches.RouteMapPointClick(point));
    }
}
