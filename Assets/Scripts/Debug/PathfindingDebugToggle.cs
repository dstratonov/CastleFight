using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Runtime keyboard toggle for debug overlays.
/// F1 = Pathfinding logs, F2 = Movement logs, F3 = Unit cells,
/// F4 = Grid walkability, F5 = Attack range, F6 = Velocity arrows,
/// F7 = Combat logs, F9 = All ON, F10 = All OFF.
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
            var overlay = DebugOverlay.Instance;
            if (overlay != null)
            {
                overlay.showUnitCells = !overlay.showUnitCells;
                Debug.Log($"[DebugToggle] Unit cells = {overlay.showUnitCells}");
            }
        }

        if (WasKeyPressed(Key.F4))
        {
            var overlay = DebugOverlay.Instance;
            if (overlay != null)
            {
                overlay.showNavMesh = !overlay.showNavMesh;
                Debug.Log($"[DebugToggle] Walkability = {overlay.showNavMesh}");
            }
        }

        if (WasKeyPressed(Key.F5))
        {
            var overlay = DebugOverlay.Instance;
            if (overlay != null)
            {
                overlay.showAttackRange = !overlay.showAttackRange;
                Debug.Log($"[DebugToggle] Attack range = {overlay.showAttackRange}");
            }
        }

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
            GameDebug.Combat = !GameDebug.Combat;
            Debug.Log($"[DebugToggle] Combat = {GameDebug.Combat}");
        }

        if (WasKeyPressed(Key.F9))
        {
            GameDebug.EnableAll();
            var overlay = DebugOverlay.Instance;
            if (overlay != null)
            {
                overlay.showNavMesh = true;
                overlay.showPaths = true;
                overlay.showVelocities = true;
                overlay.showUnitCells = true;
                overlay.showAttackRange = true;
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
                overlay.showPaths = false;
                overlay.showVelocities = false;
                overlay.showUnitCells = false;
                overlay.showAttackRange = false;
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
