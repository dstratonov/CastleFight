using UnityEngine;

public class RTSCameraController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float panSpeed = 20f;
    [SerializeField] private float edgeScrollSpeed = 15f;
    [SerializeField] private int edgeScrollBorder = 10;
    [SerializeField] private bool enableEdgeScrolling = true;

    [Header("Zoom")]
    [SerializeField] private float zoomSpeed = 5f;
    [SerializeField] private float minZoomHeight = 10f;
    [SerializeField] private float maxZoomHeight = 50f;

    [Header("Drag")]
    [SerializeField] private float dragSpeed = 2f;

    [Header("Bounds")]
    [SerializeField] private Vector2 boundsMin = new(-50f, -50f);
    [SerializeField] private Vector2 boundsMax = new(50f, 50f);

    private Vector3 dragOrigin;
    private bool isDragging;
    private Camera cam;

    private void Awake()
    {
        cam = Camera.main;
    }

    private void Update()
    {
        HandleKeyboardPan();
        if (enableEdgeScrolling) HandleEdgeScroll();
        HandleMouseDrag();
        HandleZoom();
        ClampPosition();
    }

    private void HandleKeyboardPan()
    {
        var input = Vector3.zero;

        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
            input.z += 1f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
            input.z -= 1f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
            input.x -= 1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
            input.x += 1f;

        transform.Translate(input.normalized * (panSpeed * Time.deltaTime), Space.World);
    }

    private void HandleEdgeScroll()
    {
        var input = Vector3.zero;
        var mousePos = Input.mousePosition;

        if (mousePos.y >= Screen.height - edgeScrollBorder)
            input.z += 1f;
        if (mousePos.y <= edgeScrollBorder)
            input.z -= 1f;
        if (mousePos.x >= Screen.width - edgeScrollBorder)
            input.x += 1f;
        if (mousePos.x <= edgeScrollBorder)
            input.x -= 1f;

        transform.Translate(input.normalized * (edgeScrollSpeed * Time.deltaTime), Space.World);
    }

    private void HandleMouseDrag()
    {
        if (Input.GetMouseButtonDown(2))
        {
            isDragging = true;
            dragOrigin = Input.mousePosition;
        }

        if (Input.GetMouseButtonUp(2))
            isDragging = false;

        if (isDragging)
        {
            Vector3 delta = dragOrigin - Input.mousePosition;
            transform.Translate(
                new Vector3(delta.x, 0, delta.y) * (dragSpeed * Time.deltaTime),
                Space.World
            );
            dragOrigin = Input.mousePosition;
        }
    }

    private void HandleZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Approximately(scroll, 0f)) return;

        Vector3 pos = transform.position;
        pos.y -= scroll * zoomSpeed * 100f * Time.deltaTime;
        pos.y = Mathf.Clamp(pos.y, minZoomHeight, maxZoomHeight);
        transform.position = pos;
    }

    private void ClampPosition()
    {
        Vector3 pos = transform.position;
        pos.x = Mathf.Clamp(pos.x, boundsMin.x, boundsMax.x);
        pos.z = Mathf.Clamp(pos.z, boundsMin.y, boundsMax.y);
        transform.position = pos;
    }

    public void FocusOn(Vector3 worldPosition)
    {
        Vector3 pos = transform.position;
        pos.x = worldPosition.x;
        pos.z = worldPosition.z;
        transform.position = pos;
    }
}
