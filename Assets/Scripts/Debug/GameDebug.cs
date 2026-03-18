using UnityEngine;
using System.Diagnostics;
using Debug = UnityEngine.Debug;

/// <summary>
/// Debug logging system that is completely stripped from release builds.
/// All Log* methods use [Conditional] so call sites (including string interpolation
/// argument evaluation) are removed at compile time in non-development builds.
/// </summary>
public static class GameDebug
{
    public static bool Combat;
    public static bool Movement;
    public static bool Animation;
    public static bool Spawning;
    public static bool Economy;
    public static bool Building;
    public static bool Health;
    public static bool Pathfinding;
    public static bool AI;
    public static bool StateMachine;
    public static bool Boids;
    public static bool UnitLifecycle;

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void LogCombat(string message)
    {
        if (Combat) Debug.Log(message);
    }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void LogMovement(string message)
    {
        if (Movement) Debug.Log(message);
    }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void LogAnimation(string message)
    {
        if (Animation) Debug.Log(message);
    }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void LogSpawning(string message)
    {
        if (Spawning) Debug.Log(message);
    }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void LogEconomy(string message)
    {
        if (Economy) Debug.Log(message);
    }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void LogBuilding(string message)
    {
        if (Building) Debug.Log(message);
    }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void LogHealth(string message)
    {
        if (Health) Debug.Log(message);
    }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void LogPathfinding(string message)
    {
        if (Pathfinding) Debug.Log(message);
    }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void LogAI(string message)
    {
        if (AI) Debug.Log(message);
    }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void LogStateMachine(string message)
    {
        if (StateMachine) Debug.Log(message);
    }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void LogBoids(string message)
    {
        if (Boids) Debug.Log(message);
    }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void LogUnitLifecycle(string message)
    {
        if (UnitLifecycle) Debug.Log(message);
    }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void WarnCombat(string message)
    {
        if (Combat) Debug.LogWarning(message);
    }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void WarnMovement(string message)
    {
        if (Movement) Debug.LogWarning(message);
    }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void WarnPathfinding(string message)
    {
        if (Pathfinding) Debug.LogWarning(message);
    }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void EnableAll()
    {
        Combat = true;
        Movement = true;
        Animation = true;
        Spawning = true;
        Economy = true;
        Building = true;
        Health = true;
        Pathfinding = true;
        AI = true;
        StateMachine = true;
        Boids = true;
        UnitLifecycle = true;
        Debug.Log("[GameDebug] ALL debug flags ENABLED");
    }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void DisableAll()
    {
        Combat = false;
        Movement = false;
        Animation = false;
        Spawning = false;
        Economy = false;
        Building = false;
        Health = false;
        Pathfinding = false;
        AI = false;
        StateMachine = false;
        Boids = false;
        UnitLifecycle = false;
    }
}
