# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Castle Fight is a Unity 6 (6000.3.2f1) multiplayer RTS game inspired by the Warcraft 3 custom map "Castle Fight". Players build structures that auto-spawn units; units march toward the enemy castle. Networking uses Mirror. The single scene is `Assets/Scenes/SampleScene.unity`.

## Build & Test

This is a Unity project — there is no CLI build. Open in Unity Editor and use:

- **Build:** File → Build Settings → Build (Windows Standalone)
- **Run tests:** Window → General → Test Runner → EditMode → Run All
- **Run single test:** Right-click a test in Test Runner, or use the filter bar to search by name
- **Test location:** `Assets/Tests/Editor/` — 11 NUnit `[TestFixture]` classes (254 tests)
- **No linter configured.** No .editorconfig, StyleCop, or Roslyn analyzers.
- **C# 9.0**, .NET 4.7.1 target

## Architecture

### Grid A* Pathfinding

`GridAStar` performs 8-directional A* directly on the `GridSystem` walkability grid. No NavMesh, no boids, no steering — just grid cells and waypoints.

- Diagonal movement requires both adjacent cardinal cells walkable (no corner-cutting)
- Path smoothing via line-of-sight removes unnecessary waypoints
- `UnitGridPresence` marks cells occupied by units (auto-added by `PathfindingManager`)
- `ClearanceMap` precomputes passability for different unit sizes
- Buildings/terrain are obstacles; units are NOT obstacles for pathfinding
- `PathfindingManager` manages throttling, group path caching (units sharing destinations), and replan scheduling

### Core Systems

- **EventBus** (`Core/EventBus.cs`) — Thread-safe pub/sub. 10 event structs in `GameEvents.cs` with readonly fields. Use `EventBus.Raise()` / `EventBus.Subscribe<T>()`.
- **GameRegistry** (`Core/GameRegistry.cs`) — Registry for castles and build zones. Use this instead of `FindObjectsByType`.
- **GridSystem** (`Grid/GridSystem.cs`) — Singleton grid managing walkability and building footprints. `GridLogic` is the pure-logic counterpart for testing.
- **UnitManager / BuildingManager** — Registries for live units/buildings. Return `IReadOnlyList<T>`.

### Unit Lifecycle

`Unit.cs` is the entity root. Key cached components: `Unit.Movement` (UnitMovement), `Unit.StateMachine` (UnitStateMachine).

**States:** Idle → Moving → Fighting → Dying

Combat system (UnitCombat, AttackPositionFinder, CombatTargeting) was removed during the pathfinding rewrite and is not yet reimplemented.

### Pure Logic Classes (Testable without Unity)

Several MonoBehaviour systems have extracted pure-logic counterparts with no Unity dependencies:

| MonoBehaviour | Pure Logic | Purpose |
|---|---|---|
| UnitAnimator | AnimationLogic | Animation state transitions |
| UnitStateMachine | UnitStateLogic | State machine decisions |
| GridSystem | GridLogic | Grid cell operations |
| — | PathInvalidation | Path validity checks |

### Networking

Mirror-based. `NetworkPlayer` syncs player state (gold, income, team, race). `Unit` and `Building` are `NetworkBehaviour`. Server-authoritative with no client prediction.

### Data Configuration

`GameConfig` (ScriptableObject at `Resources/GameConfig`) holds economy, hero, combat, and castle settings. Access via `GameConfig.Instance`.

`BuildingData` and `UnitData` ScriptableObjects define per-type stats. `RaceData` groups buildings into races. `TechTree` handles progression.

## Key Patterns

### ResetStatics (Domain Reload Safety)

Every singleton/manager implements this pattern — **always add it to new singletons:**

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
private static void ResetStatics()
{
    Instance = null;
}
```

Tests must call `ResetStatics()` in `[SetUp]`/`[TearDown]` for isolation.

### ISelectable Interface

`Unit`, `Building`, and `Castle` implement `ISelectable` (TeamId, DisplayName, Health, gameObject). Use this for selection/UI code instead of type-checking.

### Avoid FindObjectsByType

Use `GameRegistry`, `UnitManager.AllUnits`, `BuildingManager.GetBuildings()`, or cached component references. Cache expensive lookups in static dictionaries with `ResetStatics` cleanup.

### Path Invalidation Flow

Buildings modify the grid → `PathfindingManager` receives `BuildingPlacedEvent`/`BuildingDestroyedEvent` → all active unit paths are invalidated → replans are spread across frames.

### Performance Constraints

- A* capped at 30 path requests/frame (`MaxPathRequestsPerFrame`)
- Replans spread at max 15/frame via pending queue
- Group path cache: units heading to same destination share one A* result
- `GameDebug` logging uses `[Conditional]` attributes — stripped in release builds

## Debug Tools

Runtime F-key toggles (via `PathfindingDebugToggle`):

| Key | Visualization |
|---|---|
| F1 | Pathfinding logs |
| F2 | Movement logs |
| F3 | Unit grid cells |
| F4 | NavMesh overlay |
| F6 | Velocity arrows |
| F8 | Validate mesh |
| F9 | All ON |
| F10 | All OFF |

`GameDebug` has 14 category flags (Combat, Movement, Animation, Pathfinding, Boids, etc.) controlled via `GameDebug.Enable*()` / `GameDebug.Disable*()`.

## Task Tracking

Active development tasks are tracked in `tasks.md` at the project root, organized by status with orchestrator summaries of completed work.
