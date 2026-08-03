using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
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
    private readonly PathLegendPanel _panel;
    private readonly NodeNavigator _navigator;
    private readonly MapZoom _zoom;
    private readonly WaypointSelection _pins = new();

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
        _panel = new PathLegendPanel(screen);
        _navigator = new NodeNavigator(screen);
        _zoom = new MapZoom(screen, theMap, NodeCenters);
        // Zoomed out is the controller mode: the whole map is visible, scrolling is
        // frozen, and the d-pad walks the node grid with a gold cursor ring.
        _zoom.Toggled += () => Guard.Run("Syncing map navigation", () =>
        {
            _navigator.SetActive(_zoom.Zoomed);
            if (!_zoom.Zoomed)
                _overlay.HideCursor();
        });
        _navigator.NodeFocused += node => Guard.Run("Showing the map cursor", () =>
        {
            // The gold ring is a controller aid; with a mouse the pointer is the cursor.
            if (NControllerManager.Instance?.IsUsingDirectionalNavigation == true)
                _overlay.ShowCursor(node.Position + node.Size * 0.5f);
            else
                _overlay.HideCursor();
        });
        _panel.RouteHot += index => Guard.Run("Highlighting a route", () => OnRouteHot(index));
        _panel.RouteCold += index => Guard.Run("Unhighlighting a route", () => OnRouteCold(index));
        _panel.RouteLockToggled += index => Guard.Run("Locking a route", () => OnRouteLockToggled(index));
        _screen.Closed += OnScreenClosed;
    }

    public bool Owns(NMapPoint point) => _screen.IsAncestorOf(point);

    public bool ZoomActive => _zoom.Zoomed;

    public void ToggleZoom() => _zoom.Toggle();

    /// <summary>Every map open starts in the normal view, never zoomed out.</summary>
    public void OnOpened()
    {
        _zoom.Reset();
        _zoom.SetButtonVisible(true);
        _panel.SetShellVisible(true);
        Refresh();
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
        _pinnable = routes.SelectMany(route => route.Skip(1)).ToHashSet();
        _pins.RetainWhere(_pinnable.Contains);

        var match = PathSolver.MatchByPins(routes, _pins.Ids, PathSolver.LegendThreshold);
        // Table order: richest in elites first, fires breaking ties — the two scarce
        // resources a route is usually chosen for. Only worth computing when the
        // routes actually get rows.
        _shownRoutes = match.Shown.Count <= PathSolver.LegendThreshold
            ? match.Shown.Select(s => s.Path)
                .OrderByDescending(route => CountColumns(route)[0])
                .ThenByDescending(route => CountColumns(route)[1])
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
        UpdatePanel(pathSet.Truncated);
        _overlay.ShowPins(_pins.Ids.Select(EndpointOf).OfType<Vector2>());
        _overlay.SetHighlight(_lockedRoute);
        _panel.SetLocked(_lockedRoute);
        _navigator.SetNodes(BuildNavNodes());
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
        if (GodotObject.IsInstanceValid(_screen))
            _screen.Closed -= OnScreenClosed;
        _navigator.Dispose();
        _zoom.Dispose();
        _overlay.Dispose();
        _panel.Dispose();
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
        _panel.SetShellVisible(false);
        _zoom.SetButtonVisible(false);
        _overlay.SetHighlight(_lockedRoute);
    });

    private void OnRouteHot(int index)
    {
        _hotRoute = index;
        _panel.ShowTooltip(index);
        _overlay.SetHighlight(index);
    }

    private void OnRouteCold(int index)
    {
        if (_hotRoute == index)
            _hotRoute = -1;
        _panel.HideTooltip();
        _overlay.SetHighlight(_hotRoute >= 0 ? _hotRoute : _lockedRoute);
    }

    private void OnRouteLockToggled(int index)
    {
        _lockedRoute = _lockedRoute == index ? -1 : index;
        _lockedRouteIds = _lockedRoute >= 0 ? _shownRoutes[_lockedRoute] : null;
        _panel.SetLocked(_lockedRoute);
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

    /// <summary>
    /// The panel's count columns, in this fixed order everywhere: elites, fires,
    /// combats, shops, chests, events. "Events" are the unknown "?" nodes.
    /// </summary>
    private static readonly string[][] ColumnKinds =
    [
        [nameof(MapPointType.Elite)],
        [nameof(MapPointType.RestSite)],
        [nameof(MapPointType.Monster)],
        [nameof(MapPointType.Shop)],
        [nameof(MapPointType.Treasure)],
        [nameof(MapPointType.Unknown), nameof(MapPointType.Unassigned)],
    ];

    private void UpdatePanel(bool truncated)
    {
        if (_shownRoutes.Count == 0)
        {
            _panel.SetContent("No routes", "", [], []);
        }
        else if (_shownRoutes.Count <= PathSolver.LegendThreshold)
        {
            var columnIcons = ColumnKinds.Select(kinds => MapIcons.For(kinds[0])).ToList();
            var routes = _shownRoutes.Select((route, index) => new RouteDisplay(
                PathOverlay.RouteColors[index % PathOverlay.RouteColors.Length],
                $"{(char)('A' + index)}.",
                // The tooltip runs vertically like the map: boss end at the top.
                route.Skip(1).Reverse()
                    .Select(id => MapIcons.For(_adapter!.Graph.Node(id).RoomKind))
                    .OfType<Texture2D>()
                    .ToList(),
                CountColumns(route))).ToList();
            // No header over the table: the rows explain themselves.
            _panel.SetContent("", "", columnIcons, routes);
        }
        else
        {
            var count = $"{_shownRoutes.Count}{(truncated ? "+" : "")}";
            _panel.SetContent($"{count} routes",
                _pins.Count == 0 ? "Pin map nodes to narrow the routes" : "", [], []);
        }
    }

    private IReadOnlyList<int> CountColumns(IReadOnlyList<string> route)
    {
        var counts = new Dictionary<string, int>();
        foreach (var id in route.Skip(1))
        {
            var kind = _adapter!.Graph.Node(id).RoomKind;
            counts[kind] = counts.GetValueOrDefault(kind) + 1;
        }
        return ColumnKinds.Select(kinds => kinds.Sum(counts.GetValueOrDefault)).ToList();
    }

    private void Clear()
    {
        _zoom.Reset();
        _navigator.SetNodes([]);
        _shownRoutes = [];
        _pinnable = [];
        _overlay.Clear();
        _panel.SetContent("", "", [], []);
        _panel.HideTooltip();
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
