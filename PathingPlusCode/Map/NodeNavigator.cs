using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace PathingPlus.PathingPlusCode.Map;

/// <summary>
/// Controller navigation across the map while zoomed out. Every drawn node — not
/// just the ones on surviving routes; weighing options means looking at nodes you
/// are not currently planning through — becomes focusable in a four-way grid wired
/// from the drawn layout. Select on a non-travelable node pins it (see
/// <see cref="MapScreenPatches" />); select on a travelable node still travels.
///
/// The cursor must never be lost: re-wiring after a pin restores focus modes, which
/// drops focus off a disabled node, and the game grabs its own default control after
/// visual refreshes — so the grid re-grabs the remembered node deferred, which runs
/// after both. All touched focus state is snapshotted and restored on exit.
/// </summary>
internal sealed class NodeNavigator : IDisposable
{
    private readonly NMapScreen _screen;
    private IReadOnlyList<(NMapPoint Node, int Row, Vector2 Center)> _nodes = [];
    private bool _rotated;
    private readonly Dictionary<NMapPoint, SavedFocus> _saved = [];

    public bool Active { get; private set; }

    /// <summary>Raised when the controller cursor lands on a node.</summary>
    public event Action<NMapPoint>? NodeFocused;

    private sealed record SavedFocus(
        Control.FocusModeEnum Mode,
        NodePath Left, NodePath Right, NodePath Top, NodePath Bottom,
        Action OnFocus);

    public NodeNavigator(NMapScreen screen) => _screen = screen;

    /// <param name="takeFocus">
    /// False when focus belongs to something of the mod's own — pressing the Zoom
    /// button with a controller would otherwise throw focus onto the map, so a second
    /// press lands on a node and travels there instead of zooming back.
    /// </param>
    public void SetActive(bool active, bool takeFocus = true)
    {
        if (Active == active)
            return;
        Active = active;
        // Focus on this grid is the **controller's** cursor: the gold ring follows it,
        // and nothing else on screen shows it. Handing it to a pointer player puts an
        // invisible cursor on a map node, and a node the game thinks holds the cursor
        // stops pulsing — so the zoomed views were quietly costing a travelable node
        // its invitation to be clicked. The pointer's own cursor is the pointer.
        takeFocus &= NControllerManager.Instance?.IsUsingDirectionalNavigation == true;
        if (active)
        {
            ApplyWiring();
            if (!takeFocus)
                return;
            var start = _screen.DefaultFocusedControl as NMapPoint;
            if (start is null || !_saved.ContainsKey(start))
                start = _saved.Keys.FirstOrDefault(GodotObject.IsInstanceValid);
            start?.CallDeferred(Control.MethodName.GrabFocus);
        }
        else
        {
            RestoreWiring();
            if (takeFocus)
                _screen.DefaultFocusedControl?.CallDeferred(Control.MethodName.GrabFocus);
        }
    }

    /// <summary>
    /// Every node the d-pad can walk; re-wires in place while active. In the rotated
    /// view the map lies on its side — start left, boss right — so the wiring swaps
    /// axes to match what the player sees: right is bossward, up and down move
    /// within a floor.
    /// </summary>
    public void SetNodes(IReadOnlyList<(NMapPoint Node, int Row, Vector2 Center)> nodes, bool rotated)
    {
        _nodes = nodes;
        _rotated = rotated;
        if (Active)
            ApplyWiring();
    }

    public void Dispose() => RestoreWiring();

    private void ApplyWiring()
    {
        var remembered = _screen.GetViewport()?.GuiGetFocusOwner() as NMapPoint;

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
                Action onFocus = () => Guard.Run("Map cursor", () => NodeFocused?.Invoke(node));
                _saved[node] = new SavedFocus(
                    node.FocusMode,
                    node.FocusNeighborLeft, node.FocusNeighborRight,
                    node.FocusNeighborTop, node.FocusNeighborBottom,
                    onFocus);

                node.FocusMode = Control.FocusModeEnum.All;
                var previousInRow = c > 0 ? node.GetPathTo(rows[r][c - 1].Node) : self;
                var nextInRow = c < rows[r].Count - 1 ? node.GetPathTo(rows[r][c + 1].Node) : self;
                var bossward = r < rows.Count - 1
                    ? node.GetPathTo(NearestByX(rows[r + 1], center.X)) : self;
                var startward = r > 0
                    ? node.GetPathTo(NearestByX(rows[r - 1], center.X)) : self;

                if (_rotated)
                {
                    // On its side, a floor runs vertically (map X becomes screen Y)
                    // and bossward is to the right.
                    node.FocusNeighborTop = previousInRow;
                    node.FocusNeighborBottom = nextInRow;
                    node.FocusNeighborRight = bossward;
                    node.FocusNeighborLeft = startward;
                }
                else
                {
                    // Rows ascend toward the boss, so "up" is the next row group.
                    node.FocusNeighborLeft = previousInRow;
                    node.FocusNeighborRight = nextInRow;
                    node.FocusNeighborTop = bossward;
                    node.FocusNeighborBottom = startward;
                }
                node.FocusEntered += onFocus;
            }
        }

        if (remembered is { } focused && _saved.ContainsKey(focused))
            focused.CallDeferred(Control.MethodName.GrabFocus);
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

    private static NMapPoint NearestByX(
        List<(NMapPoint Node, int Row, Vector2 Center)> row, float x) =>
        row.MinBy(n => Math.Abs(n.Center.X - x)).Node;
}
