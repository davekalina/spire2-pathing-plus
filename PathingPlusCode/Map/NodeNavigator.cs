using Godot;
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
    private readonly Dictionary<NMapPoint, SavedFocus> _saved = [];

    public bool Active { get; private set; }

    /// <summary>Raised when the controller cursor lands on a node.</summary>
    public event Action<NMapPoint>? NodeFocused;

    private sealed record SavedFocus(
        Control.FocusModeEnum Mode,
        NodePath Left, NodePath Right, NodePath Top, NodePath Bottom,
        Action OnFocus);

    public NodeNavigator(NMapScreen screen) => _screen = screen;

    public void SetActive(bool active)
    {
        if (Active == active)
            return;
        Active = active;
        if (active)
        {
            ApplyWiring();
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

    /// <summary>Every node the d-pad can walk; re-wires in place while active.</summary>
    public void SetNodes(IReadOnlyList<(NMapPoint Node, int Row, Vector2 Center)> nodes)
    {
        _nodes = nodes;
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
