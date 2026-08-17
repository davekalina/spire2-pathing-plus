using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Runs;
using PathingPlus.PathingPlusCode.Pathing;
using System.Reflection;

namespace PathingPlus.PathingPlusCode.Map;

/// <summary>
/// One map screen's worth of Pathing Plus: the route computation, the overlay, the
/// legend panel, and the pin state, glued together. Created on the screen's first
/// <c>Open</c>, disposed when the screen leaves the tree.
/// </summary>
internal sealed class PathingView : IDisposable
{
    /// <summary>
    /// Node positions live in the screen's private point dictionary; reading it is the
    /// one reflective dependency this mod has, and losing it costs only the display.
    /// </summary>
    private static readonly FieldInfo? PointDictionaryField =
        AccessTools.Field(typeof(NMapScreen), "_mapPointDictionary");

    private readonly NMapScreen _screen;
    private readonly PathOverlay _overlay;
    private readonly RouteLegendPanel _legend;
    private readonly MapToolbar _toolbar;
    private readonly OptionsPanel _options;
    private readonly HelpTip _help;
    private readonly AutoPathMenu _autoPath;
    private readonly PathToolButton _pathTool;
    private readonly NodeNavigator _navigator;
    private readonly MapZoom _zoom;
    private readonly WaypointSelection _pins = new();
    private readonly Dictionary<NMapPoint, float> _iconBaseRotations = [];

    /// <summary>
    /// The counter-rotation tween in flight per icon, so the next change can kill it.
    /// Without a handle an old tween keeps writing `rotation_degrees` after a later
    /// snap has set it, and wins — which left every icon a quarter-turn out on the
    /// second and every subsequent map open, since closing the map starts one.
    /// </summary>
    private readonly Dictionary<NMapPoint, Tween> _iconTweens = [];
    private Control? _nativeLegend;

    private MapGraphAdapter? _adapter;
    private Dictionary<MapCoord, NMapPoint>? _nodesByCoord;
    private IReadOnlyList<IReadOnlyList<string>> _shownRoutes = [];

    /// <summary>Manual-mode links between pinned floors; what the eraser rubs out.</summary>
    private IReadOnlyList<IReadOnlyList<string>> _links = [];

    /// <summary>Matching routes beyond the legend's five: drawn, but not coloured.</summary>
    private IReadOnlyList<IReadOnlyList<string>> _backdropRoutes = [];
    private HashSet<string> _pinnable = [];

    /// <summary>Every complete route, and where the player stands — what Auto-Path scores.</summary>
    private IReadOnlyList<IReadOnlyList<string>> _completeRoutes = [];
    private string _startId = "";

    /// <summary>
    /// Steps the eraser has cut, as (from, to) in row order. Edges rather than nodes,
    /// so rubbing out one link between two nodes leaves both of them — and every other
    /// link they have — exactly as they were.
    /// </summary>
    private HashSet<(string From, string To)> _cut = [];

    /// <summary>
    /// The node the current stroke last touched, so drawing across a cut step can put
    /// it back. Cleared when the stroke ends, or a later stroke starting elsewhere
    /// would restore a link the player never drew over.
    /// </summary>
    private string? _lastDrawn;

    /// <summary>
    /// Which node the current stroke has taken on each floor, and how near it came to
    /// it. One per floor, replaceable by a nearer approach — see
    /// <see cref="OnDrawingPoint" />. Holds only this stroke's own picks, and is
    /// emptied when the stroke ends.
    /// </summary>
    private readonly Dictionary<int, (string Id, float Distance)> _strokeFloors = [];

    /// <summary>The pin set as last drawn, so a redraw can tell a change from a repeat.</summary>
    private string _pinSignature = PinsChangedSentinel;

    /// <summary>Forces the next redraw to count as a change; equals the empty set's own signature, which is harmless since nothing is then shown.</summary>
    private const string PinsChangedSentinel = "";

    private string _mapKey = "";
    private IReadOnlyList<string>? _lockedRouteIds;
    private int _hotRoute = -1;
    private int _lockedRoute = -1;

    /// <summary>The drawn route under the mouse, and whether a pinned node is too.</summary>
    private int _pointerRoute = -1;
    private int _pointerBackdrop = -1;
    private bool _overPin;

    /// <summary>
    /// How near the mouse must pass a drawn line to pick it — far tighter than the
    /// quill's <c>SnapRadius</c>, which is scaled to a node and made neighbouring
    /// routes impossible to tell apart.
    /// </summary>
    private const float HoverRadius = 14f;

    /// <summary>
    /// How near the eraser must pass a drawn step to cut it. Wider than the mouse's
    /// hover radius, because this one is dragged rather than aimed, but still narrow
    /// enough that rubbing one line out leaves the line beside it standing.
    /// </summary>
    private const float EraseRadius = 26f;

    public PathingView(NMapScreen screen)
    {
        _screen = screen;
        var theMap = screen.GetNode<Control>("TheMap");
        var points = screen.GetNode<Control>("TheMap/Points");
        _overlay = new PathOverlay(theMap, points);
        _navigator = new NodeNavigator(screen);
        _toolbar = new MapToolbar(screen);
        _zoom = new MapZoom(screen, theMap, _toolbar.Root, NodeCenters);
        // Zoomed out is the controller mode: the whole map is visible, scrolling is
        // frozen, and the d-pad walks the node grid with a gold cursor ring. The
        // rotated view re-wires the grid so right means bossward, and the node icons
        // counter-spin so they stay upright while the map turns under them.
        _zoom.Toggled += instant => Guard.Run("Syncing map navigation", () =>
        {
            _navigator.SetNodes(BuildNavNodes(), _zoom.Rotated);
            _navigator.SetActive(_zoom.Zoomed, takeFocus: !ToolbarHasFocus());
            if (!_zoom.Zoomed)
                _overlay.HideCursor();
            SyncIconRotation(instant);
            // After the deferred focus grab, so the tip that grab conjures dies too.
            Callable.From(() => Guard.Run("Hiding stale map hover tips", HideNodeHoverTips))
                .CallDeferred();
        });

        // The replacement legend covers the native one; the native stays hidden and
        // its hotkey is rerouted here for as long as this view lives.
        _legend = new RouteLegendPanel(screen);
        _nativeLegend = screen.GetNodeOrNull<Control>("%MapLegend");
        if (_nativeLegend is { })
            _nativeLegend.Visible = false;
        _legend.TypeHot += type => Guard.Run("Highlighting a node type", () =>
            _screen.HighlightPointType(type));
        _legend.TypeCold += () => Guard.Run("Clearing the node type highlight", () =>
            _screen.HighlightPointType(MapPointType.Unassigned));
        _legend.ColumnHot += index => Guard.Run("Highlighting a legend route", () =>
        {
            _hotRoute = index;
            _legend.SetHot(index);
            ApplyHighlight();
        });
        _legend.ColumnCold += index => Guard.Run("Unhighlighting a legend route", () =>
        {
            _legend.SetHot(-1);
            OnRouteCold(index);
        });
        _legend.ColumnLockToggled += index => Guard.Run("Locking a legend route", () => OnRouteLockToggled(index));
        _navigator.NodeFocused += node => Guard.Run("Showing the map cursor", () =>
        {
            // The gold ring is a controller aid; with a mouse the pointer is the cursor.
            if (NControllerManager.Instance?.IsUsingDirectionalNavigation == true)
                _overlay.ShowCursor(node.Position + node.Size * 0.5f);
            else
                _overlay.HideCursor();
        });
        _options = new OptionsPanel(screen, _toolbar.Root);
        _help = new HelpTip(screen, _toolbar.Root);
        _autoPath = new AutoPathMenu(screen, _toolbar.Root);
        _autoPath.GoalChosen += goal => Guard.Run("Auto-pathing", () => ApplyAutoPath(goal));

        // The fourth tool in the game's own drawing tray. Deferred for the same reason
        // the stick's tool switch is: taking a tool up frees one input node and adds
        // another, and the tree cannot be rearranged while input is being processed —
        // a node added there is created but never entered.
        _pathTool = new PathToolButton(screen);
        _pathTool.Pressed += () =>
            Callable.From(() => Guard.Run("Taking up the path tool",
                () => MapScreenPatches.SelectPathTool(_screen))).CallDeferred();

        WireToolbarFocus();
        PathingOptions.Changed += OnOptionsChanged;
        _screen.Closed += OnScreenClosed;
    }

    private void OnOptionsChanged() => Guard.Run("Applying a settings change", () =>
    {
        if (!_screen.IsOpen)
            return;
        // Changing the marker size should show what it looks like, not leave the
        // player staring at a map that appears not to have changed.
        _pinSignature = PinsChangedSentinel;
        _zoom.Reapply();
        Refresh();
    });

    /// <summary>
    /// In the rotated view every node icon counter-spins a quarter turn, in step with
    /// the map's own tween, so the art reads upright while the map lies on its side.
    /// Base rotations (the game gives each node a small random tilt) are captured the
    /// first time a node is seen — always outside the rotated state, so never
    /// mid-animation — and restored on the way back.
    /// </summary>
    private void SyncIconRotation(bool instant = false)
    {
        if (_nodesByCoord is null)
            return;
        foreach (var node in _nodesByCoord.Values)
        {
            if (!GodotObject.IsInstanceValid(node))
                continue;
            if (!_iconBaseRotations.TryGetValue(node, out var baseRotation))
                _iconBaseRotations[node] = baseRotation = node.RotationDegrees;
            var target = _zoom.Rotated ? baseRotation - 90f : baseRotation;
            if (_iconTweens.TryGetValue(node, out var running) && running.IsValid())
                running.Kill();
            _iconTweens.Remove(node);
            if (instant)
            {
                node.RotationDegrees = target;
                continue;
            }
            var tween = node.CreateTween();
            tween.TweenProperty(node, "rotation_degrees", target, MapZoom.TweenDuration)
                .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            _iconTweens[node] = tween;
        }
    }

    /// <summary>Any node hover tip left open when the view changes stays until dismissed; kill them.</summary>
    private void HideNodeHoverTips()
    {
        if (_nodesByCoord is null)
            return;
        foreach (var node in _nodesByCoord.Values)
            if (GodotObject.IsInstanceValid(node))
                NHoverTipSet.Remove(node);
    }

    public bool Owns(NMapPoint point) => _screen.IsAncestorOf(point);

    /// <summary>
    /// Whether the pen in hand is the mod's. It is the game's own Drawing mode
    /// underneath — the cursor, the stroke plumbing and the input drivers are all
    /// native, and only what happens to each point differs — so this flag is the whole
    /// difference between planning a route and drawing on the map.
    /// </summary>
    public bool PathMode { get; private set; }

    public void SetPathMode(bool on)
    {
        if (PathMode == on)
            return;
        PathMode = on;
        SyncToolButtons();
    }

    /// <summary>
    /// Which pen the next mouse stroke picks up, decided at the press — before the tool
    /// it applies to exists.
    ///
    /// It is armed rather than set because the press does not always become a stroke:
    /// the game refuses to start one while input is disabled or the act animation is
    /// running, and lighting the toolbar there would show a tool in a hand that is
    /// empty. So the press only leaves a note, and the tool collects it.
    /// </summary>
    private bool _armedPath;

    public void ArmPathStroke(bool path) => _armedPath = path;

    /// <summary>
    /// A tool has just taken a drawing mode, so it takes the armed pen with it — and
    /// the note is spent either way, so a press that never became a stroke cannot be
    /// collected by some later tool that had nothing to do with it.
    /// </summary>
    public void TakeArmedPen(DrawingMode mode)
    {
        var armed = _armedPath;
        _armedPath = false;
        SetPathMode(armed && mode == DrawingMode.Drawing);
    }

    /// <summary>
    /// One lit icon in the drawing tray, never two. The quill's own state comes
    /// straight from the drawings, so this is correct in both directions: turning the
    /// path tool off hands the quill its light back if it is the one that is out.
    /// </summary>
    public void SyncToolButtons() => Guard.Run("Lighting the tool in hand", () =>
    {
        _pathTool.SetSelected(PathMode);
        if (_screen.GetNodeOrNull<NMapDrawButton>("%DrawButton") is { } quill)
            quill.SetIsDrawing(
                !PathMode && _screen.Drawings.GetLocalDrawingMode() == DrawingMode.Drawing);
    });

    /// <summary>
    /// Whether focus is on something of the mod's own. Anything that reacts to a
    /// button press has to leave focus where it was, or a controller player is thrown
    /// back to the map after every press and the second one moves them a node.
    /// </summary>
    private bool ToolbarHasFocus()
    {
        if (_screen.GetViewport()?.GuiGetFocusOwner() is not { } focused)
            return false;
        return _toolbar.Root.IsAncestorOf(focused) || _autoPath.OwnsFocus(focused);
    }

    /// <summary>A stroke finished, so the next one starts with no node behind it.</summary>
    public void OnStrokeEnded()
    {
        _lastDrawn = null;
        // The next stroke picks its own node on each floor, and may disagree with this
        // one — which is how a second pass corrects a first.
        _strokeFloors.Clear();
        // And with no ink behind it either, or the next stroke's first length is drawn
        // from wherever this one happened to stop.
        _lastTrailMark = null;
        _overlay.EndTrail();
    }

    /// <summary>The mouse took over; the controller's focus ring is no longer the cursor.</summary>
    public void OnPointerUsed() => _overlay.HideCursor();

    /// <summary>
    /// The mouse moved over the map. Two things answer it: a pinned node under the
    /// pointer brings the markers back — asking where the plan goes should be enough,
    /// without having to change it — and a drawn route under the pointer lights its
    /// legend column, the mirror of hovering the column to light the route.
    /// </summary>
    public void OnPointerMoved(Vector2 globalPoint) => Guard.Run("Map hover", () =>
    {
        if (!_screen.IsOpen || _legend.Covers(globalPoint))
            return;
        if (MapPointFromGlobal(globalPoint) is not { } point)
            return;

        var overPin = _pins.Ids
            .Select(EndpointOf).OfType<Vector2>()
            .Any(center => center.DistanceTo(point) <= SnapRadius);
        if (overPin != _overPin)
        {
            _overPin = overPin;
            if (overPin)
                _overlay.PulsePins();
        }

        // Only the line between two nodes answers the pointer. A drawing tool in hand
        // means the mouse is drawing rather than pointing, and highlighting whatever
        // the stroke crosses fights the gesture; a node under the cursor is something
        // you are about to click, and lighting a route through it makes the map twitch
        // on the way to every pin.
        var picking = _screen.Drawings.GetLocalDrawingMode() == DrawingMode.None
            && !OverNode(point);
        var route = picking ? RouteNear(point) : -1;
        var backdrop = picking && route < 0 ? BackdropNear(point) : -1;
        if (route == _pointerRoute && backdrop == _pointerBackdrop)
            return;
        _pointerRoute = route;
        _pointerBackdrop = backdrop;

        _legend.SetHot(route);
        ApplyHighlight();
        if (backdrop >= 0)
        {
            _overlay.ShowTrace(Polyline(_backdropRoutes[backdrop]));
            _legend.SetPreview(PathOverlay.TraceColor, LegendCounts(_backdropRoutes[backdrop]));
        }
        else
        {
            _overlay.ClearTrace();
            _legend.ClearPreview();
        }
    });

    /// <summary>
    /// Hover deepens a route's own colour; a locked one goes to ink. The pointer wins
    /// over the legend, since it is the more recent statement of interest.
    /// </summary>
    private void ApplyHighlight()
    {
        if (_pointerRoute >= 0)
            _overlay.SetHighlight(_pointerRoute, PathOverlay.Emphasis.Hover);
        else if (_hotRoute >= 0)
            _overlay.SetHighlight(_hotRoute, PathOverlay.Emphasis.Hover);
        else
            _overlay.SetHighlight(_lockedRoute, PathOverlay.Emphasis.Lock);
    }

    /// <summary>
    /// The coloured route the point lies on, nearest first, or -1 for none. Tested
    /// against where each route is actually **drawn**, sideways of the nodes it joins
    /// — against the shared centreline the routes all coincide and picking one of two
    /// neighbours is a coin toss.
    /// </summary>
    private int RouteNear(Vector2 point)
    {
        var best = (Index: -1, Distance: float.MaxValue);
        for (var i = 0; i < _shownRoutes.Count; i++)
        {
            var shift = PathOverlay.RouteShift(i, _shownRoutes.Count);
            var centers = Polyline(_shownRoutes[i]).Select(center => center + shift).ToList();
            for (var step = 1; step < centers.Count; step++)
            {
                var distance = DistanceToSegment(point, centers[step - 1], centers[step]);
                if (distance <= HoverRadius && distance < best.Distance)
                    best = (i, distance);
            }
        }
        return best.Index;
    }

    /// <summary>
    /// The uncoloured route under the point. The backdrop is drawn as merged edges, so
    /// several routes can share the step being hovered; the first one is as good an
    /// answer as any, and beats refusing to answer at all.
    /// </summary>
    private int BackdropNear(Vector2 point)
    {
        for (var i = 0; i < _backdropRoutes.Count; i++)
        {
            var centers = Polyline(_backdropRoutes[i]);
            for (var step = 1; step < centers.Count; step++)
                if (DistanceToSegment(point, centers[step - 1], centers[step]) <= HoverRadius)
                    return i;
        }
        return -1;
    }

    /// <summary>
    /// A route as drawn, whole — including the leg out of the node the player stands
    /// on. That leg was briefly left off while travel was live, to keep the mod from
    /// drawing over the game's own "you may move here" marker; it read as the plan
    /// having a hole in it, so it is back. Hit testing goes through here too, so what
    /// can be hovered is exactly what is on screen.
    /// </summary>
    private List<Vector2> Polyline(IReadOnlyList<string> route) =>
        route.Select(EndpointOf).OfType<Vector2>().ToList();

    /// <summary>
    /// Whether the pointer is on a map node rather than on the run between two. Sized
    /// from the node's own rect, so it tracks whatever the game draws rather than a
    /// guess that drifts when the art changes.
    /// </summary>
    private bool OverNode(Vector2 point)
    {
        if (_nodesByCoord is null)
            return false;
        foreach (var node in _nodesByCoord.Values)
        {
            if (!GodotObject.IsInstanceValid(node))
                continue;
            var center = node.Position + node.Size * 0.5f;
            if (center.DistanceTo(point) <= Math.Max(node.Size.X, node.Size.Y) * 0.5f)
                return true;
        }
        return false;
    }

    /// <summary>
    /// A global point in the map's own space. Scale makes this less trivial than it
    /// looks: only an affine inverse survives the zoom's non-orthonormal basis.
    /// </summary>
    private Vector2? MapPointFromGlobal(Vector2 globalPoint) =>
        _screen.GetNodeOrNull<Control>("TheMap") is { } theMap
            ? theMap.GetGlobalTransform().AffineInverse() * globalPoint
            : null;

    public bool ZoomActive => _zoom.Zoomed;

    public void ToggleZoom() => _zoom.Toggle();

    /// <summary>
    /// The path tool's keyboard shortcut, live only while the map is. The manager keys
    /// bindings by action and runs the most recent one, so it is pushed on the way in
    /// and taken off on the way out rather than held for the screen's whole lifetime —
    /// the map screen's root stays in the tree after the map closes, and a shortcut that
    /// outlived the map would fire over combat.
    /// </summary>
    private void OnPathToolHotkey() => Guard.Run("The path tool shortcut", () =>
    {
        // The manager's own blocking-screen mechanism only covers the game's actions,
        // so a mod action would still reach here from under a pause menu. Asking whether
        // the map is the screen being played is the same test the mod's other hotkeys use.
        if (!_screen.IsOpen || !ActiveScreenContext.Instance.IsCurrent(_screen))
            return;
        MapScreenPatches.SelectPathTool(_screen);
    });

    private void ListenForHotkey(bool listening) => Guard.Run("Listening for the path tool shortcut", () =>
    {
        if (NHotkeyManager.Instance is not { } hotkeys)
            return;
        if (listening)
            hotkeys.PushHotkeyReleasedBinding(PathToolHotkey.Action, OnPathToolHotkey);
        else
            hotkeys.RemoveHotkeyReleasedBinding(PathToolHotkey.Action, OnPathToolHotkey);
    });

    /// <summary>Every map open starts in the normal view, never zoomed out.</summary>
    public void OnOpened()
    {
        _zoom.Reset();
        ListenForHotkey(true);
        _toolbar.SetVisible(true);
        _zoom.SetButtonVisible(true);
        _legend.SetShellVisible(true);
        _options.SetShellVisible(true);
        _help.SetShellVisible(true);
        _autoPath.SetShellVisible(true);
        Refresh();
        // Deferred: the node rects this frames against are only final after a layout
        // pass, and framing on pre-layout positions puts the whole act off screen.
        Callable.From(() => Guard.Run("Opening the map in its usual view", _zoom.ShowInitialView))
            .CallDeferred();
    }

    /// <summary>
    /// The native legend hotkey, rerouted: first press lands on the icon column,
    /// pressing it again returns focus to the map — same toggle the native handler had.
    /// </summary>
    public void ToggleLegendFocus()
    {
        var focused = _screen.GetViewport()?.GuiGetFocusOwner();
        if (_legend.OwnsFocus(focused))
        {
            _screen.DefaultFocusedControl?.CallDeferred(Control.MethodName.GrabFocus);
            return;
        }
        _legend.FirstFocus?.CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <summary>The native Clear button wipes the quill drawings; it wipes this too.</summary>
    public void ClearPins()
    {
        _pins.Clear();
        _cut.Clear();
        _lastDrawn = null;
        _strokeFloors.Clear();
        _hotRoute = -1;
        _lockedRoute = -1;
        if (_screen.IsOpen)
            Refresh();
    }

    public void OnMapChanged()
    {
        _zoom.Reset();
        _iconBaseRotations.Clear();
        _iconTweens.Clear();
        _adapter = null;
        _pins.Clear();
        _cut.Clear();
        _lastDrawn = null;
        _strokeFloors.Clear();
        if (_screen.IsOpen)
            Refresh();
    }

    public void OnMapPointClicked(NMapPoint point)
    {
        if (!_screen.IsOpen || _screen.IsTraveling)
            return;
        if (_screen.Drawings.GetLocalDrawingMode() != DrawingMode.None)
            return;
        if (_adapter is null)
            return;

        var id = MapGraphAdapter.IdOf(point.Point);
        if (!_pinnable.Contains(id))
            return;

        // Selecting a node says it belongs in the plan, so it takes with it any step
        // the eraser had cut at it — the same right the quill has when drawn across.
        if (!_pins.IsSelected(id))
            _cut.RemoveWhere(edge => edge.From == id || edge.To == id);
        _pins.Toggle(id);
        Refresh();
    }

    /// <summary>
    /// Double-click: every pinnable node of the clicked node's kind at once — "show
    /// me the elites" as one gesture. If the rest of the kind is already pinned it
    /// unpins them all instead, so the gesture toggles. The clicked node itself is
    /// judged by the others, because the double-click's own first click already
    /// flipped it.
    /// </summary>
    public void OnMapPointDoubleClicked(NMapPoint point)
    {
        if (!_screen.IsOpen || _screen.IsTraveling)
            return;
        if (_screen.Drawings.GetLocalDrawingMode() != DrawingMode.None)
            return;
        if (_adapter is null)
            return;

        var id = MapGraphAdapter.IdOf(point.Point);
        if (!_pinnable.Contains(id))
            return;

        var kind = NormalizedKind(_adapter.Graph.Node(id).RoomKind);
        var targets = _pinnable
            .Where(p => NormalizedKind(_adapter.Graph.Node(p).RoomKind) == kind)
            .ToList();
        var others = targets.Where(t => t != id).ToList();
        var unpinAll = others.Count > 0 && others.All(_pins.IsSelected);

        foreach (var target in targets)
        {
            if (_pins.IsSelected(target) == !unpinAll)
                continue;
            _pins.Toggle(target);
        }
        Refresh();
    }

    /// <summary>
    /// A point of a drawn stroke, in Drawing path mode. Drawing pins whatever node the
    /// stroke passes near — the gesture is the player's, the line is the mod's — and
    /// pins are only added, so a stroke wandering back over itself cannot undo itself.
    /// Erasing is the reverse: it lifts a pin the stroke touches, or the pin at the far
    /// end of a drawn link it crosses, so rubbing out a line takes the plan with it.
    /// The game never draws any of these points: the stroke it would have drawn them
    /// into is suppressed at its source, in <c>MapDrawingBeginPatch</c>.
    /// </summary>
    public void OnDrawingPoint(Control drawings, Vector2 pointInDrawings, bool erasing)
    {
        if (!_screen.IsOpen || _adapter is null || _nodesByCoord is null)
            return;
        if (MapPoint() is not { } cursor)
            return;

        // The node the player is standing on is a candidate too, even though it can
        // never be pinned. It is one end of the first step, and a stroke has to be able
        // to name it — see below.
        var caught = _pinnable.Append(_startId)
            .Select(id => (Id: id, Center: EndpointOf(id)))
            .Where(candidate => candidate.Center is not null)
            .Select(candidate => (candidate.Id, Distance: candidate.Center!.Value.DistanceTo(cursor)))
            .Where(candidate => candidate.Distance <= SnapRadius)
            .OrderBy(candidate => candidate.Distance)
            .Select(candidate => ((string Id, float Distance)?)candidate)
            .FirstOrDefault();
        var nearest = caught?.Id;

        if (!erasing)
        {
            // Before the early return: the ink answers the stroke, not the outcome of
            // it, so it has to appear over open map as much as over a node it catches.
            LeaveTrailMark(cursor);

            if (nearest is null)
                return;

            // Drawing from one node onto the next puts back the step between them if
            // the eraser had cut it: the quill says "yes, this one", and it has to be
            // able to undo the eraser. Only that one pair — every other cut stands.
            var restored = _lastDrawn is { } previous
                && (_cut.Remove((previous, nearest)) | _cut.Remove((nearest, previous)));
            _lastDrawn = nearest;

            // Standing on it counts as selecting it, so the first step needs no pin —
            // but it does need the stroke to have passed through here, or `_lastDrawn`
            // is still null when the stroke reaches the node above and the first step
            // is the one step the quill can never put back. That left an erased first
            // leg only recoverable by deselecting the node above it and starting again.
            if (nearest == _startId)
            {
                if (restored)
                    Refresh();
                return;
            }

            var floor = _adapter.Graph.Node(nearest).Row;
            if (_pins.IsSelected(nearest))
            {
                // Keep the closest approach this stroke made to its own pick, so a
                // later contender has to beat how near the stroke actually came rather
                // than merely where it first came into reach.
                if (_strokeFloors.TryGetValue(floor, out var mine) && mine.Id == nearest
                    && caught!.Value.Distance < mine.Distance)
                    _strokeFloors[floor] = (nearest, caught.Value.Distance);
                if (restored)
                    Refresh();
                return;
            }

            // One node per floor per stroke, and it is the one the stroke came nearest
            // to. A route takes a single node from each floor, so a stroke should too;
            // passing within the pen's reach of a node is not the same as meaning it,
            // and with a generous reach a crowded floor used to give up two or three of
            // them, forking the plan with nodes the line was never really near. Coming
            // nearer to another one later replaces the pick rather than adding to it,
            // so a stroke that starts ambiguously and then commits still ends up saying
            // one thing. Only this stroke's own picks are ever displaced: a pin already
            // on the map was put there deliberately, and same-floor pins are meaningful.
            if (_strokeFloors.TryGetValue(floor, out var held) && held.Id != nearest)
            {
                if (caught!.Value.Distance >= held.Distance)
                {
                    if (restored)
                        Refresh();
                    return;
                }
                _pins.Remove(held.Id);
                _cut.RemoveWhere(edge => edge.From == held.Id || edge.To == held.Id);
            }

            _strokeFloors[floor] = (nearest, caught!.Value.Distance);
            _pins.Toggle(nearest);
            Refresh();
            return;
        }

        // The eraser takes off one node — the one under it, or the nearer end of the
        // link being rubbed out. It used to remove the pin a link *led to*, which on a
        // sparse plan is the anchor for everything between two waypoints, so rubbing
        // On a node, the node goes and its links with it. Between two nodes, only that
        // one step is cut — which is the whole reason the plan is kept as edges. The
        // node test uses the node's own rect, so the middle of a run is unambiguously
        // "between".
        if (SelectedNodeAt(cursor) is { } node)
        {
            _pins.Remove(node);
            _cut.RemoveWhere(edge => edge.From == node || edge.To == node);
        }
        else if (EdgeNear(cursor) is { } edge)
        {
            _cut.Add(edge);
        }
        else
        {
            return;
        }
        Refresh();

        // Getting back to map space is subtler than it looks. The game hands us
        // `Drawings.GetGlobalTransform().Inverse() * globalPoint`, and Godot's
        // Transform2D.Inverse() is only correct for an orthonormal basis — rotation
        // is fine, scale is not. Vanilla never scales the map so their conversion
        // holds; the zoom does, and their point stops meaning what it says. Undoing
        // their exact matrix with a true inverse recovers the original global point
        // whatever the basis, and that works for the controller's drawing cursor as
        // much as the mouse.
        Vector2? MapPoint()
        {
            var theMap = _screen.GetNodeOrNull<Control>("TheMap");
            if (theMap is null || !GodotObject.IsInstanceValid(drawings))
                return null;
            var asTheGameConverted = drawings.GetGlobalTransform().Inverse();
            var global = asTheGameConverted.AffineInverse() * pointInDrawings;
            return theMap.GetGlobalTransform().AffineInverse() * global;
        }
    }

    /// <summary>
    /// The pins, plus the act's end nodes once the plan already reaches the floor
    /// below them. Drawing to one short of the end is not an ambiguous gesture, and
    /// making the player trace that last step adds nothing — but it stays out of
    /// <c>_pins</c>, so it is never persisted and the eraser has nothing of its own
    /// to take off.
    /// </summary>
    private IReadOnlyCollection<string> WithLastStep(IReadOnlySet<string> terminals)
    {
        if (_adapter is null || _pins.Count == 0 || terminals.Count == 0)
            return _pins.Ids;

        var endFloor = terminals.Max(id => _adapter.Graph.Node(id).Row);
        var planTop = _pins.Ids
            .Where(_adapter.Graph.Contains)
            .Select(id => _adapter.Graph.Node(id).Row)
            .DefaultIfEmpty(-1)
            .Max();
        if (planTop != endFloor - 1)
            return _pins.Ids;

        // Only an end the plan actually steps onto is added: with no pathfinding
        // left, an end nothing selected reaches would just sit there unattached.
        var reaching = _pins.Ids.ToHashSet();
        foreach (var id in terminals)
        {
            if (_pins.Ids.Any(pin => _adapter.Graph.Successors(pin).Contains(id)))
                reaching.Add(id);
        }
        return reaching;
    }

    /// <summary>A selected node under the point, sized from its own rect.</summary>
    private string? SelectedNodeAt(Vector2 point)
    {
        if (_adapter is null || _nodesByCoord is null)
            return null;
        foreach (var id in _pins.Ids)
        {
            if (!_adapter.TryGetPoint(id, out var mapPoint)
                || !_nodesByCoord.TryGetValue(mapPoint.coord, out var node)
                || !GodotObject.IsInstanceValid(node))
                continue;
            var center = node.Position + node.Size * 0.5f;
            if (center.DistanceTo(point) <= Math.Max(node.Size.X, node.Size.Y) * 0.5f)
                return id;
        }
        return null;
    }

    /// <summary>The drawn step nearest the point, for the eraser to cut.</summary>
    private (string From, string To)? EdgeNear(Vector2 point)
    {
        var best = (Edge: ((string, string)?)null, Distance: float.MaxValue);
        foreach (var link in _links)
        {
            for (var i = 1; i < link.Count; i++)
            {
                if (EndpointOf(link[i - 1]) is not { } from || EndpointOf(link[i]) is not { } to)
                    continue;
                var distance = DistanceToSegment(point, from, to);
                if (distance <= EraseRadius && distance < best.Distance)
                    best = ((link[i - 1], link[i]), distance);
            }
        }
        return best.Edge;
    }

    private static float DistanceToSegment(Vector2 point, Vector2 from, Vector2 to) =>
        point.DistanceTo(ClosestPointOnSegment(point, from, to));

    private static Vector2 ClosestPointOnSegment(Vector2 point, Vector2 from, Vector2 to)
    {
        var span = to - from;
        var lengthSquared = span.LengthSquared();
        if (lengthSquared <= 0.001f)
            return from;
        var t = Mathf.Clamp((point - from).Dot(span) / lengthSquared, 0f, 1f);
        return from + span * t;
    }

    /// <summary>
    /// Ink under the pen while the path tool draws: the length just travelled, drawn
    /// with the game's own line, fading and sliding onto the map step it is nearest.
    ///
    /// Lengths are laid by distance travelled rather than per event, because the funnel
    /// this comes through fires once per motion event — so a slow careful stroke and a
    /// fast flick would otherwise leave wildly different amounts of ink, and a
    /// stationary pen would pile them up on one spot.
    /// </summary>
    private void LeaveTrailMark(Vector2 cursor)
    {
        if (!PathingOptions.DrawingTrail)
            return;
        // The first point of a stroke has nothing behind it to draw from; it becomes
        // the anchor the next one measures against.
        if (_lastTrailMark is not { } previous)
        {
            _lastTrailMark = cursor;
            return;
        }
        if (previous.DistanceTo(cursor) < Math.Max(1f, PathingOptions.TrailSpacing))
            return;
        _lastTrailMark = cursor;

        // Each point carries its own destination. The overlay bends the line onto them
        // rather than sliding it, which is what makes the snap range mean what it says.
        _overlay.AddTrailPoint(cursor, SnapToEdge(cursor) ?? cursor, DrawingInk());
    }

    /// <summary>
    /// The nearest point on any step of the map, or null if the pen is nowhere near
    /// one. Null rather than a far-away point on purpose: ink that flies across open
    /// parchment to reach a line claims a connection the stroke is not making.
    /// </summary>
    private Vector2? SnapToEdge(Vector2 point)
    {
        if (!PathingOptions.TrailSnap)
            return null;
        var best = (Point: (Vector2?)null, Distance: PathingOptions.TrailSnapRadius);
        foreach (var (from, to) in _edgeSegments)
        {
            var closest = ClosestPointOnSegment(point, from, to);
            var distance = point.DistanceTo(closest);
            if (distance < best.Distance)
                best = (closest, distance);
        }
        return best.Point;
    }

    /// <summary>
    /// The colour the player's own character draws the map in, which is what the game
    /// would have inked this stroke with. Black is <c>CharacterModel</c>'s own default
    /// and reads on parchment, so it is the right answer when there is no run to ask.
    /// </summary>
    private static Color DrawingInk() => Guard.Run("Reading the drawing colour", () =>
        LocalContext.GetMe(RunManager.Instance?.DebugOnlyGetState())?.Character.MapDrawingColor
            ?? Colors.Black,
        Colors.Black);

    /// <summary>Where the trail last reached, and where the next length starts from.</summary>
    private Vector2? _lastTrailMark;

    /// <summary>
    /// Every step the map draws, as a segment — the whole graph, not the routes still
    /// open ahead. The trail snaps to what is **on screen**, and the map goes on
    /// drawing the steps behind the marker and the legs into the boss long after no
    /// complete route runs through them; taking the set from the surviving routes left
    /// the ink refusing to snap to lines the player could plainly see.
    /// </summary>
    private IReadOnlyList<(Vector2 From, Vector2 To)> _edgeSegments = [];

    private List<(Vector2 From, Vector2 To)> MapSegments()
    {
        var segments = new List<(Vector2, Vector2)>();
        if (_adapter is null)
            return segments;
        foreach (var node in _adapter.Graph.Nodes)
        {
            if (EndpointOf(node.Id) is not { } from)
                continue;
            foreach (var next in _adapter.Graph.Successors(node.Id))
                if (EndpointOf(next) is { } to)
                    segments.Add((from, to));
        }
        return segments;
    }

    /// <summary>How near a stroke must pass a node to catch it, in map units.</summary>
    private static float SnapRadius => PathingOptions.SnapRadius;

    /// <summary>"?" nodes come in two kinds that mean the same thing to a player.</summary>
    private static string NormalizedKind(string kind) =>
        kind == nameof(MapPointType.Unassigned) ? nameof(MapPointType.Unknown) : kind;

    public void Refresh()
    {
        var state = RunManager.Instance?.DebugOnlyGetState();
        var map = state?.Map;
        if (state is null || map is null)
        {
            Clear();
            return;
        }

        if (_adapter?.Map != map)
        {
            _adapter = MapGraphAdapter.Build(map);
            _mapKey = PinStore.KeyFor(_adapter.Graph);
            _pins.Clear();
            _cut.Clear();
            _lastDrawn = null;
            _lockedRouteIds = null;
            // Same map as a previous session: the pins belong to it, bring them back.
            if (PinStore.Load() is { } saved && saved.MapKey == _mapKey)
            {
                foreach (var id in saved.Pins)
                    _pins.Toggle(id);
                _cut = [.. (saved.Cut ?? []).Select(PinStore.ParseEdge).OfType<(string, string)>()];
                _lockedRouteIds = saved.LockedRoute;
            }
        }

        _nodesByCoord = ReadPointDictionary();

        var start = state.CurrentMapPoint ?? map.StartingMapPoint;
        var startId = MapGraphAdapter.IdOf(start);
        if (!_adapter.Graph.Contains(startId))
        {
            Clear();
            return;
        }

        var pathSet = PathSolver.EnumeratePaths(_adapter.Graph, [startId]);
        var routes = PathSolver.TrimTails(pathSet.Paths,
            id => _adapter.Graph.Node(id).RoomKind == nameof(MapPointType.Boss));

        // Only nodes that lie ahead on some possible route can be pinned; the current
        // node (index 0) and everything already behind the marker never qualify.
        // Pinnability comes from the full routes, so manual mode can still reach
        // every node ahead even when it draws only as far as the plan goes.
        _completeRoutes = routes;
        _edgeSegments = MapSegments();
        _startId = startId;
        _pinnable = routes.SelectMany(route => route.Skip(1)).ToHashSet();
        _pins.RetainWhere(_pinnable.Contains);
        // A cut only means anything while both its ends are in the plan. Letting one
        // outlive a deselected end is invisible state of exactly the kind the node
        // blocks used to be: two adjacent nodes selected, no line between them, and
        // nothing on screen to say why. Deselecting either end forgets the cut, so
        // selecting them again draws the step.
        _cut.RemoveWhere(edge =>
            !(edge.From == startId || _pins.IsSelected(edge.From)) || !_pins.IsSelected(edge.To));

        // `routes` are the complete walks, so their last nodes are exactly where
        // the act ends (the boss having already been trimmed off).
        var terminals = routes.Select(route => route[^1]).ToHashSet();
        bool Complete(IReadOnlyList<string> route) =>
            route.Count > 1 && terminals.Contains(route[^1]);

        // The plan is exactly the steps between selected neighbours — no
        // pathfinding, so nothing is ever drawn that the player did not point at.
        // No orphan sweep either: a selection nothing joins is simply a selection
        // nothing joins, and it says so by sitting there with no line on it.
        _links = PathSolver.ConnectSelected(
            _adapter.Graph, startId, WithLastStep(terminals), _cut);
        var assembled = PathSolver.AssembleRoutes(_links);

        // Everything the plan allows is still drawn; the legend's worth of best
        // complete ones get a colour and a column. Ranked by what a route is
        // usually chosen for: elites, then fires, then shops.
        var ranked = assembled
            .Where(Complete)
            .Select(route => (Route: route, Counts: LegendCounts(route)))
            .OrderByDescending(entry => entry.Counts[5])
            .ThenByDescending(entry => entry.Counts[3])
            .ThenByDescending(entry => entry.Counts[1])
            .Select(entry => entry.Route)
            .ToList();
        _shownRoutes = ranked.Take(PathSolver.LegendThreshold).ToList();
        _backdropRoutes = ranked.Skip(PathSolver.LegendThreshold)
            .Concat(assembled.Where(route => !Complete(route)))
            .ToList();
        _hotRoute = -1;
        // The redraw clears the trace with the dots it was drawn over, so forget what
        // the pointer was on: the next movement decides again from scratch.
        _pointerRoute = -1;
        _pointerBackdrop = -1;

        // A locked route survives recomputes (and restarts) as long as it still
        // exists — including as the tail of itself after advancing a floor along it.
        // Deviating off it means the new position is not on the stored route, no
        // suffix matches, and the lock clears.
        _lockedRoute = IndexOfRoute(_lockedRouteIds);
        _lockedRouteIds = _lockedRoute >= 0 ? _shownRoutes[_lockedRoute] : null;

        UpdateOverlay();
        UpdateLegend();
        // With travel live, the game is already marking the reachable next nodes —
        // a pin ring on one of them reads as "you are here". The pin keeps filtering;
        // only its ring yields until travel resolves the step.
        var signature = string.Join("|", _pins.Ids.OrderBy(id => id, StringComparer.Ordinal));
        var pinsChanged = signature != _pinSignature;
        _pinSignature = signature;
        _overlay.ShowPins(
            _pins.Ids
                .Where(id => !(_screen.IsTravelEnabled && IsTravelableNode(id)))
                .Select(EndpointOf).OfType<Vector2>(),
            pinsChanged);
        ApplyHighlight();
        _legend.SetLocked(_lockedRoute);
        _navigator.SetNodes(BuildNavNodes(), _zoom.Rotated);
        PersistState();
    }

    private int IndexOfRoute(IReadOnlyList<string>? ids)
    {
        if (ids is null)
            return -1;
        for (var i = 0; i < _shownRoutes.Count; i++)
            if (IsSuffixOf(_shownRoutes[i], ids))
                return i;
        return -1;
    }

    /// <summary>Whether <paramref name="route" /> is the tail of <paramref name="stored" />.</summary>
    private static bool IsSuffixOf(IReadOnlyList<string> route, IReadOnlyList<string> stored)
    {
        if (route.Count > stored.Count)
            return false;
        var offset = stored.Count - route.Count;
        for (var i = 0; i < route.Count; i++)
            if (stored[offset + i] != route[i])
                return false;
        return true;
    }

    private void PersistState()
    {
        if (_mapKey.Length == 0)
            return;
        PinStore.SaveIfChanged(new PinStore.Saved(
            _mapKey,
            _pins.Ids.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            _lockedRouteIds?.ToArray(),
            null,
            _cut.Select(PinStore.FormatEdge).OrderBy(e => e, StringComparer.Ordinal).ToArray()));
    }

    /// <summary>
    /// Every drawn node, for the d-pad grid — the whole map, not just surviving
    /// routes: weighing options means visiting nodes the current plan skips.
    /// </summary>
    private List<(NMapPoint Node, int Row, Vector2 Center)> BuildNavNodes()
    {
        var nodes = new List<(NMapPoint, int, Vector2)>();
        if (_nodesByCoord is null)
            return nodes;
        foreach (var (coord, node) in _nodesByCoord)
        {
            if (!GodotObject.IsInstanceValid(node))
                continue;
            nodes.Add((node, coord.row, node.Position + node.Size * 0.5f));
        }
        return nodes;
    }

    public void Dispose()
    {
        PathingOptions.Changed -= OnOptionsChanged;
        ListenForHotkey(false);
        if (GodotObject.IsInstanceValid(_screen))
            _screen.Closed -= OnScreenClosed;
        _autoPath.Dispose();
        _pathTool.Dispose();
        _help.Dispose();
        _options.Dispose();
        _toolbar.Dispose();
        _navigator.Dispose();
        _zoom.Dispose();
        _overlay.Dispose();
        _legend.Dispose();
        if (_nativeLegend is { } nativeLegend && GodotObject.IsInstanceValid(nativeLegend))
            nativeLegend.Visible = true;
    }

    /// <summary>Every drawn node's centre, for the zoomed-out framing.</summary>
    private IReadOnlyList<Vector2> NodeCenters() =>
        _nodesByCoord is null
            ? []
            : _nodesByCoord.Values
                .Where(GodotObject.IsInstanceValid)
                .Select(node => node.Position + node.Size * 0.5f)
                .ToList();

    private void OnScreenClosed() => Guard.Run("Resetting on map close", () =>
    {
        _zoom.Reset();
        ListenForHotkey(false);
        _hotRoute = -1;
        // The screen root stays in the tree when the map closes — the game only hides
        // its own contents — so panels parented to it must hide themselves, or they
        // linger over combat and the settings menu.
        _legend.SetShellVisible(false);
        _options.SetShellVisible(false);
        _help.SetShellVisible(false);
        _autoPath.SetShellVisible(false);
        _toolbar.SetVisible(false);
        ApplyHighlight();
    });

    private void OnRouteCold(int index)
    {
        if (_hotRoute == index)
            _hotRoute = -1;
        ApplyHighlight();
    }

    private void OnRouteLockToggled(int index)
    {
        _lockedRoute = _lockedRoute == index ? -1 : index;
        _lockedRouteIds = _lockedRoute >= 0 ? _shownRoutes[_lockedRoute] : null;
        _legend.SetLocked(_lockedRoute);
        ApplyHighlight();
        PersistState();
    }

    private void UpdateOverlay()
    {
        // A plan with nothing finished yet is still a plan: the backdrop alone must
        // keep drawing, or half-drawn paths would vanish as they were being made.
        if (_shownRoutes.Count == 0 && _backdropRoutes.Count == 0)
        {
            _overlay.Clear();
        }
        else if (_shownRoutes.Count <= PathSolver.LegendThreshold)
        {
            var polylines = _shownRoutes.Select(route => Polyline(route).ToArray()).ToList();
            _overlay.ShowRoutes(polylines, EdgesOf(_backdropRoutes));
        }
        else
        {
            _overlay.ShowUnion(EdgesOf(_shownRoutes));
        }
    }

    /// <summary>Each route's steps as drawable segments, shared edges counted once.</summary>
    private IEnumerable<(Vector2 From, Vector2 To)> EdgesOf(
        IReadOnlyList<IReadOnlyList<string>> routes)
    {
        var edges = new HashSet<(string, string)>();
        foreach (var route in routes)
            for (var i = 1; i < route.Count; i++)
                edges.Add((route[i - 1], route[i]));

        return edges
            .Select(edge => (From: EndpointOf(edge.Item1), To: EndpointOf(edge.Item2)))
            .Where(edge => edge is { From: not null, To: not null })
            .Select(edge => (edge.From!.Value, edge.To!.Value));
    }

    private void UpdateLegend()
    {
        if (_shownRoutes.Count is > 0 and <= PathSolver.LegendThreshold)
            _legend.SetRoutes(_shownRoutes.Select((route, index) => (
                PathOverlay.RouteColors[index % PathOverlay.RouteColors.Length],
                $"{(char)('A' + index)}",
                LegendCounts(route))).ToList());
        else
            _legend.SetRoutes([]);
    }

    /// <summary>Counts in the legend's row order (the native legend's type order).</summary>
    private IReadOnlyList<int> LegendCounts(IReadOnlyList<string> route)
    {
        var counts = new Dictionary<string, int>();
        foreach (var id in route.Skip(1))
        {
            var kind = _adapter!.Graph.Node(id).RoomKind;
            counts[kind] = counts.GetValueOrDefault(kind) + 1;
        }
        return RouteLegendPanel.Rows
            .Select(row => row.Kinds.Sum(counts.GetValueOrDefault))
            .ToList();
    }

    private void Clear()
    {
        _zoom.Reset();
        _navigator.SetNodes([], false);
        _legend.SetRoutes([]);
        _shownRoutes = [];
        _backdropRoutes = [];
        _pinnable = [];
        _overlay.Clear();
    }

    /// <summary>Whether this pinned node is one the player could click to travel to right now.</summary>
    private bool IsTravelableNode(string id)
    {
        if (_adapter is null || _nodesByCoord is null)
            return false;
        if (!_adapter.TryGetPoint(id, out var point))
            return false;
        return _nodesByCoord.TryGetValue(point.coord, out var node) &&
            GodotObject.IsInstanceValid(node) && node.IsEnabled;
    }

    /// <summary>
    /// Where a route line meets a node, in <c>TheMap</c> space. Every map point scene
    /// root is center-anchored with a center pivot, so read at runtime (post-layout)
    /// the visual centre is position plus half size for all three node types. The
    /// game's <c>GetLineEndpoint</c> looks different only because it runs pre-layout
    /// during <c>SetMap</c> and corrects on the dot side; copying its raw-position
    /// case put these lines half a node up-left of the art.
    /// </summary>
    private Vector2? EndpointOf(string id)
    {
        if (_adapter is null || _nodesByCoord is null)
            return null;
        if (!_adapter.TryGetPoint(id, out var point))
            return null;
        if (!_nodesByCoord.TryGetValue(point.coord, out var node) ||
            !GodotObject.IsInstanceValid(node))
            return null;
        return node.Position + node.Size * 0.5f;
    }

    private Dictionary<MapCoord, NMapPoint> ReadPointDictionary() =>
        PointDictionaryField?.GetValue(_screen) as Dictionary<MapCoord, NMapPoint>
        ?? throw new InvalidOperationException(
            "NMapScreen._mapPointDictionary is gone; the game layout has moved.");

    /// <summary>
    /// Left to right along the button row, then down to the pull-down, then on into
    /// the legend. Without this the whole toolbar is mouse-only: nothing in it is
    /// reachable from the map or the legend with a d-pad.
    /// </summary>
    private void WireToolbarFocus() => Guard.Run("Wiring toolbar focus", () =>
    {
        var row = new[] { _help.Focusable, _options.Focusable, _zoom.Focusable }
            .OfType<Control>()
            .Where(GodotObject.IsInstanceValid)
            .ToList();
        var below = _autoPath.Focusable;
        for (var i = 0; i < row.Count; i++)
        {
            if (i > 0)
                row[i].FocusNeighborLeft = row[i].GetPathTo(row[i - 1]);
            if (i < row.Count - 1)
                row[i].FocusNeighborRight = row[i].GetPathTo(row[i + 1]);
            row[i].FocusNeighborBottom = row[i].GetPathTo(below);
        }
        if (row.Count > 0)
            below.FocusNeighborTop = below.GetPathTo(row[Math.Min(1, row.Count - 1)]);
        _legend.SetTopNeighbor(below);
        if (_legend.FirstFocus is { } legendTop)
            below.FocusNeighborBottom = below.GetPathTo(legendTop);
    });

    /// <summary>
    /// Replace the plan with the routes that collect the most of one thing. The map is
    /// cleared first: this is an answer to "show me the best X", not an addition to
    /// whatever was already drawn.
    /// </summary>
    private void ApplyAutoPath(AutoPathGoal goal)
    {
        if (_adapter is null || _completeRoutes.Count == 0)
            return;

        var row = goal.Row();
        var ranked = _completeRoutes
            .Select(route => (Route: route, Counts: LegendCounts(route)))
            .OrderByDescending(entry => entry.Counts[row])
            .ThenByDescending(entry => entry.Counts[5])
            .ThenByDescending(entry => entry.Counts[3])
            .ThenByDescending(entry => entry.Counts[1])
            .ToList();
        var best = ranked[0].Counts[row];
        var chosen = ranked
            .Where(entry => entry.Counts[row] == best)
            .Take(PathSolver.LegendThreshold)
            .Select(entry => entry.Route)
            .ToList();

        _pins.Clear();
        _cut.Clear();
        _lastDrawn = null;
        _strokeFloors.Clear();
        foreach (var id in chosen.SelectMany(route => route.Skip(1)).Distinct())
            if (_pinnable.Contains(id))
                _pins.Toggle(id);

        // Selecting every node of several routes also selects pairs those routes never
        // step between, and the edge model would draw them as extra routes nobody asked
        // for. Cutting them is what makes "the best five" mean exactly five.
        var wanted = new HashSet<(string, string)>();
        foreach (var route in chosen)
            for (var i = 1; i < route.Count; i++)
                wanted.Add((route[i - 1], route[i]));
        var selected = _pins.Ids.ToHashSet();
        selected.Add(_startId);
        foreach (var from in selected)
            foreach (var to in _adapter.Graph.Successors(from))
                if (selected.Contains(to) && !wanted.Contains((from, to)))
                    _cut.Add((from, to));

        _lockedRoute = -1;
        _lockedRouteIds = null;
        _pinSignature = PinsChangedSentinel;
        Refresh();
    }
}
