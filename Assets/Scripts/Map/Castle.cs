using UnityEngine;
using Mirror;

[RequireComponent(typeof(Health))]
public class Castle : NetworkBehaviour
{
    [SyncVar] private int teamId;

    private Health health;

    public int TeamId => teamId;
    public Health Health => health;

    private void Awake()
    {
        health = GetComponent<Health>();
    }

    [Server]
    public void Initialize(int team, int maxHp = 5000)
    {
        teamId = team;
        health.Initialize(maxHp, team);
    }

    private void OnEnable()
    {
        if (health != null)
            health.OnDeath += HandleCastleDestroyed;
    }

    private void OnDisable()
    {
        if (health != null)
            health.OnDeath -= HandleCastleDestroyed;
    }

    private void HandleCastleDestroyed(GameObject killer)
    {
        EventBus.Raise(new CastleDestroyedEvent(teamId));

        if (isServer)
        {
            int winningTeam = TeamManager.Instance != null
                ? TeamManager.Instance.GetEnemyTeamId(teamId)
                : (teamId == 0 ? 1 : 0);
            GameManager.Instance?.EndMatch(winningTeam);
        }
    }
}
