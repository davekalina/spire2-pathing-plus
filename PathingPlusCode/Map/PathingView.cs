using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
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
    private readonly PlanModeController _planMode;
    private readonly WaypointSelection _pins = new();

    private MapGraphAdapter? _adapter;
    private Dictionary<MapCoord, NMapPoint>? _nodesByCoord;
    private IReadOnlyList<IReadOnlyList<string>> _shownRoutes = [];
    private HashSet<string> _pinnable = [];
    private int _hotRoute = -1;
    private int _lockedRoute = -1;

    public PathingView(NMapScreen screen)
    {
        _screen = screen;
        var theMap = screen.GetNode<Control>("TheMap");
        var points = screen.GetNode<Control>("TheMap/Points");
        _overlay = new PathOverlay(theMap, points);
        _panel = new PathLegendPanel(screen);
        _planMode = new PlanModeController(screen);
        _panel.RouteHot += index => Guard.Run("Highlighting a route", () => OnRouteHot(index));
        _panel.RouteCold += index => Guard.Run("Unhighlighting a route", () => OnRouteCold(index));
        _panel.RouteLockToggled += index => Guard.Run("Locking a route", () => OnRouteLockToggled(index));
        _screen.Closed += OnScreenClosed;
    }

    public bool Owns(NMapPoint point) => _screen.IsAncestorOf(point);

    public bool PlanModeActive => _planMode.Active;

    public void TogglePlanMode() => _planMode.Toggle();

    public void OnMapChanged()
    {
        _planMode.Deactivate();
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
            _pins.Clear();
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

        _shownRoutes = PathSolver.Filter(routes, _pins.Ids);
        _hotRoute = -1;
        _lockedRoute = -1;

        UpdateOverlay();
        UpdatePanel(pathSet.Truncated);
        _overlay.ShowPins(_pins.Ids.Select(EndpointOf).OfType<Vector2>());
        _overlay.SetHighlight(-1);
        _panel.SetLocked(-1);
        _planMode.SetNodes(BuildPlanNodes());
    }

    /// <summary>Every node on a surviving route, with where it sits on screen.</summary>
    private List<(NMapPoint Node, int Row, Vector2 Center)> BuildPlanNodes()
    {
        var nodes = new List<(NMapPoint, int, Vector2)>();
        if (_adapter is null || _nodesByCoord is null)
            return nodes;
        foreach (var id in _shownRoutes.SelectMany(route => route.Skip(1)).Distinct())
        {
            if (!_adapter.TryGetPoint(id, out var point))
                continue;
            if (!_nodesByCoord.TryGetValue(point.coord, out var node) ||
                !GodotObject.IsInstanceValid(node))
                continue;
            if (EndpointOf(id) is { } center)
                nodes.Add((node, point.coord.row, center));
        }
        return nodes;
    }

    public void Dispose()
    {
        if (GodotObject.IsInstanceValid(_screen))
            _screen.Closed -= OnScreenClosed;
        _planMode.Dispose();
        _overlay.Dispose();
        _panel.Dispose();
    }

    private void OnScreenClosed() => Guard.Run("Resetting on map close", () =>
    {
        _planMode.Deactivate();
        _hotRoute = -1;
        _panel.HideTooltip();
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
        _panel.SetLocked(_lockedRoute);
        _overlay.SetHighlight(_hotRoute >= 0 ? _hotRoute : _lockedRoute);
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

    private void UpdatePanel(bool truncated)
    {
        if (_shownRoutes.Count == 0)
        {
            _panel.SetContent(
                _pins.Count > 0 ? "No route fits the pins" : "No routes",
                _pins.Count > 0 ? "Select a pinned node to unpin it" : "", []);
        }
        else if (_shownRoutes.Count <= PathSolver.LegendThreshold)
        {
            var routes = _shownRoutes.Select((route, index) => new RouteDisplay(
                PathOverlay.RouteColors[index % PathOverlay.RouteColors.Length],
                $"Route {index + 1}",
                // The tooltip runs vertically like the map: boss end at the top.
                route.Skip(1).Reverse()
                    .Select(id => MapIcons.For(_adapter!.Graph.Node(id).RoomKind))
                    .OfType<Texture2D>()
                    .ToList(),
                Summarize(route))).ToList();
            _panel.SetContent(
                _shownRoutes.Count == 1 ? "1 route" : $"{_shownRoutes.Count} routes",
                "Hover a route to preview it", routes);
        }
        else
        {
            _panel.SetContent(
                $"{_shownRoutes.Count}{(truncated ? "+" : "")} routes",
                "Pin map nodes to narrow the routes", []);
        }
    }

    /// <summary>
    /// The same categories in the same order for every route, zeros included, so two
    /// routes can be compared row against row in the summary table.
    /// </summary>
    private IReadOnlyList<(int Count, string Noun)> Summarize(IReadOnlyList<string> route)
    {
        var counts = new Dictionary<string, int>();
        foreach (var id in route.Skip(1))
        {
            var kind = _adapter!.Graph.Node(id).RoomKind;
            counts[kind] = counts.GetValueOrDefault(kind) + 1;
        }

        int Of(params string[] kinds) => kinds.Sum(counts.GetValueOrDefault);
        (int, string) Row(int count, string noun) => (count, count == 1 ? noun : noun + "s");

        return
        [
            Row(Of(nameof(MapPointType.Elite)), "elite"),
            Row(Of(nameof(MapPointType.RestSite)), "fire"),
            Row(Of(nameof(MapPointType.Unknown), nameof(MapPointType.Unassigned)), "event"),
            Row(Of(nameof(MapPointType.Monster)), "combat"),
            Row(Of(nameof(MapPointType.Treasure)), "chest"),
            Row(Of(nameof(MapPointType.Shop)), "shop"),
        ];
    }

    private void Clear()
    {
        _planMode.Deactivate();
        _planMode.SetNodes([]);
        _shownRoutes = [];
        _pinnable = [];
        _overlay.Clear();
        _panel.SetContent("", "", []);
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
