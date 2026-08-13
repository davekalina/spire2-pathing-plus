using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;
using System.Reflection;

namespace PathingPlus.PathingPlusCode.Map;

/// <summary>The three map views the Zoom button cycles through.</summary>
internal enum MapViewMode
{
    /// <summary>The game's own scrolling view.</summary>
    Normal,

    /// <summary>The whole act on screen at once.</summary>
    Zoomed,

    /// <summary>The whole act rotated a quarter turn: start on the left, boss on the right.</summary>
    Rotated,
}

/// <summary>
/// The Zoom toggle, upper right: Normal → Zoomed → Rotated → Normal. Transitions are
/// animated — scale and rotation by tween, position by writing the drag target and
/// letting the screen's own smoothing chase it. All transforms pivot on the map
/// content's centre, which is what keeps a scale+rotation tween from lurching, and
/// scaling happens with the native scroll handlers frozen so nothing fights the view.
/// Zooming back to Normal snaps to the current row the way the screen's own
/// controller code does.
/// </summary>
internal sealed class MapZoom : IDisposable
{
    private static readonly FieldInfo? TargetDragPosField =
        AccessTools.Field(typeof(NMapScreen), "_targetDragPos");
    private static readonly FieldInfo? DistYField =
        AccessTools.Field(typeof(NMapScreen), "_distY");

    /// <summary>Shared by every view-change animation, node icon counter-spin included.</summary>
    public const double TweenDuration = 0.55;

    private readonly NMapScreen _screen;
    private readonly Control _theMap;
    private readonly Func<IReadOnlyList<Vector2>> _nodeCenters;
    private readonly Control _tray;
    private readonly MegaLabel _label;
    private Control? _button;

    /// <summary>The face's resting tone; focus lifts it to full so the d-pad is visible.</summary>
    private static readonly Color TrayIdle = new(0.92f, 0.92f, 0.92f);
    private HotkeyGlyph? _hotkeyGlyph;
    private Tween? _tween;

    public MapViewMode Mode { get; private set; } = MapViewMode.Normal;

    /// <summary>Any state where the whole act is on screen and scrolling is frozen.</summary>
    public bool Zoomed => Mode != MapViewMode.Normal;

    public bool Rotated => Mode == MapViewMode.Rotated;

    /// <summary>Raised after the state changes, either direction, any cause.</summary>
    public event Action? Toggled;

    /// <summary>The button, hidden while the map screen itself is closed.</summary>
    /// <summary>Where the d-pad lands on this control coming from elsewhere.</summary>
    public Control? Focusable => _button;

    public void SetButtonVisible(bool visible)
    {
        _tray.Visible = visible;
        // The control scheme can change between opens; re-ask on the way in.
        if (visible)
            RefreshHotkeyIcon();
    }

    public MapZoom(
        NMapScreen screen, Control theMap, Control toolbar,
        Func<IReadOnlyList<Vector2>> nodeCenters)
    {
        _screen = screen;
        _theMap = theMap;
        _nodeCenters = nodeCenters;

        // The Compendium tile art, so this reads as a real button and not floating
        // text — same background, expand, and stretch as compendium_bottom_button.
        // It sits in the toolbar's button row beside the gear.
        _tray = new Control
        {
            Name = "PathingPlusZoomButton",
            Position = new Vector2(MapToolbar.ZoomLeft, MapToolbar.ButtonRowTop),
            Size = new Vector2(MapToolbar.ZoomWidth, MapToolbar.ButtonHeight),
        };

        // The pause menu's own button face, shader and all: the same art the Resume
        // button wears, so this reads as a button of the game rather than a tile.
        var background = new TextureRect
        {
            Name = "ButtonImage",
            Texture = ResourceLoader.Load<Texture2D>(
                "res://images/ui/reward_screen/reward_item_button.png",
                null, ResourceLoader.CacheMode.Reuse),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        Guard.Run("Zoom button tint", () =>
        {
            var shader = ResourceLoader.Load<Shader>(
                "res://shaders/hsv.gdshader", null, ResourceLoader.CacheMode.Reuse);
            var material = new ShaderMaterial { Shader = shader };
            material.SetShaderParameter("h", 1.0f);
            material.SetShaderParameter("s", 0.8f);
            material.SetShaderParameter("v", 0.9f);
            background.Material = material;
        });
        _tray.AddChild(background);

        var button = new Control
        {
            Name = "ZoomButton",
            FocusMode = Control.FocusModeEnum.All,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _button = button;
        button.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _tray.AddChild(button);

        // Lettering copied from the pause menu button: same font, cream, and the deep
        // teal outline that keeps it legible against the button face.
        _label = new MegaLabel
        {
            AutoSizeEnabled = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        Guard.Run("Zoom button lettering", () =>
            _label.AddThemeFontOverride("font", ResourceLoader.Load<Font>(
                "res://themes/kreon_bold_glyph_space_one.tres",
                null, ResourceLoader.CacheMode.Reuse)));
        _label.AddThemeFontSizeOverride("font_size", 22);
        _label.AddThemeColorOverride("font_color", new Color(1f, 0.964706f, 0.886275f));
        _label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.125f));
        _label.AddThemeColorOverride("font_outline_color", new Color(0.144f, 0.3312f, 0.36f));
        _label.AddThemeConstantOverride("shadow_offset_x", 4);
        _label.AddThemeConstantOverride("shadow_offset_y", 4);
        _label.AddThemeConstantOverride("outline_size", 12);
        _label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        button.AddChild(_label);
        UpdateLabel();

        button.FocusEntered += () => Guard.Run("Zoom focus", () => _tray.Modulate = Colors.White);
        button.FocusExited += () => Guard.Run("Zoom unfocus", () => _tray.Modulate = TrayIdle);
        button.GuiInput += inputEvent => Guard.Run("Zoom button", () =>
        {
            if (!inputEvent.IsActionPressed(MegaInput.select) &&
                inputEvent is not InputEventMouseButton
                    { ButtonIndex: MouseButton.Left, Pressed: false })
                return;
            Toggle();
            button.AcceptEvent();
        });

        _tray.Modulate = TrayIdle;
        toolbar.AddChild(_tray);

        // The glyph for the hotkey that does the same thing, inside the button on its
        // left, with the label shifting across to make room — but only while the
        // glyph is actually showing, so a mouse player keeps a centred label.
        Guard.Run("Zoom hotkey glyph", () =>
        {
            _hotkeyGlyph = new HotkeyGlyph(_tray, Controller.rightTrigger, new Vector2(38, 38));
            var icon = _hotkeyGlyph.Node;
            icon.AnchorTop = icon.AnchorBottom = 0.5f;
            icon.OffsetLeft = 10f;
            icon.OffsetRight = 48f;
            icon.OffsetTop = -19f;
            icon.OffsetBottom = 19f;
            _hotkeyGlyph.VisibilityChanged += showing => Guard.Run("Zoom label spacing", () =>
                _label.OffsetLeft = showing ? 48f : 0f);
            _hotkeyGlyph.Refresh();
        });
    }

    /// <summary>Glyph and visibility follow the current control scheme.</summary>
    public void RefreshHotkeyIcon() => _hotkeyGlyph?.Refresh();

    public void Toggle()
    {
        Mode = Mode switch
        {
            MapViewMode.Normal => MapViewMode.Zoomed,
            MapViewMode.Zoomed => MapViewMode.Rotated,
            _ => MapViewMode.Normal,
        };
        Apply();
        Toggled?.Invoke();
    }

    /// <summary>Re-fit the current view after a framing setting changed.</summary>
    public void Reapply()
    {
        if (Mode != MapViewMode.Normal)
            Apply();
    }

    /// <summary>Back to the normal view — on map change, map close, and map open.</summary>
    public void Reset()
    {
        if (Mode == MapViewMode.Normal)
            return;
        Mode = MapViewMode.Normal;
        Apply();
        Toggled?.Invoke();
    }

    public void Dispose()
    {
        Reset();
        if (GodotObject.IsInstanceValid(_tray))
            _tray.QueueFree();
    }

    /// <summary>
    /// The button says what pressing it will do, not where the map is now — so it
    /// reads as an instruction rather than a state the player has to decode.
    /// </summary>
    private void UpdateLabel() => _label.Text = Mode switch
    {
        MapViewMode.Normal => "Zoom Out",
        MapViewMode.Zoomed => "Rotate",
        _ => "Zoom In",
    };

    private void Apply()
    {
        UpdateLabel();

        var centers = _nodeCenters();
        // Nothing drawn to frame yet: stay in the normal view, and say so.
        if (Mode != MapViewMode.Normal && centers.Count == 0)
        {
            Mode = MapViewMode.Normal;
            UpdateLabel();
        }

        if (Mode == MapViewMode.Normal)
        {
            var row = RunManager.Instance?.DebugOnlyGetState()?.CurrentMapCoord?.row ?? 0;
            var distY = DistYField?.GetValue(_screen) as float? ?? 155f;
            AnimateTo(1f, 0f, new Vector2(0f, Mathf.Clamp(-600f + row * distY, -600f, 1800f)));
            return;
        }

        var min = centers.Aggregate((a, b) => a.Min(b));
        var max = centers.Aggregate((a, b) => a.Max(b));
        // The boss art towers well above its node centre; give the top extra room.
        min -= new Vector2(160f, 330f);
        max += new Vector2(160f, 150f);
        var extent = max - min;
        var center = (min + max) * 0.5f;

        // Rotated, the map's height lies along the screen's width — the fit swaps —
        // and the map is fitted into a fraction of the screen, leaving the right edge
        // (the boss end) clear of the legend. Fit, extra zoom, and the two nudges are
        // all live-tunable from the settings panel.
        var rotated = Mode == MapViewMode.Rotated;
        var frameWidth = rotated ? _screen.Size.X * PathingOptions.LandscapeFit : _screen.Size.X;
        var scale = rotated
            ? Mathf.Min((frameWidth - 40f) / extent.Y, (_screen.Size.Y - 40f) / extent.X) *
                PathingOptions.LandscapeZoom
            : Mathf.Min(1f, Mathf.Min((frameWidth - 40f) / extent.X, (_screen.Size.Y - 40f) / extent.Y));
        scale = Mathf.Min(1f, scale);
        var nudge = rotated
            ? new Vector2(PathingOptions.LandscapeShiftX, PathingOptions.LandscapeShiftY)
            : Vector2.Zero;

        // Pivot on the content centre: with the pivot there, the centre lands at
        // Position + pivot regardless of scale or rotation, so one position formula
        // serves both zoomed states and the tween cannot lurch.
        _theMap.PivotOffset = center;
        AnimateTo(scale, rotated ? 90f : 0f,
            new Vector2(frameWidth * 0.5f, _screen.Size.Y * 0.5f) + nudge - center);
    }

    private void AnimateTo(float scale, float rotationDegrees, Vector2 dragTarget)
    {
        // Position: write the drag target and let UpdateScrollPosition's own lerp
        // chase it — tweening Position ourselves would fight that per-frame lerp.
        TargetDragPosField?.SetValue(_screen, dragTarget);

        _tween?.Kill();
        _tween = _theMap.CreateTween().SetParallel();
        _tween.TweenProperty(_theMap, "scale", Vector2.One * scale, TweenDuration)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        _tween.TweenProperty(_theMap, "rotation_degrees", rotationDegrees, TweenDuration)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    }
}
