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

        var mark = MakeLabel(24, "?");
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

    private static string Instructions =>
        "Pathing Plus is designed to help you draw better paths (and hopefully make " +
        "better pathing decisions as a result).\n" +
        "\n" +
        "How it works: in Drawing Mode, Pathing Plus overrides the standard drawing " +
        "controls to draw best-fit paths where you decide to draw. Use the eraser to " +
        "remove unwanted paths, and Clear Drawings to clear all of them.\n" +
        "\n" +
        "In Auto Mode, click the nodes you most want to visit and the mod works out " +
        "the path or paths that connect your nodes of choice. Double-click a node " +
        "type in the legend to select every node of that type.\n" +
        "\n" +
        "Manual Mode is Auto Mode without the attempt to run every path on to the " +
        "boss.\n" +
        "\n" +
        "Use the Zoom Out / Rotate button for different views of the map.\n" +
        "\n" +
        "This mod was also designed with FULL GAMEPAD SUPPORT in mind, because " +
        "drawing lines on the map with a gamepad isn't really a thing.";

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
