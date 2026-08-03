namespace PathingPlus.PathingPlusCode.Pathing;

/// <summary>
/// The set of map nodes the player has pinned. Any node, any floor, any number — pins
/// mark candidates being compared, and the route filter is "reaches at least one pin",
/// so overlapping or same-floor pins are meaningful rather than contradictory.
/// </summary>
public sealed class WaypointSelection
{
    private readonly HashSet<string> _ids = [];

    public IReadOnlyCollection<string> Ids => _ids;

    public int Count => _ids.Count;

    public bool IsSelected(string id) => _ids.Contains(id);

    /// <returns>True if the node is selected after the call.</returns>
    public bool Toggle(string id)
    {
        if (_ids.Remove(id))
            return false;
        _ids.Add(id);
        return true;
    }

    public void Clear() => _ids.Clear();

    /// <summary>Drop waypoints that no longer exist, e.g. after the map model changes.</summary>
    public void RetainWhere(Func<string, bool> stillValid) =>
        _ids.RemoveWhere(id => !stillValid(id));
}
