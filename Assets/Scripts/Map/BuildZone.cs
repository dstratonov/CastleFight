using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class BuildZone : MonoBehaviour
{
    [SerializeField] private int teamId;

    private BoxCollider zone;

    public int TeamId => teamId;

    private void Awake()
    {
        zone = GetComponent<BoxCollider>();
        zone.isTrigger = true;
    }

    private void OnEnable()
    {
        GameRegistry.RegisterBuildZone(this);
    }

    private void OnDisable()
    {
        GameRegistry.UnregisterBuildZone(this);
    }

    public bool ContainsPoint(Vector3 worldPoint)
    {
        return zone.bounds.Contains(worldPoint);
    }

    /// <summary>
    /// True when <paramref name="worldPoint"/> is inside any build zone owned
    /// by <paramref name="teamId"/>. If no build zones exist, returns true
    /// (placement unrestricted). Use from server-side placement code so grid
    /// is no longer needed as a placement gate.
    /// </summary>
    public static bool Contains(int teamId, Vector3 worldPoint)
    {
        var zones = GameRegistry.BuildZones;
        if (zones == null || zones.Count == 0) return true;
        for (int i = 0; i < zones.Count; i++)
        {
            var z = zones[i];
            if (z == null || z.TeamId != teamId) continue;
            if (z.ContainsPoint(worldPoint)) return true;
        }
        return false;
    }

    private void OnDrawGizmos()
    {
        var col = GetComponent<BoxCollider>();
        if (col == null) return;

        Gizmos.color = teamId == 0
            ? new Color(0f, 0f, 1f, 0.15f)
            : new Color(1f, 0f, 0f, 0.15f);

        Matrix4x4 oldMatrix = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(col.center, col.size);
        Gizmos.DrawWireCube(col.center, col.size);
        Gizmos.matrix = oldMatrix;
    }
}
