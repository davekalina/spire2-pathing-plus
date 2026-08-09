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

    /// <summary>
    /// With no more surviving routes than this, the best <see cref="LegendThreshold" />
    /// of them are picked for display; above it the union view takes over.
    /// </summary>
    public const int BestPickPool = 10;

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
    /// Manual planning, drawn the way a player draws it: a line between each pinned
    /// node and the next one along, for every pair that a path actually connects.
    /// The player's current position counts as a waypoint too, so the first leg
    /// appears without pinning where you already stand.
    ///
    /// Walking forward from each waypoint and stopping at the first waypoint reached
    /// gives exactly the segments between *adjacent* waypoints: a plan of A → B → C
    /// draws A→B and B→C rather than also drawing the redundant A→C on top of them.
    /// Where several ways connect one pair, all of them are returned — that is the
    /// auto-pathing between placed nodes. Waypoints nothing connects simply
    /// contribute no segment, which is what lets pins sit on rival branches.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> ConnectWaypoints(
        SpireMapGraph graph, string origin, IReadOnlyCollection<string> pins)
    {
        var waypoints = pins.Where(graph.Contains).ToHashSet();
        if (graph.Contains(origin))
            waypoints.Add(origin);
        if (waypoints.Count < 2)
            return [];

        // Waypoints grouped by floor, floors in order. Connecting only *consecutive*
        // occupied floors is what keeps the drawing to the plan: a route that dodges
        // every pin in between — up an edge column and back — links two floors that
        // are not neighbours in the plan, so it is never a candidate. Judging by
        // "first waypoint reached" instead let those detours in, because they are the
        // only way to reach a far pin without crossing a nearer one, leaving nothing
        // shorter to compare them against.
        var floors = waypoints
            .GroupBy(id => graph.Node(id).Row)
            .OrderBy(floor => floor.Key)
            .Select(floor => floor.OrderBy(id => id, StringComparer.Ordinal).ToList())
            .ToList();

        var segments = new List<IReadOnlyList<string>>();
        for (var i = 1; i < floors.Count && segments.Count < MaxPaths; i++)
        {
            foreach (var to in floors[i])
            {
                // Normally the floor just below, but a pin on a branch that one
                // cannot reach falls back to the nearest earlier floor that can —
                // better a longer link than a pin left dangling.
                for (var j = i - 1; j >= 0; j--)
                {
                    var linked = false;
                    foreach (var from in floors[j])
                    {
                        var shortest = ShortestPaths(graph, from, to);
                        if (shortest.Count == 0)
                            continue;
                        segments.AddRange(shortest);
                        linked = true;
                    }
                    if (linked)
                        break;
                }
            }
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
    /// Every shortest path from one node to another, or none if it cannot be reached.
    /// Ties are all returned: two equally short ways are a real choice, not clutter.
    /// </summary>
    private static List<IReadOnlyList<string>> ShortestPaths(
        SpireMapGraph graph, string from, string to)
    {
        var distance = new Dictionary<string, int> { [from] = 0 };
        var queue = new Queue<string>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            foreach (var next in graph.Successors(id))
            {
                if (distance.ContainsKey(next))
                    continue;
                distance[next] = distance[id] + 1;
                queue.Enqueue(next);
            }
        }
        if (!distance.TryGetValue(to, out var hops))
            return [];

        var paths = new List<IReadOnlyList<string>>();
        var stack = new List<string> { from };
        Walk(from);
        return paths;

        // Only successors whose distance is one deeper stay on a shortest path.
        void Walk(string id)
        {
            if (id == to)
            {
                paths.Add(stack.ToArray());
                return;
            }
            if (stack.Count > hops)
                return;
            foreach (var next in graph.Successors(id))
            {
                if (!distance.TryGetValue(next, out var d) || d != stack.Count)
                    continue;
                stack.Add(next);
                Walk(next);
                stack.RemoveAt(stack.Count - 1);
            }
        }
    }

    /// <summary>
    /// How far below the best pin coverage a route may fall and still be offered as a
    /// near-miss alternative.
    /// </summary>
    public const int NearMissTolerance = 2;

    /// <summary>
    /// Best-match pin filtering. Routes are scored by how many pins they visit; the
    /// best-scoring tier is always shown in full (ALL when a route hits every pin,
    /// the best achievable coverage when the pins conflict — never an empty result).
    /// Lower tiers, down to <see cref="NearMissTolerance" /> below the best, are
    /// appended one whole tier at a time while everything still fits the legend, so
    /// near-miss alternatives appear without an arbitrary subset of them. A route
    /// that visits no pin at all is never shown while pins exist. With no pins,
    /// every route is one tier and all are shown.
    /// </summary>
    public static PinMatch MatchByPins(
        IReadOnlyList<IReadOnlyList<string>> paths,
        IReadOnlyCollection<string> pins,
        int legendLimit)
    {
        var scored = paths
            .Select(path => (Path: path, Hits: pins.Count == 0 ? 0 : pins.Count(path.Contains)))
            .ToList();
        var maxHits = scored.Count == 0 ? 0 : scored.Max(s => s.Hits);
        var countAtMax = scored.Count(s => s.Hits == maxHits);

        var shown = new List<(IReadOnlyList<string> Path, int Hits)>();
        var minTier = pins.Count == 0 ? 0 : Math.Max(1, maxHits - NearMissTolerance);
        for (var tier = maxHits; tier >= minTier; tier--)
        {
            var tierPaths = scored.Where(s => s.Hits == tier).ToList();
            if (tierPaths.Count == 0)
                continue;
            if (tier != maxHits && shown.Count + tierPaths.Count > legendLimit)
                break;
            shown.AddRange(tierPaths.Select(s => (s.Path, s.Hits)));
            if (shown.Count >= legendLimit)
                break;
        }
        return new PinMatch(shown, maxHits, countAtMax);
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

/// <param name="Shown">Routes to display, best pin coverage first, stable order within a tier.</param>
/// <param name="MaxHits">The best pin coverage any route achieves.</param>
/// <param name="CountAtMax">How many routes achieve it.</param>
public sealed record PinMatch(
    IReadOnlyList<(IReadOnlyList<string> Path, int Hits)> Shown,
    int MaxHits,
    int CountAtMax);
