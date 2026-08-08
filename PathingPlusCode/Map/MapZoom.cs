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

    private static readonly Color Parchment = new(0.898f, 0.882f, 0.831f);

    /// <summary>Shared by every view-change animation, node icon counter-spin included.</summary>
    public const double TweenDuration = 0.55;

    private readonly NMapScreen _screen;
    private readonly Control _theMap;
    private readonly Func<IReadOnlyList<Vector2>> _nodeCenters;
    private readonly Control _tray;
    private readonly MegaLabel _label;
    private NHotkeyIcon? _hotkeyIcon;
    private Tween? _tween;

    public MapViewMode Mode { get; private set; } = MapViewMode.Normal;

    /// <summary>Any state where the whole act is on screen and scrolling is frozen.</summary>
    public bool Zoomed => Mode != MapViewMode.Normal;

    public bool Rotated => Mode == MapViewMode.Rotated;

    /// <summary>Raised after the state changes, either direction, any cause.</summary>
    public event Action? Toggled;

    /// <summary>The button, hidden while the map screen itself is closed.</summary>
    public void SetButtonVisible(bool visible)
    {
        _tray.Visible = visible;
        // The control scheme can change between opens; re-ask on the way in.
        if (visible)
            RefreshHotkeyIcon();
    }

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

        // The native glyph for the hotkey that does the same thing, shown on the
        // button's left edge only while a controller is driving.
        Guard.Run("Zoom hotkey glyph", () =>
        {
            _hotkeyIcon = SceneHelper.Instantiate<NHotkeyIcon>("ui/hotkey_icon");
            _hotkeyIcon.CustomMinimumSize = new Vector2(44, 44);
            _hotkeyIcon.AnchorTop = _hotkeyIcon.AnchorBottom = 0.5f;
            _hotkeyIcon.OffsetLeft = -50f;
            _hotkeyIcon.OffsetRight = -6f;
            _hotkeyIcon.OffsetTop = -22f;
            _hotkeyIcon.OffsetBottom = 22f;
            _hotkeyIcon.MouseFilter = Control.MouseFilterEnum.Ignore;
            _tray.AddChild(_hotkeyIcon);
            RefreshHotkeyIcon();
        });

        // The glyph follows the control scheme live: the player may have opened the
        // map with a mouse and picked up the pad afterwards.
        Guard.Run("Watching the control scheme", () =>
        {
            if (NControllerManager.Instance is not { } controllers)
                return;
            controllers.Connect(NControllerManager.SignalName.ControllerDetected,
                Callable.From(() => Guard.Run("Controller detected", RefreshHotkeyIcon)));
            controllers.Connect(NControllerManager.SignalName.MouseDetected,
                Callable.From(() => Guard.Run("Mouse detected", RefreshHotkeyIcon)));
        });
    }

    /// <summary>Glyph and visibility follow the current control scheme.</summary>
    public void RefreshHotkeyIcon()
    {
        if (_hotkeyIcon is null || !GodotObject.IsInstanceValid(_hotkeyIcon))
            return;
        _hotkeyIcon.Visible = NControllerManager.Instance?.IsUsingDirectionalNavigation == true;
        _hotkeyIcon.UpdateInput(Controller.rightTrigger);
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
