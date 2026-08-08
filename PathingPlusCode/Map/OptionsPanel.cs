using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Helpers;

namespace PathingPlus.PathingPlusCode.Map;

/// <summary>
/// The mod's settings, folded behind a gear beside the Zoom button so they cost no
/// screen space until asked for. Clicking the gear drops a panel of toggle rows and
/// sliders in the map's own tray styling; every change writes through
/// <see cref="PathingOptions" />, which persists it and redraws the map.
/// </summary>
internal sealed class OptionsPanel : IDisposable
{
    private const string GearTexturePath =
        "res://images/atlases/ui_atlas.sprites/top_bar/top_bar_settings.tres";
    private static readonly Color Parchment = new(0.898f, 0.882f, 0.831f);
    private static readonly Color GearIdle = new(1f, 1f, 1f, 0.6f);

    private readonly Control _root;
    private readonly NinePatchRect _dropdown;
    private readonly TextureRect _gear;
    private readonly Font? _font;

    /// <summary>Each row's way of re-reading its option, for the reset button.</summary>
    private readonly List<Action> _refreshers = [];

    public OptionsPanel(Control screen)
    {
        _font = screen.GetNodeOrNull<Label>("MapLegend/Header")?.GetThemeFont("font");

        _root = new Control { Name = "PathingPlusOptions", MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        // The panel exists before the gear that toggles it, so the gear's handlers
        // close over a field that is already assigned.
        _dropdown = new NinePatchRect
        {
            Name = "OptionsDropdown",
            Visible = false,
            SelfModulate = new Color(0f, 0f, 0f, 0.85f),
            Texture = ResourceLoader.Load<Texture2D>(
                "res://images/ui/tiny_nine_patch.png", null, ResourceLoader.CacheMode.Reuse),
            PatchMarginLeft = 12,
            PatchMarginTop = 12,
            PatchMarginRight = 12,
            PatchMarginBottom = 12,
            MouseFilter = Control.MouseFilterEnum.Stop,
            AnchorLeft = 1f,
            AnchorRight = 1f,
            // Left of the gear, and left of the widest the legend gets, so the two
            // never share screen space.
            OffsetLeft = -784f,
            OffsetRight = -444f,
            OffsetTop = 206f,
            OffsetBottom = 662f,
        };

        // Beside the Zoom tray (which occupies right-edge offsets -220..-48 at y 196).
        _gear = new TextureRect
        {
            Name = "OptionsGear",
            Texture = ResourceLoader.Load<Texture2D>(
                GearTexturePath, null, ResourceLoader.CacheMode.Reuse),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Stop,
            Modulate = GearIdle,
            AnchorLeft = 1f,
            AnchorRight = 1f,
            OffsetLeft = -292f,
            OffsetRight = -236f,
            OffsetTop = 206f,
            OffsetBottom = 262f,
        };
        _gear.MouseEntered += () => Guard.Run("Gear hover", () => _gear.Modulate = Colors.White);
        _gear.MouseExited += () => Guard.Run("Gear unhover", () =>
            _gear.Modulate = _dropdown.Visible ? Colors.White : GearIdle);
        _gear.GuiInput += inputEvent => Guard.Run("Toggling the settings panel", () =>
        {
            if (inputEvent is not InputEventMouseButton
                { ButtonIndex: MouseButton.Left, Pressed: false })
                return;
            _dropdown.Visible = !_dropdown.Visible;
            // Last sibling wins Godot's input picking, so an open panel must be in
            // front or the map underneath eats its clicks.
            if (_dropdown.Visible)
                _root.MoveToFront();
        });
        _root.AddChild(_gear);

        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", 16);
        margin.AddThemeConstantOverride("margin_right", 16);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _dropdown.AddChild(margin);

        var rows = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        rows.AddThemeConstantOverride("separation", 4);
        margin.AddChild(rows);

        AddToggle(rows, "Auto Path Mode",
            () => PathingOptions.AutoPath, v => PathingOptions.AutoPath = v);
        AddToggle(rows, "Small Path Markers",
            () => PathingOptions.SmallMarkers, v => PathingOptions.SmallMarkers = v);

        AddSpacer(rows);
        AddSlider(rows, "Thickness", 0.5f, 4f, 0.1f,
            () => PathingOptions.DashWidth, v => PathingOptions.DashWidth = v);
        AddSlider(rows, "Dash Length", 0.5f, 4f, 0.1f,
            () => PathingOptions.DashLength, v => PathingOptions.DashLength = v);
        AddSlider(rows, "Length Jitter", 0f, 2f, 0.1f,
            () => PathingOptions.DashLengthVariance, v => PathingOptions.DashLengthVariance = v);
        AddSlider(rows, "Spacing", 6f, 30f, 1f,
            () => PathingOptions.DashSpacing, v => PathingOptions.DashSpacing = v);
        AddSlider(rows, "Route Gap", 0f, 24f, 1f,
            () => PathingOptions.RouteSeparation, v => PathingOptions.RouteSeparation = v);

        AddSpacer(rows);
        AddSlider(rows, "Wide Fit", 0.5f, 1f, 0.01f,
            () => PathingOptions.LandscapeFit, v => PathingOptions.LandscapeFit = v);
        AddSlider(rows, "Wide Zoom", 0.6f, 1.6f, 0.05f,
            () => PathingOptions.LandscapeZoom, v => PathingOptions.LandscapeZoom = v);
        AddSlider(rows, "Wide Shift X", -400f, 400f, 10f,
            () => PathingOptions.LandscapeShiftX, v => PathingOptions.LandscapeShiftX = v);
        AddSlider(rows, "Wide Shift Y", -300f, 300f, 10f,
            () => PathingOptions.LandscapeShiftY, v => PathingOptions.LandscapeShiftY = v);

        AddSpacer(rows);
        var reset = MakeLabel(18, "Reset to defaults");
        reset.MouseFilter = Control.MouseFilterEnum.Stop;
        reset.Modulate = new Color(1f, 1f, 1f, 0.75f);
        reset.GuiInput += inputEvent => Guard.Run("Resetting settings", () =>
        {
            if (inputEvent is not InputEventMouseButton
                { ButtonIndex: MouseButton.Left, Pressed: false })
                return;
            PathingOptions.ResetDefaults();
            foreach (var refresh in _refreshers)
                refresh();
            PathingOptions.Notify();
        });
        rows.AddChild(reset);

        _root.AddChild(_dropdown);
        screen.AddChild(_root);
    }

    /// <summary>Hidden with the map screen, like every other panel this mod adds.</summary>
    public void SetShellVisible(bool visible)
    {
        _root.Visible = visible;
        if (!visible)
        {
            _dropdown.Visible = false;
            _gear.Modulate = GearIdle;
        }
    }

    public void Dispose()
    {
        if (GodotObject.IsInstanceValid(_root))
            _root.QueueFree();
    }

    private static void AddSpacer(Container into) => into.AddChild(new Control
    {
        CustomMinimumSize = new Vector2(0, 8),
        MouseFilter = Control.MouseFilterEnum.Ignore,
    });

    private void AddToggle(Container into, string name, Func<bool> get, Action<bool> set)
    {
        var row = MakeLabel(20, Render(name, get()));
        row.MouseFilter = Control.MouseFilterEnum.Stop;
        _refreshers.Add(() => row.Text = Render(name, get()));
        row.GuiInput += inputEvent => Guard.Run("Toggling a setting", () =>
        {
            if (inputEvent is not InputEventMouseButton
                { ButtonIndex: MouseButton.Left, Pressed: false })
                return;
            set(!get());
            row.Text = Render(name, get());
            PathingOptions.Notify();
        });
        into.AddChild(row);
    }

    private void AddSlider(
        Container into, string name, float min, float max, float step,
        Func<float> get, Action<float> set)
    {
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 8);

        var label = MakeLabel(18, Render(name, get()));
        label.CustomMinimumSize = new Vector2(150f, 0f);
        label.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(label);

        var slider = new HSlider
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            Value = get(),
            CustomMinimumSize = new Vector2(130f, 22f),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        slider.ValueChanged += value => Guard.Run($"Adjusting {name}", () =>
        {
            set((float)value);
            label.Text = Render(name, get());
            PathingOptions.Notify();
        });
        // Writing Value fires ValueChanged, which would set the option right back —
        // harmless, since by then the option already holds the value being shown.
        _refreshers.Add(() =>
        {
            slider.Value = get();
            label.Text = Render(name, get());
        });
        row.AddChild(slider);
        into.AddChild(row);
    }

    private static string Render(string name, bool on) => $"[{(on ? "X" : "  ")}] {name}";

    private static string Render(string name, float value) =>
        $"{name}: {(Math.Abs(value) < 10f ? value.ToString("0.00") : value.ToString("0"))}";

    private MegaLabel MakeLabel(int fontSize, string text)
    {
        var label = new MegaLabel
        {
            AutoSizeEnabled = false,
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Left,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        if (_font is { })
            label.AddThemeFontOverride("font", _font);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", Parchment);
        label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.5f));
        label.AddThemeConstantOverride("outline_size", 6);
        return label;
    }
}
