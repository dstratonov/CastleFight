using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Runtime keyboard toggle for pathfinding debug flags.
/// Auto-attached to PathfindingManager on initialization.
/// F1 = Pathfinding logs, F2 = Movement logs, F3 = Boids logs,
/// F4 = NavMesh overlay, F5 = NavMesh widths, F6 = Velocity arrows,
/// F7 = Boids forces, F9 = All ON, F10 = All OFF, F8 = Validate mesh.
/// </summary>
public class PathfindingDebugToggle : MonoBehaviour
{
    private void Update()
    {
        if (WasKeyPressed(Key.F1))
        {
            GameDebug.Pathfinding = !GameDebug.Pathfinding;
            Debug.Log($"[DebugToggle] Pathfinding = {GameDebug.Pathfinding}");
        }

        if (WasKeyPressed(Key.F2))
        {
            GameDebug.Movement = !GameDebug.Movement;
            Debug.Log($"[DebugToggle] Movement = {GameDebug.Movement}");
        }

        if (WasKeyPressed(Key.F3))
        {
            // Boids removed — no-op
            Debug.Log("[DebugToggle] Boids system removed");
        }

        if (WasKeyPressed(Key.F4))
        {
            var overlay = DebugOverlay.Instance;
            if (overlay != null)
            {
                overlay.showNavMesh = !overlay.showNavMesh;
                Debug.Log($"[DebugToggle] NavMesh overlay = {overlay.showNavMesh}");
            }
        }

        // F5: was NavMesh widths (removed with NavMesh)

        if (WasKeyPressed(Key.F6))
        {
            var overlay = DebugOverlay.Instance;
            if (overlay != null)
            {
                overlay.showVelocities = !overlay.showVelocities;
                Debug.Log($"[DebugToggle] Velocity arrows = {overlay.showVelocities}");
            }
        }

        if (WasKeyPressed(Key.F7))
        {
            var overlay = DebugOverlay.Instance;
            if (overlay != null)
            {
                // Boids forces removed
                Debug.Log("[DebugToggle] Boids forces removed");
            }
        }

        if (WasKeyPressed(Key.F8))
        {
            ValidateNavMeshNow();
        }

        if (WasKeyPressed(Key.F9))
        {
            GameDebug.EnableAll();
            var overlay = DebugOverlay.Instance;
            if (overlay != null)
            {
                overlay.showNavMesh = true;
                overlay.showPaths = true;
                // showNavMeshWidths removed
                overlay.showVelocities = true;
                // showBoidsForces removed
            }
            Debug.Log("[DebugToggle] ALL DEBUG ON");
        }

        if (WasKeyPressed(Key.F10))
        {
            GameDebug.DisableAll();
            var overlay = DebugOverlay.Instance;
            if (overlay != null)
            {
                overlay.showNavMesh = false;
                // showNavMeshWidths removed
                overlay.showVelocities = false;
                // showBoidsForces removed
            }
            Debug.Log("[DebugToggle] ALL DEBUG OFF");
        }
    }

    private static bool WasKeyPressed(Key key)
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current[key].wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyToKeyCode(key));
#endif
    }

#if !ENABLE_INPUT_SYSTEM
    private static KeyCode KeyToKeyCode(Key key)
    {
        return key switch
        {
            Key.F1 => KeyCode.F1, Key.F2 => KeyCode.F2, Key.F3 => KeyCode.F3,
            Key.F4 => KeyCode.F4, Key.F5 => KeyCode.F5, Key.F6 => KeyCode.F6,
            Key.F7 => KeyCode.F7, Key.F8 => KeyCode.F8, Key.F9 => KeyCode.F9,
            Key.F10 => KeyCode.F10, Key.F11 => KeyCode.F11, Key.F12 => KeyCode.F12,
            _ => KeyCode.None,
        };
    }
#endif

    private void ValidateNavMeshNow()
    {
        var pfm = PathfindingManager.Instance;
        if (pfm == null || !pfm.IsInitialized)
        {
            Debug.LogWarning("[DebugToggle] PathfindingManager not initialized");
            return;
        }

        Debug.Log("[DebugToggle] Grid-based A* — no NavMesh to validate");
    }
}
