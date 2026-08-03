using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using System.Reflection;

namespace PathingPlus.PathingPlusCode.Map;

/// <summary>
/// Plan Mode: a toggle that hands the whole future map to the controller.
///
/// Natively only travelable nodes are focusable; everything ahead is disabled with
/// <c>FocusMode.None</c>, and the vertical d-pad scrolls the screen instead of moving
/// focus. While Plan Mode is on, every node on a surviving route becomes focusable in
/// a four-way grid wired from the drawn layout, the screen's scroll handler is skipped
/// (see <see cref="MapScreenPatches" />), the view follows focus instead, and select
/// on a non-travelable node toggles its pin. Select on a travelable node still
/// travels — that is commitment, and it stays native.
///
/// Everything is snapshotted before it is touched and restored on the way out, so the
/// mode is inert unless the player turns it on.
/// </summary>
internal sealed class PlanModeController : IDisposable
{
    /// <summary>Where the focused node is steered to on screen, in viewport pixels.</summary>
    private const float FocusViewY = 450f;

    private static readonly FieldInfo? TargetDragPosField =
        AccessTools.Field(typeof(NMapScreen), "_targetDragPos");

    private readonly NMapScreen _screen;
    private readonly NinePatchRect _tray;
    private readonly TextureRect _buttonIcon;

    private IReadOnlyList<(NMapPoint Node, int Row, Vector2 Center)> _nodes = [];
    private readonly Dictionary<NMapPoint, SavedFocus> _saved = [];

    public bool Active { get; private set; }

    private sealed record SavedFocus(
        Control.FocusModeEnum Mode,
        NodePath Left, NodePath Right, NodePath Top, NodePath Bottom,
        Action OnFocus);

    public PlanModeController(NMapScreen screen)
    {
        _screen = screen;

        // A one-button tray stacked above the native DrawingTools, in its style but
        // deliberately not part of it: the drawing tray is its own hotkey context, and
        // joining its focus chain buried the toggle inside that mode. This button is
        // mouse-only; the controller path is the Right Trigger hotkey.
        _tray = new NinePatchRect
        {
            Name = "PathingPlusPlanTray",
            SelfModulate = new Color(0f, 0f, 0f, 0.752941f),
            Texture = ResourceLoader.Load<Texture2D>(
                "res://images/ui/tiny_nine_patch.png", null, ResourceLoader.CacheMode.Reuse),
            PatchMarginLeft = 12,
            PatchMarginTop = 12,
            PatchMarginRight = 12,
            PatchMarginBottom = 12,
        };
        _tray.AnchorTop = _tray.AnchorBottom = 1f;
        _tray.OffsetLeft = 56f;
        _tray.OffsetRight = 124f;
        _tray.OffsetTop = -184f;
        _tray.OffsetBottom = -116f;
        _tray.GrowVertical = Control.GrowDirection.Begin;

        var button = new Control
        {
            Name = "PlanModeButton",
            FocusMode = Control.FocusModeEnum.None,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        button.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _tray.AddChild(button);

        _buttonIcon = new TextureRect
        {
            Texture = ResourceLoader.Load<Texture2D>(
                "res://images/packed/map/icons/map_ping.png", null, ResourceLoader.CacheMode.Reuse),
            Material = ResourceLoader.Load<Material>(
                "res://themes/canvas_item_material_additive_shared.tres", null, ResourceLoader.CacheMode.Reuse),
            SelfModulate = new Color(1f, 1f, 1f, 0.501961f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        _buttonIcon.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _buttonIcon.OffsetLeft = 10f;
        _buttonIcon.OffsetTop = 10f;
        _buttonIcon.OffsetRight = -10f;
        _buttonIcon.OffsetBottom = -10f;
        button.AddChild(_buttonIcon);

        button.GuiInput += inputEvent => Guard.Run("Plan mode button", () =>
        {
            if (inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
                Toggle();
        });

        screen.AddChild(_tray);

        Guard.Run("Plan mode hotkey glyph", () =>
        {
            var hotkeyIcon = SceneHelper.Instantiate<NHotkeyIcon>("ui/hotkey_icon");
            hotkeyIcon.CustomMinimumSize = new Vector2(48, 48);
            hotkeyIcon.AnchorTop = hotkeyIcon.AnchorBottom = 0.5f;
            hotkeyIcon.OffsetLeft = -52f;
            hotkeyIcon.OffsetRight = -4f;
            hotkeyIcon.OffsetTop = -24f;
            hotkeyIcon.OffsetBottom = 24f;
            _tray.AddChild(hotkeyIcon);
            hotkeyIcon.UpdateInput(Controller.rightTrigger);
        });
    }

    public void Toggle()
    {
        Active = !Active;
        _buttonIcon.SelfModulate = Active ? StsColors.gold : new Color(1f, 1f, 1f, 0.501961f);
        if (Active)
        {
            ApplyWiring();
            // Start focus on a node the wiring covers, or d-pad presses go nowhere.
            var start = _screen.DefaultFocusedControl as NMapPoint;
            if (start is null || !_saved.ContainsKey(start))
                start = _saved.Keys.FirstOrDefault(GodotObject.IsInstanceValid);
            start?.CallDeferred(Control.MethodName.GrabFocus);
        }
        else
        {
            RestoreWiring();
            _screen.DefaultFocusedControl?.CallDeferred(Control.MethodName.GrabFocus);
        }
    }

    public void Deactivate()
    {
        if (!Active)
            return;
        Active = false;
        _buttonIcon.SelfModulate = new Color(1f, 1f, 1f, 0.501961f);
        RestoreWiring();
    }

    /// <summary>The nodes plan mode can walk: every node on a surviving route.</summary>
    public void SetNodes(IReadOnlyList<(NMapPoint Node, int Row, Vector2 Center)> nodes)
    {
        _nodes = nodes;
        if (Active)
            ApplyWiring();
    }

    public void Dispose()
    {
        RestoreWiring();
        if (GodotObject.IsInstanceValid(_tray))
            _tray.QueueFree();
    }

    private void ApplyWiring()
    {
        RestoreWiring();

        var self = new NodePath(".");
        var rows = _nodes.Where(n => GodotObject.IsInstanceValid(n.Node))
            .GroupBy(n => n.Row)
            .OrderBy(g => g.Key)
            .Select(g => g.OrderBy(n => n.Center.X).ToList())
            .ToList();

        for (var r = 0; r < rows.Count; r++)
        {
            for (var c = 0; c < rows[r].Count; c++)
            {
                var (node, _, center) = rows[r][c];
                Action onFocus = () => Guard.Run("Plan mode scroll", () => ScrollToward(center));
                _saved[node] = new SavedFocus(
                    node.FocusMode,
                    node.FocusNeighborLeft, node.FocusNeighborRight,
                    node.FocusNeighborTop, node.FocusNeighborBottom,
                    onFocus);

                node.FocusMode = Control.FocusModeEnum.All;
                node.FocusNeighborLeft = c > 0 ? node.GetPathTo(rows[r][c - 1].Node) : self;
                node.FocusNeighborRight = c < rows[r].Count - 1 ? node.GetPathTo(rows[r][c + 1].Node) : self;
                // Rows ascend toward the boss, so "up" is the next row group.
                node.FocusNeighborTop = r < rows.Count - 1
                    ? node.GetPathTo(NearestByX(rows[r + 1], center.X)) : self;
                node.FocusNeighborBottom = r > 0
                    ? node.GetPathTo(NearestByX(rows[r - 1], center.X)) : self;
                node.FocusEntered += onFocus;
            }
        }
    }

    private void RestoreWiring()
    {
        foreach (var (node, saved) in _saved)
        {
            if (!GodotObject.IsInstanceValid(node))
                continue;
            node.FocusEntered -= saved.OnFocus;
            node.FocusMode = saved.Mode;
            node.FocusNeighborLeft = saved.Left;
            node.FocusNeighborRight = saved.Right;
            node.FocusNeighborTop = saved.Top;
            node.FocusNeighborBottom = saved.Bottom;
        }
        _saved.Clear();
    }

    /// <summary>
    /// The native scroll handler is skipped while plan mode is on, so the view follows
    /// focus instead: steer the map's drag target until the node sits mid-screen.
    /// </summary>
    private void ScrollToward(Vector2 nodeCenter)
    {
        if (!Active)
            return;
        var targetY = Mathf.Clamp(FocusViewY - nodeCenter.Y, -600f, 1800f);
        TargetDragPosField?.SetValue(_screen, new Vector2(0f, targetY));
    }

    private static NMapPoint NearestByX(
        List<(NMapPoint Node, int Row, Vector2 Center)> row, float x) =>
        row.MinBy(n => Math.Abs(n.Center.X - x)).Node;
}
