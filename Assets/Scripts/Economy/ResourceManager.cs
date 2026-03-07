using UnityEngine;
using Mirror;
using System.Collections.Generic;

public class ResourceManager : NetworkBehaviour
{
    public static ResourceManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    [Server]
    public bool TrySpendGold(NetworkPlayer player, int amount)
    {
        if (player == null || amount <= 0) return false;
        return player.SpendGold(amount);
    }

    [Server]
    public void AwardGold(NetworkPlayer player, int amount)
    {
        if (player == null || amount <= 0) return;
        player.AddGold(amount);
    }

    [Server]
    public void AwardGoldToTeam(int teamId, int amount)
    {
        var players = NetworkGameManager.singleton?.Players;
        if (players == null) return;

        foreach (var kvp in players)
        {
            if (kvp.Value.TeamId == teamId)
                kvp.Value.AddGold(amount);
        }
    }

    public int GetGold(NetworkPlayer player)
    {
        return player != null ? player.Gold : 0;
    }

    public bool CanAfford(NetworkPlayer player, int cost)
    {
        return player != null && player.Gold >= cost;
    }
}
