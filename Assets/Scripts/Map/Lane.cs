using UnityEngine;

public class Lane : MonoBehaviour
{
    [SerializeField] private Transform[] waypoints;
    [SerializeField] private int laneIndex;

    public int LaneIndex => laneIndex;
    public int WaypointCount => waypoints != null ? waypoints.Length : 0;

    public Vector3 GetWaypoint(int index)
    {
        if (waypoints == null || index < 0 || index >= waypoints.Length)
            return transform.position;
        return waypoints[index].position;
    }

    public Vector3 GetLastWaypoint()
    {
        if (waypoints == null || waypoints.Length == 0)
            return transform.position;
        return waypoints[waypoints.Length - 1].position;
    }

    private void OnDrawGizmos()
    {
        if (waypoints == null || waypoints.Length < 2) return;

        Gizmos.color = Color.cyan;
        for (int i = 0; i < waypoints.Length; i++)
        {
            if (waypoints[i] == null) continue;
            Gizmos.DrawSphere(waypoints[i].position, 0.5f);
            if (i < waypoints.Length - 1 && waypoints[i + 1] != null)
                Gizmos.DrawLine(waypoints[i].position, waypoints[i + 1].position);
        }
    }
}
