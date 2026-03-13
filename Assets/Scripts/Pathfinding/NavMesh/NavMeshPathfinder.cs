using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// A* pathfinding on NavMesh triangles, followed by the Funnel algorithm for
/// path smoothing, and vertex expansion for unit-radius offset from walls.
/// Layer 1 of SC2 pathfinding architecture.
/// </summary>
public static class NavMeshPathfinder
{
    // ================================================================
    //  STATISTICS (reset periodically by PathfindingDiagnostic)
    // ================================================================
    public static int StatPathsRequested;
    public static int StatPathsSucceeded;
    public static int StatPathsFailed;
    public static int StatTotalAStarNodes;
    public static int StatTotalWidthRejections;
    public static int StatTotalChannelLength;
    public static int StatTotalFunnelWaypoints;
    public static int StatMaxChannelLength;
    public static float StatWorstPathRatio;
    public static int StatThrottled;

    public static void ResetStats()
    {
        StatPathsRequested = 0;
        StatPathsSucceeded = 0;
        StatPathsFailed = 0;
        StatTotalAStarNodes = 0;
        StatTotalWidthRejections = 0;
        StatTotalChannelLength = 0;
        StatTotalFunnelWaypoints = 0;
        StatMaxChannelLength = 0;
        StatWorstPathRatio = 0f;
        StatThrottled = 0;
        StatPortalExpansions = 0;
        StatDensityAffectedTris = 0;
        StatDensityMaxCost = 0f;
        StatDensityUnitsOutsideMesh = 0;
    }

    /// <summary>
    /// Full pathfinding pipeline: A* -> Build Portals -> Expand Portals -> Funnel.
    /// SC2-style: portals are shrunk inward by unitRadius BEFORE the funnel runs,
    /// so waypoints stay inside the corridor without post-hoc validation.
    /// Returns a list of waypoints in NavMesh 2D coordinates, or null if no path.
    /// </summary>
    public static List<Vector2> FindPath(NavMeshData mesh, Vector2 startPos, Vector2 goalPos, float unitRadius)
    {
        StatPathsRequested++;

        int startTri = mesh.FindTriangleAtPosition(startPos);
        int goalTri = mesh.FindTriangleAtPosition(goalPos);

        if (startTri < 0 || goalTri < 0)
        {
            StatPathsFailed++;
            if (GameDebug.Pathfinding)
                Debug.LogWarning($"[NavPathfinder] Position outside NavMesh: start tri={startTri} goal tri={goalTri} " +
                    $"at ({startPos.x:F1},{startPos.y:F1})->({goalPos.x:F1},{goalPos.y:F1}) " +
                    $"mesh has {mesh.TriangleCount} triangles, {mesh.VertexCount} vertices");
            return null;
        }

        if (startTri == goalTri)
        {
            StatPathsSucceeded++;
            StatTotalChannelLength += 1;
            StatTotalFunnelWaypoints += 2;
            return new List<Vector2> { startPos, goalPos };
        }

        var channel = AStarOnTriangles(mesh, startTri, goalTri, unitRadius);
        if (channel == null || channel.Count == 0)
        {
            StatPathsFailed++;
            if (GameDebug.Pathfinding)
            {
                ref var sTri = ref mesh.Triangles[startTri];
                ref var gTri = ref mesh.Triangles[goalTri];
                Debug.LogWarning($"[NavPathfinder] A* FAILED: startTri={startTri}(walkable={sTri.IsWalkable}, " +
                    $"N={sTri.N0},{sTri.N1},{sTri.N2}) goalTri={goalTri}(walkable={gTri.IsWalkable}, " +
                    $"N={gTri.N0},{gTri.N1},{gTri.N2}) radius={unitRadius:F2}");
            }
            return null;
        }

        var portals = BuildPortals(mesh, channel, startPos, goalPos);
        if (unitRadius > 0f)
            portals = ExpandPortals(portals, unitRadius);

        var waypoints = FunnelFromPortals(portals);

        StatPathsSucceeded++;
        StatTotalChannelLength += channel.Count;
        StatTotalFunnelWaypoints += waypoints.Count;
        if (channel.Count > StatMaxChannelLength)
            StatMaxChannelLength = channel.Count;

        float pathLen = 0f;
        for (int i = 1; i < waypoints.Count; i++)
            pathLen += Vector2.Distance(waypoints[i - 1], waypoints[i]);
        float directDist = Vector2.Distance(startPos, goalPos);
        float ratio = pathLen / Mathf.Max(directDist, 0.01f);
        if (ratio > StatWorstPathRatio)
            StatWorstPathRatio = ratio;

        if (GameDebug.Pathfinding)
        {
            Debug.Log($"[NavPathfinder] Path: {channel.Count} tris → {portals.Count} portals → {waypoints.Count} wpts | " +
                $"len={pathLen:F1} direct={directDist:F1} ratio={ratio:F2}");
        }

        return waypoints;
    }

    // ================================================================
    //  A* ON TRIANGLES
    // ================================================================

    /// <summary>
    /// A* search on the triangle graph. Returns ordered list of triangle IDs (the channel).
    /// Width filter uses Demyen edge-pair width: rejects triangles where the passage
    /// between entry and exit edges is too narrow for the unit.
    /// </summary>
    public static List<int> AStarOnTriangles(NavMeshData mesh, int startTri, int goalTri, float unitRadius)
    {
        float unitDiameter = unitRadius * 2f;
        Vector2 goalCentroid = mesh.GetCentroid(goalTri);

        var openSet = new SortedSet<(float f, int triId)>(
            Comparer<(float f, int triId)>.Create((a, b) =>
                a.f != b.f ? a.f.CompareTo(b.f) : a.triId.CompareTo(b.triId)));

        var gScore = new Dictionary<int, float>();
        var parent = new Dictionary<int, int>();
        var inClosed = new HashSet<int>();

        gScore[startTri] = 0f;
        float h = Vector2.Distance(mesh.GetCentroid(startTri), goalCentroid);
        openSet.Add((h, startTri));

        int maxIter = mesh.TriangleCount * 2;
        int iter = 0;
        int widthRejections = 0;

        while (openSet.Count > 0 && iter++ < maxIter)
        {
            var (_, current) = openSet.Min;
            openSet.Remove(openSet.Min);

            if (current == goalTri)
            {
                StatTotalAStarNodes += iter;
                StatTotalWidthRejections += widthRejections;
                return ReconstructChannel(parent, current, startTri);
            }

            if (inClosed.Contains(current)) continue;
            inClosed.Add(current);

            float currentG = gScore[current];
            Vector2 currentCentroid = mesh.GetCentroid(current);
            ref var currentTri = ref mesh.Triangles[current];

            // Determine entry edge (edge shared with parent triangle)
            int entryEdge = -1;
            if (parent.TryGetValue(current, out int parentTri))
                entryEdge = currentTri.GetEdgeToNeighbor(parentTri);

            for (int e = 0; e < 3; e++)
            {
                int neighbor = currentTri.GetNeighbor(e);
                if (neighbor < 0 || inClosed.Contains(neighbor)) continue;
                if (!mesh.Triangles[neighbor].IsWalkable) continue;

                // Width filter: check exit portal length
                float portalLen = mesh.GetEdgeLength(current, e);
                if (portalLen < unitDiameter)
                {
                    widthRejections++;
                    continue;
                }

                // Width filter: check Demyen passage width between entry and exit edges
                if (entryEdge >= 0 && entryEdge != e)
                {
                    int sharedVert = NavMeshData.SharedVertexOfEdges(entryEdge, e);
                    float passageWidth = currentTri.GetWidth(sharedVert);
                    if (passageWidth < unitDiameter)
                    {
                        widthRejections++;
                        continue;
                    }
                }

                Vector2 neighborCentroid = mesh.GetCentroid(neighbor);
                float edgeCost = Vector2.Distance(currentCentroid, neighborCentroid);
                edgeCost *= mesh.Triangles[neighbor].CostMultiplier;

                float tentativeG = currentG + edgeCost;

                if (!gScore.TryGetValue(neighbor, out float existingG) || tentativeG < existingG)
                {
                    gScore[neighbor] = tentativeG;
                    parent[neighbor] = current;
                    float fScore = tentativeG + Vector2.Distance(neighborCentroid, goalCentroid);
                    openSet.Add((fScore, neighbor));
                }
            }
        }

        StatTotalAStarNodes += iter;
        StatTotalWidthRejections += widthRejections;
        return null;
    }

    private static List<int> ReconstructChannel(Dictionary<int, int> parent, int current, int start)
    {
        var channel = new List<int>();
        while (current != start)
        {
            channel.Add(current);
            current = parent[current];
        }
        channel.Add(start);
        channel.Reverse();
        return channel;
    }

    // ================================================================
    //  PORTAL BUILDING & EXPANSION
    // ================================================================

    /// <summary>
    /// Build the portal list from a triangle channel. Each portal is the shared
    /// edge between consecutive triangles, oriented as (left, right).
    /// Start and end are degenerate portals (both sides = position).
    /// </summary>
    public static List<(Vector2 left, Vector2 right)> BuildPortals(
        NavMeshData mesh, List<int> channel, Vector2 startPos, Vector2 goalPos)
    {
        var portals = new List<(Vector2 left, Vector2 right)>(channel.Count + 1);
        portals.Add((startPos, startPos));

        for (int i = 0; i < channel.Count - 1; i++)
        {
            var (left, right) = GetPortalEdge(mesh, channel[i], channel[i + 1], startPos);
            portals.Add((left, right));
        }

        portals.Add((goalPos, goalPos));
        return portals;
    }

    /// <summary>
    /// SC2-style portal expansion: shrink each portal inward by unitRadius before
    /// the funnel runs. Each portal vertex is pushed toward the corridor center
    /// along the bisector of its neighboring edges. This guarantees the funnel
    /// produces waypoints inside the corridor without post-hoc validation.
    /// </summary>
    public static int StatPortalExpansions;

    public static List<(Vector2 left, Vector2 right)> ExpandPortals(
        List<(Vector2 left, Vector2 right)> portals, float unitRadius)
    {
        int n = portals.Count;
        if (n <= 2 || unitRadius <= 0f)
            return portals;

        var result = new List<(Vector2 left, Vector2 right)>(n);
        result.Add(portals[0]);

        for (int i = 1; i < n - 1; i++)
        {
            Vector2 left = portals[i].left;
            Vector2 right = portals[i].right;

            Vector2 expandedLeft = PushVertexInward(portals, i, true, unitRadius);
            Vector2 expandedRight = PushVertexInward(portals, i, false, unitRadius);

            Vector2 origDir = right - left;
            Vector2 expandedDir = expandedRight - expandedLeft;
            if (origDir.sqrMagnitude > 1e-6f && Vector2.Dot(origDir, expandedDir) <= 0f)
            {
                Vector2 mid = (left + right) * 0.5f;
                expandedLeft = mid;
                expandedRight = mid;
            }

            result.Add((expandedLeft, expandedRight));
            StatPortalExpansions++;
        }

        result.Add(portals[n - 1]);
        return result;
    }

    /// <summary>
    /// Push a single portal vertex inward along the bisector of its neighboring
    /// wall edges. isLeft=true for left chain, false for right chain.
    /// </summary>
    private static Vector2 PushVertexInward(
        List<(Vector2 left, Vector2 right)> portals, int portalIdx, bool isLeft, float unitRadius)
    {
        int n = portals.Count;
        Vector2 curr = isLeft ? portals[portalIdx].left : portals[portalIdx].right;

        Vector2 prev = isLeft ? portals[0].left : portals[0].right;
        for (int j = portalIdx - 1; j >= 0; j--)
        {
            Vector2 candidate = isLeft ? portals[j].left : portals[j].right;
            if ((candidate - curr).sqrMagnitude > 1e-6f)
            {
                prev = candidate;
                break;
            }
        }

        Vector2 next = isLeft ? portals[n - 1].left : portals[n - 1].right;
        for (int j = portalIdx + 1; j < n; j++)
        {
            Vector2 candidate = isLeft ? portals[j].left : portals[j].right;
            if ((candidate - curr).sqrMagnitude > 1e-6f)
            {
                next = candidate;
                break;
            }
        }

        Vector2 edgePrev = curr - prev;
        Vector2 edgeNext = next - curr;
        if (edgePrev.sqrMagnitude < 1e-8f) edgePrev = edgeNext;
        if (edgeNext.sqrMagnitude < 1e-8f) edgeNext = edgePrev;
        if (edgePrev.sqrMagnitude < 1e-8f) return curr;

        Vector2 perpPrev, perpNext;
        if (isLeft)
        {
            perpPrev = new Vector2(edgePrev.y, -edgePrev.x);
            perpNext = new Vector2(edgeNext.y, -edgeNext.x);
        }
        else
        {
            perpPrev = new Vector2(-edgePrev.y, edgePrev.x);
            perpNext = new Vector2(-edgeNext.y, edgeNext.x);
        }

        if (perpPrev.sqrMagnitude > 1e-8f) perpPrev.Normalize();
        if (perpNext.sqrMagnitude > 1e-8f) perpNext.Normalize();

        Vector2 inward = perpPrev + perpNext;
        if (inward.sqrMagnitude < 1e-8f) inward = perpPrev;
        inward.Normalize();

        return curr + inward * unitRadius;
    }

    // ================================================================
    //  FUNNEL ALGORITHM (Simple Stupid Funnel by Mikko Mononen)
    // ================================================================

    /// <summary>
    /// Run the Simple Stupid Funnel Algorithm on a pre-built portal list.
    /// Produces the geometrically shortest path through the corridor.
    /// </summary>
    public static List<Vector2> FunnelFromPortals(List<(Vector2 left, Vector2 right)> portals)
    {
        if (portals.Count <= 2)
        {
            return new List<Vector2>
            {
                portals[0].left,
                portals[portals.Count - 1].left
            };
        }

        Vector2 startPos = portals[0].left;
        Vector2 goalPos = portals[portals.Count - 1].left;

        var path = new List<Vector2> { startPos };

        Vector2 apex = startPos;
        Vector2 funLeft = portals[1].left;
        Vector2 funRight = portals[1].right;
        int apexIndex = 0, leftIndex = 1, rightIndex = 1;
        int maxFunnelIter = portals.Count * 4;
        int funnelIter = 0;

        for (int i = 2; i < portals.Count; i++)
        {
            if (++funnelIter > maxFunnelIter)
            {
                Debug.LogWarning($"[NavPathfinder] Funnel safety limit hit ({maxFunnelIter} iters) — aborting with {path.Count} waypoints");
                break;
            }

            Vector2 newLeft = portals[i].left;
            Vector2 newRight = portals[i].right;

            if (TriArea2(apex, funRight, newRight) <= 0f)
            {
                if (ApproxEqual(apex, funRight) || TriArea2(apex, funLeft, newRight) > 0f)
                {
                    funRight = newRight;
                    rightIndex = i;
                }
                else
                {
                    if (!ApproxEqual(path[path.Count - 1], funLeft))
                        path.Add(funLeft);
                    apex = funLeft;
                    apexIndex = leftIndex;
                    funLeft = apex;
                    funRight = apex;
                    leftIndex = apexIndex;
                    rightIndex = apexIndex;
                    i = apexIndex;
                    continue;
                }
            }

            if (TriArea2(apex, funLeft, newLeft) >= 0f)
            {
                if (ApproxEqual(apex, funLeft) || TriArea2(apex, funRight, newLeft) < 0f)
                {
                    funLeft = newLeft;
                    leftIndex = i;
                }
                else
                {
                    if (!ApproxEqual(path[path.Count - 1], funRight))
                        path.Add(funRight);
                    apex = funRight;
                    apexIndex = rightIndex;
                    funLeft = apex;
                    funRight = apex;
                    leftIndex = apexIndex;
                    rightIndex = apexIndex;
                    i = apexIndex;
                    continue;
                }
            }
        }

        if (!ApproxEqual(path[path.Count - 1], goalPos))
            path.Add(goalPos);

        return path;
    }

    /// <summary>
    /// Get the portal (shared edge) between two adjacent triangles, oriented
    /// consistently as (left, right) relative to the direction of travel.
    /// </summary>
    private static (Vector2 left, Vector2 right) GetPortalEdge(NavMeshData mesh, int tri0, int tri1, Vector2 referencePoint)
    {
        ref var t0 = ref mesh.Triangles[tri0];
        ref var t1 = ref mesh.Triangles[tri1];

        int shared0 = -1, shared1 = -1;
        for (int i = 0; i < 3; i++)
        {
            int v0 = t0.GetVertex(i);
            for (int j = 0; j < 3; j++)
            {
                if (v0 == t1.GetVertex(j))
                {
                    if (shared0 < 0) shared0 = v0;
                    else shared1 = v0;
                }
            }
        }

        if (shared0 < 0 || shared1 < 0)
            return (mesh.GetCentroid(tri0), mesh.GetCentroid(tri1));

        Vector2 a = mesh.Vertices[shared0];
        Vector2 b = mesh.Vertices[shared1];

        Vector2 c0 = mesh.GetCentroid(tri0);
        Vector2 c1 = mesh.GetCentroid(tri1);
        Vector2 forward = c1 - c0;

        float cross = NavMeshData.Cross2D(forward, b - a);
        return cross >= 0 ? (b, a) : (a, b);
    }

    private static float TriArea2(Vector2 a, Vector2 b, Vector2 c)
    {
        float ax = b.x - a.x, ay = b.y - a.y;
        float bx = c.x - a.x, by = c.y - a.y;
        return bx * ay - ax * by;
    }

    private static bool ApproxEqual(Vector2 a, Vector2 b)
    {
        return (a - b).sqrMagnitude < 0.0001f;
    }

    // ================================================================
    //  SOFT COST INFLATION (anti-ghosting: penalize triangles with units)
    // ================================================================

    /// <summary>
    /// Apply soft cost penalty to triangles containing idle/stationary units.
    /// A* routes around dense clusters when an alternative exists.
    /// </summary>
    public static int StatDensityAffectedTris;
    public static float StatDensityMaxCost;
    public static int StatDensityUnitsOutsideMesh;

    public static void ApplyUnitDensityCosts(NavMeshData mesh, IReadOnlyList<Unit> allUnits, float idlePenalty = 30f, float movingPenalty = 5f, float maxPenalty = 100f)
    {
        for (int i = 0; i < mesh.TriangleCount; i++)
            mesh.Triangles[i].CostMultiplier = 1f;

        if (allUnits == null) return;

        int affected = 0;
        float maxCost = 1f;
        int outsideMesh = 0;

        foreach (var unit in allUnits)
        {
            if (unit == null || unit.IsDead) continue;

            Vector2 pos = new Vector2(unit.transform.position.x, unit.transform.position.z);
            int tri = mesh.FindTriangleAtPosition(pos);
            if (tri < 0) { outsideMesh++; continue; }

            var sm = unit.StateMachine;
            bool isIdle = sm != null && (sm.CurrentState == UnitState.Idle || sm.CurrentState == UnitState.Fighting);
            float penalty = isIdle ? idlePenalty : movingPenalty;

            float prevCost = mesh.Triangles[tri].CostMultiplier;
            float newCost = Mathf.Min(prevCost + penalty, maxPenalty);
            mesh.Triangles[tri].CostMultiplier = newCost;

            if (Mathf.Approximately(prevCost, 1f)) affected++;
            if (newCost > maxCost) maxCost = newCost;
        }

        StatDensityAffectedTris = affected;
        StatDensityMaxCost = maxCost;
        StatDensityUnitsOutsideMesh = outsideMesh;
    }
}
