using PathingPlus.PathingPlusCode.Pathing;
using Xunit;

namespace PathingPlus.Tests;

public class TruncateAtPinsTests
{
    [Fact]
    public void Route_is_cut_at_its_deepest_pin()
    {
        var paths = new IReadOnlyList<string>[] { new[] { "a", "b", "c", "d" } };

        var cut = PathSolver.TruncateAtPins(paths, id => id is "b" or "c");

        Assert.Single(cut);
        Assert.Equal(new[] { "a", "b", "c" }, cut[0]);
    }

    [Fact]
    public void Routes_reaching_no_pin_are_dropped()
    {
        var paths = new IReadOnlyList<string>[]
        {
            new[] { "a", "b" },
            new[] { "a", "x" },
        };

        var cut = PathSolver.TruncateAtPins(paths, id => id == "b");

        Assert.Single(cut);
        Assert.Equal(new[] { "a", "b" }, cut[0]);
    }

    [Fact]
    public void Routes_that_collapse_to_the_same_prefix_appear_once()
    {
        var paths = new IReadOnlyList<string>[]
        {
            new[] { "a", "b", "c" },
            new[] { "a", "b", "d" },
        };

        var cut = PathSolver.TruncateAtPins(paths, id => id == "b");

        Assert.Single(cut);
        Assert.Equal(new[] { "a", "b" }, cut[0]);
    }

    [Fact]
    public void Distinct_ways_to_reach_the_same_pin_stay_distinct()
    {
        var paths = new IReadOnlyList<string>[]
        {
            new[] { "a", "left", "goal" },
            new[] { "a", "right", "goal" },
        };

        var cut = PathSolver.TruncateAtPins(paths, id => id == "goal");

        Assert.Equal(2, cut.Count);
    }

    [Fact]
    public void No_pins_means_nothing_to_draw()
    {
        var paths = new IReadOnlyList<string>[] { new[] { "a", "b", "c" } };

        Assert.Empty(PathSolver.TruncateAtPins(paths, _ => false));
    }
}
