using System.Text.Json;
using PathingPlus.PathingPlusCode.Pathing;
using Xunit;

namespace PathingPlus.Tests;

public class SavedPlanTests
{
    [Fact]
    public void A_legacy_single_pin_loads_as_one_pinned_route()
    {
        var saved = JsonSerializer.Deserialize<SavedPlan>(
            """{"MapKey":"map","Pins":["a","end"],"LockedRoute":["start","a","end"]}""")!;
        Assert.Equal(new[] { "start", "a", "end" }, Assert.Single(saved.ReadPinnedRoutes()));
        Assert.Null(saved.RetainedRoutes);
    }

    [Fact]
    public void An_explicitly_empty_pin_list_does_not_restore_a_legacy_pin()
    {
        var saved = JsonSerializer.Deserialize<SavedPlan>(
            """{"MapKey":"map","Pins":[],"LockedRoute":["start","a","end"],"LockedRoutes":[]}""")!;
        Assert.Empty(saved.ReadPinnedRoutes());
    }

    [Fact]
    public void Multiple_pins_and_cleared_alternatives_survive_a_save_round_trip()
    {
        string[][] routes = [["start", "a", "end"], ["start", "b", "end"]];
        var saved = new SavedPlan("map", ["a", "b", "end"], Cut: ["a>b"],
            LockedRoutes: routes, RetainedRoutes: routes);
        var restored = JsonSerializer.Deserialize<SavedPlan>(JsonSerializer.Serialize(saved))!;
        Assert.Equal(2, restored.ReadPinnedRoutes().Count);
        Assert.Equal(routes[0], restored.ReadPinnedRoutes()[0]);
        Assert.Equal(routes[1], restored.ReadPinnedRoutes()[1]);
        Assert.Equal(routes, restored.RetainedRoutes);
        Assert.Equal(saved.Pins, restored.Pins);
        Assert.Equal(saved.Cut, restored.Cut);
    }
}
