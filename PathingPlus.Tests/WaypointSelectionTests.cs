using PathingPlus.PathingPlusCode.Pathing;
using Xunit;

namespace PathingPlus.Tests;

public class WaypointSelectionTests
{
    [Fact]
    public void Toggle_selects_then_deselects()
    {
        var selection = new WaypointSelection();

        Assert.True(selection.Toggle("a"));
        Assert.True(selection.IsSelected("a"));

        Assert.False(selection.Toggle("a"));
        Assert.False(selection.IsSelected("a"));
        Assert.Equal(0, selection.Count);
    }

    [Fact]
    public void Any_number_of_pins_accumulate_including_same_floor_rivals()
    {
        var selection = new WaypointSelection();
        selection.Toggle("left-elite");
        selection.Toggle("right-shop");
        selection.Toggle("same-floor-rest");

        Assert.Equal(3, selection.Count);
        Assert.Contains("left-elite", selection.Ids);
        Assert.Contains("right-shop", selection.Ids);
        Assert.Contains("same-floor-rest", selection.Ids);
    }

    [Fact]
    public void RetainWhere_drops_stale_ids()
    {
        var selection = new WaypointSelection();
        selection.Toggle("a");
        selection.Toggle("b");

        selection.RetainWhere(id => id == "b");

        Assert.False(selection.IsSelected("a"));
        Assert.True(selection.IsSelected("b"));
    }

    [Fact]
    public void Clear_empties_everything()
    {
        var selection = new WaypointSelection();
        selection.Toggle("a");
        selection.Toggle("b");

        selection.Clear();

        Assert.Equal(0, selection.Count);
    }
}
