using UnityEngine;

public static class GameDebug
{
    public static bool Combat = false;
    public static bool Movement = false;
    public static bool Animation = false;
    public static bool Spawning = true;
    public static bool Economy = false;
    public static bool Building = false;
    public static bool Health = false;
    public static bool Pathfinding = false;
    public static bool AI = false;
    public static bool StateMachine = false;

    public static void Log(string prefix, string msg)
    {
        Debug.Log($"{prefix} {msg}");
    }

    public static void Warn(string prefix, string msg)
    {
        Debug.LogWarning($"{prefix} {msg}");
    }
}
