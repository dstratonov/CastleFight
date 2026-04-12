using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Runtime keyboard toggles for debug overlays and logging.
/// Reflects the A* Pro architecture — there is no grid overlay.
///
/// Logging:
///   F1 = Pathfinding logs
///   F2 = Movement logs
///   F7 = Combat logs
///
/// Visualisation (via DebugOverlay):
///   F3 = Unit paths (lines from units to their destinations)
///   F4 = A* Pro NavMesh triangles
///   F5 = Attack range rectangles
///   F6 = Velocity arrows
///   F8 = RVO agent radii (local avoidance circles)
///   F11 = Building footprints (real BoxCollider bounds)
///   F12 = Build zones
///
/// Presets:
///   F9 = All ON
///   F10 = All OFF
/// </summary>
public class PathfindingDebugToggle : MonoBehaviour
{
    private void Update()
    {
        if (WasKeyPressed(Key.F1))
        {
            GameDebug.Pathfinding = !GameDebug.Pathfinding;
            Debug.Log($"[DebugToggle] Pathfinding logs = {GameDebug.Pathfinding}");
        }

        if (WasKeyPressed(Key.F2))
        {
            GameDebug.Movement = !GameDebug.Movement;
            Debug.Log($"[DebugToggle] Movement logs = {GameDebug.Movement}");
        }

        if (WasKeyPressed(Key.F3))
        {
            var o = DebugOverlay.Instance;
            if (o != null)
            {
                o.showPaths = !o.showPaths;
                Debug.Log($"[DebugToggle] Paths = {o.showPaths}");
            }
        }

        if (WasKeyPressed(Key.F4))
        {
            var o = DebugOverlay.Instance;
            if (o != null)
            {
                o.showNavMesh = !o.showNavMesh;
                Debug.Log($"[DebugToggle] NavMesh = {o.showNavMesh}");
            }
        }

        if (WasKeyPressed(Key.F5))
        {
            var o = DebugOverlay.Instance;
            if (o != null)
            {
                o.showAttackRange = !o.showAttackRange;
                Debug.Log($"[DebugToggle] Attack range = {o.showAttackRange}");
            }
        }

        if (WasKeyPressed(Key.F6))
        {
            var o = DebugOverlay.Instance;
            if (o != null)
            {
                o.showVelocities = !o.showVelocities;
                Debug.Log($"[DebugToggle] Velocity arrows = {o.showVelocities}");
            }
        }

        if (WasKeyPressed(Key.F7))
        {
            GameDebug.Combat = !GameDebug.Combat;
            Debug.Log($"[DebugToggle] Combat logs = {GameDebug.Combat}");
        }

        if (WasKeyPressed(Key.F8))
        {
            var o = DebugOverlay.Instance;
            if (o != null)
            {
                o.showUnitRadius = !o.showUnitRadius;
                Debug.Log($"[DebugToggle] Unit radius = {o.showUnitRadius}");
            }
        }

        if (WasKeyPressed(Key.F11))
        {
            var o = DebugOverlay.Instance;
            if (o != null)
            {
                o.showBuildingFootprints = !o.showBuildingFootprints;
                Debug.Log($"[DebugToggle] Building footprints = {o.showBuildingFootprints}");
            }
        }

        if (WasKeyPressed(Key.F12))
        {
            var o = DebugOverlay.Instance;
            if (o != null)
            {
                o.showBuildZones = !o.showBuildZones;
                Debug.Log($"[DebugToggle] Build zones = {o.showBuildZones}");
            }
        }

        if (WasKeyPressed(Key.F9))
        {
            GameDebug.EnableAll();
            var o = DebugOverlay.Instance;
            if (o != null)
            {
                o.showNavMesh = true;
                o.showPaths = true;
                o.showVelocities = true;
                o.showAttackRange = true;
                o.showUnitRadius = true;
                o.showBuildingFootprints = true;
                o.showBuildZones = true;
            }
            Debug.Log("[DebugToggle] ALL DEBUG ON");
        }

        if (WasKeyPressed(Key.F10))
        {
            GameDebug.DisableAll();
            var o = DebugOverlay.Instance;
            if (o != null)
            {
                o.showNavMesh = false;
                o.showPaths = false;
                o.showVelocities = false;
                o.showAttackRange = false;
                o.showUnitRadius = false;
                o.showBuildingFootprints = false;
                o.showBuildZones = false;
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
}
