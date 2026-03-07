using UnityEngine;
using Mirror;

public class IncomeSystem : NetworkBehaviour
{
    [SerializeField] private float tickInterval = 5f;
    [SerializeField] private int baseIncomePerTick = 10;

    private float tickTimer;

    private void OnEnable()
    {
        EventBus.Subscribe<UnitKilledEvent>(OnUnitKilled);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<UnitKilledEvent>(OnUnitKilled);
    }

    private void Update()
    {
        if (!isServer) return;
        if (GameManager.Instance == null || GameManager.Instance.CurrentState != GameState.Playing) return;

        tickTimer += Time.deltaTime;
        if (tickTimer >= tickInterval)
        {
            tickTimer -= tickInterval;
            DistributeIncome();
        }
    }

    [Server]
    private void DistributeIncome()
    {
        var players = NetworkGameManager.singleton?.Players;
        if (players == null) return;

        foreach (var kvp in players)
        {
            int income = baseIncomePerTick + kvp.Value.Income;
            kvp.Value.AddGold(income);
        }
    }

    private void OnUnitKilled(UnitKilledEvent evt)
    {
        if (!isServer) return;
        if (evt.Killer == null || evt.BountyGold <= 0) return;

        var killerPlayer = evt.Killer.GetComponent<NetworkPlayer>();
        if (killerPlayer == null)
        {
            var killerUnit = evt.Killer.GetComponent<Unit>();
            if (killerUnit != null)
            {
                ResourceManager.Instance?.AwardGoldToTeam(killerUnit.TeamId, evt.BountyGold);
            }
            return;
        }

        ResourceManager.Instance?.AwardGold(killerPlayer, evt.BountyGold);
    }
}
