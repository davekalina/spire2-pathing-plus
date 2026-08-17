using Godot;

namespace PathingPlus.PathingPlusCode.Map;

/// <summary>
/// A map pin, drawn from scratch.
///
/// The game has no pin. Its map icon set is the player's own marker, a ping ring, and
/// the treasure X — nothing that says "this one is pinned", and the ink ring that was
/// standing in for one already means "a node in the plan" out on the map. So this one
/// is generated: the round-headed, pointed marker that every map has used for decades,
/// which is the shape a player will read as a pin without being told.
///
/// It is **white on transparent**, so the caller tints it — the legend gives each pin
/// its route's own colour, which is what ties the mark to the line it stands for. That
/// is also how the game's own drawing-toolbar icons work.
///
/// The outline carries a low, fixed wobble. A ruled circle-and-cone reads as a web
/// glyph dropped onto parchment; a few harmonics of noise on the radius give it the
/// slightly-off edge everything else on this map is drawn with. Fixed rather than
/// random because the pin is the same pin every time, and one that shimmered between
/// redraws would be worse than one that is merely regular.
/// </summary>
internal static class PinIcon
{
    /// <summary>Texture size. Drawn larger than it is shown, so the legend can scale it down cleanly.</summary>
    private const int Size = 64;

    /// <summary>Drawn at this multiple and shrunk, which is where the antialiasing comes from.</summary>
    private const int Supersample = 4;

    /// <summary>Shape, in fractions of the texture: the head, its hole, and the point.</summary>
    private const float HeadY = 0.34f;
    private const float HeadRadius = 0.30f;
    private const float HoleRadius = 0.125f;
    private const float TipY = 0.94f;

    /// <summary>How far off true the outline wanders. Enough to read as inked, not as broken.</summary>
    private const float Wobble = 0.7f;

    private static Image? _drawn;
    private static bool _attempted;

    /// <summary>
    /// A texture of the pin, or null if it could not be built. The pixels are worked out
    /// once; each caller gets its own texture over them, for the same reason
    /// <see cref="PathToolIcon" /> does it that way — an <c>Image</c> is inert data and
    /// survives a scene teardown, where a handed-out texture may not.
    /// </summary>
    public static Texture2D? Build() => Guard.Run("Drawing the pin", () =>
    {
        if (!_attempted)
        {
            _attempted = true;
            _drawn = Draw();
        }
        return _drawn is null ? null : (Texture2D)ImageTexture.CreateFromImage(_drawn);
    }, null);

    private static Image Draw()
    {
        var n = Size * Supersample;
        var pixels = new byte[n * n * 4];
        for (var y = 0; y < n; y++)
        {
            var py = (y + 0.5f) / n;
            for (var x = 0; x < n; x++)
            {
                var px = (x + 0.5f) / n;
                var dx = px - 0.5f;
                var dy = py - HeadY;
                var distance = MathF.Sqrt(dx * dx + dy * dy);
                var angle = MathF.Atan2(dy, dx);

                var head = distance <= HeadRadius * (1f + Wobble * (
                    0.055f * MathF.Sin(3f * angle + 0.7f) +
                    0.035f * MathF.Sin(5f * angle + 2.1f) +
                    0.022f * MathF.Sin(9f * angle + 4.3f)));
                var hole = distance <= HoleRadius * (1f + Wobble * (
                    0.05f * MathF.Sin(4f * angle + 1.3f) +
                    0.03f * MathF.Sin(7f * angle + 3.7f)));

                // The point: the head's width tapering to nothing, bowed out slightly at
                // its middle so the sides are drawn rather than ruled.
                var along = Mathf.Clamp((py - HeadY) / (TipY - HeadY), 0f, 1f);
                var cone = py >= HeadY && py <= TipY &&
                    MathF.Abs(dx) <= HeadRadius * (1f - along) *
                        (1f + Wobble * 0.10f * MathF.Sin(along * MathF.PI));

                var offset = (y * n + x) * 4;
                pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = 255;
                pixels[offset + 3] = (byte)((head || cone) && !hole ? 255 : 0);
            }
        }

        var image = Image.CreateFromData(n, n, false, Image.Format.Rgba8, pixels);
        image.Resize(Size, Size, Image.Interpolation.Lanczos);
        return image;
    }
}
