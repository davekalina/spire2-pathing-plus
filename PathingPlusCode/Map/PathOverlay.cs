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

    /// <summary>
    /// A route with no colour of its own, picked out under the pointer. Deliberately
    /// outside the five route colours so it is never mistaken for one of them, and
    /// deliberately not the ink: nothing has been chosen yet.
    /// </summary>
    public static readonly Color TraceColor = new(1f, 0.78f, 0.24f);

    private const float HighlightScaleFactor = 1.25f;
    private const float FadedAlpha = 0.15f;

    /// <summary>
    /// Ring size for a pinned node. Smaller than the game's own stamp on a visited
    /// node, so the two are never confused at a glance.
    /// </summary>
    private const float PinRingScale = 0.87f * 0.75f;

    /// <summary>How long a marker answers the gesture that made it, and how it arrives and leaves.</summary>
    private const double MarkerFadeIn = 0.15;
    private const double MarkerHold = 3.0;
    private const double MarkerFadeOut = 0.6;

    /// <summary>How long a mark of the drawing trail lasts, from full ink to nothing.</summary>
    private const double TrailLife = 1.0;

    private readonly Control _layer;
    private readonly Control _dotLayer;
    private readonly Control _pinLayer;
    private readonly Control _cursorLayer;
    private readonly Control _trailLayer;
    private readonly List<List<(TextureRect Dot, Vector2 BaseScale)>> _routeDots = [];
    private readonly List<Color> _routeColors = [];
    private readonly List<(TextureRect Dot, Vector2 BaseScale)> _unionDots = [];
    private readonly List<(TextureRect Dot, Vector2 BaseScale)> _traceDots = [];
    private readonly List<TextureRect> _pinRings = [];
    private Tween? _pinFade;
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
        // Last, so the ink being laid down is never hidden under the plan it is making.
        _trailLayer = MakeSubLayer("Trail");
    }

    /// <summary>
    /// One mark of the drawing trail: ink where the pen is, fading to nothing over a
    /// second and drifting onto <paramref name="target" /> as it goes.
    ///
    /// The mod's stroke is invisible by design — the native line is suppressed so the
    /// route can be the drawing — which reads as a dead pen until the first node is
    /// caught. This says "yes, that registered" without putting a scribble on the map:
    /// the ink is gone a second later, and it has spent that second sliding onto the
    /// line the stroke is really making, which is the thing worth teaching.
    ///
    /// Each mark owns its own tween and frees itself at the end of it, so the trail
    /// needs no bookkeeping and no per-frame work.
    /// </summary>
    public void AddTrailMark(Vector2 at, Vector2 target, Color ink)
    {
        var half = new Vector2(8, 8);
        // The map's own dash, small and spun at random: an ink speck in the hand the
        // rest of the map is drawn in, rather than a geometric dot laid over it.
        var random = new Random(unchecked((int)at.X * 397 ^ (int)at.Y));
        var mark = new TextureRect
        {
            Texture = ResourceLoader.Load<Texture2D>(
                "res://images/atlases/compressed.sprites/map/map_dot.tres",
                null, ResourceLoader.CacheMode.Reuse),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Size = new Vector2(16, 16),
            PivotOffset = half,
            Position = at - half,
            Rotation = (float)(random.NextDouble() * Math.Tau),
            Scale = Vector2.One * (0.9f + (float)random.NextDouble() * 0.4f),
            Modulate = ink,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _trailLayer.AddChild(mark);

        var tween = mark.CreateTween().SetParallel();
        // Quadratic ease-in: the ink holds while the pen is still near it and then goes
        // quickly. A linear fade spends the whole second visibly dying, which reads as
        // a smear rather than a mark.
        tween.TweenProperty(mark, "modulate:a", 0f, TrailLife)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        if (target != at)
            tween.TweenProperty(mark, "position", target - half, TrailLife)
                .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
        tween.Chain().TweenCallback(Callable.From(mark.QueueFree));
    }

    private Control MakeSubLayer(string name)
    {
        var layer = new Control { Name = name, MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(layer);
        return layer;
    }

    /// <summary>
    /// Up to <see cref="RouteColors" />.Length full routes, one colour each, over an
    /// optional backdrop of everything else that matched. The backdrop goes down
    /// first so the coloured runs sit on top of it: a plan wider than the legend can
    /// hold still shows all of itself, with the picks standing out from the rest.
    /// </summary>
    public void ShowRoutes(
        IReadOnlyList<Vector2[]> routes, IEnumerable<(Vector2 From, Vector2 To)>? backdrop = null)
    {
        ClearDots();
        if (backdrop is not null)
            foreach (var (from, to) in backdrop)
                ScatterDots([from, to], UnionColor, _unionDots);

        for (var i = 0; i < routes.Count; i++)
        {
            if (routes[i].Length < 2)
                continue;
            var shift = RouteShift(i, routes.Count);
            var color = RouteColors[i % RouteColors.Length];
            var dots = new List<(TextureRect, Vector2)>();
            ScatterDots(routes[i].Select(p => p + shift).ToArray(), color, dots);
            _routeDots.Add(dots);
            _routeColors.Add(color);
        }
    }

    /// <summary>
    /// Where route <paramref name="index" /> of <paramref name="count" /> is actually
    /// drawn, sideways of the nodes it joins, so parallel runs stay legible. Hit
    /// testing has to use the same offset — against the shared centreline every route
    /// sits on top of every other, and picking between neighbours is impossible.
    /// </summary>
    public static Vector2 RouteShift(int index, int count) =>
        new((index - (count - 1) * 0.5f) * PathingOptions.RouteSeparation, 0f);

    /// <summary>
    /// A single route picked out over everything else, for one that has no colour of
    /// its own — the backdrop is drawn as merged edges, so there is nothing per-route
    /// there to light up. Hovering it should still say which way it goes.
    /// </summary>
    public void ShowTrace(IReadOnlyList<Vector2> points)
    {
        ClearTrace();
        if (points.Count < 2)
            return;
        ScatterDots([.. points], TraceColor, _traceDots);
        foreach (var (dot, baseScale) in _traceDots)
            dot.Scale = baseScale * HighlightScaleFactor;
    }

    /// <summary>
    /// The same hue, fuller and brighter. Lifting toward white would wash out on
    /// parchment; saturating it makes the route jump forward while staying itself.
    /// </summary>
    private static Color Deepen(Color color) => Color.FromHsv(
        color.H, Math.Min(1f, color.S * 1.4f), Math.Min(1f, color.V * 1.15f), color.A);

    public void ClearTrace()
    {
        foreach (var (dot, _) in _traceDots)
            dot.QueueFree();
        _traceDots.Clear();
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

    /// <summary>How a singled-out route is drawn.</summary>
    public enum Emphasis
    {
        /// <summary>
        /// Passing interest: the route's own colour, deepened. Turning it to ink on
        /// hover reads as a commitment the player has not made, and loses the colour
        /// that ties the line to its legend column at the moment they are matching
        /// one to the other.
        /// </summary>
        Hover,

        /// <summary>Chosen: ink, the way the game marks a path already travelled.</summary>
        Lock,
    }

    /// <summary>−1 restores every route; otherwise that one stands out and the rest fade.</summary>
    public void SetHighlight(int index, Emphasis emphasis = Emphasis.Lock)
    {
        for (var i = 0; i < _routeDots.Count; i++)
        {
            var (color, factor) = index < 0 ? (_routeColors[i], 1f)
                : i == index
                    ? (emphasis is Emphasis.Lock ? HighlightInk : Deepen(_routeColors[i]),
                        HighlightScaleFactor)
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

    /// <summary>
    /// Rings on the pinned nodes — but only just after the plan changed.
    ///
    /// A ring that stays put is read as "somewhere I have been", which is precisely
    /// the wrong thing to see on a node ahead at the moment of choosing where to move.
    /// As feedback while drawing it is exactly right. So it fades in on the change,
    /// holds, and fades out, and the map is clean again by the time it matters.
    /// </summary>
    /// <param name="changed">
    /// Whether the plan just changed. A redraw for any other reason — travelling,
    /// zooming, reopening the map — must not restart the timer, or the markers
    /// reappear at the worst moment.
    /// </param>
    public void ShowPins(IEnumerable<Vector2> centers, bool changed)
    {
        foreach (var ring in _pinRings)
            ring.QueueFree();
        _pinRings.Clear();

        foreach (var center in centers)
        {
            var ring = MakeInkRing(center, PinInk, PinRingScale);
            _pinRings.Add(ring);
            _pinLayer.AddChild(ring);
        }

        if (!changed)
            return;
        if (_pinRings.Count == 0)
        {
            _pinFade?.Kill();
            _pinLayer.Modulate = _pinLayer.Modulate with { A = 0f };
            return;
        }
        PulsePins();
    }

    /// <summary>
    /// Show the markers and start the clock again — for asking after them rather than
    /// changing them, as hovering a pinned node does.
    /// </summary>
    public void PulsePins()
    {
        if (_pinRings.Count == 0)
            return;
        _pinFade?.Kill();
        _pinFade = _pinLayer.CreateTween();
        _pinFade.TweenProperty(_pinLayer, "modulate:a", 1f, MarkerFadeIn);
        _pinFade.TweenInterval(MarkerHold);
        _pinFade.TweenProperty(_pinLayer, "modulate:a", 0f, MarkerFadeOut);
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
        ShowPins([], true);
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
        ClearTrace();
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
