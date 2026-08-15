using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace PathingPlus.PathingPlusCode.Map;

/// <summary>What an auto-path should collect as much of as it can.</summary>
internal enum AutoPathGoal
{
    Elites,
    Fires,
    Shops,
    Events,
    Combats,
}

internal static class AutoPathGoals
{
    /// <summary>
    /// The goal's row in <see cref="RouteLegendPanel.Rows" />, so a route is scored
    /// with the very counts the legend already shows for it. Treasure is left out:
    /// act maps carry one, and maximising it is not a choice.
    /// </summary>
    public static int Row(this AutoPathGoal goal) => goal switch
    {
        AutoPathGoal.Events => 0,
        AutoPathGoal.Shops => 1,
        AutoPathGoal.Fires => 3,
        AutoPathGoal.Combats => 4,
        _ => 5,
    };

    public static string Label(this AutoPathGoal goal) => $"Max {goal}";
}

/// <summary>
/// The Auto-Path pull-down on the toolbar: pick what to collect and the map is
/// redrawn with the routes that collect most of it.
///
/// It sits on the chip rather than in the settings panel because it is an action, not
/// a preference — and it replaces the plan outright, so it belongs where the plan is,
/// not two clicks away behind a gear.
/// </summary>
internal sealed class AutoPathMenu : IDisposable
{
    private static readonly Color Parchment = new(0.96f, 0.94f, 0.88f);
    private static readonly Color Idle = new(1f, 1f, 1f, 0.75f);

    /// <summary>The face at rest, and lifted to full when hovered or focused.</summary>
    private static readonly Color FaceIdle = new(0.82f, 0.82f, 0.82f);

    private const float OptionHeight = 34f;

    public event Action<AutoPathGoal>? GoalChosen;

    private readonly Control _root;
    private readonly Control _catcher;
    private readonly Control _list = null!;
    private readonly Control _header;
    private readonly MegaLabel _headerLabel;
    private readonly TextureRect _face = null!;
    private readonly List<Control> _options = [];
    private readonly Font? _font;

    public AutoPathMenu(Control screen, Control toolbar)
    {
        _font = screen.GetNodeOrNull<Label>("MapLegend/Header")?.GetThemeFont("font");

        _header = new Control
        {
            Name = "AutoPath",
            Position = new Vector2(MapToolbar.AutoPathLeft, MapToolbar.AutoPathTop),
            Size = new Vector2(MapToolbar.AutoPathWidth, MapToolbar.AutoPathHeight),
            FocusMode = Control.FocusModeEnum.All,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        // The pause menu's button face, as the Zoom button wears. A bare label was
        // almost invisible when focused: there was nothing to light up but the text.
        _face = new TextureRect
        {
            Texture = ResourceLoader.Load<Texture2D>(
                "res://images/ui/reward_screen/reward_item_button.png",
                null, ResourceLoader.CacheMode.Reuse),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = FaceIdle,
        };
        _face.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        Guard.Run("Auto-Path button tint", () =>
        {
            var shader = ResourceLoader.Load<Shader>(
                "res://shaders/hsv.gdshader", null, ResourceLoader.CacheMode.Reuse);
            var material = new ShaderMaterial { Shader = shader };
            material.SetShaderParameter("h", 1.0f);
            material.SetShaderParameter("s", 0.55f);
            material.SetShaderParameter("v", 0.85f);
            _face.Material = material;
        });
        _header.AddChild(_face);

        _headerLabel = MakeLabel(20, "Auto-Path  ▼");
        _headerLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _headerLabel.VerticalAlignment = VerticalAlignment.Center;
        _headerLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _headerLabel.Modulate = Idle;
        _header.AddChild(_headerLabel);

        _header.MouseEntered += () => Guard.Run("Auto-Path hover", () => Emphasize(true));
        _header.MouseExited += () => Guard.Run("Auto-Path unhover", () => Emphasize(_list.Visible));
        _header.FocusEntered += () => Guard.Run("Auto-Path focus", () => Emphasize(true));
        _header.FocusExited += () => Guard.Run("Auto-Path unfocus", () => Emphasize(_list.Visible));
        _header.GuiInput += inputEvent => Guard.Run("Auto-Path toggle", () =>
        {
            if (!Activated(inputEvent))
                return;
            SetOpen(!_list.Visible);
            _header.AcceptEvent();
        });
        toolbar.AddChild(_header);

        _root = new Control { Name = "AutoPathMenu", MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        _catcher = new Control
        {
            Name = "Dismisser",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _catcher.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _catcher.GuiInput += inputEvent => Guard.Run("Dismissing Auto-Path", () =>
        {
            if (inputEvent is InputEventMouseButton { Pressed: false })
                SetOpen(false);
        });
        _root.AddChild(_catcher);

        var goals = Enum.GetValues<AutoPathGoal>();
        _list = new Control
        {
            Name = "Goals",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop,
            AnchorLeft = 1f,
            AnchorRight = 1f,
            OffsetLeft = -(MapToolbar.Width + 24f - MapToolbar.AutoPathLeft),
            OffsetRight = -(24f + MapToolbar.AutoPathLeft),
            GrowHorizontal = Control.GrowDirection.Begin,
        };
        // Hangs directly off the header, wherever the toolbar sits.
        _list.OffsetTop = 190f + MapToolbar.AutoPathTop + MapToolbar.AutoPathHeight;
        _list.OffsetBottom = _list.OffsetTop + goals.Length * OptionHeight + 20f;

        var parchment = new TextureRect
        {
            Texture = ResourceLoader.Load<Texture2D>(
                "res://images/packed/common_ui/submenu_panel_short.png",
                null, ResourceLoader.CacheMode.Reuse),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Modulate = new Color(0.42f, 0.38f, 0.34f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        parchment.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _list.AddChild(parchment);

        _list.GuiInput += inputEvent => Guard.Run("Closing Auto-Path", () =>
        {
            if (!inputEvent.IsActionPressed(MegaInput.cancel))
                return;
            SetOpen(false);
            _list.AcceptEvent();
            _header.CallDeferred(Control.MethodName.GrabFocus);
        });

        for (var i = 0; i < goals.Length; i++)
        {
            var goal = goals[i];
            var option = new Control
            {
                Name = goal.ToString(),
                Position = new Vector2(18f, 10f + i * OptionHeight),
                Size = new Vector2(MapToolbar.AutoPathWidth - 36f, OptionHeight),
                FocusMode = Control.FocusModeEnum.All,
                MouseFilter = Control.MouseFilterEnum.Stop,
            };
            var label = MakeLabel(19, goal.Label());
            label.VerticalAlignment = VerticalAlignment.Center;
            label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            label.Modulate = Idle;
            option.AddChild(label);

            option.MouseEntered += () => Guard.Run("Goal hover", () => label.Modulate = Colors.White);
            option.MouseExited += () => Guard.Run("Goal unhover", () => label.Modulate = Idle);
            option.FocusEntered += () => Guard.Run("Goal focus", () => label.Modulate = Colors.White);
            option.FocusExited += () => Guard.Run("Goal unfocus", () => label.Modulate = Idle);
            option.GuiInput += inputEvent => Guard.Run("Choosing an auto-path goal", () =>
            {
                if (!Activated(inputEvent))
                    return;
                option.AcceptEvent();
                SetOpen(false);
                GoalChosen?.Invoke(goal);
                // Back to the header, not to whatever the map does with a stray press.
                _header.CallDeferred(Control.MethodName.GrabFocus);
            });
            _list.AddChild(option);
            _options.Add(option);
        }

        _root.AddChild(_list);
        screen.AddChild(_root);

        // Only now, with both subtrees in the tree. GetPathTo needs a common parent,
        // and until _root is under the screen the options and the header — which went
        // into the toolbar above — are in two disjoint trees. Godot answers that with
        // "Parameter "common_parent" is null" and an empty path, so the first option's
        // way back up to the header silently went nowhere.
        for (var i = 0; i < _options.Count; i++)
        {
            _options[i].FocusNeighborTop = i > 0
                ? _options[i].GetPathTo(_options[i - 1])
                : _options[i].GetPathTo(_header);
            _options[i].FocusNeighborBottom = i < _options.Count - 1
                ? _options[i].GetPathTo(_options[i + 1])
                : new NodePath(".");
        }
    }

    /// <summary>Where the d-pad lands on this control coming from elsewhere.</summary>
    public Control Focusable => _header;

    /// <summary>Whether this control, or its open list, holds focus.</summary>
    public bool OwnsFocus(Control? focused) =>
        focused is { } control && (control == _header || _list.IsAncestorOf(control));

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

    private static bool Activated(InputEvent inputEvent) =>
        inputEvent.IsActionPressed(MegaInput.select) ||
        inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false };

    private void SetOpen(bool open)
    {
        _list.Visible = open;
        _catcher.Visible = open;
        Emphasize(open || _header.HasFocus());
        _headerLabel.Text = open ? "Auto-Path  ▲" : "Auto-Path  ▼";
        if (!open)
            return;
        _root.MoveToFront();
        // A controller opening the list needs somewhere to be, or the d-pad would
        // still be walking the toolbar behind it.
        _options.FirstOrDefault()?.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void Emphasize(bool on)
    {
        _headerLabel.Modulate = on ? Colors.White : Idle;
        if (GodotObject.IsInstanceValid(_face))
            _face.Modulate = on ? Colors.White : FaceIdle;
    }

    private MegaLabel MakeLabel(int fontSize, string text)
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
        label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.55f));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 2);
        label.AddThemeConstantOverride("outline_size", 0);
        return label;
    }
}
