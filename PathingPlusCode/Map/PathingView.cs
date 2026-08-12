using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
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
    private readonly NodeNavigator _navigator;
    private readonly MapZoom _zoom;
    private readonly WaypointSelection _pins = new();
    private readonly Dictionary<NMapPoint, float> _iconBaseRotations = [];
    private Control? _nativeLegend;

    private MapGraphAdapter? _adapter;
    private Dictionary<MapCoord, NMapPoint>? _nodesByCoord;
    private IReadOnlyList<IReadOnlyList<string>> _shownRoutes = [];

    /// <summary>Manual-mode links between pinned floors; what the eraser rubs out.</summary>
    private IReadOnlyList<IReadOnlyList<string>> _links = [];

    /// <summary>Matching routes beyond the legend's five: drawn, but not coloured.</summary>
    private IReadOnlyList<IReadOnlyList<string>> _backdropRoutes = [];
    private HashSet<string> _pinnable = [];

    /// <summary>
    /// Nodes the eraser struck. Kept out of the plan *and* out of the solver's
    /// routing, so rubbing out a step removes that step instead of the solver
    /// re-linking its neighbours straight back through it.
    /// </summary>
    private HashSet<string> _blocked = [];

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
        _zoom.Toggled += () => Guard.Run("Syncing map navigation", () =>
        {
            _navigator.SetNodes(BuildNavNodes(), _zoom.Rotated);
            _navigator.SetActive(_zoom.Zoomed);
            if (!_zoom.Zoomed)
                _overlay.HideCursor();
            SyncIconRotation();
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
            _overlay.SetHighlight(index);
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
    private void SyncIconRotation()
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
            node.CreateTween()
                .TweenProperty(node, "rotation_degrees", target, MapZoom.TweenDuration)
                .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
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

        var route = RouteNear(point);
        var backdrop = route >= 0 ? -1 : BackdropNear(point);
        if (route == _pointerRoute && backdrop == _pointerBackdrop)
            return;
        _pointerRoute = route;
        _pointerBackdrop = backdrop;

        _legend.SetHot(route);
        _overlay.SetHighlight(route >= 0 ? route : _hotRoute >= 0 ? _hotRoute : _lockedRoute);
        if (backdrop >= 0)
            _overlay.ShowTrace(Polyline(_backdropRoutes[backdrop]));
        else
            _overlay.ClearTrace();
    });

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

    private List<Vector2> Polyline(IReadOnlyList<string> route) =>
        route.Select(EndpointOf).OfType<Vector2>().ToList();

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

    /// <summary>Every map open starts in the normal view, never zoomed out.</summary>
    public void OnOpened()
    {
        _zoom.Reset();
        _toolbar.SetVisible(true);
        _zoom.SetButtonVisible(true);
        _legend.SetShellVisible(true);
        _options.SetShellVisible(true);
        _help.SetShellVisible(true);
        Refresh();
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
        _blocked.Clear();
        _hotRoute = -1;
        _lockedRoute = -1;
        if (_screen.IsOpen)
            Refresh();
    }

    public void OnMapChanged()
    {
        _zoom.Reset();
        _iconBaseRotations.Clear();
        _adapter = null;
        _pins.Clear();
        _blocked.Clear();
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

        // A click is the plainest statement of intent there is, so it always wins over
        // an earlier erase. Without this, clicking a node the eraser had struck did
        // nothing visible at all: the pin went on, the block kept it out of the plan,
        // and the orphan sweep took it straight back off again.
        _blocked.Remove(id);
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
            if (!unpinAll)
                _blocked.Remove(target);
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

        var nearest = _pinnable
            .Select(id => (Id: id, Center: EndpointOf(id)))
            .Where(candidate => candidate.Center is not null)
            .Select(candidate => (candidate.Id, Distance: candidate.Center!.Value.DistanceTo(cursor)))
            .Where(candidate => candidate.Distance <= SnapRadius)
            .OrderBy(candidate => candidate.Distance)
            .Select(candidate => candidate.Id)
            .FirstOrDefault();

        if (!erasing)
        {
            // Drawing over an erased node takes it off the blocked list: the quill is
            // how you say "yes, this one" and it has to be able to undo the eraser.
            if (nearest is null)
                return;
            var unblocked = _blocked.Remove(nearest);
            if (!unblocked && _pins.IsSelected(nearest))
                return;
            if (!_pins.IsSelected(nearest))
                _pins.Toggle(nearest);
            Refresh();
            return;
        }

        // The eraser takes off one node — the one under it, or the nearer end of the
        // link being rubbed out. It used to remove the pin a link *led to*, which on a
        // sparse plan is the anchor for everything between two waypoints, so rubbing
        // one step took out a whole branch.
        var target = nearest is not null && OnPlan(nearest)
            ? nearest
            : NodeOfLinkNear(cursor);
        if (target is null)
            return;

        _pins.Remove(target);
        _blocked.Add(target);
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

    /// <summary>Whether this node is part of the plan as currently drawn.</summary>
    private bool OnPlan(string id) => _links.Any(link => link.Contains(id));

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

        // Unreachable ends contribute nothing, so adding them all is safe: only the
        // one the drawn plan can actually get to produces a link.
        var reaching = _pins.Ids.ToHashSet();
        foreach (var id in terminals.Where(id => !_blocked.Contains(id)))
            reaching.Add(id);
        return reaching;
    }

    /// <summary>
    /// The node to lift when the eraser is on a link rather than on a node: whichever
    /// end of the crossed step is nearer the cursor. Erasing a step should cost that
    /// step, and the nearer end is the one the player is pointing at.
    /// </summary>
    private string? NodeOfLinkNear(Vector2 point)
    {
        var best = (Id: (string?)null, Distance: float.MaxValue);
        foreach (var link in _links)
        {
            for (var i = 1; i < link.Count; i++)
            {
                if (EndpointOf(link[i - 1]) is not { } from || EndpointOf(link[i]) is not { } to)
                    continue;
                if (DistanceToSegment(point, from, to) > SnapRadius)
                    continue;

                // Never the origin: the player is standing on it.
                var (nearId, nearPoint) = point.DistanceTo(from) <= point.DistanceTo(to)
                    ? (link[i - 1], from)
                    : (link[i], to);
                var distance = point.DistanceTo(nearPoint);
                if (_pinnable.Contains(nearId) && distance < best.Distance)
                    best = (nearId, distance);
            }
        }
        return best.Id;
    }

    private static float DistanceToSegment(Vector2 point, Vector2 from, Vector2 to)
    {
        var span = to - from;
        var lengthSquared = span.LengthSquared();
        if (lengthSquared <= 0.001f)
            return point.DistanceTo(from);
        var t = Mathf.Clamp((point - from).Dot(span) / lengthSquared, 0f, 1f);
        return point.DistanceTo(from + span * t);
    }

    /// <summary>How near a stroke must pass a node to catch it, in map units.</summary>
    private const float SnapRadius = 55f;

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
            _blocked.Clear();
            _lockedRouteIds = null;
            // Same map as a previous session: the pins belong to it, bring them back.
            if (PinStore.Load() is { } saved && saved.MapKey == _mapKey)
            {
                foreach (var id in saved.Pins)
                    _pins.Toggle(id);
                _blocked = [.. saved.Blocked ?? []];
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
        _pinnable = routes.SelectMany(route => route.Skip(1)).ToHashSet();
        _pins.RetainWhere(_pinnable.Contains);
        // A node behind the marker cannot be planned around any more, so keeping it
        // blocked would only narrow future routes for no reason.
        _blocked.RemoveWhere(id => !_pinnable.Contains(id));

        if (!PathingOptions.AutoPath)
        {
            // `routes` are the complete walks, so their last nodes are exactly where
            // the act ends (the boss having already been trimmed off).
            var terminals = routes.Select(route => route[^1]).ToHashSet();
            bool Complete(IReadOnlyList<string> route) =>
                route.Count > 1 && terminals.Contains(route[^1]);

            // Manual planning draws the player's own line: links between consecutive
            // pinned floors, stitched back into whole routes so the legend counts
            // paths rather than the links they are made of.
            _links = PathSolver.ConnectWaypoints(
                _adapter.Graph, startId, WithLastStep(terminals), _blocked);

            // A block can cut the only way to a pinned node, leaving a ring with
            // nothing attached and no way to see why. Erasing the node that fed a pin
            // takes the pin too: the cascade goes exactly as far as it must, and there
            // is never an orphan left over to puzzle at.
            var orphans = _pins.Ids
                .Where(id => !_links.Any(link => link.Contains(id)))
                .ToList();
            if (orphans.Count > 0)
            {
                foreach (var id in orphans)
                    _pins.Remove(id);
                _links = PathSolver.ConnectWaypoints(
                    _adapter.Graph, startId, WithLastStep(terminals), _blocked);
            }

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
        }
        else
        {
            _links = [];
            var match = PathSolver.MatchByPins(routes, _pins.Ids, PathSolver.BestPickPool);
            if (_pins.Ids.Count == 0)
            {
                // Nothing pinned, so no route is a better answer than any other:
                // show the shape of the whole act rather than colouring five at random.
                _shownRoutes = match.Shown.Select(s => s.Path).ToList();
                _backdropRoutes = [];
            }
            else
            {
                // Keep the best five, whatever the field size. Pin coverage stays
                // paramount — a route through every pin must never lose its slot to a
                // near-miss — then elites + fires (the resources a route is chosen
                // for), then "?" nodes. The same ordering is the display order, and
                // the rest go down as backdrop: a wide field used to be answered with
                // an unlabelled union and an empty table, which said nothing about
                // which way was actually better.
                var ranked = match.Shown
                    .Select(s => (s.Path, s.Hits, Counts: LegendCounts(s.Path)))
                    .OrderByDescending(x => x.Hits)
                    .ThenByDescending(x => x.Counts[5] + x.Counts[3])
                    .ThenByDescending(x => x.Counts[0])
                    .Select(x => x.Path)
                    .ToList();
                _shownRoutes = ranked.Take(PathSolver.LegendThreshold).ToList();
                _backdropRoutes = ranked.Skip(PathSolver.LegendThreshold).ToList();
            }
        }
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
        _overlay.SetHighlight(_lockedRoute);
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
            _blocked.OrderBy(id => id, StringComparer.Ordinal).ToArray()));
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
        if (GodotObject.IsInstanceValid(_screen))
            _screen.Closed -= OnScreenClosed;
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
        _hotRoute = -1;
        // The screen root stays in the tree when the map closes — the game only hides
        // its own contents — so panels parented to it must hide themselves, or they
        // linger over combat and the settings menu.
        _legend.SetShellVisible(false);
        _options.SetShellVisible(false);
        _help.SetShellVisible(false);
        _toolbar.SetVisible(false);
        _overlay.SetHighlight(_lockedRoute);
    });

    private void OnRouteCold(int index)
    {
        if (_hotRoute == index)
            _hotRoute = -1;
        _overlay.SetHighlight(_hotRoute >= 0 ? _hotRoute : _lockedRoute);
    }

    private void OnRouteLockToggled(int index)
    {
        _lockedRoute = _lockedRoute == index ? -1 : index;
        _lockedRouteIds = _lockedRoute >= 0 ? _shownRoutes[_lockedRoute] : null;
        _legend.SetLocked(_lockedRoute);
        _overlay.SetHighlight(_hotRoute >= 0 ? _hotRoute : _lockedRoute);
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
            var polylines = _shownRoutes
                .Select(route => route.Select(EndpointOf).OfType<Vector2>().ToArray())
                .ToList();
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
}
