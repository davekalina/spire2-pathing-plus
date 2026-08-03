using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace PathingPlus.PathingPlusCode.Map;

[HarmonyPatch(typeof(NMapScreen))]
internal static class MapScreenPatches
{
    private static readonly Dictionary<NMapScreen, PathingView> Views = [];

    [HarmonyPostfix]
    [HarmonyPatch(nameof(NMapScreen.Open))]
    private static void AfterOpen(NMapScreen __instance) =>
        Guard.Run("Attaching the pathing view", () =>
        {
            if (!Views.TryGetValue(__instance, out var view))
            {
                view = new PathingView(__instance);
                Views.Add(__instance, view);
                __instance.TreeExiting += () => Detach(__instance);
            }
            view.Refresh();
        });

    [HarmonyPostfix]
    [HarmonyPatch(nameof(NMapScreen.SetMap))]
    private static void AfterSetMap(NMapScreen __instance) =>
        Guard.Run("Resetting pathing after a map change", () =>
        {
            if (Views.TryGetValue(__instance, out var view))
                view.OnMapChanged();
        });

    /// <summary>
    /// The game re-evaluates which nodes are travelable whenever position or travel
    /// permission changes — exactly the moments the routes need recomputing.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch("RecalculateTravelability")]
    private static void AfterRecalculateTravelability(NMapScreen __instance) =>
        Guard.Run("Refreshing routes after travelability change", () =>
        {
            if (__instance.IsOpen && Views.TryGetValue(__instance, out var view))
                view.Refresh();
        });

    internal static void Detach(NMapScreen screen)
    {
        if (Views.Remove(screen, out var view))
            view.Dispose();
    }

    internal static void RouteMapPointClick(NMapPoint point)
    {
        foreach (var view in Views.Values)
        {
            if (view.Owns(point))
            {
                view.OnMapPointClicked(point);
                return;
            }
        }
    }
}

/// <summary>
/// Pin clicks. A non-travelable map node is disabled, which suppresses its own click
/// signals but not its <c>_GuiInput</c> — the one place a click on it can still be
/// seen. Travelable nodes are left entirely to the game: this only ever acts on
/// disabled nodes, so a failure here can never cost the player a movement click.
/// </summary>
[HarmonyPatch(typeof(NClickableControl), nameof(NClickableControl._GuiInput))]
internal static class MapPointPinPatch
{
    [HarmonyPrefix]
    private static void BeforeGuiInput(NClickableControl __instance, InputEvent __0)
    {
        if (__instance is not NMapPoint point)
            return;
        if (__0 is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
            return;
        if (point.IsEnabled)
            return;

        Guard.Run("Handling a pin click", () => MapScreenPatches.RouteMapPointClick(point));
    }
}
