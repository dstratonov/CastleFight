using UnityEngine;
using Pathfinding;
using Pathfinding.RVO;

/// <summary>
/// Ensures A* Pro has a Recast Graph configured at startup, and the
/// scene RVOSimulator is tuned for dense crowds around obstacles.
/// Attach to the same GameObject as AstarPath.
/// Uses Start so it runs after AstarPath initializes, then adds and scans the graph.
/// </summary>
[RequireComponent(typeof(AstarPath))]
public class AStarSetup : MonoBehaviour
{
    [Header("Recast Graph Settings")]
    [SerializeField] private float cellSize = 0.3f;
    [SerializeField] private float agentRadius = 0.5f;
    [SerializeField] private float walkableHeight = 2f;
    [SerializeField] private float walkableClimb = 0.5f;
    [SerializeField] private float maxSlope = 30f;
    [SerializeField] private Vector3 boundsCenter = Vector3.zero;
    [SerializeField] private Vector3 boundsSize = new(220, 20, 220);

    private void Start()
    {
        // Start runs after all Awake/OnEnable, so AstarPath is fully initialized
        var astar = GetComponent<AstarPath>();
        if (astar == null) return;

        // Add Recast Graph if none exists
        if (astar.data.graphs == null || astar.data.graphs.Length == 0)
        {
            var graph = astar.data.AddGraph(typeof(RecastGraph)) as RecastGraph;
            graph.cellSize = cellSize;
            graph.characterRadius = agentRadius;
            graph.walkableHeight = walkableHeight;
            graph.walkableClimb = walkableClimb;
            graph.maxSlope = maxSlope;
            graph.maxEdgeLength = 12f;
            graph.minRegionSize = 10f;
            graph.forcedBoundsCenter = boundsCenter;
            graph.forcedBoundsSize = boundsSize;
            graph.enableNavmeshCutting = true;

            // Only rasterize Ground layer — buildings use NavmeshCut to carve holes
            graph.collectionSettings.rasterizeMeshes = false;
            graph.collectionSettings.rasterizeColliders = true;
            graph.collectionSettings.rasterizeTerrain = true;
            graph.collectionSettings.layerMask = LayerMask.GetMask("Ground");

            Debug.Log($"[AStarSetup] Created Recast Graph: bounds={boundsSize} cell={cellSize} radius={agentRadius}");
        }

        // Scan the graph
        astar.Scan();
        Debug.Log($"[AStarSetup] Scanned. Nodes: {astar.data.graphs[0].CountNodes()}");

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
            Debug.LogWarning("[AStarSetup] No RVOSimulator found in scene — local avoidance will be degraded");
        }
    }
}
