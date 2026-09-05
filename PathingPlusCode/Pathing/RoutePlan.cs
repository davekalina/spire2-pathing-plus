namespace PathingPlus.PathingPlusCode.Pathing;

/// <summary>Materialize chosen routes as an editable selection of nodes and steps.</summary>
public sealed record RoutePlan(HashSet<string> Pins, HashSet<(string From, string To)> Cut)
{
    public static RoutePlan FromRoutes(SpireMapGraph graph, string origin,
        IReadOnlySet<string> pinnable, IReadOnlyList<IReadOnlyList<string>> routes)
    {
        var pins = routes.SelectMany(route => route)
            .Where(id => id != origin && pinnable.Contains(id)).ToHashSet();
        var selected = pins.Append(origin).ToHashSet();
        var wanted = new HashSet<(string From, string To)>();
        foreach (var route in routes)
            for (var i = 1; i < route.Count; i++)
                wanted.Add((route[i - 1], route[i]));
        var cut = new HashSet<(string From, string To)>();
        foreach (var from in selected)
            foreach (var to in graph.Successors(from))
                if (selected.Contains(to) && !wanted.Contains((from, to)))
                    cut.Add((from, to));
        return new RoutePlan(pins, cut);
    }
}
