using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

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

    /// <summary>Where the panel hangs from, and the parchment border the rows sit inside.</summary>
    private const float PanelTop = 336f;
    private const float PanelPadding = 60f;

    private readonly Control _root;
    private readonly Control _catcher;
    private readonly Control _panel;
    private readonly VBoxContainer _rows;
    private readonly TextureRect _gear;
    private readonly Font? _font;

    /// <summary>Each row's way of re-reading its option, for the reset button.</summary>
    private readonly List<Action> _refreshers = [];

    /// <summary>
    /// Every row a d-pad can land on, in the order they read down the panel. Chained
    /// on open and again whenever a section folds, since a hidden row must not be a
    /// stop on the way past.
    /// </summary>
    private readonly List<Control> _focusables = [];

    /// <summary>Where the d-pad lands on this control coming from elsewhere.</summary>
    public Control Focusable => _gear;

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
            OffsetTop = PanelTop,
            OffsetBottom = PanelTop + 240f,
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

        // Unhandled input from a focused row bubbles up to here, which is the only
        // way off this panel with a controller: without it an open panel is a trap.
        _panel.GuiInput += inputEvent => Guard.Run("Closing the settings panel", () =>
        {
            if (!inputEvent.IsActionPressed(MegaInput.cancel))
                return;
            SetOpen(false);
            _panel.AcceptEvent();
        });

        _gear = new TextureRect
        {
            Name = "OptionsGear",
            Texture = ResourceLoader.Load<Texture2D>(
                GearTexturePath, null, ResourceLoader.CacheMode.Reuse),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Stop,
            FocusMode = Control.FocusModeEnum.All,
            Modulate = GearIdle,
            Position = new Vector2(
                MapToolbar.GearLeft,
                MapToolbar.ButtonRowTop + (MapToolbar.ButtonHeight - MapToolbar.GearSize) / 2f),
            Size = new Vector2(MapToolbar.GearSize, MapToolbar.GearSize),
        };
        _gear.MouseEntered += () => Guard.Run("Gear hover", () => _gear.Modulate = Colors.White);
        _gear.MouseExited += () => Guard.Run("Gear unhover", () =>
            _gear.Modulate = _panel.Visible ? Colors.White : GearIdle);
        _gear.FocusEntered += () => Guard.Run("Gear focus", () => _gear.Modulate = Colors.White);
        _gear.FocusExited += () => Guard.Run("Gear unfocus", () =>
            _gear.Modulate = _panel.Visible ? Colors.White : GearIdle);
        _gear.GuiInput += inputEvent => Guard.Run("Toggling the settings panel", () =>
        {
            if (!inputEvent.IsActionPressed(MegaInput.select) &&
                inputEvent is not InputEventMouseButton
                    { ButtonIndex: MouseButton.Left, Pressed: false })
                return;
            SetOpen(!_panel.Visible);
            // Or the same press travels on to the map and moves the player a node.
            _gear.AcceptEvent();
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

        _rows = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _rows.AddThemeConstantOverride("separation", 4);
        margin.AddChild(_rows);

        AddToggle(_rows, "Override Drawing Controls",
            () => PathingOptions.OverrideDrawing, v => PathingOptions.OverrideDrawing = v);

        // Line-drawing and framing numbers, folded away: they were tuned once and are
        // no use during a run, but they crowded out the two settings that are.
        AddSpacer(_rows);
        var advanced = AddSection(_rows, "Advanced");
        AddSlider(advanced, "Thickness", 0.5f, 4f, 0.1f,
            () => PathingOptions.DashWidth, v => PathingOptions.DashWidth = v);
        AddSlider(advanced, "Dash Length", 0.5f, 4f, 0.1f,
            () => PathingOptions.DashLength, v => PathingOptions.DashLength = v);
        AddSlider(advanced, "Length Jitter", 0f, 2f, 0.1f,
            () => PathingOptions.DashLengthVariance, v => PathingOptions.DashLengthVariance = v);
        AddSlider(advanced, "Spacing", 6f, 30f, 1f,
            () => PathingOptions.DashSpacing, v => PathingOptions.DashSpacing = v);
        AddSlider(advanced, "Route Gap", 0f, 24f, 1f,
            () => PathingOptions.RouteSeparation, v => PathingOptions.RouteSeparation = v);

        AddSpacer(advanced);
        AddSlider(advanced, "Wide Fit", 0.5f, 1f, 0.01f,
            () => PathingOptions.LandscapeFit, v => PathingOptions.LandscapeFit = v);
        AddSlider(advanced, "Wide Zoom", 0.6f, 1.6f, 0.05f,
            () => PathingOptions.LandscapeZoom, v => PathingOptions.LandscapeZoom = v);
        AddSlider(advanced, "Wide Shift X", -400f, 400f, 10f,
            () => PathingOptions.LandscapeShiftX, v => PathingOptions.LandscapeShiftX = v);
        AddSlider(advanced, "Wide Shift Y", -300f, 300f, 10f,
            () => PathingOptions.LandscapeShiftY, v => PathingOptions.LandscapeShiftY = v);

        AddSpacer(_rows);
        var reset = MakeLabel(17, "Reset to defaults");
        reset.MouseFilter = Control.MouseFilterEnum.Stop;
        reset.FocusMode = Control.FocusModeEnum.All;
        reset.Modulate = new Color(1f, 1f, 1f, 0.75f);
        Emphasise(reset, new Color(1f, 1f, 1f, 0.75f));
        _focusables.Add(reset);
        reset.GuiInput += inputEvent => Guard.Run("Resetting settings", () =>
        {
            if (!inputEvent.IsActionPressed(MegaInput.select) &&
                inputEvent is not InputEventMouseButton
                    { ButtonIndex: MouseButton.Left, Pressed: false })
                return;
            reset.AcceptEvent();
            PathingOptions.ResetDefaults();
            foreach (var refresh in _refreshers)
                refresh();
            PathingOptions.Notify();
        });
        _rows.AddChild(reset);

        _root.AddChild(_panel);
        screen.AddChild(_root);
        ResizePanel();
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
        if (!open)
        {
            // Back where it came from, or a controller is left with focus on a panel
            // that is no longer on screen and nowhere to go from it.
            if (_gear.GetViewport()?.GuiGetFocusOwner() is { } focused && _panel.IsAncestorOf(focused))
                _gear.CallDeferred(Control.MethodName.GrabFocus);
            return;
        }
        _root.MoveToFront();
        WireFocus();
        _focusables.FirstOrDefault(row => row.IsVisibleInTree())
            ?.CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <summary>
    /// Chain the visible rows top to bottom, with the gear above the first. Only rows
    /// actually on screen take part: a folded section's sliders would otherwise be
    /// invisible stops the d-pad walks through one at a time.
    /// </summary>
    private void WireFocus()
    {
        var live = _focusables.Where(row => GodotObject.IsInstanceValid(row) && row.IsVisibleInTree()).ToList();
        for (var i = 0; i < live.Count; i++)
        {
            live[i].FocusNeighborTop = i > 0
                ? live[i].GetPathTo(live[i - 1])
                : live[i].GetPathTo(_gear);
            live[i].FocusNeighborBottom = i < live.Count - 1
                ? live[i].GetPathTo(live[i + 1])
                : new NodePath(".");
        }
    }

    /// <summary>Focus has to be visible, and hovering already lights these rows.</summary>
    private static void Emphasise(Control row, Color? idle = null)
    {
        var rest = idle ?? Colors.White;
        row.FocusEntered += () => Guard.Run("Settings row focus", () => row.Modulate = Colors.White);
        row.FocusExited += () => Guard.Run("Settings row unfocus", () => row.Modulate = rest);
        row.MouseEntered += () => Guard.Run("Settings row hover", () => row.Modulate = Colors.White);
        row.MouseExited += () => Guard.Run("Settings row unhover", () =>
            row.Modulate = row.HasFocus() ? Colors.White : rest);
    }

    /// <summary>
    /// The panel follows its contents, so folding a section away takes the parchment
    /// with it instead of leaving an empty card hanging under the gear.
    /// </summary>
    private void ResizePanel() =>
        _panel.OffsetBottom = PanelTop
            + Math.Max(200f, _rows.GetCombinedMinimumSize().Y + PanelPadding);

    private static void AddSpacer(Container into) => into.AddChild(new Control
    {
        CustomMinimumSize = new Vector2(0, 8),
        MouseFilter = Control.MouseFilterEnum.Ignore,
    });

    /// <summary>A row that reads as a checkbox and toggles on click or select.</summary>
    private void AddToggle(Container into, string name, Func<bool> get, Action<bool> set)
    {
        var row = MakeLabel(20, Render(name, get()));
        row.MouseFilter = Control.MouseFilterEnum.Stop;
        row.FocusMode = Control.FocusModeEnum.All;
        Emphasise(row);
        _focusables.Add(row);
        _refreshers.Add(() => row.Text = Render(name, get()));
        row.GuiInput += inputEvent => Guard.Run($"Toggling {name}", () =>
        {
            if (!inputEvent.IsActionPressed(MegaInput.select) &&
                inputEvent is not InputEventMouseButton
                    { ButtonIndex: MouseButton.Left, Pressed: false })
                return;
            set(!get());
            row.Text = Render(name, get());
            PathingOptions.Notify();
            row.AcceptEvent();
        });
        into.AddChild(row);
    }

    private static string Render(string name, bool on) => $"[{(on ? "X" : "  ")}] {name}";

    /// <summary>A collapsible heading; returns the box its rows go in.</summary>
    private VBoxContainer AddSection(Container into, string name)
    {
        var body = new VBoxContainer
        {
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        body.AddThemeConstantOverride("separation", 4);

        var header = MakeLabel(20, "");
        header.MouseFilter = Control.MouseFilterEnum.Stop;
        header.FocusMode = Control.FocusModeEnum.All;
        Emphasise(header);
        _focusables.Add(header);
        void RenderHeader() => header.Text = $"{name}  {(body.Visible ? "▲" : "▼")}";
        header.GuiInput += inputEvent => Guard.Run($"Folding {name}", () =>
        {
            if (!inputEvent.IsActionPressed(MegaInput.select) &&
                inputEvent is not InputEventMouseButton
                    { ButtonIndex: MouseButton.Left, Pressed: false })
                return;
            body.Visible = !body.Visible;
            RenderHeader();
            ResizePanel();
            WireFocus();
            header.AcceptEvent();
        });
        into.AddChild(header);
        into.AddChild(body);

        RenderHeader();
        return body;
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
            FocusMode = Control.FocusModeEnum.All,
        };
        _focusables.Add(slider);
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
