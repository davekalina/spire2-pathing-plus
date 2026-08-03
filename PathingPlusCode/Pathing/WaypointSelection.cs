namespace PathingPlus.PathingPlusCode.Pathing;

/// <summary>
/// The set of map nodes the player has pinned. A route crosses each floor exactly once,
/// so at most one waypoint per row can ever be satisfied — selecting a node replaces any
/// earlier selection on the same row instead of dead-ending the filter.
/// </summary>
public sealed class WaypointSelection
{
    private readonly Dictionary<int, string> _byRow = new();

    public IReadOnlyCollection<string> Ids => _byRow.Values;

    public int Count => _byRow.Count;

    public bool IsSelected(string id) => _byRow.ContainsValue(id);

    /// <returns>True if the node is selected after the call.</returns>
    public bool Toggle(string id, int row)
    {
        if (_byRow.TryGetValue(row, out var existing) && existing == id)
        {
            _byRow.Remove(row);
            return false;
        }

        _byRow[row] = id;
        return true;
    }

    public void Clear() => _byRow.Clear();

    /// <summary>Drop waypoints that no longer exist, e.g. after the map model changes.</summary>
    public void RetainWhere(Func<string, bool> stillValid)
    {
        foreach (var row in _byRow.Where(kv => !stillValid(kv.Value)).Select(kv => kv.Key).ToList())
            _byRow.Remove(row);
    }
}
