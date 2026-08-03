using PathingPlus.PathingPlusCode.Pathing;
using Xunit;

namespace PathingPlus.Tests;

public class WaypointSelectionTests
{
    [Fact]
    public void Toggle_selects_then_deselects()
    {
        var selection = new WaypointSelection();

        Assert.True(selection.Toggle("a", 0));
        Assert.True(selection.IsSelected("a"));

        Assert.False(selection.Toggle("a", 0));
        Assert.False(selection.IsSelected("a"));
        Assert.Equal(0, selection.Count);
    }

    [Fact]
    public void Selecting_on_an_occupied_row_replaces_the_previous_pick()
    {
        var selection = new WaypointSelection();
        selection.Toggle("a", 3);

        Assert.True(selection.Toggle("b", 3));

        Assert.False(selection.IsSelected("a"));
        Assert.True(selection.IsSelected("b"));
        Assert.Equal(1, selection.Count);
    }

    [Fact]
    public void Selections_on_different_rows_accumulate()
    {
        var selection = new WaypointSelection();
        selection.Toggle("a", 1);
        selection.Toggle("b", 4);

        Assert.Equal(2, selection.Count);
        Assert.Contains("a", selection.Ids);
        Assert.Contains("b", selection.Ids);
    }

    [Fact]
    public void RetainWhere_drops_stale_ids()
    {
        var selection = new WaypointSelection();
        selection.Toggle("a", 1);
        selection.Toggle("b", 2);

        selection.RetainWhere(id => id == "b");

        Assert.False(selection.IsSelected("a"));
        Assert.True(selection.IsSelected("b"));
    }

    [Fact]
    public void Clear_empties_everything()
    {
        var selection = new WaypointSelection();
        selection.Toggle("a", 1);
        selection.Toggle("b", 2);

        selection.Clear();

        Assert.Equal(0, selection.Count);
    }
}
