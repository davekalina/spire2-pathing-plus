using MegaCrit.Sts2.Core.Map;
using PathingPlus.PathingPlusCode.Pathing;

namespace PathingPlus.PathingPlusCode.Map;

/// <summary>
/// A pure-logic snapshot of one <see cref="ActMap" />. The boss, second boss, and
/// starting point are separate objects that <c>GetAllMapPoints()</c> does not return,
/// so they are appended here the same way <c>NMapScreen.SetMap</c> does.
/// </summary>
internal sealed class MapGraphAdapter
{
    public ActMap Map { get; }
    public SpireMapGraph Graph { get; }

    private readonly Dictionary<string, MapPoint> _pointsById = [];

    private MapGraphAdapter(ActMap map, SpireMapGraph graph) => (Map, Graph) = (map, graph);

    public static string IdOf(MapPoint point) => $"c{point.coord.col}r{point.coord.row}";

    public bool TryGetPoint(string id, out MapPoint point) =>
        _pointsById.TryGetValue(id, out point!);

    public static MapGraphAdapter Build(ActMap map)
    {
        var points = new List<MapPoint>(map.GetAllMapPoints())
        {
            map.StartingMapPoint,
            map.BossMapPoint,
        };
        if (map.SecondBossMapPoint is { } secondBoss)
            points.Add(secondBoss);

        var byId = new Dictionary<string, MapPoint>();
        foreach (var point in points)
            byId.TryAdd(IdOf(point), point);

        var nodes = byId.Select(kv =>
            new GraphNode(kv.Key, kv.Value.coord.row, kv.Value.PointType.ToString()));

        // Children is a HashSet, whose order is not meaningful; sort so the route list
        // comes out in a stable left-to-right order every time the map is rebuilt.
        var edges =
            from kv in byId
            from child in kv.Value.Children.OrderBy(c => c.coord.row).ThenBy(c => c.coord.col)
            let childId = IdOf(child)
            where byId.ContainsKey(childId)
            select (kv.Key, childId);

        var adapter = new MapGraphAdapter(map, new SpireMapGraph(nodes, edges));
        foreach (var kv in byId)
            adapter._pointsById.Add(kv.Key, kv.Value);
        return adapter;
    }
}
