using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace PathingPlus.PathingPlusCode.Map;

/// <summary>
/// Gives map nodes their hover back while the mod holds the quill.
///
/// <c>NMapPoint.IsInputAllowed</c> is false whenever a drawing tool is out:
///
/// <code>
/// if (!_screen.IsTraveling)
///     return _screen.Drawings.GetLocalDrawingMode() == DrawingMode.None;
/// </code>
///
/// Vanilla only has a tool out for as long as you are scribbling, so that reads as
/// "don't fight the pen". This mod keeps one out permanently — that is the whole
/// design — which silently cost every node its idle pulse and its hover response.
/// The travelable node stopped inviting the click even though the click worked.
///
/// Every caller of that gate is a **visual**: the "you can go here" pulse in
/// <c>_Process</c>, the controller reticle, and the history hover tip in
/// <c>OnFocus</c>. Nothing on the click or travel path consults it, so restoring it
/// cannot make a stroke move the player.
/// </summary>
[HarmonyPatch(typeof(NMapPoint), "IsInputAllowed")]
internal static class MapPointHoverPatch
{
    [HarmonyPostfix]
    private static void AfterIsInputAllowed(ref bool __result)
    {
        // A ref parameter cannot be captured by the guarded lambda.
        var allowed = Guard.Run("Restoring map node hover", HoverShouldBeLive, false);
        if (allowed)
            __result = true;
    }

    private static bool HoverShouldBeLive()
    {
        // Only ever turns a false into a true, and only for the one reason the mod
        // created: a tool the player did not choose to be holding.
        if (!PathingOptions.OverrideDrawing)
            return false;
        if (NMapScreen.Instance is not { IsOpen: true } screen || screen.IsTraveling)
            return false;
        return screen.Drawings.GetLocalDrawingMode() != DrawingMode.None;
    }
}
