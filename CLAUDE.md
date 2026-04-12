# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Castle Fight is a Unity 6 (6000.3.2f1) multiplayer RTS game inspired by the Warcraft 3 custom map "Castle Fight". Players build structures that auto-spawn units; units march toward the enemy castle. Networking uses Mirror. The production scene is `Assets/Scenes/SampleScene.unity`; a deterministic editor-only test scene lives at `Assets/Scenes/TestScene_Pathfinding.unity` (see [Test Scenes](#test-scenes)).

## Build & Test

This is a Unity project — there is no CLI build. Open in Unity Editor and use:

- **Build:** File → Build Settings → Build (Windows Standalone)
- **Run tests:** Window → General → Test Runner → EditMode → Run All
- **Run single test:** Right-click a test in Test Runner, or use the filter bar to search by name
- **Test location:** `Assets/Tests/Editor/` — 7 NUnit `[TestFixture]` classes (173 tests)
- **No linter configured.** No .editorconfig, StyleCop, or Roslyn analyzers.
- **C# 9.0**, .NET 4.7.1 target

## Architecture

### A* Pathfinding Project Pro (Recast Graph)

Pathfinding uses **A* Pathfinding Project Pro v5.4.6** with a Recast Graph (NavMesh):

- `AstarPath` component on "A* Pathfinding" GameObject configures the Recast Graph
- `RVOSimulator` on "RVO Simulator" GameObject manages local avoidance for all units
- Units get `Seeker` + `RichAI` + `RVOController` added at runtime by `UnitMovement`
- Buildings add `NavmeshCut` to carve the NavMesh dynamically when placed/destroyed
- A* Pro handles path requests, throttling, replanning, and NavMesh updates automatically
- Server-authoritative: AI components only run on the server; clients interpolate positions

### GridSystem (Building Placement Only)

`GridSystem` tracks **building cell occupancy** for placement validation and combat range calculations. It does NOT handle pathfinding — that's A* Pro's job.

- `WorldToCell()` / `CellToWorld()` / `SnapToGrid()` for coordinate conversion
- `MarkCells()` / `ClearCells()` track which cells have buildings
- `CanPlaceBuilding()` / `CanPlaceBuildingFootprint()` validate placement
- `FootprintHelper` computes cell rectangles for buildings/units (used by combat)
- Build zones restrict placement per team

### Core Systems

- **EventBus** (`Core/EventBus.cs`) — Thread-safe pub/sub. 10 event structs in `GameEvents.cs` with readonly fields. Use `EventBus.Raise()` / `EventBus.Subscribe<T>()`.
- **GameRegistry** (`Core/GameRegistry.cs`) — Registry for castles and build zones. Use this instead of `FindObjectsByType`.
- **UnitManager / BuildingManager** — Registries for live units/buildings. Return `IReadOnlyList<T>`.

### Unit Lifecycle

`Unit.cs` is the entity root. Key cached components: `Unit.Movement` (UnitMovement), `Unit.StateMachine` (UnitStateMachine).

**States:** Idle → Moving → Fighting → Dying

### Pure Logic Classes (Testable without Unity)

| MonoBehaviour | Pure Logic | Purpose |
|---|---|---|
| UnitAnimator | AnimationLogic | Animation state transitions |
| UnitStateMachine | UnitStateLogic | State machine decisions |

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

### Building Placement + NavMesh Flow

Building placed → `GridSystem.MarkCells()` tracks occupancy → `NavmeshCut` carves NavMesh → A* Pro automatically replans affected unit paths.

Building destroyed → `GridSystem.ClearCells()` frees cells → `NavmeshCut` removed → NavMesh restores → units replan.

### Performance

- A* Pro handles path request throttling and caching internally
- `GameDebug` logging uses `[Conditional]` attributes — stripped in release builds
- `SpatialHashGrid` used for fast combat proximity queries

## Debug Tools

Runtime F-key toggles (via `PathfindingDebugToggle`):

| Key | Visualization |
|---|---|
| F1 | Pathfinding logs |
| F2 | Movement logs |
| F4 | Building cell overlay |
| F5 | Attack range |
| F6 | Velocity arrows |
| F7 | Combat logs |
| F9 | All ON |
| F10 | All OFF |

`GameDebug` has 14 category flags (Combat, Movement, Animation, Pathfinding, etc.) controlled via `GameDebug.Enable*()` / `GameDebug.Disable*()`.

## Test Scenes

`Assets/Scenes/SampleScene.unity` is the production gameplay scene with random AI-vs-AI behavior (`NetworkGameManager.enableAI = true`). It cannot produce reproducible pathfinding/combat bugs because every playtest generates a different map state.

`Assets/Scenes/TestScene_Pathfinding.unity` is an editor-only deterministic test harness duplicated from SampleScene with AI disabled (`NetworkGameManager.enableAI = false`) and a `DevTools` GameObject holding `PathfindingStressTest` (`disableAIOnStart = true` as a runtime fallback in case a `dontDestroyOnLoad` NetworkGameManager from SampleScene is carried into the session).

To use: open the scene, press Play (DevAutoHost auto-starts the host), press **F11** to open the stress-test panel. Scenarios:

| ID | Scenario | Tests |
|---|---|---|
| S1 | March Through Gap | Path planning through a narrow opening |
| S2 | Building Mid-Route | Auto-replanning when a building appears in the path |
| S3 | Two Armies Collide | Head-on crowd collision |
| S4 | Crowd Around Target | Pure congestion on a point |
| S5 | Obstacle Maze | Routing through 6 buildings in a maze pattern |
| S6 | Units Near Buildings | Spawn-adjacent edge-case navigation |
| S7 | Dogpile Small Target | **RVO radius + `AttackRangeHelper` fan-out regression test** — spawns N attackers onto one stationary enemy; they should form a ~180° crescent on the approach side with no mesh interpenetration |

Unit/building pickers use `AssetDatabase` under `#if UNITY_EDITOR`, so the test scene is **not** in Build Settings and only runs in-editor.

## Task Tracking

Active development tasks are tracked in `tasks.md` at the project root, organized by status with orchestrator summaries of completed work.
