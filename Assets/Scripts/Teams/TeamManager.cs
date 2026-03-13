using UnityEngine;
using System.Collections.Generic;

public class TeamManager : MonoBehaviour
{
    public static TeamManager Instance { get; private set; }

    public const int TeamCount = 2;

    private readonly List<HashSet<int>> teams = new()
    {
        new HashSet<int>(),
        new HashSet<int>()
    };

    private readonly Dictionary<int, int> playerTeamMap = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
    }

    public void AddPlayerToTeam(int playerId, int teamId)
    {
        if (teamId < 0 || teamId >= TeamCount) return;
        if (playerTeamMap.TryGetValue(playerId, out int oldTeam) && oldTeam != teamId)
            RemovePlayerFromTeam(playerId);
        teams[teamId].Add(playerId);
        playerTeamMap[playerId] = teamId;
    }

    public void RemovePlayerFromTeam(int playerId)
    {
        if (!playerTeamMap.TryGetValue(playerId, out int currentTeam)) return;
        if (currentTeam >= 0 && currentTeam < TeamCount)
            teams[currentTeam].Remove(playerId);
        playerTeamMap.Remove(playerId);
    }

    public void RemovePlayerFromTeam(int playerId, int teamId)
    {
        if (teamId < 0 || teamId >= TeamCount) return;
        teams[teamId].Remove(playerId);
        playerTeamMap.Remove(playerId);
    }

    public int GetTeamWithFewestPlayers()
    {
        return teams[0].Count <= teams[1].Count ? 0 : 1;
    }

    public int GetTeamPlayerCount(int teamId)
    {
        if (teamId < 0 || teamId >= TeamCount) return 0;
        return teams[teamId].Count;
    }

    public int GetPlayerTeam(int playerId)
    {
        return playerTeamMap.TryGetValue(playerId, out int team) ? team : -1;
    }

    public IReadOnlyCollection<int> GetTeamPlayers(int teamId)
    {
        if (teamId < 0 || teamId >= TeamCount) return System.Array.Empty<int>();
        return teams[teamId];
    }

    public bool AreOnSameTeam(int playerA, int playerB)
    {
        if (!playerTeamMap.TryGetValue(playerA, out int teamA)) return false;
        if (!playerTeamMap.TryGetValue(playerB, out int teamB)) return false;
        return teamA == teamB;
    }

    public int GetEnemyTeamId(int teamId)
    {
        return teamId == 0 ? 1 : 0;
    }
}
