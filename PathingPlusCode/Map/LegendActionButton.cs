using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace PathingPlus.PathingPlusCode.Map;

/// <summary>The native button input and pause-menu face used by the map toolbar.</summary>
internal sealed class LegendActionButton
{
    public NButton Control { get; }
    private readonly TextureRect _face;
    private readonly MegaLabel _label;
    private bool _hovered;

    public LegendActionButton(string name, string text, Font? font, Action pressed)
    {
        Control = new NButton
        {
            Name = name,
            FocusMode = Godot.Control.FocusModeEnum.All,
            MouseFilter = Godot.Control.MouseFilterEnum.Stop,
        };
        _face = new TextureRect
        {
            Texture = ResourceLoader.Load<Texture2D>("res://images/ui/reward_screen/reward_item_button.png"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Godot.Control.MouseFilterEnum.Ignore,
        };
        _face.SetAnchorsAndOffsetsPreset(Godot.Control.LayoutPreset.FullRect);
        Control.AddChild(_face);
        _label = new MegaLabel
        {
            AutoSizeEnabled = false,
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Godot.Control.MouseFilterEnum.Ignore,
        };
        if (font is not null)
            _label.AddThemeFontOverride("font", font);
        _label.AddThemeFontSizeOverride("font_size", 18);
        _label.SetAnchorsAndOffsetsPreset(Godot.Control.LayoutPreset.FullRect);
        Control.AddChild(_label);
        Control.MouseEntered += () => Guard.Run("Legend action hover", () => { _hovered = true; Refresh(); });
        Control.MouseExited += () => Guard.Run("Legend action unhover", () => { _hovered = false; Refresh(); });
        Control.FocusEntered += () => Guard.Run("Legend action focus", Refresh);
        Control.FocusExited += () => Guard.Run("Legend action unfocus", Refresh);
        Control.Released += _ => Guard.Run("Releasing a legend action", () =>
        {
            Control.AcceptEvent();
            Callable.From(() => Guard.Run("Legend action", () =>
            {
                if (GodotObject.IsInstanceValid(Control) && Control.IsVisibleInTree() && Control.IsEnabled)
                    pressed();
            })).CallDeferred();
        });
        Control.GuiInput += input => Guard.Run("Legend action input", () =>
        {
            if (input.IsAction(MegaInput.select))
            {
                // Godot emits GuiInput before calling the native _GuiInput handler.
                // Let NButton process select first, then consume it so it cannot
                // reach the map. Accepting first would disable controller activation.
                Control._GuiInput(input);
                Control.AcceptEvent();
            }
        });
    }

    public void SetAvailable(bool available)
    {
        var hadFocus = Control.HasFocus();
        if (available)
            Control.Enable();
        else
            Control.Disable();
        // Disabled actions still occupy their place in directional navigation.
        Control.FocusMode = Godot.Control.FocusModeEnum.All;
        if (hadFocus && Control.IsVisibleInTree())
            Control.GrabFocus();
        Refresh();
    }

    private void Refresh()
    {
        var focused = Control.HasFocus() || _hovered;
        var brightness = Control.IsEnabled ? (focused ? 1f : 0.82f) : (focused ? 0.65f : 0.4f);
        _face.Modulate = new Color(brightness, brightness, brightness);
        _label.Modulate = Colors.White with { A = Control.IsEnabled ? 1f : 0.5f };
        _face.PivotOffset = Control.Size * 0.5f;
        _face.Scale = Vector2.One * (focused ? 1.03f : 1f);
    }
}
