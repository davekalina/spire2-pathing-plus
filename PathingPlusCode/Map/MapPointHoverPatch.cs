using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace PathingPlus.PathingPlusCode.Map;

/// <summary>
/// Gives map nodes their hover back while the mod's own tools are out.
///
/// <c>NMapPoint.IsInputAllowed</c> is false whenever a drawing tool is out:
///
/// <code>
/// if (!_screen.IsTraveling)
///     return _screen.Drawings.GetLocalDrawingMode() == DrawingMode.None;
/// </code>
///
/// For the quill that reads as "don't fight the pen", and it is right: ink goes where
/// the hand goes and a node lighting up under it is noise. The mod's tools are the
/// other way round — the whole gesture is aimed **at** nodes — so with the path tool
/// or the eraser in hand the pulse and the hover are exactly what the player needs to
/// see, and losing them cost every node its invitation to be clicked.
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
        // Only ever turns a false into a true, and only for the mod's own tools: the
        // eraser, which lifts pins by the node it is over, and the path tool, which
        // draws by them. The game's quill keeps the game's behaviour.
        if (NMapScreen.Instance is not { IsOpen: true } screen || screen.IsTraveling)
            return false;
        return screen.Drawings.GetLocalDrawingMode() switch
        {
            DrawingMode.Erasing => true,
            DrawingMode.Drawing => MapScreenPatches.PathDrawing(screen.Drawings),
            _ => false,
        };
    }
}
