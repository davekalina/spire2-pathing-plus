using Godot;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.HoverTips;

namespace PathingPlus.PathingPlusCode.Map;

/// <summary>
/// The fourth icon in the map's drawing toolbar: the mod's path tool.
///
/// It goes in the game's own tray, beside the quill, the eraser and the broom, because
/// that is where a player already looks to change what the pen does. Everything about
/// it is copied from the native buttons rather than invented — the 60x60 slot, the
/// icon anchored across it at 1.1 scale about its own centre, the shared additive
/// material, the two tints (<c>#FFFFFF80</c> idle, <c>#57C4FF</c> live), and the 0.05s
/// lift to 1.2 scale on hover or focus. The art is <see cref="PathToolIcon" />.
///
/// The tray has to grow to hold it. It is a nine-patch anchored to the bottom-left
/// with a fixed width, so widening it moves its centre right by half a slot — and both
/// the button row and the hotkey glyph are anchored to that centre. So the row gains
/// half a slot on each side (keeping its left edge, gaining a slot on the right) and
/// the glyph is pushed back by half a slot to stay where it was. All three are put
/// back on dispose: the toolbar belongs to the game, and a mod that unloads should
/// leave it the width it found it.
/// </summary>
internal sealed class PathToolButton : IDisposable
{
    /// <summary>Matches the quill's and the eraser's slot; the broom's is 68.</summary>
    private const float Slot = 60f;

    /// <summary>The native buttons' own tints, from <c>NMapDrawButton</c>.</summary>
    private static readonly Color Live = new("57C4FFFF");
    private static readonly Color Idle = new("FFFFFF80");

    /// <summary>
    /// Where the tray's hover tips appear, from <c>NMapDrawButton</c>: offset from the
    /// button row, not from the button. All four share the row, so the tip shows in the
    /// same place whichever one is under the pointer — which is the game's behaviour and
    /// is what stops it jumping about as the pointer crosses the tray.
    /// </summary>
    private static readonly Vector2 TipOffset = new(10f, -132f);

    /// <summary>The tip's own text. David's words; see the loc keys in <see cref="Tip" />.</summary>
    private const string TipTitle = "Pathing Plus";
    private const string TipDescription = "Draw straight line paths between nodes on the map.";
    private const string TipTable = "map";
    private const string TipTitleKey = "PATHING_PLUS_TOOL.title";
    private const string TipDescriptionKey = "PATHING_PLUS_TOOL.description";

    private readonly Control? _tray;
    private readonly Control? _row;
    private readonly Control? _hotkey;
    private readonly Control? _button;
    private readonly TextureRect? _icon;
    private readonly Texture2D? _plain;
    private readonly Texture2D? _glow;

    private Tween? _tween;
    private bool _hovered;
    private bool _focused;
    private bool _selected;

    /// <summary>Whether the tray was actually grown, so disposing cannot shrink one that was not.</summary>
    private bool _widened;

    /// <summary>Raised when the player presses it; the view decides what that means.</summary>
    public event Action? Pressed;

    public PathToolButton(Control screen)
    {
        _tray = screen.GetNodeOrNull<Control>("%DrawingTools");
        _row = _tray?.GetNodeOrNull<Control>("HBoxContainer");
        _hotkey = _tray?.GetNodeOrNull<Control>("DrawingToolsHotkey");
        var clear = _row?.GetNodeOrNull<Control>("ClearButton");
        if (_tray is null || _row is null || clear is null)
            return;

        Widen(Slot);
        _widened = true;

        var art = PathToolIcon.Build();
        // The untreated sprite if the treatment could not run: a colour scroll among
        // line art is wrong, but it is a button that works, which the alternative is not.
        _plain = art?.Plain ?? PathToolIcon.Sprite();
        _glow = art?.Glow ?? _plain;

        _button = new Control
        {
            Name = "PathingPlusPathButton",
            CustomMinimumSize = new Vector2(Slot, Slot),
            FocusMode = Control.FocusModeEnum.All,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };

        _icon = new TextureRect
        {
            Name = "Icon",
            Texture = _plain,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SelfModulate = Idle,
            Scale = Vector2.One * 1.1f,
            PivotOffset = new Vector2(Slot / 2f, Slot / 2f),
        };
        _icon.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        // The same additive material the row's other three icons wear, taken from one
        // of them rather than rebuilt, so a change to it carries here automatically.
        _icon.Material = _row.GetNodeOrNull<TextureRect>("DrawButton/Icon")?.Material;
        _button.AddChild(_icon);
        _row.AddChild(_button);

        // Right of the broom, so the game's three keep the positions players know.
        // Neighbour paths are measured from the control that carries the property:
        // measured from a parent they resolve one level short and Godot rejects them.
        clear.FocusNeighborRight = clear.GetPathTo(_button);
        _button.FocusNeighborLeft = _button.GetPathTo(clear);

        _button.MouseEntered += () => Guard.Run("Path tool hover", () => SetHovered(true));
        _button.MouseExited += () => Guard.Run("Path tool unhover", () => SetHovered(false));
        _button.FocusEntered += () => Guard.Run("Path tool focus", () => SetFocused(true));
        _button.FocusExited += () => Guard.Run("Path tool unfocus", () => SetFocused(false));
        _button.GuiInput += inputEvent => Guard.Run("Path tool press", () =>
        {
            if (!inputEvent.IsActionPressed(MegaInput.select) &&
                inputEvent is not InputEventMouseButton
                    { ButtonIndex: MouseButton.Left, Pressed: false })
                return;
            Pressed?.Invoke();
            // Or the same press travels on to the map and moves the player a node.
            _button.AcceptEvent();
        });
    }

    /// <summary>Where the d-pad lands on this control coming from elsewhere.</summary>
    public Control? Focusable => _button;

    /// <summary>
    /// Whether the path tool is the one in hand. Selected swaps in the glow art and the
    /// live tint, which is exactly how the game shows a chosen quill or eraser.
    /// </summary>
    public void SetSelected(bool selected)
    {
        if (_icon is null || !GodotObject.IsInstanceValid(_icon) || _selected == selected)
            return;
        _selected = selected;
        _icon.Texture = selected ? _glow : _plain;
        // Through the tween rather than straight onto the property: the lift from a
        // hover is 0.05s long, and one still in flight would otherwise write the tint
        // back over this a frame later.
        Animate();
    }

    public void Dispose()
    {
        _tween?.Kill();
        // Only if it is ours: the tip is keyed on the row the whole tray shares.
        if (_hovered || _focused)
            Guard.Run("Taking down the path tool's hover tip", () => ShowTip(false));
        if (_button is { } button && GodotObject.IsInstanceValid(button))
            button.QueueFree();
        if (_widened)
            Widen(-Slot);
    }

    /// <summary>
    /// Grow (or shrink) the tray by one slot, carrying the row and the hotkey glyph
    /// with it. Both are anchored to the tray's centre, which moves half as far as its
    /// right edge does.
    /// </summary>
    private void Widen(float slot) => Guard.Run("Resizing the drawing toolbar", () =>
    {
        if (_tray is null || _row is null || !GodotObject.IsInstanceValid(_tray))
            return;
        _tray.OffsetRight += slot;
        _row.OffsetLeft -= slot / 2f;
        _row.OffsetRight += slot / 2f;
        if (_hotkey is { } hotkey && GodotObject.IsInstanceValid(hotkey))
        {
            hotkey.OffsetLeft -= slot / 2f;
            hotkey.OffsetRight -= slot / 2f;
        }
    });

    private void SetHovered(bool hovered)
    {
        _hovered = hovered;
        Lift();
    }

    private void SetFocused(bool focused)
    {
        _focused = focused;
        Lift();
    }

    /// <summary>
    /// Hover and controller focus are one state to these buttons, and this is that state
    /// changing. Only this drives the tip — selection must not, because the tip is keyed
    /// on the shared row, and taking it down on selection would take down whichever
    /// neighbour's tip happened to be up when a hotkey selected this one.
    /// </summary>
    private void Lift()
    {
        Guard.Run("The path tool's hover tip", () => ShowTip(_hovered || _focused));
        Animate();
    }

    /// <summary>
    /// The tray's hover tip, shown and hidden exactly as the game's three do it: keyed
    /// on the shared row, so showing this one replaces whichever was up.
    /// </summary>
    private void ShowTip(bool shown)
    {
        if (_row is null || !GodotObject.IsInstanceValid(_row))
            return;
        if (!shown)
        {
            NHoverTipSet.Remove(_row);
            return;
        }
        if (Tip() is not { } tip)
            return;
        NHoverTipSet.CreateAndShow(_row, tip)?.SetGlobalPosition(_row.GlobalPosition + TipOffset);
    }

    /// <summary>
    /// Built fresh each time rather than cached, because the text is resolved through
    /// the game's own loc tables and those are replaced wholesale when the language
    /// changes — see <see cref="ModStrings" />. Both entries have to be in place before
    /// the <c>LocString</c>s are read, or reading them throws.
    /// </summary>
    private static HoverTip? Tip()
    {
        // The shortcut goes in the title, the way the game's own two say "(Right-Click)"
        // and "(Middle-Click)" — and it is read live, so a rebind is reflected the next
        // time the tip is shown. Unbound simply says nothing.
        var key = PathToolHotkey.CurrentKeyLabel();
        var title = key is null ? TipTitle : $"{TipTitle} ({key})";
        if (!ModStrings.Ensure(TipTable, TipTitleKey, title) ||
            !ModStrings.Ensure(TipTable, TipDescriptionKey, TipDescription))
            return null;
        return new HoverTip(
            new LocString(TipTable, TipTitleKey),
            new LocString(TipTable, TipDescriptionKey));
    }

    /// <summary>
    /// The native lift: hover and controller focus are the same state to these buttons,
    /// and both raise the icon to 1.2 and light it. Dropping back returns it to
    /// whichever tint its selected state calls for.
    /// </summary>
    private void Animate()
    {
        if (_icon is null || !GodotObject.IsInstanceValid(_icon))
            return;
        var lit = _hovered || _focused;
        _tween?.Kill();
        _tween = _icon.CreateTween().SetParallel();
        _tween.TweenProperty(_icon, "scale", Vector2.One * (lit ? 1.2f : 1.1f), 0.05);
        _tween.TweenProperty(_icon, "self_modulate", lit || _selected ? Live : Idle, 0.05);
    }
}
