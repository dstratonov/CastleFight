using UnityEngine;

public static class GameDebug
{
    public static bool Combat = true;
    public static bool Movement = true;
    public static bool Animation = true;
    public static bool Spawning = true;
    public static bool Economy = true;
    public static bool Building = true;
    public static bool Health = true;
    public static bool Pathfinding = true;
    public static bool AI = true;
    public static bool StateMachine = true;

    public static void Log(string prefix, string msg)
    {
        Debug.Log($"{prefix} {msg}");
    }

    public static void Warn(string prefix, string msg)
    {
        Debug.LogWarning($"{prefix} {msg}");
    }
}
