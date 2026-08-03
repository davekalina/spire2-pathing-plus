using PathingPlus.PathingPlusCode.Pathing;
using Xunit;

namespace PathingPlus.Tests;

public class PathSolverTests
{
    // A small act map:
    //
    //   row 3:        boss
    //                /    \
    //   row 2:     c1      c2
    //             /  \    /
    //   row 1:   b1    b2
    //            |    /  \
    //   row 0:   a1  a2   (a2 -> b2 only)
    //
    // Complete routes from {a1, a2}:
    //   a1 b1 c1 boss
    //   a2 b2 c1 boss
    //   a2 b2 c2 boss
    private static SpireMapGraph Graph() => new(
        new GraphNode[]
        {
            new("a1", 0, "monster"), new("a2", 0, "monster"),
            new("b1", 1, "event"), new("b2", 1, "elite"),
            new("c1", 2, "rest"), new("c2", 2, "shop"),
            new("boss", 3, "boss"),
        },
        new[]
        {
            ("a1", "b1"), ("a2", "b2"),
            ("b1", "c1"), ("b2", "c1"), ("b2", "c2"),
            ("c1", "boss"), ("c2", "boss"),
        });

    [Fact]
    public void Enumerates_every_complete_route()
    {
        var set = PathSolver.EnumeratePaths(Graph(), new[] { "a1", "a2" });

        Assert.False(set.Truncated);
        Assert.Equal(3, set.Paths.Count);
        Assert.Contains(set.Paths, p => p.SequenceEqual(new[] { "a1", "b1", "c1", "boss" }));
        Assert.Contains(set.Paths, p => p.SequenceEqual(new[] { "a2", "b2", "c1", "boss" }));
        Assert.Contains(set.Paths, p => p.SequenceEqual(new[] { "a2", "b2", "c2", "boss" }));
    }

    [Fact]
    public void Starting_mid_map_only_walks_forward()
    {
        var set = PathSolver.EnumeratePaths(Graph(), new[] { "b2" });

        Assert.Equal(2, set.Paths.Count);
        Assert.All(set.Paths, p => Assert.Equal("b2", p[0]));
    }

    [Fact]
    public void Unknown_start_ids_are_ignored()
    {
        var set = PathSolver.EnumeratePaths(Graph(), new[] { "nope", "a1" });

        Assert.Single(set.Paths);
    }

    [Fact]
    public void No_pins_shows_every_route_in_one_tier()
    {
        var all = PathSolver.EnumeratePaths(Graph(), new[] { "a1", "a2" }).Paths;

        var match = PathSolver.MatchByPins(all, Array.Empty<string>(), 5);

        Assert.Equal(3, match.Shown.Count);
        Assert.Equal(0, match.MaxHits);
        Assert.All(match.Shown, s => Assert.Equal(0, s.Hits));
    }

    [Fact]
    public void Full_coverage_route_comes_first_with_near_misses_behind()
    {
        var all = PathSolver.EnumeratePaths(Graph(), new[] { "a1", "a2" }).Paths;

        // a2-b2-c1 hits both pins; the other two routes hit one each and fit the
        // legend, so they follow as near-misses.
        var match = PathSolver.MatchByPins(all, new[] { "b2", "c1" }, 5);

        Assert.Equal(3, match.Shown.Count);
        Assert.Equal(2, match.MaxHits);
        Assert.Equal(1, match.CountAtMax);
        Assert.Equal(new[] { "a2", "b2", "c1", "boss" }, match.Shown[0].Path);
        Assert.Equal(2, match.Shown[0].Hits);
        Assert.All(match.Shown.Skip(1), s => Assert.Equal(1, s.Hits));
    }

    [Fact]
    public void Conflicting_pins_degrade_to_best_coverage_instead_of_nothing()
    {
        var all = PathSolver.EnumeratePaths(Graph(), new[] { "a1", "a2" }).Paths;

        // No route holds both b1 and c2; each candidate stays visible at 1/2.
        var match = PathSolver.MatchByPins(all, new[] { "b1", "c2" }, 5);

        Assert.Equal(1, match.MaxHits);
        Assert.Equal(2, match.CountAtMax);
        Assert.Equal(2, match.Shown.Count);
    }

    [Fact]
    public void Routes_hitting_no_pin_are_never_shown()
    {
        var all = PathSolver.EnumeratePaths(Graph(), new[] { "a1", "a2" }).Paths;

        var match = PathSolver.MatchByPins(all, new[] { "b1" }, 5);

        Assert.Single(match.Shown);
        Assert.Contains("b1", match.Shown[0].Path);
    }

    [Fact]
    public void A_lower_tier_that_does_not_fit_the_legend_is_left_out_whole()
    {
        // One full match plus six 1-hit near-misses.
        var paths = new IReadOnlyList<string>[]
        {
            new[] { "p1", "p2", "top" },
            new[] { "p1", "n1" }, new[] { "p1", "n2" }, new[] { "p1", "n3" },
            new[] { "p1", "n4" }, new[] { "p1", "n5" }, new[] { "p1", "n6" },
        };

        var match = PathSolver.MatchByPins(paths, new[] { "p1", "p2" }, 5);

        // The 1-hit tier holds six routes; 1 + 6 > 5, so only the full match shows.
        Assert.Single(match.Shown);
        Assert.Equal(2, match.Shown[0].Hits);
    }

    [Fact]
    public void Tiers_more_than_two_below_the_best_are_never_shown()
    {
        var paths = new IReadOnlyList<string>[]
        {
            new[] { "p1", "p2", "p3", "p4" },
            new[] { "p1", "x" },
        };

        var match = PathSolver.MatchByPins(paths, new[] { "p1", "p2", "p3", "p4" }, 5);

        // The second route hits 1 of 4 — three below the best; tolerance is two.
        Assert.Single(match.Shown);
        Assert.Equal(4, match.Shown[0].Hits);
    }

    [Fact]
    public void Wide_map_hits_the_cap_instead_of_hanging()
    {
        // 2 choices per row across 15 rows: 32768 routes, beyond MaxPaths.
        var nodes = new List<GraphNode> { new("start", 0, "monster") };
        var edges = new List<(string, string)>();
        var previous = new List<string> { "start" };
        for (var row = 1; row <= 15; row++)
        {
            var current = new List<string> { $"L{row}", $"R{row}" };
            nodes.AddRange(current.Select(id => new GraphNode(id, row, "monster")));
            edges.AddRange(previous.SelectMany(p => current.Select(c => (p, c))));
            previous = current;
        }

        var set = PathSolver.EnumeratePaths(new SpireMapGraph(nodes, edges), new[] { "start" });

        Assert.True(set.Truncated);
        Assert.Equal(PathSolver.MaxPaths, set.Paths.Count);
    }
}
