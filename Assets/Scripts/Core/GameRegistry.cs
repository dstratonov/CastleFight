using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Central registry for commonly-queried game objects.
/// Objects register/unregister themselves in OnEnable/OnDisable.
/// Eliminates expensive FindObjectsByType calls throughout the codebase.
/// </summary>
public static class GameRegistry
{
    private static readonly List<Castle> castles = new(2);
    private static readonly List<BuildZone> buildZones = new(4);

    public static IReadOnlyList<Castle> Castles => castles;
    public static IReadOnlyList<BuildZone> BuildZones => buildZones;

    public static void RegisterCastle(Castle c) { if (!castles.Contains(c)) castles.Add(c); }
    public static void UnregisterCastle(Castle c) { castles.Remove(c); }

    public static void RegisterBuildZone(BuildZone z) { if (!buildZones.Contains(z)) buildZones.Add(z); }
    public static void UnregisterBuildZone(BuildZone z) { buildZones.Remove(z); }

    /// <summary>Find the castle belonging to the given team, or null.</summary>
    public static Castle GetCastle(int teamId)
    {
        foreach (var c in castles)
            if (c != null && c.TeamId == teamId) return c;
        return null;
    }

    /// <summary>Find the enemy castle for the given team.</summary>
    public static Castle GetEnemyCastle(int myTeamId)
    {
        foreach (var c in castles)
            if (c != null && c.TeamId != myTeamId) return c;
        return null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        castles.Clear();
        buildZones.Clear();
    }
}
