namespace PathingPlus.PathingPlusCode.Pathing;

/// <summary>
/// A game-independent snapshot of an act map: nodes on rows (floors) with directed
/// edges toward the boss. Built from the game's map model by an adapter on the Godot
/// side; everything in this namespace must stay free of Godot and sts2 types so the
/// test project can link it.
/// </summary>
public sealed class SpireMapGraph
{
    private readonly Dictionary<string, GraphNode> _nodes;
    private readonly Dictionary<string, List<string>> _edges;

    public SpireMapGraph(IEnumerable<GraphNode> nodes, IEnumerable<(string From, string To)> edges)
    {
        _nodes = nodes.ToDictionary(n => n.Id);
        _edges = new Dictionary<string, List<string>>();
        foreach (var (from, to) in edges)
        {
            if (!_nodes.ContainsKey(from) || !_nodes.ContainsKey(to))
                throw new ArgumentException($"Edge {from} -> {to} references a node not in the graph.");
            if (!_edges.TryGetValue(from, out var list))
                _edges[from] = list = new List<string>();
            if (!list.Contains(to))
                list.Add(to);
        }
    }

    public IReadOnlyCollection<GraphNode> Nodes => _nodes.Values;

    public bool Contains(string id) => _nodes.ContainsKey(id);

    public GraphNode Node(string id) => _nodes[id];

    public IReadOnlyList<string> Successors(string id) =>
        _edges.TryGetValue(id, out var list) ? list : Array.Empty<string>();
}

/// <param name="Id">Stable identity of the node within one act map.</param>
/// <param name="Row">Floor index, ascending toward the boss.</param>
/// <param name="RoomKind">Opaque room descriptor (icon choice happens on the UI side).</param>
public sealed record GraphNode(string Id, int Row, string RoomKind);
