using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;

namespace PathingPlus.PathingPlusCode.Map;

/// <summary>
/// The replacement Legend: the native legend's parchment, drawn in its place but 75%
/// wider, transposed — node types as rows, one column per computed route (up to
/// eight), the route letter in its trail colour heading each column.
///
/// Hovering or focusing a type icon fires the game's own
/// <c>HighlightPointType</c> broadcast, exactly what the native legend items do.
/// Hovering, focusing, or selecting a route column highlights that route on the map;
/// select locks it. Controller: the native legend hotkey lands on the icon column,
/// up/down walks the types, right moves across the route columns, select locks.
/// </summary>
internal sealed class RouteLegendPanel : IDisposable
{
    /// <summary>Type rows, in the native legend's own order.</summary>
    public static readonly (MapPointType Type, string IconKind, string[] Kinds)[] Rows =
    [
        (MapPointType.Unknown, nameof(MapPointType.Unknown),
            [nameof(MapPointType.Unknown), nameof(MapPointType.Unassigned)]),
        (MapPointType.Shop, nameof(MapPointType.Shop), [nameof(MapPointType.Shop)]),
        (MapPointType.Treasure, nameof(MapPointType.Treasure), [nameof(MapPointType.Treasure)]),
        (MapPointType.RestSite, nameof(MapPointType.RestSite), [nameof(MapPointType.RestSite)]),
        (MapPointType.Monster, nameof(MapPointType.Monster), [nameof(MapPointType.Monster)]),
        (MapPointType.Elite, nameof(MapPointType.Elite), [nameof(MapPointType.Elite)]),
    ];

    private const float IconColumnX = 36f;
    private const float IconSize = 46f;
    private const float FirstRowY = 96f;
    private const float RowHeight = 52f;
    private const float ColumnsStartX = 104f;
    private const float ColumnWidth = 44f;
    private const float HeaderY = 40f;

    public event Action<MapPointType>? TypeHot;
    public event Action? TypeCold;
    public event Action<int>? ColumnHot;
    public event Action<int>? ColumnCold;
    public event Action<int>? ColumnLockToggled;

    private readonly Control _panel;
    private HotkeyGlyph? _hotkeyGlyph;
    private readonly ColorRect _rowMark;
    private readonly Font? _font;
    private readonly List<Control> _iconCells = [];
    private readonly List<MegaLabel> _typeNames = [];
    private readonly List<Control> _columns = [];
    private readonly List<Panel> _columnMarks = [];
    private readonly List<Color> _columnColors = [];
    private int _hot = -1;
    private int _locked = -1;

    public RouteLegendPanel(Control screen)
    {
        _font = screen.GetNodeOrNull<Label>("MapLegend/Header")?.GetThemeFont("font");

        // Bottom right, in the space the mod's old routes table held — out of the
        // rotated view's way — wearing the native legend parchment. Six route
        // columns fit this width.
        _panel = new Control { Name = "PathingPlusLegend", MouseFilter = Control.MouseFilterEnum.Stop };
        _panel.AnchorLeft = _panel.AnchorRight = 1f;
        _panel.AnchorTop = _panel.AnchorBottom = 1f;
        _panel.OffsetLeft = -421f;
        _panel.OffsetRight = -24f;
        _panel.OffsetTop = -582f;
        _panel.OffsetBottom = -128f;
        _panel.GrowHorizontal = Control.GrowDirection.Begin;
        _panel.GrowVertical = Control.GrowDirection.Begin;

        var background = new TextureRect
        {
            Name = "Parchment",
            Texture = ResourceLoader.Load<Texture2D>(
                "res://images/atlases/ui_atlas.sprites/map/map_legend.tres",
                null, ResourceLoader.CacheMode.Reuse),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _panel.AddChild(background);

        // The band behind whichever type row is hovered or focused — without it,
        // controller focus on an icon is invisible inside the panel itself.
        _rowMark = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.15f),
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _rowMark.AnchorLeft = 0f;
        _rowMark.AnchorRight = 1f;
        _rowMark.OffsetLeft = 22f;
        _rowMark.OffsetRight = -20f;
        _panel.AddChild(_rowMark);

        for (var r = 0; r < Rows.Length; r++)
        {
            var row = r;
            var cell = new Control
            {
                Name = $"Type{Rows[r].Type}",
                Position = new Vector2(IconColumnX, FirstRowY + r * RowHeight),
                Size = new Vector2(IconSize + 8, RowHeight),
                FocusMode = Control.FocusModeEnum.All,
                MouseFilter = Control.MouseFilterEnum.Stop,
            };
            cell.AddChild(new TextureRect
            {
                Texture = MapIcons.For(Rows[r].IconKind),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                Position = new Vector2(4, (RowHeight - IconSize) / 2),
                Size = new Vector2(IconSize, IconSize),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
            cell.MouseEntered += () => Guard.Run("Type hover", () => OnTypeHot(row));
            cell.MouseExited += () => Guard.Run("Type unhover", OnTypeCold);
            cell.FocusEntered += () => Guard.Run("Type focus", () => OnTypeHot(row));
            cell.FocusExited += () => Guard.Run("Type unfocus", OnTypeCold);
            _iconCells.Add(cell);
            _panel.AddChild(cell);

            // With no routes to tabulate this is a plain legend, so read like one.
            var name = MakeLabel(24, TypeNames[r], StsColors.legendText);
            name.HorizontalAlignment = HorizontalAlignment.Left;
            name.Position = new Vector2(ColumnsStartX, FirstRowY + r * RowHeight);
            name.Size = new Vector2(200, RowHeight);
            _typeNames.Add(name);
            _panel.AddChild(name);
        }

        // The hotkey that lands focus here, shown top-left on a pad — the native
        // legend carried the same glyph and this panel replaces it.
        Guard.Run("Legend hotkey glyph", () =>
        {
            _hotkeyGlyph = new HotkeyGlyph(_panel, MegaInput.confirm, new Vector2(44, 44));
            var icon = _hotkeyGlyph.Node;
            icon.Position = new Vector2(IconColumnX - 4f, 34f);
            icon.Size = new Vector2(44, 44);
        });

        screen.AddChild(_panel);
        FitWidth(0);
        WireIconFocus();
    }

    /// <summary>Row names for the no-routes state, matching the native legend's wording.</summary>
    private static readonly string[] TypeNames =
        ["Unknown", "Merchant", "Treasure", "Rest Site", "Enemy", "Elite"];

    private void OnTypeHot(int row)
    {
        _rowMark.AnchorTop = _rowMark.AnchorBottom = 0f;
        _rowMark.OffsetTop = FirstRowY + row * RowHeight;
        _rowMark.OffsetBottom = FirstRowY + (row + 1) * RowHeight;
        _rowMark.Visible = true;
        TypeHot?.Invoke(Rows[row].Type);
    }

    private void OnTypeCold()
    {
        _rowMark.Visible = false;
        TypeCold?.Invoke();
    }

    /// <summary>Right edge stays put; the left edge comes in to hug the content.</summary>
    private void FitWidth(int columnCount)
    {
        var width = columnCount > 0
            ? ColumnsStartX + columnCount * ColumnWidth + 22f
            : ColumnsStartX + 210f;
        _panel.OffsetLeft = _panel.OffsetRight - width;
    }

    /// <summary>One column per route: its colour, its letter, its counts in row order.</summary>
    public void SetRoutes(IReadOnlyList<(Color Color, string Letter, IReadOnlyList<int> Counts)> routes)
    {
        foreach (var column in _columns)
        {
            _panel.RemoveChild(column);
            column.QueueFree();
        }
        _columns.Clear();
        _columnMarks.Clear();
        _columnColors.Clear();
        _hot = -1;

        FitWidth(routes.Count);
        foreach (var name in _typeNames)
            name.Visible = routes.Count == 0;

        for (var i = 0; i < routes.Count; i++)
        {
            var index = i;
            var column = new Control
            {
                Name = $"Route{routes[i].Letter}",
                Position = new Vector2(ColumnsStartX + i * ColumnWidth, HeaderY),
                Size = new Vector2(ColumnWidth, FirstRowY - HeaderY + Rows.Length * RowHeight),
                FocusMode = Control.FocusModeEnum.All,
                MouseFilter = Control.MouseFilterEnum.Stop,
            };

            var mark = new Panel
            {
                Visible = false,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            mark.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            column.AddChild(mark);
            _columnMarks.Add(mark);
            _columnColors.Add(routes[i].Color);

            var letter = MakeLabel(28, routes[i].Letter, routes[i].Color);
            letter.Position = new Vector2(0, 0);
            letter.Size = new Vector2(ColumnWidth, FirstRowY - HeaderY);
            column.AddChild(letter);

            for (var r = 0; r < Rows.Length && r < routes[i].Counts.Count; r++)
            {
                var count = MakeLabel(24, routes[i].Counts[r].ToString(), StsColors.legendText);
                count.Position = new Vector2(0, FirstRowY - HeaderY + r * RowHeight);
                count.Size = new Vector2(ColumnWidth, RowHeight);
                if (routes[i].Counts[r] == 0)
                    count.Modulate = new Color(1f, 1f, 1f, 0.4f);
                column.AddChild(count);
            }

            column.MouseEntered += () => Guard.Run("Column hover", () => ColumnHot?.Invoke(index));
            column.MouseExited += () => Guard.Run("Column unhover", () => ColumnCold?.Invoke(index));
            column.FocusEntered += () => Guard.Run("Column focus", () => ColumnHot?.Invoke(index));
            column.FocusExited += () => Guard.Run("Column unfocus", () => ColumnCold?.Invoke(index));
            column.GuiInput += inputEvent => Guard.Run("Column select", () =>
            {
                var selected = inputEvent.IsActionPressed(MegaInput.select) ||
                    inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false };
                if (selected)
                    ColumnLockToggled?.Invoke(index);
            });

            _columns.Add(column);
            _panel.AddChild(column);
        }

        WireIconFocus();
    }

    public void SetLocked(int index)
    {
        _locked = index;
        RefreshMarks();
    }

    /// <summary>The column under the mouse or holding focus, tinted so it reads as hot.</summary>
    public void SetHot(int index)
    {
        _hot = index;
        RefreshMarks();
    }

    private void RefreshMarks()
    {
        for (var i = 0; i < _columnMarks.Count; i++)
        {
            var locked = i == _locked;
            var hot = i == _hot;
            _columnMarks[i].Visible = locked || hot;
            if (!locked && !hot)
                continue;

            var style = new StyleBoxFlat
            {
                CornerRadiusTopLeft = 8,
                CornerRadiusTopRight = 8,
                CornerRadiusBottomLeft = 8,
                CornerRadiusBottomRight = 8,
            };
            if (locked)
            {
                // The route's own colour, but deepened and framed. Half the palette is
                // pale, and a pale wash over pale parchment reads as nothing — it was
                // the border that made a locked column unmistakable, not the fill.
                style.BgColor = Deepen(_columnColors[i], 0.55f) with { A = 0.42f };
                style.BorderColor = Deepen(_columnColors[i], 0.7f);
                style.SetBorderWidthAll(4);
            }
            else
            {
                // Merely hovered: a shadow, deliberately weaker than a lock.
                style.BgColor = new Color(0f, 0f, 0f, 0.15f);
            }
            _columnMarks[i].AddThemeStyleboxOverride("panel", style);
        }
    }

    /// <summary>Toward black, keeping the hue, so light route colours still bite.</summary>
    private static Color Deepen(Color color, float amount) =>
        new(color.R * amount, color.G * amount, color.B * amount, color.A);

    /// <summary>
    /// Whether the pointer is over the panel. Map-side route hover has to stand down
    /// while it is, or it would clear the very column the legend just lit.
    /// </summary>
    public bool Covers(Vector2 globalPoint) =>
        _panel.Visible && _panel.GetGlobalRect().HasPoint(globalPoint);

    /// <summary>Where the legend hotkey lands; null when the panel is hidden.</summary>
    public Control? FirstFocus => _panel.Visible ? _iconCells.FirstOrDefault() : null;

    public bool OwnsFocus(Control? focused) =>
        focused is { } control && _panel.IsAncestorOf(control);

    public void SetShellVisible(bool visible) => _panel.Visible = visible;

    public void Dispose()
    {
        _hotkeyGlyph?.Dispose();
        if (GodotObject.IsInstanceValid(_panel))
            _panel.QueueFree();
    }

    /// <summary>
    /// Icon cells chain vertically; right from any icon enters the route columns,
    /// which chain horizontally. Every edge is parked — an unset neighbour falls back
    /// to a viewport-wide search.
    /// </summary>
    private void WireIconFocus()
    {
        var self = new NodePath(".");
        for (var r = 0; r < _iconCells.Count; r++)
        {
            var cell = _iconCells[r];
            cell.FocusNeighborLeft = self;
            cell.FocusNeighborTop = r > 0 ? cell.GetPathTo(_iconCells[r - 1]) : self;
            cell.FocusNeighborBottom = r < _iconCells.Count - 1 ? cell.GetPathTo(_iconCells[r + 1]) : self;
            cell.FocusNeighborRight = _columns.Count > 0 ? cell.GetPathTo(_columns[0]) : self;
        }
        for (var i = 0; i < _columns.Count; i++)
        {
            var column = _columns[i];
            column.FocusNeighborTop = self;
            column.FocusNeighborBottom = self;
            column.FocusNeighborLeft = i > 0
                ? column.GetPathTo(_columns[i - 1])
                : _iconCells.Count > 0 ? column.GetPathTo(_iconCells[0]) : self;
            column.FocusNeighborRight = i < _columns.Count - 1 ? column.GetPathTo(_columns[i + 1]) : self;
        }
    }

    private MegaLabel MakeLabel(int fontSize, string text, Color color)
    {
        var label = new MegaLabel
        {
            AutoSizeEnabled = false,
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        if (_font is { })
            label.AddThemeFontOverride("font", _font);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.15f));
        return label;
    }
}
