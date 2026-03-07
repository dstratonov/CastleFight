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

    public bool ContainsPoint(Vector3 worldPoint)
    {
        return zone.bounds.Contains(worldPoint);
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
