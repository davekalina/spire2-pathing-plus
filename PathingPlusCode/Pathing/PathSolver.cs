namespace PathingPlus.PathingPlusCode.Pathing;

/// <summary>
/// Enumerates complete routes through an act map and filters them by waypoint
/// selections. A route runs from a start node to any node with no outgoing edges.
/// </summary>
public static class PathSolver
{
    /// <summary>
    /// Safety net against a pathological map. Act maps produce at most a few hundred
    /// routes; hitting this cap means the graph adapter fed us something wrong, and the
    /// UI should say "too many" rather than freeze.
    /// </summary>
    public const int MaxPaths = 4096;

    /// <summary>The most routes the legend ever shows at once.</summary>
    public const int LegendThreshold = 5;

    public static PathSet EnumeratePaths(SpireMapGraph graph, IEnumerable<string> startIds)
    {
        var paths = new List<IReadOnlyList<string>>();
        var truncated = false;
        var stack = new List<string>();

        var starts = startIds.Where(graph.Contains)
            .OrderBy(id => graph.Node(id).Row)
            .ThenBy(id => id, StringComparer.Ordinal);
        foreach (var start in starts)
        {
            if (truncated) break;
            Walk(start);
        }

        return new PathSet(paths, truncated);

        void Walk(string id)
        {
            if (truncated) return;
            stack.Add(id);
            var next = graph.Successors(id);
            if (next.Count == 0)
            {
                if (paths.Count >= MaxPaths) truncated = true;
                else paths.Add(stack.ToArray());
            }
            else
            {
                foreach (var successor in next) Walk(successor);
            }
            stack.RemoveAt(stack.Count - 1);
        }
    }

    /// <summary>
    /// The plan as literal edges: one segment for every step of the map that joins two
    /// selected nodes, less the ones the eraser has cut.
    ///
    /// There is no pathfinding here, and that is the whole point. Its predecessor,
    /// <c>ConnectWaypoints</c>, bridged selected nodes with shortest paths and fell
    /// back to ever-earlier floors when a link was cut. That is the trap worth
    /// remembering: **in a layered map every route between two rows is the same
    /// length**, one edge per row, so "shortest" chooses nothing and a fallback
    /// returns *every* route from that floor at once — erasing one step could summon
    /// a whole sweep of line across the far side of the map. Selecting two adjacent
    /// nodes draws the step between them; selecting two that the map does not join
    /// draws nothing. Nothing appears that the player did not point at.
    /// </summary>
    /// <param name="cut">
    /// Steps the eraser has taken out, as (from, to) in row order. Kept apart from the
    /// selection so rubbing out one link between two nodes leaves both nodes, and
    /// every other link they have, alone.
    /// </param>
    public static IReadOnlyList<IReadOnlyList<string>> ConnectSelected(
        SpireMapGraph graph, string origin, IReadOnlyCollection<string> selected,
        IReadOnlyCollection<(string From, string To)>? cut = null)
    {
        var chosen = selected.Where(graph.Contains).ToHashSet();
        // The player's own position is always part of the plan: the first step should
        // appear without having to select where you already stand.
        if (graph.Contains(origin))
            chosen.Add(origin);
        if (chosen.Count < 2)
            return [];

        var severed = cut as IReadOnlySet<(string From, string To)> ?? cut?.ToHashSet() ?? [];
        var segments = new List<IReadOnlyList<string>>();
        foreach (var from in chosen
            .OrderBy(id => graph.Node(id).Row)
            .ThenBy(id => id, StringComparer.Ordinal))
        {
            foreach (var to in graph.Successors(from))
                if (chosen.Contains(to) && !severed.Contains((from, to)))
                    segments.Add([from, to]);
        }
        return segments;
    }

    /// <summary>
    /// Stitch segments into the complete routes they describe. The segments are the
    /// links; a player looking at them sees whole paths, and the legend should count
    /// what those paths hold, not what each link holds. A plan that forks draws one
    /// route per branch; where two equally short links join the same pair, each
    /// combination is its own route.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> AssembleRoutes(
        IReadOnlyList<IReadOnlyList<string>> segments)
    {
        if (segments.Count == 0)
            return [];

        var bySource = segments
            .GroupBy(segment => segment[0])
            .ToDictionary(group => group.Key, group => group.ToList());
        var reachable = segments.Select(segment => segment[^1]).ToHashSet();
        var roots = segments
            .Select(segment => segment[0])
            .Where(id => !reachable.Contains(id))
            .Distinct()
            .ToList();

        var routes = new List<IReadOnlyList<string>>();
        var walked = new List<string>();
        foreach (var root in roots)
        {
            walked.Clear();
            walked.Add(root);
            Walk(root);
        }
        return routes;

        void Walk(string id)
        {
            if (routes.Count >= MaxPaths)
                return;
            if (!bySource.TryGetValue(id, out var onward))
            {
                routes.Add(walked.ToArray());
                return;
            }
            foreach (var segment in onward)
            {
                var before = walked.Count;
                // Skip(1): the segment starts on the node the walk already stands on.
                walked.AddRange(segment.Skip(1));
                Walk(segment[^1]);
                walked.RemoveRange(before, walked.Count - before);
            }
        }
    }


    /// <summary>
    /// Drop trailing nodes matching <paramref name="trimTail" /> (the boss, where every
    /// route converges anyway), then dedupe: on a double-boss map two routes that differ
    /// only in which boss they end at are the same walk across the grid.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> TrimTails(
        IReadOnlyList<IReadOnlyList<string>> paths, Func<string, bool> trimTail)
    {
        var result = new List<IReadOnlyList<string>>();
        var seen = new HashSet<string>();
        foreach (var path in paths)
        {
            var length = path.Count;
            while (length > 0 && trimTail(path[length - 1]))
                length--;
            if (length == 0)
                continue;
            var trimmed = path.Take(length).ToArray();
            if (seen.Add(string.Join("|", trimmed)))
                result.Add(trimmed);
        }
        return result;
    }
}

public sealed record PathSet(IReadOnlyList<IReadOnlyList<string>> Paths, bool Truncated);

