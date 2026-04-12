using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class RTSCameraController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float basePanSpeed = 25f;
    [SerializeField] private float panAcceleration = 8f;
    [SerializeField] private float panDamping = 6f;

    [Header("Edge Scrolling")]
    [SerializeField] private bool enableEdgeScrolling = true;
    [SerializeField] private float edgeScrollSpeed = 20f;
    [SerializeField] private int edgeScrollBorder = 12;

    [Header("Zoom")]
    [SerializeField] private float zoomStepPercent = 0.20f;
    [SerializeField] private float zoomLerpSpeed = 25f;
    [SerializeField] private float minHeight = 10f;
    [SerializeField] private float maxHeight = 70f;

    [Header("Bounds")]
    [SerializeField] private Vector2 boundsMin = new(-80f, -80f);
    [SerializeField] private Vector2 boundsMax = new(80f, 80f);

    private Camera cam;
    private Vector3 velocity;
    private float targetHeight;
    private Vector3 targetXZ;
    private bool isDragging;
    private Vector3 dragWorldOrigin;

    private Vector3? focusTarget;
    private float focusSpeed = 5f;

    private Plane groundPlane;

    private void Start()
    {
        cam = Camera.main;
        targetHeight = Mathf.Clamp(transform.position.y, minHeight, maxHeight);
        targetXZ = new Vector3(transform.position.x, 0f, transform.position.z);

        Vector3 pos = transform.position;
        pos.y = targetHeight;
        transform.position = pos;

        groundPlane = new Plane(Vector3.up, Vector3.zero);
    }

    private void Update()
    {
        if (!Application.isFocused) return;

        if (cam == null)
        {
            cam = Camera.main;
            if (cam == null) return;
        }

        var keyboard = Keyboard.current;
        var mouse = Mouse.current;
        if (keyboard == null || mouse == null) return;

        if (keyboard.spaceKey.wasPressedThisFrame)
        {
            FocusOnAllyCastle();
        }

        if (focusTarget.HasValue)
        {
            UpdateFocus(keyboard, mouse);
            ApplyZoomInterpolation();
            ClampPosition();
            return;
        }

        bool pointerOverUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        Vector3 input = GetKeyboardInput(keyboard);
        if (enableEdgeScrolling && !pointerOverUI && IsMouseInsideScreen(mouse))
            input += GetEdgeScrollInput(mouse);

        float heightRatio = Mathf.Lerp(0.5f, 2f, Mathf.InverseLerp(minHeight, maxHeight, transform.position.y));
        Vector3 desiredVelocity = input.normalized * basePanSpeed * heightRatio;

        if (input.sqrMagnitude > 0.01f)
            velocity = Vector3.Lerp(velocity, desiredVelocity, panAcceleration * Time.deltaTime);
        else
            velocity = Vector3.Lerp(velocity, Vector3.zero, panDamping * Time.deltaTime);

        transform.position += velocity * Time.deltaTime;
        targetXZ = new Vector3(transform.position.x, 0f, transform.position.z);

        HandleMouseDrag(mouse, pointerOverUI);
        HandleZoom(mouse, pointerOverUI);
        ApplyZoomInterpolation();
        ClampPosition();
    }

    private Vector3 GetKeyboardInput(Keyboard kb)
    {
        float inputX = 0f;
        float inputZ = 0f;
        if (kb.wKey.isPressed || kb.upArrowKey.isPressed) inputZ += 1f;
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed) inputZ -= 1f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) inputX -= 1f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) inputX += 1f;

        if (Mathf.Approximately(inputX, 0f) && Mathf.Approximately(inputZ, 0f))
            return Vector3.zero;

        Vector3 forward = cam.transform.forward;
        Vector3 right = cam.transform.right;
        forward.y = 0f;
        right.y = 0f;
        forward.Normalize();
        right.Normalize();

        return (forward * inputZ + right * inputX);
    }

    private Vector3 GetEdgeScrollInput(Mouse mouse)
    {
        float inputX = 0f;
        float inputZ = 0f;
        var mp = mouse.position.ReadValue();
        if (mp.y >= Screen.height - edgeScrollBorder) inputZ += 1f;
        if (mp.y <= edgeScrollBorder) inputZ -= 1f;
        if (mp.x >= Screen.width - edgeScrollBorder) inputX += 1f;
        if (mp.x <= edgeScrollBorder) inputX -= 1f;

        if (Mathf.Approximately(inputX, 0f) && Mathf.Approximately(inputZ, 0f))
            return Vector3.zero;

        Vector3 forward = cam.transform.forward;
        Vector3 right = cam.transform.right;
        forward.y = 0f;
        right.y = 0f;
        forward.Normalize();
        right.Normalize();

        return (forward * inputZ + right * inputX) * (edgeScrollSpeed / basePanSpeed);
    }

    private bool IsMouseInsideScreen(Mouse mouse)
    {
        Vector2 mp = mouse.position.ReadValue();
        return mp.x >= 0 && mp.x <= Screen.width && mp.y >= 0 && mp.y <= Screen.height;
    }

    private void HandleMouseDrag(Mouse mouse, bool pointerOverUI)
    {
        if (mouse.middleButton.wasPressedThisFrame && !pointerOverUI)
        {
            Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            if (groundPlane.Raycast(ray, out float enter))
            {
                isDragging = true;
                dragWorldOrigin = ray.GetPoint(enter);
                velocity = Vector3.zero;
            }
        }

        if (mouse.middleButton.wasReleasedThisFrame)
            isDragging = false;

        if (isDragging)
        {
            Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            if (groundPlane.Raycast(ray, out float enter))
            {
                Vector3 dragWorldCurrent = ray.GetPoint(enter);
                Vector3 delta = dragWorldOrigin - dragWorldCurrent;
                delta.y = 0f;
                transform.position += delta;
                targetXZ = new Vector3(transform.position.x, 0f, transform.position.z);
            }
        }
    }

    private void HandleZoom(Mouse mouse, bool pointerOverUI)
    {
        float scrollRaw = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scrollRaw) < 0.1f || pointerOverUI)
            return;

        // Normalize: sign gives direction, magnitude scales with scroll speed
        // Standard Windows notch = 120, but we handle any value
        float scrollSign = Mathf.Sign(scrollRaw);
        float scrollMag = Mathf.Abs(scrollRaw);

        // Number of "notches" — continuous for smooth wheels, discrete for clicky wheels
        float notches = scrollMag >= 20f ? Mathf.Clamp(scrollMag / 120f, 0.5f, 3f) : 1f;
        notches *= scrollSign;

        float oldTarget = targetHeight;
        targetHeight *= Mathf.Pow(1f - zoomStepPercent, notches);
        targetHeight = Mathf.Clamp(targetHeight, minHeight, maxHeight);

        // Zoom-to-cursor: shift XZ toward/away from cursor proportionally
        float heightChange = targetHeight / oldTarget;
        if (Mathf.Abs(heightChange - 1f) > 0.001f)
        {
            Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            if (groundPlane.Raycast(ray, out float enter))
            {
                Vector3 cursorGround = ray.GetPoint(enter);
                Vector3 camXZ = new Vector3(transform.position.x, 0f, transform.position.z);
                Vector3 toward = new Vector3(cursorGround.x, 0f, cursorGround.z) - camXZ;
                float pullFactor = 1f - heightChange;
                targetXZ = camXZ + toward * pullFactor;
            }
        }
    }

    private void ApplyZoomInterpolation()
    {
        Vector3 pos = transform.position;

        float t = 1f - Mathf.Exp(-zoomLerpSpeed * Time.deltaTime);

        pos.y = Mathf.Lerp(pos.y, targetHeight, t);
        pos.x = Mathf.Lerp(pos.x, targetXZ.x, t);
        pos.z = Mathf.Lerp(pos.z, targetXZ.z, t);

        if (Mathf.Abs(pos.y - targetHeight) < 0.01f)
            pos.y = targetHeight;

        transform.position = pos;
    }

    private void ClampPosition()
    {
        Vector3 pos = transform.position;
        pos.x = Mathf.Clamp(pos.x, boundsMin.x, boundsMax.x);
        pos.z = Mathf.Clamp(pos.z, boundsMin.y, boundsMax.y);
        transform.position = pos;
        targetXZ.x = pos.x;
        targetXZ.z = pos.z;
    }

    public void FocusOn(Vector3 worldPosition)
    {
        focusTarget = new Vector3(worldPosition.x, transform.position.y, worldPosition.z);
        velocity = Vector3.zero;
    }

    /// <summary>Snaps camera to the local player's allied castle.</summary>
    public void FocusOnAllyCastle()
    {
        var local = NetworkPlayer.Local;
        if (local == null) return;

        var castle = GameRegistry.GetCastle(local.TeamId);
        if (castle != null)
            FocusOn(castle.transform.position);
    }

    private void UpdateFocus(Keyboard keyboard, Mouse mouse)
    {
        if (!focusTarget.HasValue) return;

        bool hasKeyInput = keyboard.wKey.isPressed || keyboard.sKey.isPressed ||
                           keyboard.aKey.isPressed || keyboard.dKey.isPressed ||
                           keyboard.upArrowKey.isPressed || keyboard.downArrowKey.isPressed ||
                           keyboard.leftArrowKey.isPressed || keyboard.rightArrowKey.isPressed;
        bool hasDrag = mouse.middleButton.wasPressedThisFrame;

        if (hasKeyInput || hasDrag)
        {
            focusTarget = null;
            return;
        }

        Vector3 pos = transform.position;
        Vector3 target = focusTarget.Value;
        pos = Vector3.Lerp(pos, target, focusSpeed * Time.deltaTime);
        transform.position = pos;
        targetXZ = new Vector3(pos.x, 0f, pos.z);

        if (Vector3.Distance(new Vector3(pos.x, 0, pos.z), new Vector3(target.x, 0, target.z)) < 0.1f)
            focusTarget = null;
    }
}
