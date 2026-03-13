using UnityEngine;

public static class GameDebug
{
    public static bool Combat = false;
    public static bool Movement = false;
    public static bool Animation = false;
    public static bool Spawning = false;
    public static bool Economy = false;
    public static bool Building = false;
    public static bool Health = false;
    public static bool Pathfinding = false;
    public static bool AI = false;
    public static bool StateMachine = false;
    public static bool Boids = false;
    public static bool UnitLifecycle = false;

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
