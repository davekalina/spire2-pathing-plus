using Godot;
using MegaCrit.Sts2.Core.Helpers;

namespace PathingPlus.PathingPlusCode.Map;

/// <summary>
/// The route dots and pin rings drawn over the map. Lives in its own layer inserted
/// into <c>TheMap</c> directly below <c>Points</c>: above the game's dotted
/// connections, below the node icons, panning with the map for free. The game's own
/// dots are never touched, so there is nothing for <c>SetMap</c> to stomp.
///
/// Routes are runs of the game's <c>map_dot</c> texture with the same hand-placed
/// wobble the native connections use — spacing, jitter, random flips, rotation noise
/// — so they read as part of the map rather than lines ruled over it. The wobble is
/// seeded from the route itself, so a refresh does not make the dots shimmer.
/// </summary>
internal sealed class PathOverlay : IDisposable
{
    /// <summary>One colour per legend slot, straight from the game's palette.</summary>
    public static readonly Color[] RouteColors =
    [
        StsColors.blue,
        StsColors.red,
        StsColors.green,
        StsColors.orange,
        StsColors.purple,
        StsColors.pink,
        StsColors.aqua,
        StsColors.cream,
    ];

    // Solid enough to read at a glance: this is the whole display before the first
    // pin narrows an act's routes below the legend threshold.
    private static readonly Color UnionColor = StsColors.darkBlue with { A = 0.65f };

    /// <summary>
    /// The game circles a visited node in near-black ink (map_circle_vfx tints its
    /// white art); pins use the same art in rust so a planned stop reads strongly and
    /// still cannot be mistaken for a visited one.
    /// </summary>
    private static readonly Color PinInk = new(0.72f, 0.35f, 0.18f);

    private static readonly Color CursorInk = StsColors.gold;

    /// <summary>Highlight is the game's traveled-path ink: dark reads on parchment, white does not.</summary>
    private static readonly Color HighlightInk = StsColors.pathDotTraveled;

    private const float HighlightScaleFactor = 1.25f;
    private const float FadedAlpha = 0.15f;

    /// <summary>Ring size for a pinned node, and the smaller alternative.</summary>
    private const float PinRingScale = 0.87f;

    private readonly Control _layer;
    private readonly Control _dotLayer;
    private readonly Control _pinLayer;
    private readonly Control _cursorLayer;
    private readonly List<List<(TextureRect Dot, Vector2 BaseScale)>> _routeDots = [];
    private readonly List<Color> _routeColors = [];
    private readonly List<(TextureRect Dot, Vector2 BaseScale)> _unionDots = [];
    private readonly List<TextureRect> _pinRings = [];
    private TextureRect? _cursor;

    public PathOverlay(Control theMap, Control points)
    {
        _layer = new Control { Name = "PathingPlusOverlay", MouseFilter = Control.MouseFilterEnum.Ignore };
        _layer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        theMap.AddChild(_layer);
        theMap.MoveChild(_layer, points.GetIndex());

        // Stacking is tree order only, NEVER ZIndex: a nonzero ZIndex is
        // canvas-global and draws over screens the game layers above the map —
        // a z-10 highlight once cut a black line across the deck screen.
        _dotLayer = MakeSubLayer("Dots");
        _pinLayer = MakeSubLayer("Pins");
        _cursorLayer = MakeSubLayer("Cursor");
    }

    private Control MakeSubLayer(string name)
    {
        var layer = new Control { Name = name, MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(layer);
        return layer;
    }

    /// <summary>Up to <see cref="RouteColors" />.Length full routes, one colour each.</summary>
    public void ShowRoutes(IReadOnlyList<Vector2[]> routes)
    {
        ClearDots();
        for (var i = 0; i < routes.Count; i++)
        {
            if (routes[i].Length < 2)
                continue;
            var shift = new Vector2(
                (i - (routes.Count - 1) * 0.5f) * PathingOptions.RouteSeparation, 0f);
            var color = RouteColors[i % RouteColors.Length];
            var dots = new List<(TextureRect, Vector2)>();
            ScatterDots(routes[i].Select(p => p + shift).ToArray(), color, dots);
            _routeDots.Add(dots);
            _routeColors.Add(color);
        }
    }

    /// <summary>
    /// Too many routes for individual colours: draw each surviving edge once, faintly,
    /// so the shape of "everything that still fits the pins" is visible.
    /// </summary>
    public void ShowUnion(IEnumerable<(Vector2 From, Vector2 To)> edges)
    {
        ClearDots();
        foreach (var (from, to) in edges)
            ScatterDots([from, to], UnionColor, _unionDots);
    }

    /// <summary>−1 restores every route; otherwise that route turns to ink while the rest fade.</summary>
    public void SetHighlight(int index)
    {
        for (var i = 0; i < _routeDots.Count; i++)
        {
            var (color, factor) = index < 0 ? (_routeColors[i], 1f)
                : i == index ? (HighlightInk, HighlightScaleFactor)
                : (_routeColors[i] with { A = FadedAlpha }, 1f);
            foreach (var (dot, baseScale) in _routeDots[i])
            {
                dot.Modulate = color;
                dot.Scale = baseScale * factor;
            }
        }

        // Prominence by tree order within our own sub-layer, never by ZIndex.
        if (index >= 0 && index < _routeDots.Count)
            foreach (var (dot, _) in _routeDots[index])
                _dotLayer.MoveChild(dot, _dotLayer.GetChildCount() - 1);
    }

    public void ShowPins(IEnumerable<Vector2> centers)
    {
        foreach (var ring in _pinRings)
            ring.QueueFree();
        _pinRings.Clear();

        foreach (var center in centers)
        {
            var ring = MakeInkRing(center, PinInk,
                PathingOptions.SmallMarkers ? PinRingScale * 0.75f : PinRingScale);
            _pinRings.Add(ring);
            _pinLayer.AddChild(ring);
        }
    }

    /// <summary>The controller cursor: a gold ink ring on whichever node holds focus.</summary>
    public void ShowCursor(Vector2 center)
    {
        if (_cursor is null || !GodotObject.IsInstanceValid(_cursor))
        {
            _cursor = MakeInkRing(center, CursorInk, 1.02f);
            _cursorLayer.AddChild(_cursor);
        }
        _cursor.Position = center - new Vector2(100, 100);
        _cursor.RotationDegrees = MathF.Abs(center.X * 7.3f + center.Y * 3.1f) % 360f;
        _cursor.Visible = true;
    }

    public void HideCursor()
    {
        if (_cursor is { } cursor && GodotObject.IsInstanceValid(cursor))
            cursor.Visible = false;
    }

    /// <summary>
    /// The hand-inked circle the game stamps on visited nodes — always the last of
    /// its five frames: the earlier ones are the drawing animation and look torn when
    /// used as stills. Rotation derives from the position so a refresh redraws it
    /// unchanged.
    /// </summary>
    private static TextureRect MakeInkRing(Vector2 center, Color color, float scale) => new()
    {
        Texture = ResourceLoader.Load<Texture2D>(
            "res://images/atlases/compressed.sprites/map/map_circle_4.tres",
            null, ResourceLoader.CacheMode.Reuse),
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        Size = new Vector2(200, 200),
        PivotOffset = new Vector2(100, 100),
        Position = center - new Vector2(100, 100),
        RotationDegrees = MathF.Abs(center.X * 7.3f + center.Y * 3.1f) % 360f,
        Scale = Vector2.One * scale,
        Modulate = color,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    public void Clear()
    {
        ClearDots();
        ShowPins([]);
        HideCursor();
    }

    public void Dispose()
    {
        if (GodotObject.IsInstanceValid(_layer))
            _layer.QueueFree();
    }

    /// <summary>
    /// The native connection look, from <c>NMapScreen.CreatePath</c>: a dot every
    /// 22 px, skipping the segment ends so runs stop short of the node art, each dot
    /// nudged, spun a little, and randomly mirrored. On top of the native recipe, each
    /// dash is stretched along the direction of travel by a random amount, so
    /// neighbouring dashes bridge their gaps and the run reads as a drawn stroke
    /// rather than a row of dots.
    /// </summary>
    private void ScatterDots(Vector2[] polyline, Color color, List<(TextureRect, Vector2)> sink)
    {
        var texture = ResourceLoader.Load<Texture2D>(
            "res://images/atlases/compressed.sprites/map/map_dot.tres",
            null, ResourceLoader.CacheMode.Reuse);
        var random = new Random(SeedFor(polyline));

        var spacing = Mathf.Max(1f, PathingOptions.DashSpacing);
        for (var s = 1; s < polyline.Length; s++)
        {
            var start = polyline[s - 1];
            var direction = (polyline[s] - start).Normalized();
            var angle = direction.Angle() + MathF.PI / 2f;
            var count = (int)(start.DistanceTo(polyline[s]) / spacing) + 1;
            for (var i = 1; i < count; i++)
            {
                var center = start + direction * (i * spacing) + new Vector2(
                    (float)(random.NextDouble() * 6.0 - 3.0),
                    (float)(random.NextDouble() * 6.0 - 3.0));
                // The rotation puts the texture's Y axis along the path: X is girth,
                // Y is length. Lengths beyond the spacing make neighbouring dashes
                // overlap, which is what reads as one continuous stroke.
                var baseScale = new Vector2(
                    PathingOptions.DashWidth,
                    PathingOptions.DashLength +
                        (float)random.NextDouble() * PathingOptions.DashLengthVariance);
                var dot = new TextureRect
                {
                    Texture = texture,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    Size = new Vector2(16, 16),
                    PivotOffset = new Vector2(8, 8),
                    Position = center - new Vector2(8, 8),
                    Rotation = angle + Gaussian(random) * 0.1f,
                    FlipH = random.Next(2) == 0,
                    Scale = baseScale,
                    Modulate = color,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
                sink.Add((dot, baseScale));
                _dotLayer.AddChild(dot);
            }
        }
    }

    private void ClearDots()
    {
        foreach (var (dot, _) in _routeDots.SelectMany(run => run).Concat(_unionDots))
            dot.QueueFree();
        _routeDots.Clear();
        _routeColors.Clear();
        _unionDots.Clear();
    }

    /// <summary>Stable per-route seed so the wobble survives a recompute unchanged.</summary>
    private static int SeedFor(Vector2[] polyline)
    {
        var hash = 17;
        foreach (var point in polyline)
            hash = unchecked(hash * 31 + ((int)point.X * 397 ^ (int)point.Y));
        return hash;
    }

    private static float Gaussian(Random random)
    {
        var u1 = 1.0 - random.NextDouble();
        var u2 = random.NextDouble();
        return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
    }
}
