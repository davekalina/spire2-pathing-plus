using Godot;

namespace PathingPlus.PathingPlusCode.Map;

/// <summary>
/// The lines and pin rings drawn over the map. Lives in its own layer inserted into
/// <c>TheMap</c> directly below <c>Points</c>: above the game's dotted connections,
/// below the node icons, and panning with the map for free. The game's own dots are
/// never touched, so there is nothing for <c>SetMap</c> or the travel animation to
/// stomp and nothing to restore.
/// </summary>
internal sealed class PathOverlay : IDisposable
{
    /// <summary>One colour per legend slot; distinct against every act's parchment.</summary>
    public static readonly Color[] RouteColors =
    [
        new(0.31f, 0.76f, 0.97f), // sky
        new(0.94f, 0.42f, 0.38f), // ember
        new(0.45f, 0.80f, 0.52f), // moss
        new(0.99f, 0.76f, 0.35f), // amber
        new(0.73f, 0.50f, 0.88f), // violet
    ];

    private static readonly Color UnionColor = new(1f, 1f, 1f, 0.32f);
    private static readonly Color PinColor = new(1f, 0.83f, 0.36f);
    private static readonly Color FadedModulate = new(1f, 1f, 1f, 0.12f);

    private const float RouteWidth = 5f;
    private const float HighlightWidth = 9f;

    /// <summary>Parallel routes share edges; a small per-route shift keeps all visible.</summary>
    private const float RouteSeparation = 6f;

    private readonly Control _layer;
    private readonly List<Line2D> _routeLines = [];
    private readonly List<Color> _routeColors = [];
    private readonly List<Line2D> _unionLines = [];
    private readonly List<Line2D> _pinRings = [];

    public PathOverlay(Control theMap, Control points)
    {
        _layer = new Control { Name = "PathingPlusOverlay", MouseFilter = Control.MouseFilterEnum.Ignore };
        _layer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        theMap.AddChild(_layer);
        theMap.MoveChild(_layer, points.GetIndex());
    }

    /// <summary>Up to <see cref="RouteColors" />.Length full routes, one colour each.</summary>
    public void ShowRoutes(IReadOnlyList<Vector2[]> routes)
    {
        ClearLines();
        for (var i = 0; i < routes.Count; i++)
        {
            if (routes[i].Length < 2)
                continue;
            var shift = new Vector2((i - (routes.Count - 1) * 0.5f) * RouteSeparation, 0f);
            var color = RouteColors[i % RouteColors.Length];
            var line = MakeLine(color, RouteWidth);
            line.Points = routes[i].Select(p => p + shift).ToArray();
            _routeLines.Add(line);
            _routeColors.Add(color);
            _layer.AddChild(line);
        }
    }

    /// <summary>
    /// Too many routes for individual colours: draw each surviving edge once, faintly,
    /// so the shape of "everything that still fits the pins" is visible.
    /// </summary>
    public void ShowUnion(IEnumerable<(Vector2 From, Vector2 To)> edges)
    {
        ClearLines();
        foreach (var (from, to) in edges)
        {
            var line = MakeLine(UnionColor, RouteWidth);
            line.Points = [from, to];
            _unionLines.Add(line);
            _layer.AddChild(line);
        }
    }

    /// <summary>−1 restores every route; otherwise that route goes white and wide while the rest fade.</summary>
    public void SetHighlight(int index)
    {
        for (var i = 0; i < _routeLines.Count; i++)
        {
            var line = _routeLines[i];
            if (index >= 0 && i == index)
            {
                line.DefaultColor = Colors.White;
                line.Width = HighlightWidth;
                line.Modulate = Colors.White;
                line.ZIndex = 10;
            }
            else
            {
                line.DefaultColor = _routeColors[i];
                line.Width = RouteWidth;
                line.Modulate = index >= 0 ? FadedModulate : Colors.White;
                line.ZIndex = 0;
            }
        }
    }

    public void ShowPins(IEnumerable<Vector2> centers)
    {
        foreach (var ring in _pinRings)
            ring.QueueFree();
        _pinRings.Clear();

        foreach (var center in centers)
        {
            var ring = MakeLine(PinColor, 4f);
            ring.Points = Circle(42f);
            ring.Position = center;
            _pinRings.Add(ring);
            _layer.AddChild(ring);
        }
    }

    public void Clear()
    {
        ClearLines();
        ShowPins([]);
    }

    public void Dispose()
    {
        if (GodotObject.IsInstanceValid(_layer))
            _layer.QueueFree();
    }

    private void ClearLines()
    {
        foreach (var line in _routeLines.Concat(_unionLines))
            line.QueueFree();
        _routeLines.Clear();
        _routeColors.Clear();
        _unionLines.Clear();
    }

    private static Line2D MakeLine(Color color, float width) => new()
    {
        Width = width,
        DefaultColor = color,
        JointMode = Line2D.LineJointMode.Round,
        BeginCapMode = Line2D.LineCapMode.Round,
        EndCapMode = Line2D.LineCapMode.Round,
        Antialiased = true,
    };

    private static Vector2[] Circle(float radius)
    {
        const int segments = 24;
        var points = new Vector2[segments + 1];
        for (var i = 0; i <= segments; i++)
        {
            var angle = Mathf.Tau * i / segments;
            points[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }
        return points;
    }
}
