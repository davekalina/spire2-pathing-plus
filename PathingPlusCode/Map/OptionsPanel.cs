using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Helpers;

namespace PathingPlus.PathingPlusCode.Map;

/// <summary>
/// The mod's settings, folded behind the gear in the toolbar so they cost no screen
/// space until asked for. The panel drops directly beneath the toolbar wearing the
/// same Compendium card art — upright here, since this one is taller than it is wide
/// — and closes when anything outside it is clicked.
/// </summary>
internal sealed class OptionsPanel : IDisposable
{
    private const string GearTexturePath =
        "res://images/atlases/ui_atlas.sprites/top_bar/top_bar_settings.tres";
    private static readonly Color Parchment = new(0.898f, 0.882f, 0.831f);
    private static readonly Color GearIdle = new(1f, 1f, 1f, 0.6f);

    private const float PanelWidth = 372f;
    private const float PanelHeight = 500f;

    private readonly Control _root;
    private readonly Control _catcher;
    private readonly Control _panel;
    private readonly TextureRect _gear;
    private readonly Font? _font;

    /// <summary>Each row's way of re-reading its option, for the reset button.</summary>
    private readonly List<Action> _refreshers = [];

    public OptionsPanel(Control screen, Control toolbar)
    {
        _font = screen.GetNodeOrNull<Label>("MapLegend/Header")?.GetThemeFont("font");

        _root = new Control { Name = "PathingPlusOptions", MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        // Anything outside the panel dismisses it. Added before the panel so the
        // panel keeps its own clicks, and only alive while the panel is open.
        _catcher = new Control
        {
            Name = "Dismisser",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _catcher.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _catcher.GuiInput += inputEvent => Guard.Run("Dismissing the settings panel", () =>
        {
            if (inputEvent is InputEventMouseButton { Pressed: false })
                SetOpen(false);
        });
        _root.AddChild(_catcher);

        // Directly under the toolbar and sharing its right edge, so the panel reads as
        // hanging off the gear rather than floating over the map.
        _panel = new Control
        {
            Name = "OptionsDropdown",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop,
            AnchorLeft = 1f,
            AnchorRight = 1f,
            OffsetLeft = -(PanelWidth + 24f),
            OffsetRight = -24f,
            OffsetTop = 336f,
            OffsetBottom = 336f + PanelHeight,
            GrowHorizontal = Control.GrowDirection.Begin,
        };
        var parchment = new TextureRect
        {
            Name = "Panel",
            Texture = ResourceLoader.Load<Texture2D>(
                "res://images/packed/common_ui/submenu_panel_short.png",
                null, ResourceLoader.CacheMode.Reuse),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        parchment.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _panel.AddChild(parchment);

        _gear = new TextureRect
        {
            Name = "OptionsGear",
            Texture = ResourceLoader.Load<Texture2D>(
                GearTexturePath, null, ResourceLoader.CacheMode.Reuse),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Stop,
            Modulate = GearIdle,
            Position = new Vector2(
                MapToolbar.GearLeft,
                MapToolbar.ButtonRowTop + (MapToolbar.ButtonHeight - MapToolbar.GearSize) / 2f),
            Size = new Vector2(MapToolbar.GearSize, MapToolbar.GearSize),
        };
        _gear.MouseEntered += () => Guard.Run("Gear hover", () => _gear.Modulate = Colors.White);
        _gear.MouseExited += () => Guard.Run("Gear unhover", () =>
            _gear.Modulate = _panel.Visible ? Colors.White : GearIdle);
        _gear.GuiInput += inputEvent => Guard.Run("Toggling the settings panel", () =>
        {
            if (inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
                SetOpen(!_panel.Visible);
        });
        toolbar.AddChild(_gear);

        // The parchment's torn border eats the outer ~26 px, so the rows sit inside it.
        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", 32);
        margin.AddThemeConstantOverride("margin_right", 32);
        margin.AddThemeConstantOverride("margin_top", 28);
        margin.AddThemeConstantOverride("margin_bottom", 32);
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _panel.AddChild(margin);

        var rows = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        rows.AddThemeConstantOverride("separation", 4);
        margin.AddChild(rows);

        AddModeDropdown(rows);
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
        var reset = MakeLabel(17, "Reset to defaults");
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

        _root.AddChild(_panel);
        screen.AddChild(_root);
    }

    /// <summary>Hidden with the map screen, like every other panel this mod adds.</summary>
    public void SetShellVisible(bool visible)
    {
        _root.Visible = visible;
        if (!visible)
            SetOpen(false);
    }

    public void Dispose()
    {
        if (GodotObject.IsInstanceValid(_root))
            _root.QueueFree();
    }

    private void SetOpen(bool open)
    {
        _panel.Visible = open;
        _catcher.Visible = open;
        _gear.Modulate = open ? Colors.White : GearIdle;
        if (open)
            _root.MoveToFront();
    }

    private static void AddSpacer(Container into) => into.AddChild(new Control
    {
        CustomMinimumSize = new Vector2(0, 8),
        MouseFilter = Control.MouseFilterEnum.Ignore,
    });

    /// <summary>
    /// Path Mode as a proper pull-down: the current choice with the alternatives
    /// tucked underneath until asked for, rather than a row that has to be clicked
    /// blindly until the wanted mode comes round.
    /// </summary>
    private void AddModeDropdown(Container into)
    {
        var options = new VBoxContainer
        {
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        options.AddThemeConstantOverride("separation", 0);

        var header = MakeLabel(20, "");
        header.MouseFilter = Control.MouseFilterEnum.Stop;
        void RenderHeader() =>
            header.Text = $"Path Mode: {PathingOptions.Mode}  {(options.Visible ? "▲" : "▼")}";
        header.GuiInput += inputEvent => Guard.Run("Opening the path mode list", () =>
        {
            if (inputEvent is not InputEventMouseButton
                { ButtonIndex: MouseButton.Left, Pressed: false })
                return;
            options.Visible = !options.Visible;
            RenderHeader();
        });
        into.AddChild(header);

        foreach (var mode in Enum.GetValues<PathMode>())
        {
            var choice = mode;
            var row = MakeLabel(18, $"   {mode}");
            row.MouseFilter = Control.MouseFilterEnum.Stop;
            row.Modulate = new Color(1f, 1f, 1f, 0.8f);
            row.MouseEntered += () => Guard.Run("Path mode hover", () =>
                row.Modulate = Colors.White);
            row.MouseExited += () => Guard.Run("Path mode unhover", () =>
                row.Modulate = new Color(1f, 1f, 1f, 0.8f));
            row.GuiInput += inputEvent => Guard.Run("Choosing a path mode", () =>
            {
                if (inputEvent is not InputEventMouseButton
                    { ButtonIndex: MouseButton.Left, Pressed: false })
                    return;
                PathingOptions.Mode = choice;
                options.Visible = false;
                RenderHeader();
                PathingOptions.Notify();
            });
            options.AddChild(row);
        }
        into.AddChild(options);

        RenderHeader();
        _refreshers.Add(RenderHeader);
    }

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

        var label = MakeLabel(17, Render(name, get()));
        label.CustomMinimumSize = new Vector2(158f, 0f);
        label.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(label);

        var slider = new HSlider
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            Value = get(),
            CustomMinimumSize = new Vector2(126f, 22f),
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
        label.AddThemeColorOverride("font_outline_color", new Color(0.12f, 0.10f, 0.08f));
        label.AddThemeConstantOverride("outline_size", 8);
        return label;
    }
}
