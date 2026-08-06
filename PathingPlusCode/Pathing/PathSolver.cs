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
