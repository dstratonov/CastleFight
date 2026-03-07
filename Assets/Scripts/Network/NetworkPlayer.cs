using UnityEngine;
using Mirror;

public class NetworkPlayer : NetworkBehaviour
{
    [SyncVar] private int playerId;
    [SyncVar] private int teamId;
    [SyncVar] private string selectedRaceId;
    [SyncVar(hook = nameof(OnGoldChanged))] private int gold;
    [SyncVar] private int income;

    public int PlayerId => playerId;
    public int TeamId => teamId;
    public string SelectedRaceId => selectedRaceId;
    public int Gold => gold;
    public int Income => income;

    [Server]
    public void Initialize(int id, int team)
    {
        playerId = id;
        teamId = team;
        var config = Resources.Load<GameConfig>("GameConfig");
        gold = config != null ? config.startingGold : 100;
        income = config != null ? config.passiveIncomeAmount : 10;
    }

    [Server]
    public bool SpendGold(int amount)
    {
        if (gold < amount) return false;
        gold -= amount;
        return true;
    }

    [Server]
    public void AddGold(int amount)
    {
        gold += amount;
    }

    [Server]
    public void AddIncome(int amount)
    {
        income += amount;
    }

    [Server]
    public void SetRace(string raceId)
    {
        selectedRaceId = raceId;
    }

    [Command]
    public void CmdSelectRace(string raceId)
    {
        SetRace(raceId);
    }

    private void OnGoldChanged(int oldGold, int newGold)
    {
        EventBus.Raise(new GoldChangedEvent(playerId, newGold, newGold - oldGold));
    }
}
