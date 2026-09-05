using PathingPlus.PathingPlusCode.Pathing;
using Xunit;

namespace PathingPlus.Tests;

public class RoutePlanTests
{
    private static SpireMapGraph Graph() => new(
        [new("start", 0, ""), new("a", 1, ""), new("b", 1, ""),
         new("c", 2, ""), new("d", 2, ""), new("end", 3, "")],
        [("start", "a"), ("start", "b"), ("a", "c"), ("a", "d"),
         ("b", "c"), ("b", "d"), ("c", "end"), ("d", "end")]);

    [Fact]
    public void Keeping_one_suggestion_removes_other_branches_from_the_editable_plan()
    {
        var graph = Graph();
        var pinnable = graph.Nodes.Select(node => node.Id).Where(id => id != "start").ToHashSet();
        IReadOnlyList<string> chosen = ["start", "a", "c", "end"];
        var plan = RoutePlan.FromRoutes(graph, "start", pinnable, [chosen]);
        var rebuilt = PathSolver.AssembleRoutes(PathSolver.ConnectSelected(graph, "start", plan.Pins, plan.Cut));
        Assert.Equal(chosen, Assert.Single(rebuilt));
        Assert.DoesNotContain("b", plan.Pins);
        Assert.DoesNotContain("d", plan.Pins);
        // Redraw/reopen cannot bring back an alternative: only these pins and cuts persist.
        var restored = PathSolver.AssembleRoutes(PathSolver.ConnectSelected(graph, "start", plan.Pins.ToArray(), plan.Cut.ToArray()));
        Assert.Equal(chosen, Assert.Single(restored));
    }

    [Fact]
    public void Multiple_suggestions_do_not_add_cross_connections()
    {
        var graph = Graph();
        IReadOnlyList<string> first = ["start", "a", "c", "end"];
        IReadOnlyList<string> second = ["start", "b", "d", "end"];
        var plan = RoutePlan.FromRoutes(graph, "start", graph.Nodes.Select(node => node.Id).ToHashSet(), [first, second]);
        var rebuilt = PathSolver.AssembleRoutes(PathSolver.ConnectSelected(graph, "start", plan.Pins, plan.Cut));
        Assert.Equal(2, rebuilt.Count);
        Assert.Contains(rebuilt, route => route.SequenceEqual(first));
        Assert.Contains(rebuilt, route => route.SequenceEqual(second));
        Assert.Contains(("a", "d"), plan.Cut);
        Assert.Contains(("b", "c"), plan.Cut);
    }

    [Fact]
    public void A_manually_drawn_route_starting_above_the_player_keeps_its_first_node()
    {
        var graph = Graph();
        IReadOnlyList<string> chosen = ["c", "end"];
        var plan = RoutePlan.FromRoutes(graph, "start", graph.Nodes.Select(node => node.Id).ToHashSet(), [chosen]);
        var rebuilt = PathSolver.AssembleRoutes(PathSolver.ConnectSelected(graph, "start", plan.Pins, plan.Cut));
        Assert.Equal(chosen, Assert.Single(rebuilt));
    }

    [Fact]
    public void The_kept_route_can_be_edited_with_normal_selection_and_erasing()
    {
        var graph = Graph();
        var plan = RoutePlan.FromRoutes(graph, "start", graph.Nodes.Select(node => node.Id).ToHashSet(),
            [["start", "a", "c", "end"]]);
        plan.Pins.Remove("c");
        plan.Pins.Add("d");
        var rebuilt = PathSolver.AssembleRoutes(PathSolver.ConnectSelected(graph, "start", plan.Pins, plan.Cut));
        Assert.Equal(new[] { "start", "a", "d", "end" }, Assert.Single(rebuilt));
    }

    [Fact]
    public void Clearing_unpinned_routes_does_not_reintroduce_combinations_at_shared_junctions()
    {
        var graph = new SpireMapGraph(
            [new("start", 0, ""), new("a", 1, ""), new("b", 1, ""), new("join", 2, ""),
             new("c", 3, ""), new("d", 3, ""), new("end", 4, "")],
            [("start", "a"), ("start", "b"), ("a", "join"), ("b", "join"),
             ("join", "c"), ("join", "d"), ("c", "end"), ("d", "end")]);
        IReadOnlyList<string> first = ["start", "a", "join", "c", "end"];
        IReadOnlyList<string> second = ["start", "b", "join", "d", "end"];
        var plan = RoutePlan.FromRoutes(graph, "start", graph.Nodes.Select(node => node.Id).ToHashSet(), [first, second]);
        var assembled = PathSolver.AssembleRoutes(PathSolver.ConnectSelected(graph, "start", plan.Pins, plan.Cut));
        Assert.Equal(4, assembled.Count);
        var retained = RouteDisplay.MatchRemaining(assembled, [first, second]);
        Assert.Equal(2, retained.Count);
        Assert.Contains(retained, route => route.SequenceEqual(first));
        Assert.Contains(retained, route => route.SequenceEqual(second));

        plan.Pins.Remove("a");
        plan.Pins.Remove("b");
        var afterTravel = PathSolver.AssembleRoutes(PathSolver.ConnectSelected(graph, "join", plan.Pins, plan.Cut));
        Assert.Equal(2, RouteDisplay.MatchRemaining(afterTravel, retained).Count);
    }
}
