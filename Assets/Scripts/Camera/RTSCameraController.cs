using UnityEngine;
using UnityEngine.InputSystem;

public class RTSCameraController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float panSpeed = 20f;
    [SerializeField] private float edgeScrollSpeed = 15f;
    [SerializeField] private int edgeScrollBorder = 10;
    [SerializeField] private bool enableEdgeScrolling = false;

    [Header("Zoom")]
    [SerializeField] private float zoomSpeed = 5f;
    [SerializeField] private float minZoomHeight = 8f;
    [SerializeField] private float maxZoomHeight = 80f;

    [Header("Drag")]
    [SerializeField] private float dragSpeed = 2f;

    [Header("Bounds")]
    [SerializeField] private Vector2 boundsMin = new(-50f, -50f);
    [SerializeField] private Vector2 boundsMax = new(50f, 50f);

    private Vector3 dragOrigin;
    private bool isDragging;

    private void Update()
    {
        if (!Application.isFocused) return;

        var keyboard = Keyboard.current;
        var mouse = Mouse.current;
        if (keyboard == null || mouse == null) return;

        HandleKeyboardPan(keyboard);
        if (enableEdgeScrolling && IsMouseInsideScreen(mouse)) HandleEdgeScroll(mouse);
        HandleMouseDrag(mouse);
        HandleZoom(mouse);
        ClampPosition();
    }

    private bool IsMouseInsideScreen(Mouse mouse)
    {
        Vector2 mp = mouse.position.ReadValue();
        return mp.x >= 0 && mp.x <= Screen.width &&
               mp.y >= 0 && mp.y <= Screen.height;
    }

    private void HandleKeyboardPan(Keyboard kb)
    {
        var input = Vector3.zero;

        if (kb.wKey.isPressed || kb.upArrowKey.isPressed)
            input.z += 1f;
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed)
            input.z -= 1f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)
            input.x -= 1f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed)
            input.x += 1f;

        transform.Translate(input.normalized * (panSpeed * Time.deltaTime), Space.World);
    }

    private void HandleEdgeScroll(Mouse mouse)
    {
        var input = Vector3.zero;
        var mousePos = mouse.position.ReadValue();

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

    private void HandleMouseDrag(Mouse mouse)
    {
        if (mouse.middleButton.wasPressedThisFrame)
        {
            isDragging = true;
            dragOrigin = (Vector3)mouse.position.ReadValue();
        }

        if (mouse.middleButton.wasReleasedThisFrame)
            isDragging = false;

        if (isDragging)
        {
            Vector3 current = (Vector3)mouse.position.ReadValue();
            Vector3 delta = dragOrigin - current;
            transform.Translate(
                new Vector3(delta.x, 0, delta.y) * (dragSpeed * Time.deltaTime),
                Space.World
            );
            dragOrigin = current;
        }
    }

    private void HandleZoom(Mouse mouse)
    {
        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Approximately(scroll, 0f)) return;

        float scrollNormalized = Mathf.Sign(scroll);
        Vector3 pos = transform.position;
        pos.y -= scrollNormalized * zoomSpeed;
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
