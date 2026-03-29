# Targeting System

## Overview

Units auto-acquire and attack enemy targets. The system is server-authoritative — all targeting, damage, and state transitions run on the server only.

The enemy castle is the unit's **default target** (soft lock). Units march toward it and attack anything hostile they encounter along the way. Higher-priority targets override the default.

## Architecture

```
IAttackable (Unit, Building, Castle)
  ├── Health, ArmorType, TargetRadius, Priority

TargetingState (per-unit composition)
  ├── Current target + lock type (Hard/Soft)
  ├── TrySetTarget() — respects lock rules
  ├── ShouldScan — true when soft-locked or no target
  └── Validate() — checks alive + leash

TargetingService (static, stateless)
  ├── FindTarget() — scans units → buildings → castle
  └── GetDefaultTarget() — returns enemy castle

UnitCombat (per-unit MonoBehaviour)
  ├── Owns a TargetingState
  ├── Calls TargetingService on scan ticks
  ├── Chase: FindAttackCell() → UnitMovement
  └── Attack: DamageSystem → Health.TakeDamage()
```

## IAttackable Interface

Implemented by `Unit`, `Building`, and `Castle`:

```csharp
public interface IAttackable
{
    GameObject gameObject { get; }
    Health Health { get; }
    int TeamId { get; }
    ArmorType ArmorType { get; }
    float TargetRadius { get; }
    TargetPriority Priority { get; }
}
```

## Target Priority & Lock Types

| Priority | Target Type | Lock | Behavior |
|---|---|---|---|
| `Default` (0) | Castle | **Soft** | Keeps scanning for higher-priority targets |
| `Building` (10) | Buildings | **Hard** | Committed — no scanning while engaged |
| `Unit` (20) | Units | **Hard** | Committed — no scanning while engaged |

### Hard Lock
The attacker is committed to this target. No scanning for new targets while the current target is alive and in leash range. Only broken when:
- Target dies or is destroyed
- Target moves beyond leash range (1.5x aggro radius, units only)

### Soft Lock
The attacker keeps scanning every 0.25s. If a target with equal or higher priority appears, the attacker switches to it. Used for the default objective (castle) so units can be interrupted by nearby enemies.

## Target Upgrade Rules

`TargetingState.TrySetTarget()` enforces:

| Current Lock | New Priority | Result |
|---|---|---|
| None | Any | Accept |
| Soft | >= current | Accept (switch) |
| Soft | < current | Reject |
| Hard | > current | Accept (override) |
| Hard | <= current | Reject (stay locked) |

## Flow

### 1. Spawn (first scan)

On spawn, the unit has no target. The first scan tick calls `TargetingService.FindTarget()` which always returns something — if no enemies are nearby, it falls back to the enemy castle. The castle is assigned as a **soft lock** and the unit starts marching toward it.

### 2. Scan (every 0.25s, only when `ShouldScan`)

Only runs when soft-locked or no target (hard lock skips scanning entirely).

`TargetingService.FindTarget()` checks in priority order:
1. **Enemy units** within `aggroRadius` (via `SpatialHashGrid`)
2. **Enemy buildings** within `attackRange + unitRadius` (via `BuildingManager`)
3. **Enemy castle** within `attackRange + unitRadius` (via `GameRegistry`)
4. **Fallback:** enemy castle regardless of range (default objective)

The service **always returns a target** — the castle fallback ensures no unit is ever without an objective. The result is passed to `TargetingState.TrySetTarget()` which accepts or rejects based on lock rules.

### 3. Chase

If out of attack range, `FindAttackCell()` finds the nearest walkable cell (full footprint check) within attack range of the target. The attack position is cached and recomputed when the target moves > 1.5 units.

### 4. Attack

When in range:
- Movement stops
- Unit faces target
- Damage dealt on cooldown: `DamageSystem.CalculateDamage(baseDamage, attackType, target.ArmorType)`
- Attack animation triggered

### 5. Target Lost

When the current target becomes invalid (dies, destroyed, out of leash):
1. `TargetingState` clears
2. Next scan runs immediately (`ShouldScan` = true with no target)
3. `FindTarget` scans for enemies → if none found → returns castle as fallback
4. Castle assigned as soft lock → unit marches toward it
5. While marching, scan keeps running (soft lock) → picks up enemies along the way

## Effective Attack Range

```
effectiveRange = attackRange + attacker.EffectiveRadius + target.TargetRadius
```

## Attack Position Pathfinding

When chasing, the unit paths to a cell **within attack range** of the target, not to the target's cell:

1. Spiral search outward from the target cell
2. For each cell: check full footprint walkability + within attack range
3. Pick the cell closest to the unit
4. Cache result until target moves > 1.5 units

## Data (UnitData fields)

| Field | Type | Description |
|---|---|---|
| `aggroRadius` | float | Detection range for enemy units (default 8, >= `attackRange`) |
| `attackRange` | float | Distance at which attacks land |
| `attackDamage` | float | Base damage per hit |
| `attackSpeed` | float | Attacks per second |
| `attackType` | AttackType | Normal, Pierce, Magic, Siege, Hero, Chaos |
| `armorType` | ArmorType | Unarmored, Light, Medium, Heavy, Fortified, Hero |

## Debug

Enable combat logging with `GameDebug.Combat = true`. Logs target acquisition (with priority and lock type), target loss, and damage dealt.
