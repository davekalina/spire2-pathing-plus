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

    /// <summary>Row geometry: swatch and name, then one count column per category.</summary>
    private const float ColumnsStartX = 148f;
    private const float ColumnWidth = 38f;
    private const float RowHeight = 42f;

    private readonly Control _screen;
    private readonly PanelContainer _panel;
    private readonly VBoxContainer _list;
    private readonly MegaLabel _header;
    private readonly MegaLabel _hint;
    private readonly PanelContainer _routeTooltip;
    private readonly VBoxContainer _routeTooltipIcons;
    private readonly Font? _font;

    private readonly List<Control> _rows = [];
    private readonly List<ColorRect> _lockMarks = [];
    private readonly List<RouteDisplay> _rowData = [];
    private Control? _columnHeader;
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

        // One tooltip: the route as a vertical icon column, boss end at the top,
        // matching the map. The category counts live in the panel's own table.
        (_routeTooltip, var routeContent) = MakeTooltipShell("PathingPlusRouteTooltip");
        _routeTooltipIcons = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _routeTooltipIcons.AddThemeConstantOverride("separation", 2);
        routeContent.AddChild(_routeTooltipIcons);

        screen.AddChild(_panel);
        screen.AddChild(_routeTooltip);
    }

    private (PanelContainer Panel, MarginContainer Content) MakeTooltipShell(string name)
    {
        var panel = new PanelContainer
        {
            Name = name,
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        panel.AddThemeStyleboxOverride("panel", MakePanelStyle());
        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        panel.AddChild(margin);
        return (panel, margin);
    }

    public void SetContent(
        string headerText,
        string hintText,
        IReadOnlyList<Texture2D?> columnIcons,
        IReadOnlyList<RouteDisplay> routes)
    {
        _header.Text = headerText;
        _header.Visible = headerText.Length > 0;
        _hint.Text = hintText;
        _hint.Visible = hintText.Length > 0;

        HideTooltip();
        foreach (var row in _rows)
        {
            _list.RemoveChild(row);
            row.QueueFree();
        }
        _rows.Clear();
        _lockMarks.Clear();
        _rowData.Clear();
        if (_columnHeader is { })
        {
            _list.RemoveChild(_columnHeader);
            _columnHeader.QueueFree();
            _columnHeader = null;
        }

        if (routes.Count > 0)
        {
            _columnHeader = BuildColumnHeader(columnIcons);
            _list.AddChild(_columnHeader);
            _list.MoveChild(_columnHeader, 1);
        }

        for (var i = 0; i < routes.Count; i++)
        {
            var row = BuildRow(i, routes[i].Color, routes[i].Label, routes[i].Counts);
            _rows.Add(row);
            _rowData.Add(routes[i]);
            // Rows sit between the column header and the hint.
            _list.AddChild(row);
            _list.MoveChild(row, 2 + i);
        }

        WireFocus();
    }

    /// <summary>Category icons above their count columns; the name column stays blank.</summary>
    private Control BuildColumnHeader(IReadOnlyList<Texture2D?> icons)
    {
        var header = new Control
        {
            Name = "RouteColumns",
            CustomMinimumSize = new Vector2(ColumnsStartX + ColumnWidth * icons.Count, 34),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        for (var c = 0; c < icons.Count; c++)
        {
            // ExpandMode must be set before Size: a control cannot be smaller than
            // its minimum, and until IgnoreSize is applied the minimum is the
            // texture's own ~90 px — Size assigned first silently clamps to that.
            header.AddChild(new TextureRect
            {
                Texture = icons[c],
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                Position = new Vector2(ColumnsStartX + c * ColumnWidth + (ColumnWidth - 26) / 2, 4),
                Size = new Vector2(26, 26),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        }
        return header;
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

        Empty(_routeTooltipIcons);
        foreach (var texture in route.Icons)
        {
            _routeTooltipIcons.AddChild(new TextureRect
            {
                Texture = texture,
                CustomMinimumSize = new Vector2(60, 60),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        }

        var rowRect = _rows[index].GetGlobalRect();
        var panelRect = _panel.GetGlobalRect();
        var iconsSize = _routeTooltip.GetCombinedMinimumSize();
        _routeTooltip.Size = iconsSize;
        _routeTooltip.GlobalPosition = Clamp(new Vector2(
            panelRect.Position.X - iconsSize.X - 12f,
            rowRect.GetCenter().Y - iconsSize.Y * 0.5f), iconsSize);
        _routeTooltip.Visible = true;
    }

    public void HideTooltip() => _routeTooltip.Visible = false;

    /// <summary>
    /// Remove before freeing: QueueFree alone leaves the child in the tree until end
    /// of frame, and a rebuild in the same frame would double the panel's minimum
    /// size — the giant empty tooltip bug.
    /// </summary>
    private static void Empty(Node container)
    {
        foreach (var child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }

    private Vector2 Clamp(Vector2 position, Vector2 size) => new(
        Mathf.Max(position.X, 8f),
        Mathf.Clamp(position.Y, 8f, _screen.Size.Y - size.Y - 8f));

    public void Dispose()
    {
        RestoreNativeNeighbor();
        if (GodotObject.IsInstanceValid(_panel))
            _panel.QueueFree();
        if (GodotObject.IsInstanceValid(_routeTooltip))
            _routeTooltip.QueueFree();
    }

    private Control BuildRow(int index, Color color, string text, IReadOnlyList<int> counts)
    {
        var row = new Control
        {
            Name = $"Route{index + 1}",
            CustomMinimumSize = new Vector2(ColumnsStartX + ColumnWidth * counts.Count, RowHeight),
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
        label.Size = new Vector2(ColumnsStartX - 58, RowHeight);
        label.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(label);

        for (var c = 0; c < counts.Count; c++)
        {
            var count = MakeLabel(19, counts[c].ToString());
            count.Position = new Vector2(ColumnsStartX + c * ColumnWidth, 0);
            count.Size = new Vector2(ColumnWidth, RowHeight);
            count.HorizontalAlignment = HorizontalAlignment.Center;
            count.VerticalAlignment = VerticalAlignment.Center;
            // Zeros stay for alignment but recede, so the counts that matter pop.
            if (counts[c] == 0)
                count.Modulate = new Color(1f, 1f, 1f, 0.35f);
            row.AddChild(count);
        }

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
/// <param name="Counts">Per-category counts matching the panel's column icons.</param>
internal sealed record RouteDisplay(
    Color Color,
    string Label,
    IReadOnlyList<Texture2D> Icons,
    IReadOnlyList<int> Counts);
