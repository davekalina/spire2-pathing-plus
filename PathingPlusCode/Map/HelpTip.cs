using Godot;
using MegaCrit.Sts2.addons.mega_text;

namespace PathingPlus.PathingPlusCode.Map;

/// <summary>
/// The "?" badge beside the settings gear, and the panel of instructions it shows.
///
/// The mod repurposes controls the game already taught the player — the quill, the
/// eraser, Clear Drawings — so nothing on screen announces that they now mean
/// something different. This is where that is written down, one hover away, instead
/// of only on a Workshop page nobody reads twice.
/// </summary>
internal sealed class HelpTip : IDisposable
{
    private const float TipWidth = 600f;
    private const float TipHeight = 830f;

    /// <summary>Gap between the tip's right edge and the toolbar's left edge.</summary>
    private const float TipGap = 16f;

    /// <summary>Inset from the panel's edge, clear of the art's torn border.</summary>
    private const float Pad = 46f;

    private static readonly Color Parchment = new(0.96f, 0.94f, 0.88f);
    private static readonly Color BadgeIdle = new(1f, 1f, 1f, 0.6f);

    private readonly Control _root;
    private readonly Control _tip;
    private readonly Control _badge;
    private readonly TextureRect _circle;
    private readonly Font? _font;

    /// <summary>Clicked open, so it survives the pointer moving away to read it.</summary>
    private bool _pinned;

    public HelpTip(Control screen, Control toolbar)
    {
        _font = screen.GetNodeOrNull<Label>("MapLegend/Header")?.GetThemeFont("font");

        _root = new Control { Name = "PathingPlusHelp", MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        // Left of the toolbar and level with it: the map's own art is centred, so the
        // right margin is the one place a panel this size does not cover anything.
        _tip = new Control
        {
            Name = "HelpPanel",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorLeft = 1f,
            AnchorRight = 1f,
            OffsetRight = -(MapToolbar.Width + 24f + TipGap),
            OffsetLeft = -(MapToolbar.Width + 24f + TipGap + TipWidth),
            OffsetTop = 148f,
            OffsetBottom = 148f + TipHeight,
            GrowHorizontal = Control.GrowDirection.Begin,
        };
        // Darkened well below the card's own tone: this panel is a wall of body text,
        // and pale lettering only separates from parchment once the parchment stops
        // competing with it for the light end of the range.
        var parchment = new TextureRect
        {
            Name = "Panel",
            Texture = ResourceLoader.Load<Texture2D>(
                "res://images/packed/common_ui/submenu_panel_short.png",
                null, ResourceLoader.CacheMode.Reuse),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Modulate = new Color(0.40f, 0.36f, 0.33f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        parchment.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _tip.AddChild(parchment);

        // Absolute placement rather than a container: an autowrapping Label reports a
        // near-zero minimum width, so inside a VBox it would collapse to one word per
        // line. Given an explicit rect it wraps to the width it was handed.
        const float bodyTop = 138f;
        var title = MakeLabel(24, $"{MainFile.ModName} {MainFile.Version}\nby {MainFile.Author}");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.Position = new Vector2(Pad, 44f);
        title.Size = new Vector2(TipWidth - Pad * 2f, 76f);
        _tip.AddChild(title);

        var body = MakeLabel(17, Instructions);
        body.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.Position = new Vector2(Pad, bodyTop);
        body.Size = new Vector2(TipWidth - Pad * 2f, TipHeight - bodyTop - Pad);
        _tip.AddChild(body);

        _root.AddChild(_tip);
        screen.AddChild(_root);

        _badge = new Control
        {
            Name = "HelpBadge",
            MouseFilter = Control.MouseFilterEnum.Stop,
            Position = new Vector2(
                MapToolbar.HelpLeft,
                MapToolbar.ButtonRowTop + (MapToolbar.ButtonHeight - MapToolbar.HelpSize) / 2f),
            Size = new Vector2(MapToolbar.HelpSize, MapToolbar.HelpSize),
        };

        // The map's own hand-inked circle, the one the game stamps on visited nodes,
        // so the badge is drawn in the same hand as everything else on this screen.
        _circle = new TextureRect
        {
            Texture = ResourceLoader.Load<Texture2D>(
                "res://images/atlases/compressed.sprites/map/map_circle_4.tres",
                null, ResourceLoader.CacheMode.Reuse),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Size = new Vector2(MapToolbar.HelpSize, MapToolbar.HelpSize),
            Modulate = BadgeIdle,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _badge.AddChild(_circle);

        // Well inside the ring: the glyph filling the circle read as a crowded blob
        // rather than a badge.
        var mark = MakeLabel(22, "?");
        mark.HorizontalAlignment = HorizontalAlignment.Center;
        mark.VerticalAlignment = VerticalAlignment.Center;
        mark.Size = new Vector2(MapToolbar.HelpSize, MapToolbar.HelpSize);
        _badge.AddChild(mark);

        _badge.MouseEntered += () => Guard.Run("Help hover", () => Show(true));
        _badge.MouseExited += () => Guard.Run("Help unhover", () =>
        {
            if (!_pinned)
                Show(false);
        });
        _badge.GuiInput += inputEvent => Guard.Run("Pinning the help panel", () =>
        {
            if (inputEvent is not InputEventMouseButton
                { ButtonIndex: MouseButton.Left, Pressed: false })
                return;
            _pinned = !_pinned;
            Show(_pinned || _tip.Visible);
        });
        toolbar.AddChild(_badge);
    }

    /// <summary>Hidden with the map screen, like every other panel this mod adds.</summary>
    public void SetShellVisible(bool visible)
    {
        _root.Visible = visible;
        if (!visible)
        {
            _pinned = false;
            Show(false);
        }
    }

    public void Dispose()
    {
        if (GodotObject.IsInstanceValid(_root))
            _root.QueueFree();
    }

    private void Show(bool shown)
    {
        _tip.Visible = shown;
        _circle.Modulate = shown ? Colors.White : BadgeIdle;
        if (shown)
            _root.MoveToFront();
    }

    /// <summary>
    /// The panel's prose, from <c>text/help.txt</c> — human-facing text belongs in a
    /// file a human edits, not in a string literal behind a rebuild of the code.
    ///
    /// Comment lines go, and the lines within a paragraph are joined so the file can
    /// be hard-wrapped for editing without every one of those wraps turning into a
    /// line break on screen.
    /// </summary>
    private static string Instructions => _instructions ??= Guard.Run("Reading the help text", () =>
    {
        using var stream = typeof(HelpTip).Assembly.GetManifestResourceStream("help.txt");
        if (stream is null)
            return MissingText;
        using var reader = new StreamReader(stream);
        var lines = reader.ReadToEnd()
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n')
            .Where(line => !line.TrimStart().StartsWith('#'));

        var paragraphs = new List<string>();
        var current = new List<string>();
        foreach (var line in lines)
        {
            var text = line.Trim();
            if (text.Length == 0)
            {
                if (current.Count > 0)
                    paragraphs.Add(string.Join('\n', current));
                current.Clear();
                continue;
            }
            // A bullet is its own line; anything else continues the line above it. Without
            // this a list would be joined into one run-on paragraph by the same rule that
            // usefully unwraps prose.
            if (current.Count == 0 || text.StartsWith("- ") || text.StartsWith("* "))
                current.Add(text);
            else
                current[^1] = $"{current[^1]} {text}";
        }
        if (current.Count > 0)
            paragraphs.Add(string.Join('\n', current));

        return paragraphs.Count > 0 ? string.Join("\n\n", paragraphs) : MissingText;
    }, MissingText);

    private const string MissingText =
        "The help text could not be read. See text/help.txt in the mod's source.";

    private static string? _instructions;

    private MegaLabel MakeLabel(int fontSize, string text)
    {
        var label = new MegaLabel
        {
            AutoSizeEnabled = false,
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        if (_font is { })
            label.AddThemeFontOverride("font", _font);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", Parchment);
        // A drop shadow, not an outline. An outline traces every glyph on all sides,
        // which at this size closes the counters and thickens the strokes until a
        // paragraph turns into a grey mass; the game's own body text casts a shadow
        // down-right and leaves the letterforms alone.
        label.AddThemeConstantOverride("outline_size", 0);
        label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.75f));
        label.AddThemeConstantOverride("shadow_offset_x", 2);
        label.AddThemeConstantOverride("shadow_offset_y", 3);
        label.AddThemeConstantOverride("shadow_outline_size", 1);
        label.AddThemeConstantOverride("line_spacing", 5);
        return label;
    }
}
