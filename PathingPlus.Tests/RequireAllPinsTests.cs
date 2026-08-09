using PathingPlus.PathingPlusCode.Pathing;
using Xunit;

namespace PathingPlus.Tests;

/// <summary>
/// Manual planning is connect-the-dots: pinning a second node further along must
/// narrow the drawing to routes through both, not re-open every other way of
/// reaching the second one.
/// </summary>
public class RequireAllPinsTests
{
    private static readonly IReadOnlyList<string>[] Routes =
    [
        new[] { "start", "a1", "b1", "goal" },
        new[] { "start", "a2", "b1", "goal" },
        new[] { "start", "a2", "b2", "goal" },
    ];

    [Fact]
    public void One_pin_keeps_every_way_of_reaching_it()
    {
        var kept = PathSolver.RequireAllPins(Routes, new[] { "b1" });

        Assert.Equal(2, kept.Count);
    }

    [Fact]
    public void A_second_pin_narrows_rather_than_widens()
    {
        // Pinning a1 then b1 must leave only the route through both — the a2 way of
        // reaching b1 is a different plan, not an alternative to this one.
        var kept = PathSolver.RequireAllPins(Routes, new[] { "a1", "b1" });

        Assert.Single(kept);
        Assert.Equal(new[] { "start", "a1", "b1", "goal" }, kept[0]);
    }

    [Fact]
    public void Pins_no_single_route_can_satisfy_yield_nothing()
    {
        Assert.Empty(PathSolver.RequireAllPins(Routes, new[] { "a1", "b2" }));
    }

    [Fact]
    public void No_pins_returns_the_input_unchanged()
    {
        Assert.Same(Routes, PathSolver.RequireAllPins(Routes, Array.Empty<string>()));
    }

    [Fact]
    public void Required_pins_then_truncation_draws_only_the_planned_stretch()
    {
        var kept = PathSolver.RequireAllPins(Routes, new[] { "a1", "b1" });

        var drawn = PathSolver.TruncateAtPins(kept, id => id is "a1" or "b1");

        Assert.Single(drawn);
        Assert.Equal(new[] { "start", "a1", "b1" }, drawn[0]);
    }
}
