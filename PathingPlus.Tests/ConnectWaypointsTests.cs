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
}
