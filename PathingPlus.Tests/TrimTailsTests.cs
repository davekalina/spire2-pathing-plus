using PathingPlus.PathingPlusCode.Pathing;
using Xunit;

namespace PathingPlus.Tests;

public class TrimTailsTests
{
    private static bool IsBoss(string id) => id.StartsWith("boss");

    [Fact]
    public void Trims_trailing_boss_nodes()
    {
        var paths = new IReadOnlyList<string>[] { new[] { "a", "b", "boss" } };

        var trimmed = PathSolver.TrimTails(paths, IsBoss);

        Assert.Single(trimmed);
        Assert.Equal(new[] { "a", "b" }, trimmed[0]);
    }

    [Fact]
    public void Double_boss_variants_collapse_into_one_route()
    {
        var paths = new IReadOnlyList<string>[]
        {
            new[] { "a", "b", "boss1" },
            new[] { "a", "b", "boss1", "boss2" },
        };

        var trimmed = PathSolver.TrimTails(paths, IsBoss);

        Assert.Single(trimmed);
        Assert.Equal(new[] { "a", "b" }, trimmed[0]);
    }

    [Fact]
    public void Distinct_walks_stay_distinct()
    {
        var paths = new IReadOnlyList<string>[]
        {
            new[] { "a", "b", "boss" },
            new[] { "a", "c", "boss" },
        };

        var trimmed = PathSolver.TrimTails(paths, IsBoss);

        Assert.Equal(2, trimmed.Count);
    }

    [Fact]
    public void A_path_that_is_all_tail_disappears()
    {
        var paths = new IReadOnlyList<string>[] { new[] { "boss1", "boss2" } };

        Assert.Empty(PathSolver.TrimTails(paths, IsBoss));
    }

    [Fact]
    public void Middle_nodes_matching_the_predicate_are_kept()
    {
        var paths = new IReadOnlyList<string>[] { new[] { "a", "bossy-event", "b" } };

        var trimmed = PathSolver.TrimTails(paths, id => id == "bossy-event");

        Assert.Single(trimmed);
        Assert.Equal(new[] { "a", "bossy-event", "b" }, trimmed[0]);
    }
}
