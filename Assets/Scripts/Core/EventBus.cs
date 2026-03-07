using System;
using System.Collections.Generic;
using UnityEngine;

public static class EventBus
{
    private static readonly Dictionary<Type, List<Delegate>> subscribers = new();
    private static readonly object lockObj = new();

    public static void Subscribe<T>(Action<T> handler) where T : struct
    {
        lock (lockObj)
        {
            var type = typeof(T);
            if (!subscribers.ContainsKey(type))
                subscribers[type] = new List<Delegate>();
            subscribers[type].Add(handler);
        }
    }

    public static void Unsubscribe<T>(Action<T> handler) where T : struct
    {
        lock (lockObj)
        {
            var type = typeof(T);
            if (subscribers.ContainsKey(type))
                subscribers[type].Remove(handler);
        }
    }

    public static void Raise<T>(T evt) where T : struct
    {
        List<Delegate> handlersCopy;
        lock (lockObj)
        {
            var type = typeof(T);
            if (!subscribers.ContainsKey(type)) return;
            handlersCopy = new List<Delegate>(subscribers[type]);
        }

        foreach (var handler in handlersCopy)
        {
            try
            {
                ((Action<T>)handler)?.Invoke(evt);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }

    public static void Clear()
    {
        lock (lockObj)
        {
            subscribers.Clear();
        }
    }
}

// --- Game Events ---

public struct GameStateChangedEvent
{
    public GameState OldState;
    public GameState NewState;

    public GameStateChangedEvent(GameState oldState, GameState newState)
    {
        OldState = oldState;
        NewState = newState;
    }
}

public struct GameOverEvent
{
    public int WinningTeamId;
    public GameOverEvent(int winningTeamId) { WinningTeamId = winningTeamId; }
}

public struct BuildingPlacedEvent
{
    public GameObject Building;
    public int OwnerPlayerId;
    public int TeamId;

    public BuildingPlacedEvent(GameObject building, int ownerPlayerId, int teamId)
    {
        Building = building;
        OwnerPlayerId = ownerPlayerId;
        TeamId = teamId;
    }
}

public struct BuildingDestroyedEvent
{
    public GameObject Building;
    public int TeamId;

    public BuildingDestroyedEvent(GameObject building, int teamId)
    {
        Building = building;
        TeamId = teamId;
    }
}

public struct UnitSpawnedEvent
{
    public GameObject Unit;
    public int TeamId;

    public UnitSpawnedEvent(GameObject unit, int teamId)
    {
        Unit = unit;
        TeamId = teamId;
    }
}

public struct UnitKilledEvent
{
    public GameObject Unit;
    public GameObject Killer;
    public int BountyGold;

    public UnitKilledEvent(GameObject unit, GameObject killer, int bountyGold)
    {
        Unit = unit;
        Killer = killer;
        BountyGold = bountyGold;
    }
}

public struct CastleDestroyedEvent
{
    public int TeamId;
    public CastleDestroyedEvent(int teamId) { TeamId = teamId; }
}

public struct GoldChangedEvent
{
    public int PlayerId;
    public int NewAmount;
    public int Delta;

    public GoldChangedEvent(int playerId, int newAmount, int delta)
    {
        PlayerId = playerId;
        NewAmount = newAmount;
        Delta = delta;
    }
}

public struct HeroMovedEvent
{
    public GameObject Hero;
    public Vector3 Position;

    public HeroMovedEvent(GameObject hero, Vector3 position)
    {
        Hero = hero;
        Position = position;
    }
}
