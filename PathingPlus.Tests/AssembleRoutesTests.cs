using PathingPlus.PathingPlusCode.Pathing;
using Xunit;

namespace PathingPlus.Tests;

/// <summary>
/// The legend counts routes, so the links between pinned floors have to be stitched
/// back into the whole paths a player sees on the map.
/// </summary>
public class AssembleRoutesTests
{
    [Fact]
    public void Links_in_a_chain_become_one_route()
    {
        var links = new IReadOnlyList<string>[]
        {
            new[] { "here", "a", "mid" },
            new[] { "mid", "b", "top" },
        };

        var routes = PathSolver.AssembleRoutes(links);

        Assert.Single(routes);
        Assert.Equal(new[] { "here", "a", "mid", "b", "top" }, routes[0]);
    }

    [Fact]
    public void A_fork_becomes_one_route_per_branch()
    {
        // The screenshot case: one plan that splits into a left and a right way up.
        var links = new IReadOnlyList<string>[]
        {
            new[] { "here", "left1", "leftTop" },
            new[] { "here", "right1", "rightTop" },
        };

        var routes = PathSolver.AssembleRoutes(links);

        Assert.Equal(2, routes.Count);
        Assert.Contains(routes, r => r.Contains("leftTop"));
        Assert.Contains(routes, r => r.Contains("rightTop"));
    }

    [Fact]
    public void Two_ways_between_the_same_pair_make_two_routes()
    {
        var links = new IReadOnlyList<string>[]
        {
            new[] { "here", "viaX", "mid" },
            new[] { "here", "viaY", "mid" },
            new[] { "mid", "top" },
        };

        var routes = PathSolver.AssembleRoutes(links);

        Assert.Equal(2, routes.Count);
        Assert.All(routes, r => Assert.Equal("top", r[^1]));
        Assert.Contains(routes, r => r.Contains("viaX"));
        Assert.Contains(routes, r => r.Contains("viaY"));
    }

    [Fact]
    public void A_route_is_not_repeated_for_each_of_its_links()
    {
        var links = new IReadOnlyList<string>[] { new[] { "here", "only" } };

        Assert.Single(PathSolver.AssembleRoutes(links));
    }

    [Fact]
    public void No_links_assemble_into_no_routes()
    {
        Assert.Empty(PathSolver.AssembleRoutes([]));
    }
}
