namespace PathingPlus.PathingPlusCode.Pathing;

/// <summary>Backward-compatible plan data, independent of the game's file APIs.</summary>
public sealed record SavedPlan(
    string MapKey, string[] Pins, string[]? LockedRoute = null,
    string[]? Blocked = null, string[]? Cut = null,
    string[][]? LockedRoutes = null, string[][]? RetainedRoutes = null)
{
    // The plural field is authoritative, including an explicitly empty list.
    // LockedRoute and Blocked remain readable for older local and Workshop saves.
    public IReadOnlyList<IReadOnlyList<string>> ReadPinnedRoutes() =>
        LockedRoutes is not null ? LockedRoutes
        : LockedRoute is { Length: > 0 } route ? [route] : [];
}
