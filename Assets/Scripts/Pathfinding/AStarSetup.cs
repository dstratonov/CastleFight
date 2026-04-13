using System.Collections.Generic;
using UnityEngine;
using Pathfinding;
using Pathfinding.RVO;

/// <summary>
/// Maps continuous unit radii onto a finite set of recast graphs.
/// Each graph is baked for a specific clearance radius, and units use
/// the smallest graph that can still accommodate their footprint.
/// </summary>
public static class UnitPathingProfile
{
    private static readonly float[] GraphRadii = { 0.5f, 1f, 2f };
    private static readonly PathingBucket[] Buckets =
    {
        new(0.75f, 0.5f),
        new(1.5f, 1.0f),
        new(float.PositiveInfinity, 2.0f),
    };

    public static float DefaultGraphRadius => GraphRadii[0];

    public static IReadOnlyList<float> SupportedGraphRadii => GraphRadii;

    public static float NormalizeUnitRadius(float unitRadius)
    {
        return unitRadius > 0.01f ? unitRadius : DefaultGraphRadius;
    }

    public static float GetGraphRadiusForUnit(float unitRadius)
    {
        float requiredRadius = NormalizeUnitRadius(unitRadius);
        for (int i = 0; i < Buckets.Length; i++)
        {
            if (requiredRadius <= Buckets[i].MaxUnitRadius + 0.001f)
                return Buckets[i].GraphRadius;
        }

        return GraphRadii[GraphRadii.Length - 1];
    }

    public static string GetGraphName(float graphRadius)
    {
        return $"Recast R{graphRadius:0.0}";
    }

    public static string GetGraphNameForUnit(float unitRadius)
    {
        return GetGraphName(GetGraphRadiusForUnit(unitRadius));
    }

    public static bool TryGetGraph(AstarPath astar, float unitRadius, out RecastGraph graph)
    {
        graph = null;
        if (astar == null || astar.data == null)
            return false;

        string expectedName = GetGraphNameForUnit(unitRadius);
        foreach (RecastGraph recast in astar.data.FindGraphsOfType(typeof(RecastGraph)))
        {
            if (recast != null && recast.name == expectedName)
            {
                graph = recast;
                return true;
            }
        }

        return false;
    }

    public static bool TryGetGraphMask(AstarPath astar, float unitRadius, out GraphMask graphMask)
    {
        graphMask = GraphMask.everything;
        if (!TryGetGraph(astar, unitRadius, out RecastGraph graph))
            return false;

        graphMask = GraphMask.FromGraph(graph);
        return true;
    }

    public static NearestNodeConstraint BuildWalkableConstraint(AstarPath astar, float unitRadius)
    {
        var constraint = NearestNodeConstraint.Walkable;
        if (TryGetGraphMask(astar, unitRadius, out GraphMask graphMask))
            constraint.graphMask = graphMask;
        return constraint;
    }

    private readonly struct PathingBucket
    {
        public PathingBucket(float maxUnitRadius, float graphRadius)
        {
            MaxUnitRadius = maxUnitRadius;
            GraphRadius = graphRadius;
        }

        public float MaxUnitRadius { get; }
        public float GraphRadius { get; }
    }
}

/// <summary>
/// Ensures A* Pro has recast graphs configured at startup, and the
/// scene RVOSimulator is tuned for dense crowds around obstacles.
/// Attach to the same GameObject as AstarPath.
/// Uses Start so it runs after AstarPath initializes, then normalizes
/// the graph set and scans it.
/// </summary>
[RequireComponent(typeof(AstarPath))]
public class AStarSetup : MonoBehaviour
{
    [Header("Recast Graph Settings")]
    [SerializeField] private float cellSize = 0.3f;
    [SerializeField] private float walkableHeight = 2f;
    [SerializeField] private float walkableClimb = 0.5f;
    [SerializeField] private float maxSlope = 30f;
    [SerializeField] private Vector3 boundsCenter = Vector3.zero;
    [SerializeField] private Vector3 boundsSize = new(220, 20, 220);

    private void Start()
    {
        // Start runs after all Awake/OnEnable, so AstarPath is fully initialized.
        var astar = GetComponent<AstarPath>();
        if (astar == null)
            return;

        EnsureRecastGraphs(astar);

        // Scan after all runtime graphs have been normalized to the expected
        // pathing profiles.
        astar.Scan();
        LogGraphSummary(astar);

        // Configure the scene RVOSimulator so RVO agents respect navmesh
        // edges (NavmeshCut holes carved by buildings/castles). Without this,
        // a crowd can push units through building walls.
        var sim = Object.FindFirstObjectByType<RVOSimulator>();
        if (sim != null)
        {
            sim.useNavmeshAsObstacle = true;
            // symmetryBreakingBias helps deadlocked symmetric cases (e.g.
            // two units walking directly at each other). Default 0.1 is fine,
            // but 0.2 is a bit more aggressive for RTS crowds.
            sim.symmetryBreakingBias = 0.2f;
            Debug.Log("[AStarSetup] RVOSimulator configured: useNavmeshAsObstacle=true");
        }
        else
        {
            Debug.LogWarning("[AStarSetup] No RVOSimulator found in scene - local avoidance will be degraded");
        }
    }

    private void EnsureRecastGraphs(AstarPath astar)
    {
        var existingRecasts = new List<RecastGraph>();
        foreach (RecastGraph graph in astar.data.FindGraphsOfType(typeof(RecastGraph)))
        {
            if (graph != null)
                existingRecasts.Add(graph);
        }

        var assigned = new HashSet<RecastGraph>();
        var supportedRadii = UnitPathingProfile.SupportedGraphRadii;
        for (int i = 0; i < supportedRadii.Count; i++)
        {
            float graphRadius = supportedRadii[i];
            string graphName = UnitPathingProfile.GetGraphName(graphRadius);

            RecastGraph graph = FindNamedGraph(existingRecasts, assigned, graphName)
                ?? TakeLegacyGraph(existingRecasts, assigned);

            if (graph == null)
            {
                graph = astar.data.AddGraph(typeof(RecastGraph)) as RecastGraph;
                if (graph == null)
                    continue;
            }

            assigned.Add(graph);
            ConfigureGraph(graph, graphName, graphRadius);
        }

        for (int i = 0; i < existingRecasts.Count; i++)
        {
            RecastGraph graph = existingRecasts[i];
            if (graph != null && !assigned.Contains(graph))
                astar.data.RemoveGraph(graph);
        }
    }

    private static RecastGraph FindNamedGraph(List<RecastGraph> existingRecasts, HashSet<RecastGraph> assigned, string graphName)
    {
        for (int i = 0; i < existingRecasts.Count; i++)
        {
            RecastGraph graph = existingRecasts[i];
            if (graph != null && !assigned.Contains(graph) && graph.name == graphName)
                return graph;
        }

        return null;
    }

    private static RecastGraph TakeLegacyGraph(List<RecastGraph> existingRecasts, HashSet<RecastGraph> assigned)
    {
        for (int i = 0; i < existingRecasts.Count; i++)
        {
            RecastGraph graph = existingRecasts[i];
            if (graph != null && !assigned.Contains(graph))
                return graph;
        }

        return null;
    }

    private void ConfigureGraph(RecastGraph graph, string graphName, float graphRadius)
    {
        graph.name = graphName;
        graph.cellSize = cellSize;
        graph.characterRadius = graphRadius;
        graph.walkableHeight = walkableHeight;
        graph.walkableClimb = walkableClimb;
        graph.maxSlope = maxSlope;
        graph.maxEdgeLength = 12f;
        graph.minRegionSize = 10f;
        graph.forcedBoundsCenter = boundsCenter;
        graph.forcedBoundsSize = boundsSize;
        graph.enableNavmeshCutting = true;

        // Only rasterize Ground layer - buildings use NavmeshCut to carve holes.
        graph.collectionSettings.rasterizeMeshes = false;
        graph.collectionSettings.rasterizeColliders = true;
        graph.collectionSettings.rasterizeTerrain = true;
        graph.collectionSettings.layerMask = LayerMask.GetMask("Ground");
    }

    private void LogGraphSummary(AstarPath astar)
    {
        foreach (RecastGraph graph in astar.data.FindGraphsOfType(typeof(RecastGraph)))
        {
            if (graph == null)
                continue;

            Debug.Log($"[AStarSetup] Scanned {graph.name}: nodes={graph.CountNodes()} radius={graph.characterRadius:0.0}");
        }
    }
}
