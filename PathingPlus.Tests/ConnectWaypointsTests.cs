using PathingPlus.PathingPlusCode.Pathing;
using Xunit;

namespace PathingPlus.Tests;

/// <summary>
/// Manual planning draws segments between waypoints, so pins on rival branches each
/// draw what they can instead of cancelling one another out.
/// </summary>
public class ConnectWaypointsTests
{
    //   row 3:      top
    //              /   \
    //   row 2:   left   right
    //              \   /
    //   row 1:      mid
    //               |
    //   row 0:     here
    private static SpireMapGraph Graph() => new(
        new GraphNode[]
        {
            new("here", 0, "monster"),
            new("mid", 1, "event"),
            new("left", 2, "elite"), new("right", 2, "shop"),
            new("top", 3, "rest"),
        },
        new[]
        {
            ("here", "mid"),
            ("mid", "left"), ("mid", "right"),
            ("left", "top"), ("right", "top"),
        });

    [Fact]
    public void One_pin_draws_the_leg_from_where_the_player_stands()
    {
        var segments = PathSolver.ConnectWaypoints(Graph(), "here", new[] { "left" });

        Assert.Single(segments);
        Assert.Equal(new[] { "here", "mid", "left" }, segments[0]);
    }

    [Fact]
    public void Pins_on_rival_branches_each_draw_their_own_segment()
    {
        // The case that used to draw nothing: no single route holds both.
        var segments = PathSolver.ConnectWaypoints(Graph(), "here", new[] { "left", "right" });

        Assert.Equal(2, segments.Count);
        Assert.Contains(segments, s => s.SequenceEqual(new[] { "here", "mid", "left" }));
        Assert.Contains(segments, s => s.SequenceEqual(new[] { "here", "mid", "right" }));
    }

    [Fact]
    public void A_pin_between_two_others_splits_the_line_at_it()
    {
        var segments = PathSolver.ConnectWaypoints(Graph(), "here", new[] { "mid", "left" });

        // here→mid and mid→left, and no redundant here→left drawn over them.
        Assert.Equal(2, segments.Count);
        Assert.Contains(segments, s => s.SequenceEqual(new[] { "here", "mid" }));
        Assert.Contains(segments, s => s.SequenceEqual(new[] { "mid", "left" }));
    }

    [Fact]
    public void Every_way_between_one_pair_is_offered()
    {
        var segments = PathSolver.ConnectWaypoints(Graph(), "mid", new[] { "top" });

        Assert.Equal(2, segments.Count);
        Assert.All(segments, s => Assert.Equal("mid", s[0]));
        Assert.All(segments, s => Assert.Equal("top", s[^1]));
    }

    [Fact]
    public void Segments_always_run_forward_along_the_map()
    {
        var graph = Graph();

        var segments = PathSolver.ConnectWaypoints(graph, "here", new[] { "mid", "left", "top" });

        Assert.NotEmpty(segments);
        Assert.All(segments, segment =>
            Assert.True(graph.Node(segment[0]).Row < graph.Node(segment[^1]).Row,
                $"{segment[0]} -> {segment[^1]} does not advance"));
    }

    [Fact]
    public void No_pins_draws_nothing()
    {
        Assert.Empty(PathSolver.ConnectWaypoints(Graph(), "here", Array.Empty<string>()));
    }

    [Fact]
    public void An_erased_node_is_not_routed_through()
    {
        // The point of blocking. Pin "top" with "left" erased and the line must take
        // the right-hand way; without it the solver picks the shortest link, which
        // runs straight back through the node just rubbed out.
        var segments = PathSolver.ConnectWaypoints(
            Graph(), "here", new[] { "top" }, new[] { "left" });

        Assert.NotEmpty(segments);
        Assert.All(segments, s => Assert.DoesNotContain("left", s));
        Assert.Contains(segments, s => s.SequenceEqual(new[] { "here", "mid", "right", "top" }));
    }

    [Fact]
    public void An_erased_node_cannot_be_a_waypoint()
    {
        var segments = PathSolver.ConnectWaypoints(
            Graph(), "here", new[] { "left" }, new[] { "left" });

        Assert.Empty(segments);
    }

    [Fact]
    public void Erasing_the_only_way_through_leaves_the_pin_unlinked()
    {
        // "mid" is the sole route out of "here", so blocking it cannot be routed
        // around — the plan loses that leg rather than quietly ignoring the erase.
        var segments = PathSolver.ConnectWaypoints(
            Graph(), "here", new[] { "top" }, new[] { "mid" });

        Assert.Empty(segments);
    }

    [Fact]
    public void The_long_way_round_is_culled_in_favour_of_the_direct_one()
    {
        // "here" reaches "goal" directly, or by a four-hop detour out to the side —
        // the map's edge columns, which used to draw over the intended line.
        var graph = new SpireMapGraph(
            new GraphNode[]
            {
                new("here", 0, "monster"),
                new("direct", 1, "event"), new("far1", 1, "monster"),
                new("far2", 2, "monster"), new("far3", 3, "monster"),
                new("goal", 4, "elite"),
            },
            new[]
            {
                ("here", "direct"), ("direct", "goal"),
                ("here", "far1"), ("far1", "far2"), ("far2", "far3"), ("far3", "goal"),
            });

        var segments = PathSolver.ConnectWaypoints(graph, "here", new[] { "goal" });

        Assert.Single(segments);
        Assert.Equal(new[] { "here", "direct", "goal" }, segments[0]);
    }

    [Fact]
    public void A_detour_that_dodges_every_pin_is_not_a_connection()
    {
        // The real regression: the right-hand column reaches "top" without touching
        // "mid", so it used to be the only path of its pair and survived any
        // shortest-per-pair rule. Only consecutive pinned floors may link.
        var graph = new SpireMapGraph(
            new GraphNode[]
            {
                new("here", 0, "monster"),
                new("m1", 1, "monster"), new("r1", 1, "monster"),
                new("mid", 2, "elite"), new("r2", 2, "monster"),
                new("m3", 3, "monster"), new("r3", 3, "monster"),
                new("top", 4, "rest"),
            },
            new[]
            {
                ("here", "m1"), ("m1", "mid"), ("mid", "m3"), ("m3", "top"),
                ("here", "r1"), ("r1", "r2"), ("r2", "r3"), ("r3", "top"),
            });

        var segments = PathSolver.ConnectWaypoints(graph, "here", new[] { "mid", "top" });

        Assert.Equal(2, segments.Count);
        Assert.Contains(segments, s => s.SequenceEqual(new[] { "here", "m1", "mid" }));
        Assert.Contains(segments, s => s.SequenceEqual(new[] { "mid", "m3", "top" }));
        Assert.DoesNotContain(segments, s => s.Contains("r1") || s.Contains("r2"));
    }

    [Fact]
    public void Equally_short_ways_between_one_pair_both_survive()
    {
        var segments = PathSolver.ConnectWaypoints(Graph(), "mid", new[] { "top" });

        Assert.Equal(2, segments.Count);
        Assert.All(segments, s => Assert.Equal(3, s.Count));
    }
}
