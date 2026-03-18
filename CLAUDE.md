# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Castle Fight is a Unity 6 (6000.3.2f1) multiplayer RTS game inspired by the Warcraft 3 custom map "Castle Fight". Players build structures that auto-spawn units; units march toward the enemy castle. Networking uses Mirror. The single scene is `Assets/Scenes/SampleScene.unity`.

## Build & Test

This is a Unity project — there is no CLI build. Open in Unity Editor and use:

- **Build:** File → Build Settings → Build (Windows Standalone)
- **Run tests:** Window → General → Test Runner → EditMode → Run All
- **Run single test:** Right-click a test in Test Runner, or use the filter bar to search by name
- **Test location:** `Assets/Tests/Editor/` — all 56 test files are NUnit `[TestFixture]` classes (629+ tests)
- **No linter configured.** No .editorconfig, StyleCop, or Roslyn analyzers.
- **C# 9.0**, .NET 4.7.1 target

## Architecture

### Two-Layer Pathfinding (SC2-style)

The pathfinding system has two completely independent layers with no shared state:

**Layer 1 — Strategic (NavMesh):** `CDTriangulator` builds a Constrained Delaunay Triangulation from the grid. `NavMeshPathfinder` runs A* on triangles, then the Funnel algorithm produces waypoints. Vertex expansion offsets corners by unit radius. The base NavMesh is cached and rebuilt only when buildings are placed/destroyed (deferred to next frame).

**Layer 2 — Tactical (Boids):** `BoidsManager` handles unit-to-unit avoidance via separation, alignment, cohesion, and side-steering. Uses `SpatialHashGrid` for O(1) neighbor queries. Density stop at 60% area occupancy is **bypassed during combat approach**.

### Core Systems

- **EventBus** (`Core/EventBus.cs`) — Thread-safe pub/sub. 10 event structs in `GameEvents.cs` with readonly fields. Use `EventBus.Raise()` / `EventBus.Subscribe<T>()`.
- **GameRegistry** (`Core/GameRegistry.cs`) — Registry for castles and build zones. Use this instead of `FindObjectsByType`.
- **GridSystem** (`Grid/GridSystem.cs`) — Singleton grid managing walkability and building footprints. `GridLogic` is the pure-logic counterpart for testing.
- **UnitManager / BuildingManager** — Registries for live units/buildings. Return `IReadOnlyList<T>`.

### Unit Lifecycle

`Unit.cs` is the entity root. Key cached components: `Unit.Movement` (UnitMovement), `Unit.Combat` (UnitCombat), `Unit.StateMachine` (UnitStateMachine).

**States:** Idle → Moving → Fighting → Dying

**Combat flow:** `UnitCombat` scans for targets → `AttackPositionFinder` finds a slot (Dijkstra-based, max 4 engagers per target) → `UnitMovement` follows path → attack when in range. SC2-style fallback: when all slots are claimed, find closest walkable cell within attack range.

### Pure Logic Classes (Testable without Unity)

Several MonoBehaviour systems have extracted pure-logic counterparts with no Unity dependencies:

| MonoBehaviour | Pure Logic | Purpose |
|---|---|---|
| UnitMovement | MovementLogic | Stuck detection, density checks |
| UnitAnimator | AnimationLogic | Animation state transitions |
| UnitStateMachine | UnitStateLogic | State machine decisions |
| GridSystem | GridLogic | Grid cell operations |
| — | CombatTargeting | Target selection, approach progress |
| — | PathInvalidation | Path validity checks |

### Networking

Mirror-based. `NetworkPlayer` syncs player state (gold, income, team, race). `Unit` and `Building` are `NetworkBehaviour`. Combat is server-authoritative with no client prediction. Boids run client-side only.

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

### NavMesh Rebuild Flow

Buildings modify the grid → `PathfindingManager` receives `BuildingPlacedEvent`/`BuildingDestroyedEvent` → rebuild is deferred to next frame via `pendingRebuild` flag → full CDT rebuild from grid state. The NavMesh is never incrementally patched.

### Performance Constraints

- A* capped at 20 path requests/frame (`MaxPathRequestsPerFrame`)
- Replan staggered across 15 frames (`replanFrameSlot = instanceID % 15`)
- Boids use `SpatialHashGrid` — never iterate all units for neighbor queries
- `GameDebug` logging uses `[Conditional]` attributes — stripped in release builds

## Debug Tools

Runtime F-key toggles (via `PathfindingDebugToggle`):

| Key | Visualization |
|---|---|
| F1 | Pathfinding paths |
| F2 | Movement diagnostics |
| F3 | Boids forces |
| F4 | NavMesh overlay |
| F5 | Portal widths |
| F6 | Velocity arrows |
| F7 | Boids separation/cohesion |
| F8 | Validate mesh |
| F9 | All ON |
| F10 | All OFF |

`GameDebug` has 14 category flags (Combat, Movement, Animation, Pathfinding, Boids, etc.) controlled via `GameDebug.Enable*()` / `GameDebug.Disable*()`.

## Task Tracking

Active development tasks are tracked in `tasks.md` at the project root, organized by status with orchestrator summaries of completed work.
