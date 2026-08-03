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
    public void One_waypoint_keeps_only_routes_through_it()
    {
        var all = PathSolver.EnumeratePaths(Graph(), new[] { "a1", "a2" }).Paths;

        var filtered = PathSolver.Filter(all, new[] { "c1" });

        Assert.Equal(2, filtered.Count);
        Assert.All(filtered, p => Assert.Contains("c1", p));
    }

    [Fact]
    public void Stacked_waypoints_intersect()
    {
        var all = PathSolver.EnumeratePaths(Graph(), new[] { "a1", "a2" }).Paths;

        var filtered = PathSolver.Filter(all, new[] { "b2", "c1" });

        Assert.Single(filtered);
        Assert.Equal(new[] { "a2", "b2", "c1", "boss" }, filtered[0]);
    }

    [Fact]
    public void No_waypoints_returns_the_input_unchanged()
    {
        var all = PathSolver.EnumeratePaths(Graph(), new[] { "a1", "a2" }).Paths;

        Assert.Same(all, PathSolver.Filter(all, Array.Empty<string>()));
    }

    [Fact]
    public void Unsatisfiable_waypoints_yield_no_routes()
    {
        var all = PathSolver.EnumeratePaths(Graph(), new[] { "a1", "a2" }).Paths;

        Assert.Empty(PathSolver.Filter(all, new[] { "b1", "c2" }));
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
