using UnityEngine;

// ====================================================================
// Game State Events
// ====================================================================

public struct GameStateChangedEvent
{
    public readonly GameState OldState;
    public readonly GameState NewState;

    public GameStateChangedEvent(GameState oldState, GameState newState)
    {
        OldState = oldState;
        NewState = newState;
    }
}

public struct GameOverEvent
{
    public readonly int WinningTeamId;
    public GameOverEvent(int winningTeamId) { WinningTeamId = winningTeamId; }
}

// ====================================================================
// Building Events
// ====================================================================

public struct BuildingPlacedEvent
{
    public readonly GameObject Building;
    public readonly int OwnerPlayerId;
    public readonly int TeamId;

    public BuildingPlacedEvent(GameObject building, int ownerPlayerId, int teamId)
    {
        Building = building;
        OwnerPlayerId = ownerPlayerId;
        TeamId = teamId;
    }
}

public struct BuildingDestroyedEvent
{
    public readonly GameObject Building;
    public readonly int TeamId;

    public BuildingDestroyedEvent(GameObject building, int teamId)
    {
        Building = building;
        TeamId = teamId;
    }
}

// ====================================================================
// Unit Events
// ====================================================================

public struct UnitSpawnedEvent
{
    public readonly GameObject Unit;
    public readonly int TeamId;

    public UnitSpawnedEvent(GameObject unit, int teamId)
    {
        Unit = unit;
        TeamId = teamId;
    }
}

public struct UnitKilledEvent
{
    public readonly GameObject Unit;
    public readonly GameObject Killer;
    public readonly int BountyGold;

    public UnitKilledEvent(GameObject unit, GameObject killer, int bountyGold)
    {
        Unit = unit;
        Killer = killer;
        BountyGold = bountyGold;
    }
}

// ====================================================================
// Castle Events
// ====================================================================

public struct CastleDestroyedEvent
{
    public readonly int TeamId;
    public CastleDestroyedEvent(int teamId) { TeamId = teamId; }
}

// ====================================================================
// Economy Events
// ====================================================================

public struct GoldChangedEvent
{
    public readonly int PlayerId;
    public readonly int NewAmount;
    public readonly int Delta;

    public GoldChangedEvent(int playerId, int newAmount, int delta)
    {
        PlayerId = playerId;
        NewAmount = newAmount;
        Delta = delta;
    }
}

// ====================================================================
// UI Events
// ====================================================================

public struct SelectionChangedEvent
{
    public readonly GameObject Selected;
    public SelectionChangedEvent(GameObject selected) { Selected = selected; }
}
