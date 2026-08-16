using Godot;

namespace PathingPlus.PathingPlusCode.Map;

/// <summary>
/// The path tool's icon, drawn from the game's own art at load time.
///
/// The map button in the top bar is a colour illustration — a parchment scroll with a
/// dashed route and an X on it, which is this mod's subject matter down to the last
/// detail. The drawing toolbar it has to join is not colour illustration: the quill,
/// the eraser and the broom are single-colour line art in an 80x80 texture, each with
/// a second "glow" copy that the game swaps in while that tool is selected. So the
/// scroll is re-drawn in that idiom rather than pasted in as it is.
///
/// It is re-drawn **here, at runtime, from the atlas** rather than shipped as a pair
/// of PNGs. The mod then carries no copy of Mega Crit's art, needs no `.pck` to hold
/// one, and follows the art if a patch redraws it. Everything below is arithmetic on
/// one 114x104 sprite, run once per game session.
///
/// The treatment, in three steps:
///
/// 1. **The silhouette becomes a stroke.** The scroll's shape is eroded and subtracted
///    from itself, which leaves a ring of even width where its edge was — the contour,
///    the same weight all the way round, like a pen drew it.
/// 2. **The dark markings become the same white.** The route's dashes and the X are
///    near-black on parchment, so a luminance ramp lifts them out and drops the
///    parchment entirely.
/// 3. **The glow copy** is the result blurred and brightened underneath itself, which
///    is measurably what the game's own glow variants are (fitted against
///    <c>drawing_quill.png</c> and its glow: a Gaussian of about 6.5px at 1.6 gain
///    reproduces it to within a couple of levels out of 255).
///
/// If any of that fails — the atlas moves, the region moves, a decompressor is missing
/// — <see cref="Build" /> returns null and the caller falls back to the untreated
/// sprite. A wrong-looking icon is a far better outcome than no button.
/// </summary>
internal static class PathToolIcon
{
    /// <summary>The top bar's map button, the sprite this is drawn from.</summary>
    private const string SpritePath =
        "res://images/atlases/ui_atlas.sprites/top_bar/top_bar_map.tres";

    /// <summary>Texture size of the game's own drawing icons, which this must match.</summary>
    private const int Canvas = 80;

    /// <summary>
    /// Where the art sits inside that canvas. The three native icons occupy 51-56 px
    /// across, centred near (39, 40) — measured off the shipped PNGs rather than
    /// guessed, so this one sits on the same optical centre in the row.
    /// </summary>
    private const int ArtWidth = 54;
    private const int CenterX = 39;
    private const int CenterY = 40;

    /// <summary>
    /// The sprite is worked at twice its own size. The morphology below moves in whole
    /// pixels, and at native size the smallest usable step would be a third of the
    /// finished stroke's width — too coarse to land it on the same weight as the quill.
    /// </summary>
    private const int Supersample = 2;

    /// <summary>Erosion radius for the contour, in supersampled pixels.</summary>
    private const int OutlineRadius = 5;

    /// <summary>The dashes and the X are thickened by this much, for the same reason.</summary>
    private const int InkRadius = 1;

    /// <summary>Luminance at and below which a pixel is pure ink, and above which it is none.</summary>
    private const float InkDark = 0.12f;
    private const float InkLight = 0.34f;

    /// <summary>
    /// Three box passes of this width stand in for the Gaussian, to within a third of
    /// one alpha level of it — a real Gaussian would mean a kernel per pixel for no
    /// visible difference.
    /// </summary>
    private const int GlowBox = 13;
    private const float GlowGain = 1.6f;

    /// <summary>The two states of the button: idle, and selected.</summary>
    internal sealed record Art(Texture2D Plain, Texture2D Glow);

    /// <summary>
    /// Pixels are cached, textures are not: an <c>ImageTexture</c> handed out once and
    /// held across a scene teardown can come back disposed, which is the same trap
    /// <see cref="MapIcons" /> avoids by not caching at all. An <c>Image</c> is inert
    /// data and survives, so the arithmetic runs once and each caller gets its own
    /// texture built from the result.
    /// </summary>
    private static Image? _plain;
    private static Image? _glow;
    private static bool _attempted;

    public static Art? Build() => Guard.Run("Drawing the path tool icon", () =>
    {
        if (!_attempted)
        {
            _attempted = true;
            if (Draw() is { } drawn)
                (_plain, _glow) = drawn;
        }
        return _plain is null || _glow is null
            ? null
            : new Art(ImageTexture.CreateFromImage(_plain), ImageTexture.CreateFromImage(_glow));
    }, null);

    /// <summary>The untreated sprite, for when the treatment could not be applied.</summary>
    public static Texture2D? Sprite() => ResourceLoader.Load<Texture2D>(
        SpritePath, null, ResourceLoader.CacheMode.Reuse);

    private static (Image Plain, Image Glow)? Draw()
    {
        if (ResourceLoader.Load<AtlasTexture>(SpritePath, null, ResourceLoader.CacheMode.Reuse)
            is not { Atlas: { } sheet } sprite)
            return null;

        // The sheet is BC7 in video memory, so what comes back is block-compressed and
        // has to be expanded before any of it can be read as pixels.
        if (sheet.GetImage() is not { } atlas)
            return null;
        if (atlas.IsCompressed() && atlas.Decompress() != Error.Ok)
            return null;
        // Cutting a region out of a mipmapped image is refused; the sheet arrives with
        // them because it is a texture, and nothing here wants any level but the first.
        if (atlas.HasMipmaps())
            atlas.ClearMipmaps();

        var region = atlas.GetRegion((Rect2I)sprite.Region);
        var sourceWidth = region.GetWidth();
        var sourceHeight = region.GetHeight();
        if (sourceWidth <= 0 || sourceHeight <= 0)
            return null;
        region.Convert(Image.Format.Rgba8);

        var width = sourceWidth * Supersample;
        var height = sourceHeight * Supersample;
        region.Resize(width, height, Image.Interpolation.Lanczos);
        var pixels = region.GetData();
        var count = width * height;

        // Two masks in one pass: where the sprite is (its shape) and how dark it is
        // there (its markings). The alpha cut is hard on purpose — the sprite's edge is
        // feathered over several pixels, and eroding a soft edge yields a soft ring
        // that reads grey against line art that is white.
        var shape = new byte[count];
        var ink = new byte[count];
        for (var i = 0; i < count; i++)
        {
            var offset = i * 4;
            if (pixels[offset + 3] <= 127)
                continue;
            shape[i] = 255;
            var luminance =
                (0.2126f * pixels[offset] + 0.7152f * pixels[offset + 1] + 0.0722f * pixels[offset + 2])
                / 255f;
            var weight = Mathf.Clamp((InkLight - luminance) / (InkLight - InkDark), 0f, 1f);
            ink[i] = (byte)Mathf.RoundToInt(weight * 255f);
        }

        var inner = Morph(shape, width, height, OutlineRadius, smallest: true);
        var thickened = Morph(ink, width, height, InkRadius, smallest: false);

        // White throughout; the alpha channel carries the whole drawing, exactly as it
        // does in the game's own icons, so the row's tint applies to this one too.
        var drawing = new byte[count * 4];
        for (var i = 0; i < count; i++)
        {
            var offset = i * 4;
            drawing[offset] = drawing[offset + 1] = drawing[offset + 2] = 255;
            drawing[offset + 3] = (byte)Math.Max(Math.Max(0, shape[i] - inner[i]), thickened[i]);
        }

        var artHeight = Mathf.RoundToInt(ArtWidth * (float)sourceHeight / sourceWidth);
        var line = Image.CreateFromData(width, height, false, Image.Format.Rgba8, drawing);
        line.Resize(ArtWidth, artHeight, Image.Interpolation.Lanczos);

        var plain = Image.CreateEmpty(Canvas, Canvas, false, Image.Format.Rgba8);
        plain.Fill(new Color(1f, 1f, 1f, 0f));
        plain.BlitRect(
            line,
            new Rect2I(0, 0, ArtWidth, artHeight),
            new Vector2I(CenterX - ArtWidth / 2, CenterY - artHeight / 2));

        // Every step above is an engine call that reports failure by pushing an error
        // and returning, not by throwing — so the guard around all this would never see
        // it, and a blank texture would go up as the button's face. Asking the result
        // whether anything was actually drawn catches all of them at once.
        return Drawn(plain) ? (plain, Glow(plain)) : null;
    }

    /// <summary>Whether an icon came out of that with any ink on it at all.</summary>
    private static bool Drawn(Image image)
    {
        var pixels = image.GetData();
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] > 0)
                return true;
        }
        return false;
    }

    /// <summary>The icon laid over a blurred, brightened copy of itself.</summary>
    private static Image Glow(Image plain)
    {
        var pixels = plain.GetData();
        var count = Canvas * Canvas;
        var alpha = new float[count];
        for (var i = 0; i < count; i++)
            alpha[i] = pixels[i * 4 + 3];

        var spread = alpha;
        for (var pass = 0; pass < 3; pass++)
            spread = Blur(spread, Canvas, Canvas, GlowBox);

        var glow = new byte[count * 4];
        for (var i = 0; i < count; i++)
        {
            var offset = i * 4;
            glow[offset] = glow[offset + 1] = glow[offset + 2] = 255;
            glow[offset + 3] = (byte)Mathf.RoundToInt(
                Mathf.Clamp(Math.Max(alpha[i], spread[i] * GlowGain), 0f, 255f));
        }
        return Image.CreateFromData(Canvas, Canvas, false, Image.Format.Rgba8, glow);
    }

    /// <summary>
    /// Erode or dilate by a square kernel, done as a horizontal pass then a vertical
    /// one — the same result as the square, at a cost that grows with the radius rather
    /// than its square. Outside the image counts as empty, so a shape touching the edge
    /// still erodes away from it instead of growing a false contour along it.
    /// </summary>
    private static byte[] Morph(byte[] source, int width, int height, int radius, bool smallest)
    {
        if (radius <= 0)
            return source;

        var horizontal = new byte[source.Length];
        var result = new byte[source.Length];
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                var best = smallest ? 255 : 0;
                for (var step = -radius; step <= radius; step++)
                {
                    var at = x + step;
                    var value = at < 0 || at >= width ? 0 : source[row + at];
                    best = smallest ? Math.Min(best, value) : Math.Max(best, value);
                }
                horizontal[row + x] = (byte)best;
            }
        }
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var best = smallest ? 255 : 0;
                for (var step = -radius; step <= radius; step++)
                {
                    var at = y + step;
                    var value = at < 0 || at >= height ? 0 : horizontal[at * width + x];
                    best = smallest ? Math.Min(best, value) : Math.Max(best, value);
                }
                result[y * width + x] = (byte)best;
            }
        }
        return result;
    }

    /// <summary>
    /// One box pass, horizontal then vertical. Outside the canvas is empty rather than
    /// a smear of the edge pixel: the glow spreads far enough to reach all four sides,
    /// and repeating the border there would pile up a bright rim along them.
    /// </summary>
    private static float[] Blur(float[] source, int width, int height, int box)
    {
        var radius = box / 2;
        var span = radius * 2 + 1;
        var horizontal = new float[source.Length];
        var result = new float[source.Length];
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                var total = 0f;
                for (var step = -radius; step <= radius; step++)
                {
                    var at = x + step;
                    if (at >= 0 && at < width)
                        total += source[row + at];
                }
                horizontal[row + x] = total / span;
            }
        }
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var total = 0f;
                for (var step = -radius; step <= radius; step++)
                {
                    var at = y + step;
                    if (at >= 0 && at < height)
                        total += horizontal[at * width + x];
                }
                result[y * width + x] = total / span;
            }
        }
        return result;
    }
}
