using PathingPlus.PathingPlusCode.Pathing;
using Xunit;

namespace PathingPlus.Tests;

/// <summary>
/// The plan is the steps between selected neighbours and nothing else. These tests
/// exist mostly to pin down what the model refuses to do: no bridging, no rerouting,
/// no path appearing that the player did not point at.
/// </summary>
public class ConnectSelectedTests
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
    public void The_step_out_of_the_players_own_node_needs_no_selecting()
    {
        var segments = PathSolver.ConnectSelected(Graph(), "here", new[] { "mid" });

        Assert.Single(segments);
        Assert.Equal(new[] { "here", "mid" }, segments[0]);
    }

    [Fact]
    public void A_gap_in_the_selection_is_left_as_a_gap()
    {
        // "left" is two steps from "here" and "mid" is not selected. The old model
        // bridged this; this one draws nothing, because nothing was pointed at.
        var segments = PathSolver.ConnectSelected(Graph(), "here", new[] { "left" });

        Assert.Empty(segments);
    }

    [Fact]
    public void Selecting_both_ends_of_a_step_draws_that_step()
    {
        var segments = PathSolver.ConnectSelected(Graph(), "here", new[] { "mid", "left", "top" });

        Assert.Equal(
            new[] { new[] { "here", "mid" }, new[] { "mid", "left" }, new[] { "left", "top" } },
            segments);
    }

    [Fact]
    public void A_fork_draws_both_prongs()
    {
        var segments = PathSolver.ConnectSelected(
            Graph(), "here", new[] { "mid", "left", "right" });

        Assert.Equal(3, segments.Count);
        Assert.Contains(segments, s => s[0] == "mid" && s[1] == "left");
        Assert.Contains(segments, s => s[0] == "mid" && s[1] == "right");
    }

    [Fact]
    public void Cutting_one_step_leaves_every_other_step_alone()
    {
        var segments = PathSolver.ConnectSelected(
            Graph(), "here",
            new[] { "mid", "left", "right", "top" },
            new[] { ("mid", "left") });

        Assert.DoesNotContain(segments, s => s[0] == "mid" && s[1] == "left");
        // "left" keeps its own selection and its other link; only the cut step is gone.
        Assert.Contains(segments, s => s[0] == "left" && s[1] == "top");
        Assert.Contains(segments, s => s[0] == "mid" && s[1] == "right");
    }

    [Fact]
    public void Two_selected_nodes_the_map_does_not_join_draw_nothing()
    {
        var segments = PathSolver.ConnectSelected(Graph(), "here", new[] { "left", "right" });

        Assert.Empty(segments);
    }

    [Fact]
    public void Selected_steps_assemble_into_whole_routes()
    {
        var segments = PathSolver.ConnectSelected(
            Graph(), "here", new[] { "mid", "left", "right", "top" });
        var routes = PathSolver.AssembleRoutes(segments);

        Assert.Equal(2, routes.Count);
        Assert.Contains(routes, r => r.SequenceEqual(new[] { "here", "mid", "left", "top" }));
        Assert.Contains(routes, r => r.SequenceEqual(new[] { "here", "mid", "right", "top" }));
    }
}
