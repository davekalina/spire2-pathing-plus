using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using System.Reflection;

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
            view.OnOpened();
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
    /// Natively the vertical d-pad scrolls the map and left/right/select snap the
    /// view back to the current row. Zoomed out, the whole map is visible, the d-pad
    /// belongs to the node grid, and scrolling is pure noise — the native handler
    /// sits out. Falling back to true leaves the game exactly native.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch("ProcessControllerEvent")]
    private static bool BeforeProcessControllerEvent(NMapScreen __instance) =>
        Guard.Run("Suspending native map scroll",
            () => !ZoomActive(__instance), true);

    /// <summary>
    /// Zoomed out, the screen's own mouse handling — drag-to-pan and the wheel — is
    /// frozen. Node clicks are unaffected: map points receive their own gui input.
    ///
    /// The one exception is how a stroke begins. A right or middle press is what
    /// creates the drawing input node, and it does so in this very handler, so
    /// freezing it wholesale left Drawing mode dead in both zoomed views. That press
    /// is let through, and nothing else: the game returns early once a stroke exists,
    /// so no pan can follow it, and the motion that continues the stroke arrives at
    /// the drawing node's own input rather than here.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(nameof(NMapScreen._GuiInput))]
    private static bool BeforeScreenGuiInput(NMapScreen __instance, InputEvent __0) =>
        Guard.Run("Freezing map input while zoomed out", () =>
        {
            if (!ZoomActive(__instance))
                return true;
            // The drawing press always goes through, override or not: this freeze is
            // here to stop native *scrolling* while the whole act is on screen, and
            // gating it on the override left the zoomed views unable to draw at all
            // for anyone who had turned the override off.
            return __0 is InputEventMouseButton
            {
                Pressed: true,
                ButtonIndex: MouseButton.Right or MouseButton.Middle,
            };
        }, true);

    /// <summary>Whether the whole-act view is up, where scrolling is suspended.</summary>
    internal static bool ZoomedOut(NMapScreen screen) => ZoomActive(screen);

    private static bool ZoomActive(NMapScreen screen) =>
        Views.TryGetValue(screen, out var view) && view.ZoomActive;

    /// <summary>
    /// The native legend is hidden under the replacement one, so its hotkey lands on
    /// the replacement instead — same press-to-enter, press-again-to-leave toggle.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch("OnLegendHotkeyPressed")]
    private static bool BeforeLegendHotkeyPressed(NMapScreen __instance) =>
        Guard.Run("Rerouting the legend hotkey", () =>
        {
            if (!Views.TryGetValue(__instance, out var view))
                return true;
            view.ToggleLegendFocus();
            return false;
        }, true);

    /// <summary>
    /// Zoomed out, selecting a node plans with it rather than moving to it — **unless
    /// it is a node you could actually travel to**, which is always a move.
    ///
    /// This used to swallow travel in the zoomed views outright, on the reasoning that
    /// they were a look at the act rather than a place to play from. Once the wide view
    /// could be the *default* that stopped being true: it made zooming in a required
    /// step on every single turn, to press the one node the game was already offering.
    /// Falling back to true keeps travel native if anything here breaks.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(nameof(NMapScreen.OnMapPointSelectedLocally))]
    private static bool BeforeMapPointSelected(NMapScreen __instance, NMapPoint __0) =>
        Guard.Run("Pinning instead of traveling while zoomed", () =>
        {
            if (!Views.TryGetValue(__instance, out var view) || !view.ZoomActive)
                return true;
            // Enabled means the game is offering this node as a move; let it have it.
            if (GodotObject.IsInstanceValid(__0) && __0.IsEnabled)
                return true;
            view.OnMapPointClicked(__0);
            return false;
        }, true);

    /// <summary>
    /// Right Trigger toggles the zoomed-out view (and with it controller node
    /// navigation) while the map screen is the active context. The trigger is an
    /// axis, so a press produces a stream of motion events past the threshold; the
    /// held latch turns that into one toggle per pull.
    /// </summary>
    private static bool _rightTriggerHeld;
    private static bool _stickPressHeld;
    private static bool _peekHeld;

    /// <summary>Switching quill and eraser is the screen's own doing; we just press it.</summary>
    private static readonly MethodInfo? DrawingButtonPressed =
        AccessTools.Method(typeof(NMapScreen), "OnMapDrawingButtonPressed");
    private static readonly MethodInfo? ErasingButtonPressed =
        AccessTools.Method(typeof(NMapScreen), "OnMapErasingButtonPressed");

    /// <summary>
    /// The screen's live tool, which is the honest state. The drawings' own mode is
    /// not: <c>Create</c> sets it before the input node is in the tree, so a tool that
    /// never started still reports itself as selected.
    /// </summary>
    private static readonly FieldInfo? DrawingInputField =
        AccessTools.Field(typeof(NMapScreen), "_drawingInput");

    /// <summary>Keeps the on-screen quill and eraser buttons showing the live tool.</summary>
    private static readonly MethodInfo? UpdateButtonStates =
        AccessTools.Method(typeof(NMapScreen), "UpdateDrawingButtonStates");

    [HarmonyPostfix]
    [HarmonyPatch(nameof(NMapScreen._Input))]
    private static void AfterInput(NMapScreen __instance, InputEvent __0) =>
        Guard.Run("Map hotkeys", () =>
        {
            // The controller's gold ring is a cursor only while the controller is the
            // cursor. Nothing announces the switch to a mouse — the focus events that
            // place the ring simply stop coming — so the mouse's own first move is
            // what takes it down.
            if (__0 is InputEventMouse && Views.TryGetValue(__instance, out var pointed))
            {
                pointed.OnPointerUsed();
                if (__0 is InputEventMouseMotion motion)
                    pointed.OnPointerMoved(motion.GlobalPosition);
            }

            // Every joypad axis, straight off the wire. This is how the right stick is
            // read: the device list is empty under Steam Input, so nothing can be
            // asked per-device, but the events themselves still arrive.
            if (__0 is InputEventJoypadMotion motionEvent)
                RightStickScrollPatch.NoteJoypadMotion(motionEvent);


            // The device may have just changed; a tool in hand has to change with it.
            // Deferred for the same reason the tool switch is: the tree cannot be
            // rearranged while input is being processed.
            if (Ready(__instance) && LiveTools(__instance).Count > 0)
            {
                var current = __instance;
                Callable.From(() => Guard.Run("Matching the drawing tool to the input",
                    () => SyncToolToInput(current))).CallDeferred();
            }

            if (Pulled(__0, Controller.rightTrigger, ref _rightTriggerHeld) &&
                Ready(__instance) && Views.TryGetValue(__instance, out var view))
            {
                view.ToggleZoom();
                __instance.GetViewport().SetInputAsHandled();
            }

            // Clicking the left stick cycles the drawing tools: nothing → quill →
            // eraser → quill. Leaving drawing altogether stays with the game's cancel.
            // Both the game's own stick-click action and `peek` are accepted, because
            // Steam Input commonly binds L3 to peek and then the game never sees the
            // stick click at all.
            var clicked = Pulled(__0, Controller.lStickPress, ref _stickPressHeld);
            var peeked = Pulled(__0, MegaInput.peek, ref _peekHeld);
            if ((!clicked && !peeked) || !Ready(__instance))
                return;

            var screen = __instance;
            // Deferred on purpose: the switch frees one input node and adds another,
            // and the tree cannot be rearranged during input processing — a node added
            // there is created but never entered, so the tool reads as selected while
            // nothing listens to it.
            Callable.From(() => Guard.Run("Switching the drawing tool", () => SwitchTool(screen)))
                .CallDeferred();
            __instance.GetViewport().SetInputAsHandled();
        });

    /// <summary>
    /// Swap the quill for the eraser, or start the quill when neither is out.
    ///
    /// The tool is built here rather than by asking the screen's own buttons. Those
    /// handlers stop the old tool and start the new one in a single call, and the
    /// drawings' mode is set in the middle of it — so any teardown that lands late,
    /// from a straggler being freed to the mod's own tidying, quietly resets the mode
    /// to None and the new tool then throws "not currently in a drawing mode" on its
    /// first stroke. Doing the steps in the open lets the mode be asserted **last**,
    /// after the node is in the tree, where nothing else can undo it.
    ///
    /// The cursor's position carries across, since switching tool is not moving hand.
    /// </summary>
    private static void SwitchTool(NMapScreen screen)
    {
        if (!GodotObject.IsInstanceValid(screen) || !screen.IsOpen)
            return;

        var live = LiveTools(screen);
        var wanted = live.Any(input => input.DrawingMode == DrawingMode.Drawing)
            ? DrawingMode.Erasing
            : DrawingMode.Drawing;
        BuildTool(screen, wanted, live);
    }

    /// <summary>
    /// Rebuild the tool when the player changes device mid-draw.
    ///
    /// <c>NMapDrawingInput.Create</c> picks its implementation from
    /// <c>IsUsingDirectionalNavigation</c> **at the moment of creation** and never
    /// revisits it. Pick the quill up with the mouse and you hold a
    /// <c>NMouseModeMapDrawingInput</c> for as long as it stays out — one that follows
    /// the pointer and never asks the left stick anything, so on a controller the
    /// cursor simply will not move. The reverse strands a controller cursor when the
    /// mouse comes back. Swapping the tool for the matching kind, keeping mode and
    /// cursor position, is the whole fix.
    /// </summary>
    private static void SyncToolToInput(NMapScreen screen)
    {
        if (!GodotObject.IsInstanceValid(screen) || !screen.IsOpen)
            return;
        var live = LiveTools(screen);
        if (live.Count == 0)
            return;

        var wantController = NControllerManager.Instance?.IsUsingDirectionalNavigation == true;
        if (wantController == live.Any(input => input is NControllerMapDrawingInput))
            return;

        BuildTool(screen, live[0].DrawingMode, live);
    }

    private static List<NMapDrawingInput> LiveTools(NMapScreen screen) =>
        screen.GetChildren().OfType<NMapDrawingInput>()
            .Where(input => GodotObject.IsInstanceValid(input) && !input.IsQueuedForDeletion())
            .ToList();

    private static void BuildTool(
        NMapScreen screen, DrawingMode wanted, List<NMapDrawingInput> live)
    {
        var carryOver = live.Select(CursorOf).OfType<Control>().FirstOrDefault()?.GlobalPosition;

        foreach (var input in live)
            input.StopDrawing();
        DrawingInputField?.SetValue(screen, null);

        var tool = NMapDrawingInput.Create(screen.Drawings, wanted);
        tool.Connect(NMapDrawingInput.SignalName.Finished, Callable.From(() =>
            Guard.Run("Clearing the finished drawing tool", () =>
            {
                DrawingInputField?.SetValue(screen, null);
                UpdateButtonStates?.Invoke(screen, null);
            })));
        screen.AddChild(tool);
        DrawingInputField?.SetValue(screen, tool);

        // Last word on the mode, once the node is live and every teardown has run.
        screen.Drawings.SetDrawingModeLocal(wanted);
        UpdateButtonStates?.Invoke(screen, null);

        if (carryOver is { } position && CursorOf(tool) is { } cursor)
            cursor.GlobalPosition = position;
    }

    private static Control? CursorOf(Node? input) =>
        input is null ? null : input.GetNodeOrNull<Control>("%Cursor");

    private static bool Ready(NMapScreen screen) =>
        screen.IsOpen && ActiveScreenContext.Instance.IsCurrent(screen);

    /// <summary>
    /// One toggle per press. A trigger is an axis, so crossing the threshold produces
    /// a stream of events; the latch turns that into a single press, and costs
    /// nothing for a plain button.
    /// </summary>
    private static bool Pulled(InputEvent inputEvent, StringName action, ref bool held)
    {
        if (inputEvent.IsActionReleased(action))
        {
            held = false;
            return false;
        }
        if (!inputEvent.IsActionPressed(action) || held)
            return false;
        held = true;
        return true;
    }

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

    internal static void RouteMapPointDoubleClick(NMapPoint point)
    {
        foreach (var view in Views.Values)
        {
            if (view.Owns(point))
            {
                view.OnMapPointDoubleClicked(point);
                return;
            }
        }
    }

    internal static void RouteDrawingPoint(NMapDrawings drawings, Vector2 point, bool erasing)
    {
        foreach (var (screen, view) in Views)
        {
            if (GodotObject.IsInstanceValid(screen) && screen.IsAncestorOf(drawings))
            {
                view.OnDrawingPoint(drawings, point, erasing);
                return;
            }
        }
    }

    /// <summary>
    /// The drawing point the game *should* have computed.
    ///
    /// Every caller hands <c>NMapDrawings</c> a position worked out as
    /// <c>drawings.GetGlobalTransform().Inverse() * globalPoint</c>, and Godot only
    /// defines <c>Transform2D.Inverse()</c> for an **orthonormal** basis — rotation is
    /// fine, scale is not. Vanilla never scales the map so the shortcut holds there;
    /// the mod's zoomed views do scale it, and the point stops meaning what it says.
    /// Undoing their exact matrix with a true inverse recovers the real global point,
    /// which converts back properly.
    ///
    /// A no-op when the map is unscaled, so it can simply always be applied.
    /// </summary>
    internal static Vector2 CorrectDrawingPoint(NMapDrawings drawings, Vector2 point)
    {
        var transform = drawings.GetGlobalTransform();
        var global = transform.Inverse().AffineInverse() * point;
        return transform.AffineInverse() * global;
    }

    internal static void RouteStrokeEnded(NMapDrawings drawings)
    {
        foreach (var (screen, view) in Views)
        {
            if (GodotObject.IsInstanceValid(screen) && screen.IsAncestorOf(drawings))
            {
                view.OnStrokeEnded();
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
/// Drawing mode, replaced. Every freehand point — mouse or controller — funnels
/// through <c>UpdateCurrentLinePositionLocal</c>, so in Drawing path mode the mod
/// takes the stroke and drops the line: each point snaps to the nearest node it
/// passes, and the mod's own trails join them up. Returning true leaves the game's
/// drawing exactly as it was, which is what every other mode wants.
/// </summary>
[HarmonyPatch(typeof(NMapDrawings), nameof(NMapDrawings.UpdateCurrentLinePositionLocal))]
internal static class MapDrawingSnapPatch
{

    [HarmonyPrefix]
    private static bool BeforeUpdateLine(NMapDrawings __instance, ref Vector2 __0)
    {
        if (!PathingOptions.OverrideDrawing)
        {
            // Standing aside, but the point still has to be right: the zoomed views
            // scale the map and the game's own conversion cannot survive that. Guarded
            // like every other boundary — a bad matrix costs the correction, not the
            // player's stroke.
            var given = __0;
            __0 = Guard.Run("Correcting a drawn point",
                () => MapScreenPatches.CorrectDrawingPoint(__instance, given), given);
            return true;
        }

        // Copied out first: a ref parameter cannot be captured by the guarded lambda.
        var point = __0;
        Guard.Run("Snapping a drawn stroke to the map", () =>
        {
            var drawing = __instance.GetLocalDrawingMode();
            if (drawing is not (DrawingMode.Drawing or DrawingMode.Erasing))
                return;
            // The game hands us the point in the drawings node's own space, and it is
            // the cursor for a controller as much as the mouse for a pointer — so it,
            // not the mouse, is what the stroke follows.
            MapScreenPatches.RouteDrawingPoint(
                __instance, point, drawing == DrawingMode.Erasing);
        });

        // Never the original, whatever happened above. The line exists — it has to,
        // or the game stops forwarding motion at all (see MapDrawingBeginPatch) — but
        // it must never gain a point: the mod's own trails are the drawing here.
        return false;
    }
}

/// <summary>
/// The stroke that is begun but never seen.
///
/// <c>BeginLine</c> creates a Line2D and seeds it with two points half a pixel apart —
/// which, round-capped and in the character's drawing colour, renders as a dot. In
/// Drawing path mode every later point of that stroke is dropped, so the seed is the
/// only thing that ever gets drawn: one blob of native ink per stroke, left on the map
/// after the mod has drawn the real route.
///
/// The line still has to be **created**, though, and that is not optional.
/// <c>IsDrawing</c> is defined as <c>currentlyDrawingLine != null</c>, and every input
/// driver — mouse mode, mouse-held, and the controller — forwards motion only while
/// <c>IsLocalDrawing()</c>. Refusing to begin the line therefore does not suppress a
/// stroke, it suppresses every point of it, and Drawing mode stops working entirely.
/// So the line is made as usual and hidden, which leaves the drivers satisfied and the
/// map clean. Each hidden line is a two-point node that lives until the drawings are
/// cleared, exactly as the game's own lines do.
///
/// The flag scopes this to the local player's strokes: <c>CreateLineForPlayer</c> also
/// runs for lines arriving from other players, and those should still be visible.
/// </summary>
[HarmonyPatch(typeof(NMapDrawings), nameof(NMapDrawings.BeginLineLocal))]
internal static class MapDrawingBeginPatch
{
    internal static bool Hiding;

    [HarmonyPrefix]
    private static void BeforeBeginLine(NMapDrawings __instance, ref Vector2 __0)
    {
        Hiding = PathingOptions.OverrideDrawing;
        if (Hiding)
            return;
        var given = __0;
        __0 = Guard.Run("Correcting the first point of a stroke",
            () => MapScreenPatches.CorrectDrawingPoint(__instance, given), given);
    }

    [HarmonyFinalizer]
    private static void AfterBeginLine() => Hiding = false;
}

/// <summary>
/// The stroke ended. The mod tracks the node a stroke last touched so that drawing
/// from one node onto the next can restore a cut step between them; carrying that
/// across the gap between two strokes would restore a link the player never drew.
/// </summary>
[HarmonyPatch(typeof(NMapDrawings), nameof(NMapDrawings.StopLineLocal))]
internal static class MapDrawingStopPatch
{
    [HarmonyPostfix]
    private static void AfterStopLine(NMapDrawings __instance) =>
        Guard.Run("Ending a stroke", () => MapScreenPatches.RouteStrokeEnded(__instance));
}

/// <summary>Hides the line <see cref="MapDrawingBeginPatch" /> is standing over.</summary>
[HarmonyPatch(typeof(NMapDrawings), "CreateLineForPlayer")]
internal static class MapDrawingCreateLinePatch
{
    [HarmonyPostfix]
    private static void AfterCreateLine(Line2D __result) =>
        Guard.Run("Hiding a native stroke", () =>
        {
            if (MapDrawingBeginPatch.Hiding && GodotObject.IsInstanceValid(__result))
                __result.Visible = false;
        });
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
    /// <summary>
    /// A double-click announces itself on the second PRESS, but the pin action runs
    /// on releases — so the press arms this, and the matching release becomes the
    /// select-all-of-type action instead of a third single toggle.
    /// </summary>
    private static NMapPoint? _doubleClickArmed;

    /// <summary>
    /// The controller has no DoubleClick flag: two select presses on the same node
    /// within this window make the second one the type-select, mirroring the mouse
    /// (whose first click also lands as a single toggle before the gesture resolves).
    /// </summary>
    private const ulong DoublePressWindowMs = 400;

    private static NMapPoint? _lastSelectPoint;
    private static ulong _lastSelectMs;

    [HarmonyPrefix]
    private static void BeforeGuiInput(NClickableControl __instance, InputEvent __0) =>
        Guard.Run("Reading a press on a map node", () => ReadPress(__instance, __0));

    private static void ReadPress(NClickableControl __instance, InputEvent __0)
    {
        if (__instance is not NMapPoint point)
            return;
        if (point.IsEnabled)
            return;

        if (__0 is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true, DoubleClick: true })
        {
            _doubleClickArmed = point;
            return;
        }

        var mouseRelease = __0 is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false };
        var selectPress = !mouseRelease && __0.IsActionPressed(MegaInput.select);
        if (!mouseRelease && !selectPress)
            return;

        var isTypeSelect = mouseRelease && ReferenceEquals(_doubleClickArmed, point);
        _doubleClickArmed = null;

        if (selectPress)
        {
            var now = Time.GetTicksMsec();
            if (ReferenceEquals(_lastSelectPoint, point) && now - _lastSelectMs <= DoublePressWindowMs)
            {
                isTypeSelect = true;
                _lastSelectPoint = null;
            }
            else
            {
                _lastSelectPoint = point;
                _lastSelectMs = now;
            }
        }

        if (isTypeSelect)
            Guard.Run("Selecting every node of a type", () => MapScreenPatches.RouteMapPointDoubleClick(point));
        else
            Guard.Run("Handling a pin press", () => MapScreenPatches.RouteMapPointClick(point));
    }
}
