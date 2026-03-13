using UnityEngine;
using Mirror;

public class NetworkPlayer : NetworkBehaviour
{
    public static NetworkPlayer Local { get; private set; }

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

    public override void OnStartLocalPlayer()
    {
        Local = this;
    }

    private void OnDestroy()
    {
        if (Local == this) Local = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Local = null;
    }

    [Server]
    public void Initialize(int id, int team)
    {
        playerId = id;
        teamId = team;
        var config = GameConfig.Instance;
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
