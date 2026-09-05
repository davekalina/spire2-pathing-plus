namespace PathingPlus.PathingPlusCode.Pathing;

/// <summary>Pages of complete routes, retaining every pinned route independently.</summary>
public sealed record RouteDisplay(
    IReadOnlyList<IReadOnlyList<string>> Shown,
    IReadOnlyList<IReadOnlyList<string>> Backdrop,
    IReadOnlyList<IReadOnlyList<string>> Pinned,
    IReadOnlyList<string>? Selected,
    int Page,
    int PageCount)
{
    public static RouteDisplay Arrange(
        IReadOnlyList<IReadOnlyList<string>> ranked,
        IReadOnlyList<IReadOnlyList<string>> pinned,
        IReadOnlyList<string>? selected,
        int page)
    {
        // Search the entire plan before applying the display limit. A pin must not
        // disappear just because a new, higher-scoring route took its column.
        pinned = MatchRemaining(ranked, pinned);
        selected = FindRemaining(ranked, selected);
        // Reserve the pins while there is room to browse alternatives. Once pins
        // fill a page, page the entire set with pins first; no artificial pin limit.
        var reserve = pinned.Count < PathSolver.LegendThreshold;
        var pinnedSet = pinned.ToHashSet();
        var others = reserve
            ? ranked.Where(route => !pinnedSet.Contains(route)).ToList()
            : ranked.OrderByDescending(pinnedSet.Contains).ToList();
        var capacity = PathSolver.LegendThreshold - (reserve ? pinned.Count : 0);
        var pageCount = Math.Max(1, (others.Count + capacity - 1) / capacity);
        page = Math.Clamp(page, 0, pageCount - 1);
        if (selected is not null && others.Contains(selected))
            page = others.IndexOf(selected) / capacity;
        var visible = others.Skip(page * capacity).Take(capacity).ToHashSet();
        if (reserve)
            visible.UnionWith(pinned);
        return new RouteDisplay(
            ranked.Where(visible.Contains).ToList(),
            ranked.Where(route => !visible.Contains(route)).ToList(),
            pinned, selected, page, pageCount);
    }

    /// <summary>Keep surviving tails and coalesce pins that converge after travel.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> MatchRemaining(
        IReadOnlyList<IReadOnlyList<string>> routes,
        IReadOnlyList<IReadOnlyList<string>> stored) =>
        stored.Select(route => FindRemaining(routes, route)).OfType<IReadOnlyList<string>>().Distinct().ToList();

    private static IReadOnlyList<string>? FindRemaining(
        IReadOnlyList<IReadOnlyList<string>> routes, IReadOnlyList<string>? stored) =>
        stored is null ? null : routes.FirstOrDefault(route =>
            route.Count <= stored.Count &&
            route.SequenceEqual(stored.Skip(stored.Count - route.Count)));
}
