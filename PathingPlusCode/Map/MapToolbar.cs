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
    /// <summary>Tray geometry, and the slots the gear and the Zoom button sit in.</summary>
    public const float Width = 288f;
    public const float ButtonRowTop = 12f;
    public const float ButtonHeight = 60f;
    public const float GearLeft = 14f;
    public const float GearSize = 56f;
    public const float ZoomLeft = 82f;
    public const float ZoomWidth = 192f;

    private const float BylineTop = 78f;
    private const float Height = 112f;

    private static readonly Color Parchment = new(0.898f, 0.882f, 0.831f);

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

        var background = new NinePatchRect
        {
            Name = "Tray",
            SelfModulate = new Color(0f, 0f, 0f, 0.75f),
            Texture = ResourceLoader.Load<Texture2D>(
                "res://images/ui/tiny_nine_patch.png", null, ResourceLoader.CacheMode.Reuse),
            PatchMarginLeft = 12,
            PatchMarginTop = 12,
            PatchMarginRight = 12,
            PatchMarginBottom = 12,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(background);

        var byline = new MegaLabel
        {
            AutoSizeEnabled = false,
            Text = $"{MainFile.ModName} {MainFile.Version} by {MainFile.Author}",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            // Present, not shouting: it is a signature, not a control.
            Modulate = new Color(1f, 1f, 1f, 0.7f),
            Position = new Vector2(0f, BylineTop),
            Size = new Vector2(Width, 22f),
        };
        if (screen.GetNodeOrNull<Label>("MapLegend/Header")?.GetThemeFont("font") is { } font)
            byline.AddThemeFontOverride("font", font);
        byline.AddThemeFontSizeOverride("font_size", 15);
        byline.AddThemeColorOverride("font_color", Parchment);
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
