using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace PathingPlus.PathingPlusCode.Map;

/// <summary>
/// The controller glyph for a hotkey, shown only while a controller is driving.
///
/// The game resolves glyphs in two layers and the native `NHotkeyIcon` only uses the
/// upper one: `NInputManager.GetHotkeyIcon` maps a **remappable action** to whatever
/// button it is bound to. A raw button like the right trigger is not an action, so
/// that lookup returns null, `UpdateInput` leaves the scene's placeholder art in
/// place, and the icon silently claims to be the south face button. Falling through
/// to `NControllerManager.GetHotkeyIcon`, which reads the controller config's glyph
/// map directly, is what makes a raw button render as itself.
/// </summary>
internal sealed class HotkeyGlyph : IDisposable
{
    private readonly TextureRect _icon;
    private readonly string _hotkey;

    public HotkeyGlyph(Control parent, string hotkey, Vector2 size)
    {
        _hotkey = hotkey;
        _icon = new TextureRect
        {
            Name = "HotkeyGlyph",
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = size,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        parent.AddChild(_icon);
        Refresh();

        Guard.Run("Watching the control scheme for a glyph", () =>
        {
            if (NControllerManager.Instance is not { } controllers)
                return;
            foreach (var signal in new[]
            {
                NControllerManager.SignalName.ControllerDetected,
                NControllerManager.SignalName.MouseDetected,
                NControllerManager.SignalName.ControllerTypeChanged,
            })
                controllers.Connect(signal,
                    Callable.From(() => Guard.Run("Refreshing a hotkey glyph", Refresh)));
        });
    }

    /// <summary>Place it like any control; the caller owns the layout.</summary>
    public TextureRect Node => _icon;

    public void Refresh()
    {
        if (!GodotObject.IsInstanceValid(_icon))
            return;
        var controllers = NControllerManager.Instance;
        _icon.Visible = controllers?.IsUsingDirectionalNavigation == true;
        if (!_icon.Visible)
            return;
        // Action first (it honours rebinding), then the raw button.
        _icon.Texture = NInputManager.Instance?.GetHotkeyIcon(_hotkey)
            ?? controllers?.GetHotkeyIcon(_hotkey);
    }

    public void Dispose()
    {
        if (GodotObject.IsInstanceValid(_icon))
            _icon.QueueFree();
    }
}
