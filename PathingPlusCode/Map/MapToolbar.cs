using Godot;
using MegaCrit.Sts2.addons.mega_text;

namespace PathingPlus.PathingPlusCode.Map;

/// <summary>
/// The mod's corner of the map screen: one tray holding the settings gear, the Zoom
/// button, and the byline beneath them.
///
/// They were three separate things in three separate places — a bare gear, a tile
/// button, and a line of text — which read as unrelated widgets that happened to
/// land near each other. One tray in the map's own dark nine-patch, the same
/// material as the drawing tools, makes them one cluster that plainly belongs to the
/// mod, and gives the byline somewhere to sit quietly instead of competing.
/// </summary>
internal sealed class MapToolbar : IDisposable
{
    /// <summary>
    /// Tray geometry, and the slots along its button row: help, gear, then Zoom.
    /// The tray grew to fit the help badge rather than squeezing the Zoom button,
    /// which has to hold "Zoom Out" without wrapping.
    /// </summary>
    public const float Width = 348f;
    public const float ButtonRowTop = 22f;
    public const float ButtonHeight = 62f;
    public const float HelpLeft = 22f;
    public const float HelpSize = 52f;
    public const float GearLeft = 84f;
    public const float GearSize = 46f;
    public const float ZoomLeft = 140f;
    public const float ZoomWidth = 184f;

    /// <summary>The Auto-Path pull-down, its own row beneath the buttons.</summary>
    public const float AutoPathTop = 94f;
    public const float AutoPathHeight = 40f;
    public const float AutoPathLeft = 22f;
    public const float AutoPathWidth = Width - AutoPathLeft * 2f;

    private const float BylineTop = 146f;
    private const float Height = 196f;

    private readonly Control _root;

    public MapToolbar(Control screen)
    {
        _root = new Control { Name = "PathingPlusToolbar", MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.AnchorLeft = _root.AnchorRight = 1f;
        _root.OffsetLeft = -(Width + 24f);
        _root.OffsetRight = -24f;
        // Below the top bar and the debug version/seed block in the corner.
        _root.OffsetTop = 190f;
        _root.OffsetBottom = 190f + Height;
        _root.GrowHorizontal = Control.GrowDirection.Begin;

        // The Compendium's own card panel, laid on its side: that art is portrait
        // (368x490) and this dock is landscape, so it is built at swapped dimensions
        // and turned a quarter clockwise. Rotating about the top-left corner sends
        // local (x, y) to (-y, x), so the node starts at the tray's right edge for
        // the turned rectangle to land exactly on the tray.
        var background = new TextureRect
        {
            Name = "Tray",
            Texture = ResourceLoader.Load<Texture2D>(
                "res://images/packed/common_ui/submenu_panel_short.png",
                null, ResourceLoader.CacheMode.Reuse),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            PivotOffset = Vector2.Zero,
            Size = new Vector2(Height, Width),
            Position = new Vector2(Width, 0f),
            RotationDegrees = 90f,
        };
        _root.AddChild(background);

        var byline = new MegaLabel
        {
            AutoSizeEnabled = false,
            // Name and version only. The author belongs in the help panel, where
            // there is room to say it once and properly.
            Text = $"{MainFile.ModName} {MainFile.Version}",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Position = new Vector2(0f, BylineTop),
            Size = new Vector2(Width, 24f),
        };
        if (screen.GetNodeOrNull<Label>("MapLegend/Header")?.GetThemeFont("font") is { } font)
            byline.AddThemeFontOverride("font", font);
        byline.AddThemeFontSizeOverride("font_size", 16);
        // Pale lettering carried by a dark shadow, the way the game sets its own text
        // over busy art: the drop shadow does the separating, so the glyphs stay light
        // and thin instead of turning into dark ink the panel's grain can swallow.
        byline.AddThemeColorOverride("font_color", new Color(0.96f, 0.93f, 0.86f));
        byline.AddThemeColorOverride("font_outline_color", new Color(0.09f, 0.07f, 0.06f, 0.85f));
        byline.AddThemeConstantOverride("outline_size", 6);
        byline.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.4f));
        byline.AddThemeConstantOverride("shadow_offset_x", 1);
        byline.AddThemeConstantOverride("shadow_offset_y", 2);
        _root.AddChild(byline);

        screen.AddChild(_root);
    }

    /// <summary>Where the gear and the Zoom button attach.</summary>
    public Control Root => _root;

    public void SetVisible(bool visible) => _root.Visible = visible;

    public void Dispose()
    {
        if (GodotObject.IsInstanceValid(_root))
            _root.QueueFree();
    }
}
