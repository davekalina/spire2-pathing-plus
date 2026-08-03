using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.ControllerInput;

namespace PathingPlus.PathingPlusCode.Map;

/// <summary>
/// The routes panel, bottom-right above the native Share button, dressed like the
/// map's own DrawingTools tray (same nine-patch, same dark modulate). One focusable
/// row per surviving route; hovering or focusing a row raises <see cref="RouteHot" />
/// and shows the icon-sequence tooltip, select/click raises <see cref="RouteLockToggled" />.
///
/// Gamepad reach: the native map legend's last item hands focus down to the first row
/// here, so the existing Legend hotkey is the whole journey. The original neighbour is
/// restored whenever the rows disappear.
/// </summary>
internal sealed class PathLegendPanel : IDisposable
{
    public event Action<int>? RouteHot;
    public event Action<int>? RouteCold;
    public event Action<int>? RouteLockToggled;

    private static readonly Color Parchment = new(0.898f, 0.882f, 0.831f);
    private static readonly Color HintColor = new(0.898f, 0.882f, 0.831f, 0.7f);

    private readonly Control _screen;
    private readonly PanelContainer _panel;
    private readonly VBoxContainer _list;
    private readonly MegaLabel _header;
    private readonly MegaLabel _hint;
    private readonly PanelContainer _tooltip;
    private readonly MegaLabel _tooltipHeader;
    private readonly VBoxContainer _tooltipIcons;
    private readonly VBoxContainer _tooltipSummary;
    private readonly Font? _font;

    private readonly List<Control> _rows = [];
    private readonly List<ColorRect> _lockMarks = [];
    private readonly List<RouteDisplay> _rowData = [];
    private Control? _nativeNeighbor;
    private NodePath? _nativeNeighborOriginalBottom;

    public PathLegendPanel(Control screen)
    {
        _screen = screen;
        _font = screen.GetNodeOrNull<Label>("MapLegend/Header")?.GetThemeFont("font");

        _panel = new PanelContainer { Name = "PathingPlusRoutes" };
        _panel.AddThemeStyleboxOverride("panel", MakePanelStyle());
        _panel.AnchorLeft = _panel.AnchorRight = 1f;
        _panel.AnchorTop = _panel.AnchorBottom = 1f;
        _panel.OffsetRight = -48f;
        _panel.OffsetBottom = -128f; // clear of the Share button at -112
        _panel.GrowHorizontal = Control.GrowDirection.Begin;
        _panel.GrowVertical = Control.GrowDirection.Begin;

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 14);
        margin.AddThemeConstantOverride("margin_right", 14);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        _panel.AddChild(margin);

        _list = new VBoxContainer();
        _list.AddThemeConstantOverride("separation", 2);
        margin.AddChild(_list);

        _header = MakeLabel(22);
        _list.AddChild(_header);
        _hint = MakeLabel(15);
        _hint.AddThemeColorOverride("font_color", HintColor);
        _list.AddChild(_hint);

        _tooltip = new PanelContainer
        {
            Name = "PathingPlusRouteTooltip",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _tooltip.AddThemeStyleboxOverride("panel", MakePanelStyle());
        var tooltipMargin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        tooltipMargin.AddThemeConstantOverride("margin_left", 12);
        tooltipMargin.AddThemeConstantOverride("margin_right", 12);
        tooltipMargin.AddThemeConstantOverride("margin_top", 8);
        tooltipMargin.AddThemeConstantOverride("margin_bottom", 8);
        _tooltip.AddChild(tooltipMargin);

        // Header on top; below it the route runs vertically like the map itself
        // (boss end at the top), with the fixed-order summary beside it.
        var tooltipStack = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        tooltipStack.AddThemeConstantOverride("separation", 6);
        tooltipMargin.AddChild(tooltipStack);
        _tooltipHeader = MakeLabel(20);
        tooltipStack.AddChild(_tooltipHeader);
        var tooltipColumns = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        tooltipColumns.AddThemeConstantOverride("separation", 14);
        tooltipStack.AddChild(tooltipColumns);
        _tooltipIcons = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _tooltipIcons.AddThemeConstantOverride("separation", 2);
        tooltipColumns.AddChild(_tooltipIcons);
        _tooltipSummary = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _tooltipSummary.AddThemeConstantOverride("separation", 0);
        tooltipColumns.AddChild(_tooltipSummary);

        screen.AddChild(_panel);
        screen.AddChild(_tooltip);
    }

    public void SetContent(string headerText, string hintText, IReadOnlyList<RouteDisplay> routes)
    {
        _header.Text = headerText;
        _hint.Text = hintText;
        _hint.Visible = hintText.Length > 0;

        HideTooltip();
        foreach (var row in _rows)
            row.QueueFree();
        _rows.Clear();
        _lockMarks.Clear();
        _rowData.Clear();

        for (var i = 0; i < routes.Count; i++)
        {
            var row = BuildRow(i, routes[i].Color, routes[i].Label);
            _rows.Add(row);
            _rowData.Add(routes[i]);
            // Rows sit between the header and the hint.
            _list.AddChild(row);
            _list.MoveChild(row, 1 + i);
        }

        WireFocus();
    }

    public void SetLocked(int index)
    {
        for (var i = 0; i < _lockMarks.Count; i++)
            _lockMarks[i].Visible = i == index;
    }

    public void ShowTooltip(int index)
    {
        if (index < 0 || index >= _rows.Count)
            return;
        var route = _rowData[index];

        _tooltipHeader.Text = route.Label;
        _tooltipHeader.AddThemeColorOverride("font_color", route.Color);

        foreach (var child in _tooltipIcons.GetChildren())
            child.QueueFree();
        foreach (var texture in route.Icons)
        {
            _tooltipIcons.AddChild(new TextureRect
            {
                Texture = texture,
                CustomMinimumSize = new Vector2(30, 30),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        }

        foreach (var child in _tooltipSummary.GetChildren())
            child.QueueFree();
        foreach (var line in route.Summary)
            _tooltipSummary.AddChild(MakeLabel(16, line));

        var size = _tooltip.GetCombinedMinimumSize();
        _tooltip.Size = size;
        var rowRect = _rows[index].GetGlobalRect();
        var panelRect = _panel.GetGlobalRect();
        var position = new Vector2(
            panelRect.Position.X - size.X - 12f,
            rowRect.GetCenter().Y - size.Y * 0.5f);
        position.X = Mathf.Max(position.X, 8f);
        position.Y = Mathf.Clamp(position.Y, 8f, _screen.Size.Y - size.Y - 8f);
        _tooltip.GlobalPosition = position;
        _tooltip.Visible = true;
    }

    public void HideTooltip() => _tooltip.Visible = false;

    public void Dispose()
    {
        RestoreNativeNeighbor();
        if (GodotObject.IsInstanceValid(_panel))
            _panel.QueueFree();
        if (GodotObject.IsInstanceValid(_tooltip))
            _tooltip.QueueFree();
    }

    private Control BuildRow(int index, Color color, string text)
    {
        var row = new Control
        {
            Name = $"Route{index + 1}",
            CustomMinimumSize = new Vector2(280, 42),
            FocusMode = Control.FocusModeEnum.All,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };

        var lockMark = new ColorRect
        {
            Color = new Color(1f, 1f, 1f, 0.13f),
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        lockMark.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        row.AddChild(lockMark);
        _lockMarks.Add(lockMark);

        row.AddChild(new ColorRect
        {
            Color = color,
            Position = new Vector2(10, 18),
            Size = new Vector2(30, 6),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        var label = MakeLabel(20, text);
        label.Position = new Vector2(54, 0);
        label.Size = new Vector2(220, 42);
        label.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(label);

        row.MouseEntered += () => Guard.Run("Route row hover", () => RouteHot?.Invoke(index));
        row.MouseExited += () => Guard.Run("Route row unhover", () => RouteCold?.Invoke(index));
        row.FocusEntered += () => Guard.Run("Route row focus", () => RouteHot?.Invoke(index));
        row.FocusExited += () => Guard.Run("Route row unfocus", () => RouteCold?.Invoke(index));
        row.GuiInput += inputEvent => Guard.Run("Route row select", () =>
        {
            // Native controls act on select, not ui_accept; mouse acts on release.
            var selected = inputEvent.IsActionPressed(MegaInput.select) ||
                inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false };
            if (selected)
                RouteLockToggled?.Invoke(index);
        });

        return row;
    }

    /// <summary>
    /// Every focusable control names all four neighbours; an unset one falls back to a
    /// viewport-wide geometric search that can land focus anywhere.
    /// </summary>
    private void WireFocus()
    {
        RestoreNativeNeighbor();
        if (_rows.Count == 0)
            return;

        var nativeItems = _screen.GetNodeOrNull<Control>("MapLegend/LegendItems");
        _nativeNeighbor = nativeItems?.GetChildren().OfType<Control>()
            .LastOrDefault(c => c.FocusMode != Control.FocusModeEnum.None && c.Visible);
        if (_nativeNeighbor is { })
        {
            _nativeNeighborOriginalBottom = _nativeNeighbor.FocusNeighborBottom;
            _nativeNeighbor.FocusNeighborBottom = _nativeNeighbor.GetPathTo(_rows[0]);
        }

        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            var self = new NodePath(".");
            row.FocusNeighborLeft = self;
            row.FocusNeighborRight = self;
            row.FocusNeighborTop = i > 0
                ? row.GetPathTo(_rows[i - 1])
                : _nativeNeighbor is { } neighbor ? row.GetPathTo(neighbor) : self;
            row.FocusNeighborBottom = i < _rows.Count - 1 ? row.GetPathTo(_rows[i + 1]) : self;
        }
    }

    private void RestoreNativeNeighbor()
    {
        if (_nativeNeighbor is { } neighbor && GodotObject.IsInstanceValid(neighbor))
            neighbor.FocusNeighborBottom = _nativeNeighborOriginalBottom ?? new NodePath();
        _nativeNeighbor = null;
        _nativeNeighborOriginalBottom = null;
    }

    private MegaLabel MakeLabel(int fontSize, string text = "")
    {
        var label = new MegaLabel
        {
            AutoSizeEnabled = false,
            Text = text,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        if (_font is { })
            label.AddThemeFontOverride("font", _font);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", Parchment);
        label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.4f));
        return label;
    }

    /// <summary>The DrawingTools tray look: tiny_nine_patch darkened to a translucent slab.</summary>
    private static StyleBoxTexture MakePanelStyle() => new()
    {
        Texture = ResourceLoader.Load<Texture2D>(
            "res://images/ui/tiny_nine_patch.png", null, ResourceLoader.CacheMode.Reuse),
        TextureMarginLeft = 12,
        TextureMarginTop = 12,
        TextureMarginRight = 12,
        TextureMarginBottom = 12,
        ModulateColor = new Color(0f, 0f, 0f, 0.75f),
    };
}

/// <param name="Icons">Room icons in map order: the boss end first, the next step last.</param>
/// <param name="Summary">Category counts, always the same categories in the same order.</param>
internal sealed record RouteDisplay(
    Color Color,
    string Label,
    IReadOnlyList<Texture2D> Icons,
    IReadOnlyList<string> Summary);
