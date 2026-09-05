using PathingPlus.PathingPlusCode.Pathing;
using Xunit;

namespace PathingPlus.Tests;

public class RouteDisplayTests
{
    private static IReadOnlyList<IReadOnlyList<string>> Routes(int count) =>
        Enumerable.Range(0, count).Select(i => (IReadOnlyList<string>)new[] { "start", $"branch{i}", "end" }).ToList();

    [Fact]
    public void Selecting_a_route_outside_the_first_five_gives_it_a_stable_column()
    {
        var routes = Routes(12);
        var display = RouteDisplay.Arrange(routes, [], routes[9], 0);
        Assert.Equal(1, display.Page);
        Assert.Contains(routes[9], display.Shown);
        Assert.DoesNotContain(routes[9], display.Backdrop);
        // A redraw with fresh route objects preserves the explicit selection.
        var refreshed = RouteDisplay.Arrange(Routes(12), [], display.Selected, display.Page);
        Assert.Equal(routes[9], refreshed.Selected);
        Assert.Contains(refreshed.Selected!, refreshed.Shown);
    }

    [Fact]
    public void Every_complete_route_can_be_reached_through_pages()
    {
        var routes = Routes(17);
        var seen = new HashSet<IReadOnlyList<string>>();
        var first = RouteDisplay.Arrange(routes, [], null, 0);
        for (var page = 0; page < first.PageCount; page++)
        {
            var display = RouteDisplay.Arrange(routes, [], null, page);
            Assert.InRange(display.Shown.Count, 1, PathSolver.LegendThreshold);
            seen.UnionWith(display.Shown);
        }
        Assert.Equal(routes.Count, seen.Count);
    }

    [Fact]
    public void A_low_ranked_pin_stays_visible_on_every_page()
    {
        var routes = Routes(18);
        var locked = routes[^1];
        var first = RouteDisplay.Arrange(routes, [locked], null, 0);
        var seen = new HashSet<IReadOnlyList<string>>();
        for (var page = 0; page < first.PageCount; page++)
        {
            var display = RouteDisplay.Arrange(routes, [locked], null, page);
            Assert.Contains(locked, display.Shown);
            Assert.InRange(display.Shown.Count, 1, PathSolver.LegendThreshold);
            Assert.DoesNotContain(locked, display.Backdrop);
            seen.UnionWith(display.Shown);
        }
        Assert.Equal(routes.Count, seen.Count);
    }

    [Fact]
    public void The_selected_candidate_and_existing_pin_are_both_available()
    {
        var routes = Routes(12);
        var display = RouteDisplay.Arrange(routes, [routes[0]], routes[10], 0);
        Assert.Contains(routes[0], display.Shown);
        Assert.Contains(routes[10], display.Shown);
        Assert.Equal(4, display.Shown.Count);
        Assert.Equal(2, display.Page);
    }

    [Fact]
    public void Travel_retains_the_remaining_tail_even_below_the_first_five()
    {
        var routes = Routes(12);
        var tails = routes.Select(route => (IReadOnlyList<string>)route.Skip(1).ToArray()).ToList();
        var display = RouteDisplay.Arrange(tails, [routes[10]], routes[11], 0);
        Assert.Equal(tails[10], Assert.Single(display.Pinned));
        Assert.Equal(tails[11], display.Selected);
        Assert.Contains(tails[10], display.Shown);
        Assert.Contains(tails[11], display.Shown);
    }

    [Fact]
    public void Removing_the_chosen_route_clears_its_pin_and_selection()
    {
        var routes = Routes(12);
        var display = RouteDisplay.Arrange(routes.Take(5).ToList(), [routes[10]], routes[11], 2);
        Assert.Empty(display.Pinned);
        Assert.Null(display.Selected);
        Assert.Equal(0, display.Page);
        Assert.Equal(1, display.PageCount);
    }

    [Fact]
    public void A_pin_survives_a_change_in_ranking()
    {
        var routes = Routes(12);
        var display = RouteDisplay.Arrange(routes.Reverse().ToList(), [routes[0].ToArray()], null, 0);
        Assert.Equal(routes[0], Assert.Single(display.Pinned));
        Assert.Contains(display.Pinned[0], display.Shown);
    }

    [Fact]
    public void Keeping_a_single_route_leaves_one_page_and_no_backdrop()
    {
        var route = Routes(1)[0];
        var display = RouteDisplay.Arrange([route], [route], null, 3);
        Assert.Single(display.Shown);
        Assert.Empty(display.Backdrop);
        Assert.Equal(0, display.Page);
        Assert.Equal(1, display.PageCount);
    }

    [Fact]
    public void Empty_plan_clears_all_route_references()
    {
        var route = Routes(1)[0];
        var display = RouteDisplay.Arrange([], [route], route, 2);
        Assert.Empty(display.Shown);
        Assert.Empty(display.Backdrop);
        Assert.Empty(display.Pinned);
        Assert.Null(display.Selected);
        Assert.Equal(0, display.Page);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(12)]
    public void Multiple_pins_survive_paging_without_blocking_access_to_other_routes(int pinCount)
    {
        var routes = Routes(17);
        var pins = routes.TakeLast(pinCount).ToList();
        var first = RouteDisplay.Arrange(routes, pins, null, 0);
        var seen = new HashSet<IReadOnlyList<string>>();
        for (var page = 0; page < first.PageCount; page++)
        {
            var display = RouteDisplay.Arrange(routes, pins, null, page);
            Assert.Equal(pinCount, display.Pinned.Count);
            Assert.InRange(display.Shown.Count, 1, PathSolver.LegendThreshold);
            if (pinCount < PathSolver.LegendThreshold)
                Assert.All(pins, pin => Assert.Contains(pin, display.Shown));
            seen.UnionWith(display.Shown);
        }
        Assert.Equal(routes.Count, seen.Count);
    }

    [Fact]
    public void A_selected_alternative_is_reachable_even_when_pins_fill_a_page()
    {
        var routes = Routes(12);
        var display = RouteDisplay.Arrange(routes, routes.Take(7).ToList(), routes[11], 0);
        Assert.Equal(7, display.Pinned.Count);
        Assert.Contains(routes[11], display.Shown);
        Assert.Equal(2, display.Page);
    }

    [Fact]
    public void Removing_one_pinned_route_does_not_remove_other_pins()
    {
        var routes = Routes(12);
        var remaining = routes.Where(route => route != routes[10]).ToList();
        var display = RouteDisplay.Arrange(remaining, [routes[0], routes[10], routes[11]], null, 0);
        Assert.Equal(2, display.Pinned.Count);
        Assert.Contains(routes[0], display.Pinned);
        Assert.Contains(routes[11], display.Pinned);
    }

    [Fact]
    public void Pins_that_share_the_same_remaining_tail_merge_after_travel()
    {
        IReadOnlyList<string> first = ["start", "a", "join", "end"];
        IReadOnlyList<string> second = ["start", "b", "join", "end"];
        IReadOnlyList<string> tail = ["join", "end"];
        var display = RouteDisplay.Arrange([tail], [first, second], null, 0);
        Assert.Equal(tail, Assert.Single(display.Pinned));
    }
}
