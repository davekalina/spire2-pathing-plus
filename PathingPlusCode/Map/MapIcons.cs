using Godot;
using MegaCrit.Sts2.Core.Map;

namespace PathingPlus.PathingPlusCode.Map;

/// <summary>
/// The map's own node icons, for the route tooltips. Same art, same resource paths as
/// <c>NNormalMapPoint.IconName</c>; an unknown ("?") node stays a "?" — what it will
/// resolve into is information the player is not meant to have yet.
/// </summary>
internal static class MapIcons
{
    public static Texture2D? For(string roomKind)
    {
        var name = roomKind switch
        {
            nameof(MapPointType.Monster) => "map_monster",
            nameof(MapPointType.Elite) => "map_elite",
            nameof(MapPointType.Treasure) => "map_chest",
            nameof(MapPointType.Shop) => "map_shop",
            nameof(MapPointType.RestSite) => "map_rest",
            nameof(MapPointType.Unknown) => "map_unknown",
            nameof(MapPointType.Unassigned) => "map_unknown",
            _ => null, // Boss and Ancient have no compact icon; callers skip them.
        };
        if (name is null)
            return null;

        // No static caching: the engine's resource cache frees textures on scene
        // teardown, and a held wrapper would come back disposed. CacheMode.Reuse makes
        // repeat loads cheap without the mod holding anything.
        return ResourceLoader.Load<Texture2D>(
            $"res://images/atlases/ui_atlas.sprites/map/icons/{name}.tres",
            null, ResourceLoader.CacheMode.Reuse);
    }
}
