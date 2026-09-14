using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace PathingPlus.PathingPlusCode.Map;

/// <summary>
/// The replacement Legend: the native legend's parchment, drawn in its place but 75%
/// wider, transposed — node types as rows, one column per computed route (up to
/// five), each headed by a dash of its own line colour, or by a pin once that route
/// is pinned. Looking away folds the table down to its pinned columns with a smooth
/// resize. The empty parchment can be dragged to move the panel.
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

    private const float IconColumnX = 30f;
    private const float IconSize = 42f;
    private const float FirstRowY = 60f;
    private const float RowHeight = 46f;
    private const float ColumnsStartX = 88f;
    private const float ColumnWidth = 42f;

    /// <summary>
    /// The band above the rows, holding each column's key. Short, because what it holds
    /// is a mark rather than lettering — the letters that used to head these columns
    /// needed a 28pt line and told the player nothing the colour did not. Not as short
    /// as it looks it could be, though: like the bottom, the legend art's torn top edge
    /// comes in from the control's own bounds, and trimming this to what the *rect*
    /// allowed put the head of each column above the parchment.
    /// </summary>
    private const float HeaderY = 20f;

    /// <summary>
    /// Parchment left below the last row. Generous, and deliberately more than the inset
    /// at the top: the legend art's torn lower edge eats into its own rectangle, so a
    /// margin measured off the control's bounds ran the last row off the bottom of the
    /// parchment it was supposed to be sitting on.
    /// </summary>
    private const float BottomPad = 36f;

    /// <summary>Point size of the type names, and the width to assume if the font is gone.</summary>
    private const int NameFontSize = 24;
    private const float FallbackNamesWidth = 200f;

    /// <summary>
    /// The lettering carries an 8px outline, which is drawn outside the box the font
    /// measures. Without this the longest name's edge sits flush against the parchment.
    /// </summary>
    private const float NameOutlinePad = 10f;

    /// <summary>Margin past the last column, matching the inset on the left.</summary>
    private const float EdgePad = 18f;

    /// <summary>The key mark heading a column, sized to sit inside the header band.</summary>
    private const float KeySize = 26f;

    /// <summary>
    /// The map's own dash, which is what the routes themselves are drawn from. A column
    /// is headed by a sample of its line — and by <see cref="PinIcon" /> once that route
    /// is locked.
    /// </summary>
    private const string DashTexture = "res://images/atlases/compressed.sprites/map/map_dot.tres";

    public event Action<MapPointType>? TypeHot;
    public event Action? TypeCold;
    public event Action<int>? ColumnHot;
    public event Action<int>? ColumnCold;
    public event Action<int>? ColumnLockToggled;
    public event Action? ClearUnpinned;
    public event Action<int>? PageChanged;

    private readonly Control _panel;
    private readonly Control _screen;
    private readonly Action _screenResized;
    private bool _dragging;
    private Vector2 _dragGrabOffset;
    private float _middleRightTop = 336f;
    private const float ScreenMargin = 24f;
    private const double ResizeSeconds = 0.18;
    private Tween? _resizeTween;
    private Vector2 _targetSize;
    private bool _layoutReady;
    private HotkeyGlyph? _hotkeyGlyph;
    private readonly ColorRect _rowMark;
    private readonly Font? _font;
    private readonly LegendActionButton _clearUnpinned;
    private readonly LegendActionButton _previousPage;
    private readonly LegendActionButton _nextPage;
    private readonly MegaLabel _pageLabel;
    private int _page;
    private int _pageCount = 1;
    private int _selected = -1;
    private const float ActionHeight = 34f;
    private const float ActionGap = 8f;
    private const float ActionInset = 24f;
    private readonly List<Control> _iconCells = [];
    private readonly List<MegaLabel> _typeNames = [];
    private readonly List<Control> _columns = [];
    private readonly List<Panel> _columnMarks = [];
    private readonly List<Color> _columnColors = [];

    /// <summary>
    /// Which route each drawn column belongs to, or -1 for the preview. Once the table
    /// can fold to a single column, a column's place in the row stops being its route's
    /// index, and every hover, lock and mark has to go through this instead.
    /// </summary>
    private readonly List<int> _columnRoutes = [];

    /// <summary>Each column's key mark, swapped between the route dash and the pin.</summary>
    private readonly List<TextureRect> _columnKeys = [];

    /// <summary>
    /// The generated pin, kept for this panel's lifetime rather than rebuilt per mark —
    /// the marks are refreshed on every hover. Re-made if it is ever found invalid.
    /// </summary>
    private Texture2D? _pin;

    private int _hot = -1;
    private IReadOnlySet<int> _pinned = new HashSet<int>();
    private int _pinnedCount;

    /// <summary>
    /// Pinned, and folded down to the pinned columns — or waiting to be. The fold is held
    /// back until the player looks away, because collapsing the table under a pointer
    /// that is still choosing from it moves the very thing being read.
    /// </summary>
    private bool _foldPending;
    private bool _folded;

    private bool _expandPending;

    private Texture2D? PinMark()
    {
        if (_pin is null || !GodotObject.IsInstanceValid(_pin))
            _pin = PinIcon.Build();
        return _pin;
    }

    /// <summary>The current page (including the pin), and the hover-only preview.</summary>
    private IReadOnlyList<(Color Color, string Key, IReadOnlyList<int> Counts)> _routes = [];
    private (Color Color, IReadOnlyList<int> Counts)? _preview;

    public RouteLegendPanel(Control screen)
    {
        _screen = screen;
        _screenResized = () => Guard.Run("Keeping the legend on screen", ApplyPlacement);
        _font = screen.GetNodeOrNull<Label>("MapLegend/Header")?.GetThemeFont("font");

        // Bottom right, in the space the mod's old routes table held — out of the
        // rotated view's way — wearing the native legend parchment.
        _panel = new Control
        {
            Name = "PathingPlusLegend",
            MouseFilter = Control.MouseFilterEnum.Stop,
            // New columns are revealed as the parchment expands to contain them.
            ClipContents = true,
        };
        // Interactive children stop their own mouse events. Only empty background
        // reaches this handler; Godot keeps sending the held gesture to this control
        // even when the pointer crosses a child or leaves the panel.
        _panel.GuiInput += inputEvent => Guard.Run("Dragging the legend", () => DragBackground(inputEvent));

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
            cell.FocusEntered += () => Guard.Run("Type focus", () =>
            {
                ShowAlternativesForController();
                OnTypeHot(row);
            });
            cell.FocusExited += () => Guard.Run("Type unfocus", () =>
            {
                OnTypeCold();
                NoteFocusChanged();
            });
            _iconCells.Add(cell);
            _panel.AddChild(cell);

            // With no routes to tabulate this is a plain legend, so read like one.
            var name = MakeLabel(NameFontSize, TypeNames[r], StsColors.legendText);
            name.HorizontalAlignment = HorizontalAlignment.Left;
            name.Position = new Vector2(ColumnsStartX, FirstRowY + r * RowHeight);
            name.Size = new Vector2(NamesWidth(), RowHeight);
            _typeNames.Add(name);
            _panel.AddChild(name);
        }

        // The hotkey that lands focus here, shown top-left on a pad — the native
        // legend carried the same glyph and this panel replaces it.
        Guard.Run("Legend hotkey glyph", () =>
        {
            _hotkeyGlyph = new HotkeyGlyph(_panel, MegaInput.confirm, new Vector2(34, 34));
            var icon = _hotkeyGlyph.Node;
            icon.Position = new Vector2(IconColumnX + 4f, HeaderY + 1f);
            icon.Size = new Vector2(34, 34);
        });

        screen.AddChild(_panel);
        using var labelStream = typeof(RouteLegendPanel).Assembly.GetManifestResourceStream("clear-unpinned.txt")
            ?? throw new InvalidOperationException("Missing clear-unpinned.txt resource.");
        using var labelReader = new System.IO.StreamReader(labelStream);
        _clearUnpinned = new LegendActionButton("ClearUnpinned", labelReader.ReadToEnd().Trim(), _font,
            () => ClearUnpinned?.Invoke());
        _previousPage = new LegendActionButton("PreviousRoutes", "‹", _font, () => PageChanged?.Invoke(-1));
        _nextPage = new LegendActionButton("NextRoutes", "›", _font, () => PageChanged?.Invoke(1));
        _pageLabel = MakeLabel(18, "", StsColors.legendText);
        foreach (var action in new[] { _clearUnpinned, _previousPage, _nextPage })
        {
            _panel.AddChild(action.Control);
            action.Control.FocusExited += NoteFocusChanged;
            action.Control.FocusEntered += ShowAlternativesForController;
        }
        _panel.AddChild(_pageLabel);
        FitPanel(0);
        WireIconFocus();
        _screen.Resized += _screenResized;
    }

    private Vector2 PresetPosition => new(
        _screen.Size.X - ScreenMargin - _panel.Size.X,
        PathingOptions.LegendMiddleRight
            ? _middleRightTop
            : _screen.Size.Y - 112f - _panel.Size.Y);

    private Vector2 ClampPosition(Vector2 position) => new(
        Mathf.Clamp(position.X, ScreenMargin, Math.Max(ScreenMargin, _screen.Size.X - _panel.Size.X - ScreenMargin)),
        Mathf.Clamp(position.Y, ScreenMargin, Math.Max(ScreenMargin, _screen.Size.Y - _panel.Size.Y - ScreenMargin)));

    public void ApplyPlacement()
    {
        if (_dragging)
            return;
        _panel.Position = ClampPosition(PresetPosition +
            new Vector2(PathingOptions.LegendOffsetX, PathingOptions.LegendOffsetY));
    }

    public void SetMiddleRightTop(float top)
    {
        _middleRightTop = top;
        ApplyPlacement();
    }

    private void DragBackground(InputEvent inputEvent)
    {
        if (!_panel.IsVisibleInTree())
            return;
        if (inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            if (button.Pressed)
            {
                StopResize();
                _dragGrabOffset = PointerInScreen(button.Position) - _panel.Position;
                _dragging = true;
            }
            else if (_dragging)
            {
                EndDrag();
            }
            _panel.AcceptEvent();
        }
        else if (_dragging && inputEvent is InputEventMouseMotion motion)
        {
            // A release outside the window can be missed. Do not keep moving the
            // legend when the pointer returns with no button held.
            if ((motion.ButtonMask & MouseButtonMask.Left) == 0)
            {
                EndDrag();
            }
            else
                _panel.Position = ClampPosition(PointerInScreen(motion.Position) - _dragGrabOffset);
            _panel.AcceptEvent();
        }
    }

    private void EndDrag()
    {
        _dragging = false;
        PathingOptions.SaveLegendOffset(_panel.Position - PresetPosition);
        ResizeToContents();
    }

    private Vector2 PointerInScreen(Vector2 localPoint) =>
        _screen.GetGlobalTransform().AffineInverse() * (_panel.GetGlobalTransform() * localPoint);

    /// <summary>Row names for the no-routes state, matching the native legend's wording.</summary>
    private static readonly string[] TypeNames =
        ["Unknown", "Merchant", "Treasure", "Rest Site", "Enemy", "Elite"];

    /// <summary>
    /// How much room the type names actually need, measured from the font rather than
    /// reserved by a constant.
    ///
    /// With no routes yet the legend is nothing but those six words, so that constant
    /// *was* the panel's width — and being generous enough for the longest of them in
    /// any font left a third of the parchment blank at the very moment the panel has
    /// least to say. Measured once and kept: the names never change, and this is read
    /// on every render.
    /// </summary>
    private float _namesWidth;

    private float NamesWidth()
    {
        if (_namesWidth > 0f)
            return _namesWidth;
        if (_font is { } font)
            foreach (var name in TypeNames)
                _namesWidth = Mathf.Max(_namesWidth,
                    font.GetStringSize(name, HorizontalAlignment.Left, -1f, NameFontSize).X);
        _namesWidth = _namesWidth > 0f ? _namesWidth + NameOutlinePad : FallbackNamesWidth;
        return _namesWidth;
    }

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

    /// <summary>
    /// Fit the visible columns and footer controls. A changed target retimes the
    /// resize from its current dimensions; repeated redraws leave it running.
    /// </summary>
    private void FitPanel(int columnCount)
    {
        var clearing = _pinnedCount > 0;
        var paging = _routes.Count > 0 && _pageCount > 1;
        var width = columnCount > 0
            ? ColumnsStartX + columnCount * ColumnWidth + EdgePad
            : ColumnsStartX + NamesWidth() + EdgePad;
        if (clearing || paging)
            width = Math.Max(width, 208f);
        var footerHeight = clearing ? ActionHeight + ActionGap : 0f;
        if (paging)
            footerHeight += ActionHeight + ActionGap;
        var height = FirstRowY + Rows.Length * RowHeight + BottomPad + footerHeight;
        SetActionVisible(_clearUnpinned.Control, clearing);
        SetActionVisible(_previousPage.Control, paging);
        SetActionVisible(_nextPage.Control, paging);
        _pageLabel.Visible = paging;
        _clearUnpinned.SetAvailable(clearing);
        _previousPage.SetAvailable(_page > 0);
        _nextPage.SetAvailable(_page + 1 < _pageCount);
        _pageLabel.Text = $"{_page + 1} / {_pageCount}";

        var target = new Vector2(width, height);
        var changed = !_targetSize.IsEqualApprox(target);
        _targetSize = target;
        if (!_layoutReady || !_panel.IsVisibleInTree())
        {
            _layoutReady = true;
            StopResize();
            LayoutPanel(target);
        }
        else
        {
            LayoutPanel(_panel.Size);
            if (changed)
                ResizeToContents();
        }
    }

    private void StopResize()
    {
        _resizeTween?.Kill();
        _resizeTween = null;
    }

    private void ResizeToContents()
    {
        StopResize();
        if (_dragging)
            return;
        if (_panel.Size.IsEqualApprox(_targetSize) || !_panel.IsVisibleInTree())
        {
            LayoutPanel(_targetSize);
            return;
        }
        _resizeTween = _panel.CreateTween();
        _resizeTween.TweenMethod(Callable.From<Vector2>(size =>
            Guard.Run("Resizing the legend", () => LayoutPanel(size))), _panel.Size, _targetSize, ResizeSeconds)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
    }

    /// <summary>Move the panel edges and footer widths together on every tween step.</summary>
    private void LayoutPanel(Vector2 size)
    {
        _panel.Size = size;
        ApplyPlacement();
        var width = size.X;
        var clearing = _clearUnpinned.Control.Visible;
        var actionY = FirstRowY + Rows.Length * RowHeight + ActionGap;
        _clearUnpinned.Control.Position = new Vector2(ActionInset, actionY);
        _clearUnpinned.Control.Size = new Vector2(width - ActionInset * 2f, ActionHeight);
        var pageY = actionY + (clearing ? ActionHeight + ActionGap : 0f);
        _previousPage.Control.Position = new Vector2(ActionInset, pageY);
        _previousPage.Control.Size = new Vector2(ActionHeight, ActionHeight);
        _nextPage.Control.Position = new Vector2(width - ActionInset - ActionHeight, pageY);
        _nextPage.Control.Size = new Vector2(ActionHeight, ActionHeight);
        _pageLabel.Position = new Vector2(ActionInset + ActionHeight, pageY);
        _pageLabel.Size = new Vector2(width - 2f * (ActionInset + ActionHeight), ActionHeight);
    }

    private void SetActionVisible(Control action, bool visible)
    {
        if (!visible && action.HasFocus())
            _iconCells[0].GrabFocus();
        action.Visible = visible;
    }

    /// <summary>One column per route: its colour, stable identity, and counts in row order.</summary>
    public void SetRoutes(IReadOnlyList<(Color Color, string Key, IReadOnlyList<int> Counts)> routes,
        IReadOnlySet<int>? pinned = null, int selected = -1, int page = 0, int pageCount = 1,
        int pinnedCount = 0)
    {
        var focusedKey = FocusedRouteKey();
        _routes = routes;
        _selected = selected;
        _page = page;
        _pageCount = pageCount;
        UpdatePins(pinned ?? new HashSet<int>(), pinnedCount);
        _preview = null;
        _hot = -1;
        Render(focusedKey);
    }

    /// <summary>
    /// A headerless extra column for the route under the cursor when it is not one of
    /// the five. Above the legend threshold most of what is drawn has no column, and
    /// hovering one of those used to light the map while the table said nothing — the
    /// one moment the counts are actually being asked for.
    /// </summary>
    public void SetPreview(Color color, IReadOnlyList<int> counts)
    {
        if (_preview is { } shown && shown.Counts.SequenceEqual(counts))
            return;
        _preview = (color, counts);
        Render();
    }

    public void ClearPreview()
    {
        if (_preview is null)
            return;
        _preview = null;
        Render();
    }

    private string? FocusedRouteKey()
    {
        var column = _columns.FindIndex(control => control.HasFocus());
        if (column < 0)
            return null;
        var route = _columnRoutes[column];
        return route >= 0 && route < _routes.Count ? _routes[route].Key : null;
    }

    private void Render(string? focusedKey = null)
    {
        focusedKey ??= FocusedRouteKey();
        foreach (var column in _columns)
        {
            _panel.RemoveChild(column);
            column.QueueFree();
        }
        _columns.Clear();
        _columnMarks.Clear();
        _columnColors.Clear();
        _columnRoutes.Clear();
        _columnKeys.Clear();

        // A folded page keeps every pin on that page and the explicitly selected
        // candidate. A page of alternatives remains usable even if all pins are on
        // another page. Hover previews can still be compared alongside the pins.
        var routes = new List<(int Route, Color Color, string Key, IReadOnlyList<int> Counts)>();
        for (var i = 0; i < _routes.Count; i++)
            if (!_folded || _pinned.Count == 0 || _pinned.Contains(i) || i == _selected)
                routes.Add((i, _routes[i].Color, _routes[i].Key, _routes[i].Counts));
        if (_preview is { } extra)
            routes.Add((-1, extra.Color, "", extra.Counts));

        FitPanel(routes.Count);
        foreach (var name in _typeNames)
            name.Visible = routes.Count == 0;

        for (var i = 0; i < routes.Count; i++)
        {
            var index = routes[i].Route;
            // The preview column reports nothing: it exists because the pointer is on
            // the map, and clicking a column that vanishes on the next mouse move
            // would lock a route the player cannot see the name of.
            var interactive = index >= 0;
            var column = new Control
            {
                Name = $"Route{i}",
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
            _columnRoutes.Add(index);

            // The column's key: a dash of the very texture its route is drawn with, in
            // its colour — a sample of that line rather than a letter standing in for
            // one. RefreshMarks swaps it for the pin ring while the route is locked.
            var key = new TextureRect
            {
                Name = "Key",
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                Position = new Vector2((ColumnWidth - KeySize) / 2f, (FirstRowY - HeaderY - KeySize) / 2f),
                Size = new Vector2(KeySize, KeySize),
                PivotOffset = new Vector2(KeySize / 2f, KeySize / 2f),
                Modulate = routes[i].Color,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            _columnKeys.Add(key);
            column.AddChild(key);

            for (var r = 0; r < Rows.Length && r < routes[i].Counts.Count; r++)
            {
                var count = MakeLabel(24, routes[i].Counts[r].ToString(), StsColors.legendText);
                count.Position = new Vector2(0, FirstRowY - HeaderY + r * RowHeight);
                count.Size = new Vector2(ColumnWidth, RowHeight);
                if (routes[i].Counts[r] == 0)
                    count.Modulate = new Color(1f, 1f, 1f, 0.4f);
                column.AddChild(count);
            }

            if (interactive)
            {
                column.MouseEntered += () => Guard.Run("Column hover", () => ColumnHot?.Invoke(index));
                column.MouseExited += () => Guard.Run("Column unhover", () => ColumnCold?.Invoke(index));
                column.FocusEntered += () => Guard.Run("Column focus", () =>
                {
                    ShowAlternativesForController();
                    ColumnHot?.Invoke(index);
                });
                column.FocusExited += () => Guard.Run("Column unfocus", () =>
                {
                    ColumnCold?.Invoke(index);
                    NoteFocusChanged();
                });
                column.GuiInput += inputEvent => Guard.Run("Column select", () =>
                {
                    var selected = inputEvent.IsActionPressed(MegaInput.select) ||
                        inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false };
                    if (selected)
                    {
                        column.AcceptEvent();
                        var key = routes.First(route => route.Route == index).Key;
                        Callable.From(() => Guard.Run("Selecting a legend column", () =>
                        {
                            if (!GodotObject.IsInstanceValid(_panel) || !_panel.IsVisibleInTree())
                                return;
                            var current = _routes.ToList().FindIndex(route => route.Key == key);
                            if (current >= 0)
                                ColumnLockToggled?.Invoke(current);
                        })).CallDeferred();
                    }
                    else if (inputEvent.IsActionReleased(MegaInput.select))
                        column.AcceptEvent();
                });
            }
            else
            {
                column.FocusMode = Control.FocusModeEnum.None;
                column.MouseFilter = Control.MouseFilterEnum.Ignore;
            }

            _columns.Add(column);
            _panel.AddChild(column);
        }

        RefreshMarks();
        WireIconFocus();
        if (focusedKey is not null)
        {
            var index = _columnRoutes.FindIndex(route => route >= 0 && _routes[route].Key == focusedKey);
            var target = index >= 0 ? _columns[index] : _columns.FirstOrDefault(control => control.FocusMode != Control.FocusModeEnum.None);
            (target ?? _iconCells[0]).GrabFocus();
        }
    }

    /// <summary>
    /// Pinning arms the fold. Removing a pin expands the page for comparison.
    /// </summary>
    private void UpdatePins(IReadOnlySet<int> pinned, int count)
    {
        var previousCount = _pinnedCount;
        _pinned = pinned;
        _pinnedCount = count;
        if (count == 0 || count < previousCount)
        {
            _foldPending = false;
            _folded = false;
        }
        else if (!_folded)
        {
            _foldPending = true;
        }
    }

    public void Expand()
    {
        // Looking at alternatives is temporary while pins remain. Re-arm the fold
        // on every visit, even when no route or pin refresh happens between visits.
        _foldPending = _pinnedCount > 0;
        _folded = false;
    }

    /// <summary>Re-entering the legend makes it possible to add another pin.</summary>
    private void ShowAlternativesForController() => Guard.Run("Showing routes on controller focus", () =>
    {
        if (NControllerManager.Instance?.IsUsingDirectionalNavigation == true)
            ShowAlternatives();
    });

    public void ShowAlternatives()
    {
        if (_dragging || !_folded || _expandPending)
            return;
        _expandPending = true;
        Callable.From(() => Guard.Run("Expanding route choices", () =>
        {
            _expandPending = false;
            if (!GodotObject.IsInstanceValid(_panel) || !_panel.IsVisibleInTree() || !_folded)
                return;
            Expand();
            Render();
        })).CallDeferred();
    }

    /// <summary>
    /// The player's attention has left the legend — the pointer is off it, or focus has
    /// gone elsewhere — so an armed fold happens now.
    ///
    /// Held until this moment on purpose. Folding on the click itself would pull four
    /// columns out from under a pointer that is still in the middle of comparing them,
    /// and the panel would jump away from the cursor at the instant of choosing.
    /// </summary>
    public void LookedAway()
    {
        if (_dragging || !_foldPending)
            return;
        _foldPending = false;
        _folded = true;
        Render();
    }

    /// <summary>
    /// Focus has left one of our controls. Whether it left the *legend* can only be
    /// answered once the next control has taken focus, so the question waits a frame —
    /// otherwise stepping from one column to the next would read as leaving.
    /// </summary>
    private void NoteFocusChanged() =>
        Callable.From(() => Guard.Run("Following focus out of the legend", () =>
        {
            if (!GodotObject.IsInstanceValid(_panel))
                return;
            if (!OwnsFocus(_panel.GetViewport()?.GuiGetFocusOwner()))
                LookedAway();
        })).CallDeferred();

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
            // By route, not by place in the row: folded, the one column left is not
            // column zero's route.
            var route = _columnRoutes[i];
            // The preview column is only ever there because the pointer is on its route.
            var locked = route >= 0 && _pinned.Contains(route);
            var hot = route < 0 || route == _hot || route == _selected;

            // The key: the route's dash normally, the pin ring once it is locked. Set
            // before the early return below, since every column has one whether or not
            // it is currently marked.
            if (i < _columnKeys.Count)
            {
                _columnKeys[i].Texture = locked
                    ? PinMark()
                    : ResourceLoader.Load<Texture2D>(
                        DashTexture, null, ResourceLoader.CacheMode.Reuse);
                // The dash texture runs along its own Y axis, so a quarter turn lays it
                // across the column the way a length of route reads. The pin stands up.
                _columnKeys[i].Rotation = locked ? 0f : Mathf.Pi / 2f;
                _columnKeys[i].Scale = locked ? Vector2.One : new Vector2(0.85f, 1.5f);
            }

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
                // Locked and *under the cursor* has to be its own state, or a pad
                // moving onto the column it has already pinned gets no answer at all —
                // the lock's own frame is already there, so nothing changes and the
                // cursor is simply lost. A pale, heavier frame is that answer: still
                // plainly the lock's frame, unmistakably lit.
                style.BorderColor = hot
                    ? Lift(_columnColors[i], 0.55f)
                    : Deepen(_columnColors[i], 0.7f);
                style.SetBorderWidthAll(hot ? 6 : 4);
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
    /// Toward cream, for the one place a colour has to get *lighter* rather than
    /// darker: a locked column's frame under the cursor, which is read against the
    /// darkened frame of the same colour rather than against the parchment.
    /// </summary>
    private static Color Lift(Color color, float amount) =>
        color.Lerp(new Color(1f, 0.98f, 0.92f), amount) with { A = color.A };

    /// <summary>
    /// Whether the pointer is over the panel. Map-side route hover has to stand down
    /// while it is, or it would clear the very column the legend just lit.
    /// </summary>
    public bool Covers(Vector2 globalPoint) =>
        _panel.Visible && (_dragging || _panel.GetGlobalRect().HasPoint(globalPoint));

    /// <summary>Where the legend hotkey lands; null when the panel is hidden.</summary>
    public Control? FirstFocus => _panel.Visible ? _iconCells.FirstOrDefault() : null;

    public bool OwnsFocus(Control? focused) =>
        focused is { } control && _panel.IsAncestorOf(control);

    public void SetShellVisible(bool visible)
    {
        if (!visible && _dragging)
        {
            _dragging = false;
            PathingOptions.SaveLegendOffset(_panel.Position - PresetPosition);
        }
        _panel.Visible = visible;
        StopResize();
        LayoutPanel(_targetSize);
    }

    public void Dispose()
    {
        StopResize();
        if (GodotObject.IsInstanceValid(_screen))
            _screen.Resized -= _screenResized;
        _hotkeyGlyph?.Dispose();
        if (GodotObject.IsInstanceValid(_panel))
            _panel.QueueFree();
    }

    /// <summary>
    /// Icon cells chain vertically; right from any icon enters the route columns,
    /// which chain horizontally. Every edge is parked — an unset neighbour falls back
    /// to a viewport-wide search.
    /// </summary>
    /// <summary>
    /// Where the d-pad goes on leaving the top of the legend — the mod's toolbar, which
    /// is otherwise unreachable without a mouse.
    /// </summary>
    public void SetTopNeighbor(Control? control)
    {
        _topNeighbor = control;
        WireIconFocus();
    }

    private Control? _topNeighbor;

    private void WireIconFocus()
    {
        var self = new NodePath(".");
        var columns = _columns.Where(control => control.FocusMode != Control.FocusModeEnum.None).ToList();
        var clear = _clearUnpinned.Control.Visible ? _clearUnpinned.Control : null;
        var previous = _previousPage.Control.Visible ? _previousPage.Control : null;
        var next = _nextPage.Control.Visible ? _nextPage.Control : null;
        var firstFooter = clear ?? previous;
        // Relative to the control that carries the property, never to the panel: these
        // live one level down, so a path measured from the panel resolves short and
        // Godot rejects it outright ("Neighbor focus node path is invalid").
        var above = _topNeighbor is { } target && GodotObject.IsInstanceValid(target)
            ? target
            : null;
        NodePath Up(Control from) => above is null ? self : from.GetPathTo(above);
        for (var r = 0; r < _iconCells.Count; r++)
        {
            var cell = _iconCells[r];
            cell.FocusNeighborLeft = self;
            cell.FocusNeighborTop = r > 0 ? cell.GetPathTo(_iconCells[r - 1]) : Up(cell);
            cell.FocusNeighborBottom = r < _iconCells.Count - 1 ? cell.GetPathTo(_iconCells[r + 1])
                : firstFooter is not null ? cell.GetPathTo(firstFooter) : self;
            cell.FocusNeighborRight = columns.Count > 0 ? cell.GetPathTo(columns[0]) : self;
        }
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            column.FocusNeighborTop = Up(column);
            column.FocusNeighborBottom = firstFooter is not null ? column.GetPathTo(firstFooter) : self;
            column.FocusNeighborLeft = i > 0
                ? column.GetPathTo(columns[i - 1])
                : _iconCells.Count > 0 ? column.GetPathTo(_iconCells[0]) : self;
            column.FocusNeighborRight = i < columns.Count - 1 ? column.GetPathTo(columns[i + 1]) : self;
        }
        if (clear is not null)
        {
            clear.FocusNeighborTop = clear.GetPathTo(columns.FirstOrDefault() ?? _iconCells[^1]);
            clear.FocusNeighborBottom = previous is not null ? clear.GetPathTo(previous) : self;
            clear.FocusNeighborLeft = clear.FocusNeighborRight = self;
        }
        if (previous is not null && next is not null)
        {
            var pageAbove = clear ?? columns.FirstOrDefault() ?? _iconCells[^1];
            previous.FocusNeighborTop = previous.GetPathTo(pageAbove);
            previous.FocusNeighborBottom = previous.FocusNeighborLeft = self;
            previous.FocusNeighborRight = previous.GetPathTo(next);
            next.FocusNeighborTop = next.GetPathTo(pageAbove);
            next.FocusNeighborBottom = next.FocusNeighborRight = self;
            next.FocusNeighborLeft = next.GetPathTo(previous);
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
