using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Helpers;
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

    private static readonly Color Parchment = new(0.898f, 0.882f, 0.831f);

    /// <summary>Shared by every view-change animation, node icon counter-spin included.</summary>
    public const double TweenDuration = 0.55;

    private readonly NMapScreen _screen;
    private readonly Control _theMap;
    private readonly Func<IReadOnlyList<Vector2>> _nodeCenters;
    private readonly Control _tray;
    private readonly MegaLabel _label;
    private Tween? _tween;

    public MapViewMode Mode { get; private set; } = MapViewMode.Normal;

    /// <summary>Any state where the whole act is on screen and scrolling is frozen.</summary>
    public bool Zoomed => Mode != MapViewMode.Normal;

    public bool Rotated => Mode == MapViewMode.Rotated;

    /// <summary>Raised after the state changes, either direction, any cause.</summary>
    public event Action? Toggled;

    /// <summary>The button, hidden while the map screen itself is closed.</summary>
    public void SetButtonVisible(bool visible) => _tray.Visible = visible;

    public MapZoom(NMapScreen screen, Control theMap, Func<IReadOnlyList<Vector2>> nodeCenters)
    {
        _screen = screen;
        _theMap = theMap;
        _nodeCenters = nodeCenters;

        // The Compendium tile art, so this reads as a real button and not floating
        // text — same background, expand, and stretch as compendium_bottom_button.
        _tray = new Control { Name = "PathingPlusZoomTray" };
        _tray.AnchorLeft = _tray.AnchorRight = 1f;
        _tray.OffsetLeft = -220f;
        _tray.OffsetRight = -48f;
        // Below the top bar AND the debug version/modded readout in the corner.
        _tray.OffsetTop = 196f;
        _tray.OffsetBottom = 272f;
        _tray.GrowHorizontal = Control.GrowDirection.Begin;

        var background = new TextureRect
        {
            Name = "BgPanel",
            Texture = ResourceLoader.Load<Texture2D>(
                "res://images/packed/common_ui/submenu_compendium_button.png",
                null, ResourceLoader.CacheMode.Reuse),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            ClipContents = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _tray.AddChild(background);

        var button = new Control
        {
            Name = "ZoomButton",
            FocusMode = Control.FocusModeEnum.None,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        button.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _tray.AddChild(button);

        _label = new MegaLabel
        {
            AutoSizeEnabled = false,
            Text = "Zoom",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        if (screen.GetNodeOrNull<Label>("MapLegend/Header")?.GetThemeFont("font") is { } font)
            _label.AddThemeFontOverride("font", font);
        _label.AddThemeFontSizeOverride("font_size", 28);
        _label.AddThemeColorOverride("font_color", Parchment);
        _label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.55f));
        _label.AddThemeConstantOverride("shadow_offset_x", 4);
        _label.AddThemeConstantOverride("shadow_offset_y", 3);
        _label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        button.AddChild(_label);

        button.GuiInput += inputEvent => Guard.Run("Zoom button", () =>
        {
            if (inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
                Toggle();
        });

        screen.AddChild(_tray);
    }

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

    private void Apply()
    {
        _label.AddThemeColorOverride("font_color", Zoomed ? StsColors.gold : Parchment);

        var centers = _nodeCenters();
        if (Mode != MapViewMode.Normal && centers.Count == 0)
        {
            Mode = MapViewMode.Normal;
            _label.AddThemeColorOverride("font_color", Parchment);
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
        // and the map takes only the left 85% of the screen, leaving the right edge
        // (the boss end) clear of the legend.
        var frameWidth = Mode == MapViewMode.Rotated ? _screen.Size.X * 0.85f : _screen.Size.X;
        var scale = Mode == MapViewMode.Rotated
            ? Mathf.Min(1f, Mathf.Min((frameWidth - 40f) / extent.Y, (_screen.Size.Y - 40f) / extent.X))
            : Mathf.Min(1f, Mathf.Min((frameWidth - 40f) / extent.X, (_screen.Size.Y - 40f) / extent.Y));
        // A strict 85% fit proved too timid: 10% back in, letting the map's right
        // end overlap the legend a little.
        if (Mode == MapViewMode.Rotated)
            scale = Mathf.Min(1f, scale * 1.1f);
        var rotation = Mode == MapViewMode.Rotated ? 90f : 0f;

        // Pivot on the content centre: with the pivot there, the centre lands at
        // Position + pivot regardless of scale or rotation, so one position formula
        // serves both zoomed states and the tween cannot lurch.
        _theMap.PivotOffset = center;
        AnimateTo(scale, rotation, new Vector2(frameWidth * 0.5f, _screen.Size.Y * 0.5f) - center);
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
