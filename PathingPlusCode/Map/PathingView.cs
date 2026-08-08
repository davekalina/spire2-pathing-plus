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
    private readonly OptionsPanel _options;
    private readonly NodeNavigator _navigator;
    private readonly MapZoom _zoom;
    private readonly WaypointSelection _pins = new();
    private readonly Dictionary<NMapPoint, float> _iconBaseRotations = [];
    private Control? _nativeLegend;

    private MapGraphAdapter? _adapter;
    private Dictionary<MapCoord, NMapPoint>? _nodesByCoord;
    private IReadOnlyList<IReadOnlyList<string>> _shownRoutes = [];
    private HashSet<string> _pinnable = [];
    private string _mapKey = "";
    private IReadOnlyList<string>? _lockedRouteIds;
    private int _hotRoute = -1;
    private int _lockedRoute = -1;

    public PathingView(NMapScreen screen)
    {
        _screen = screen;
        var theMap = screen.GetNode<Control>("TheMap");
        var points = screen.GetNode<Control>("TheMap/Points");
        _overlay = new PathOverlay(theMap, points);
        _navigator = new NodeNavigator(screen);
        _zoom = new MapZoom(screen, theMap, NodeCenters);
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
        _options = new OptionsPanel(screen);
        PathingOptions.Changed += OnOptionsChanged;
        _screen.Closed += OnScreenClosed;
    }

    private void OnOptionsChanged() => Guard.Run("Applying a settings change", () =>
    {
        if (_screen.IsOpen)
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

    public bool ZoomActive => _zoom.Zoomed;

    public void ToggleZoom() => _zoom.Toggle();

    /// <summary>Every map open starts in the normal view, never zoomed out.</summary>
    public void OnOpened()
    {
        _zoom.Reset();
        _zoom.SetButtonVisible(true);
        _legend.SetShellVisible(true);
        _options.SetShellVisible(true);
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
            _lockedRouteIds = null;
            // Same map as a previous session: the pins belong to it, bring them back.
            if (PinStore.Load() is { } saved && saved.MapKey == _mapKey)
            {
                foreach (var id in saved.Pins)
                    _pins.Toggle(id);
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

        if (!PathingOptions.AutoPath)
            routes = PathSolver.TruncateAtPins(routes, _pins.IsSelected);

        var match = PathSolver.MatchByPins(routes, _pins.Ids, PathSolver.BestPickPool);
        // Up to ten candidates: keep the best five. Pin coverage stays paramount —
        // a route through every pin must never lose its slot to a near-miss — then
        // elites + fires (the resources a route is chosen for), then "?" nodes.
        // The same ordering is the display order.
        _shownRoutes = match.Shown.Count <= PathSolver.BestPickPool
            ? match.Shown
                .Select(s => (s.Path, s.Hits, Counts: LegendCounts(s.Path)))
                .OrderByDescending(x => x.Hits)
                .ThenByDescending(x => x.Counts[5] + x.Counts[3])
                .ThenByDescending(x => x.Counts[0])
                .Take(PathSolver.LegendThreshold)
                .Select(x => x.Path)
                .ToList()
            : match.Shown.Select(s => s.Path).ToList();
        _hotRoute = -1;

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
        _overlay.ShowPins(_pins.Ids
            .Where(id => !(_screen.IsTravelEnabled && IsTravelableNode(id)))
            .Select(EndpointOf).OfType<Vector2>());
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
            _lockedRouteIds?.ToArray()));
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
        _options.Dispose();
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
        _zoom.SetButtonVisible(false);
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
        if (_shownRoutes.Count == 0)
        {
            _overlay.Clear();
        }
        else if (_shownRoutes.Count <= PathSolver.LegendThreshold)
        {
            var polylines = _shownRoutes
                .Select(route => route.Select(EndpointOf).OfType<Vector2>().ToArray())
                .ToList();
            _overlay.ShowRoutes(polylines);
        }
        else
        {
            var edges = new HashSet<(string, string)>();
            foreach (var route in _shownRoutes)
                for (var i = 1; i < route.Count; i++)
                    edges.Add((route[i - 1], route[i]));

            _overlay.ShowUnion(edges
                .Select(edge => (From: EndpointOf(edge.Item1), To: EndpointOf(edge.Item2)))
                .Where(edge => edge is { From: not null, To: not null })
                .Select(edge => (edge.From!.Value, edge.To!.Value)));
        }
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
