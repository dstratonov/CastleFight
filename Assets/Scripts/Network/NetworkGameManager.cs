using UnityEngine;
using Mirror;
using System.Collections.Generic;

public class NetworkGameManager : NetworkManager
{
    [Header("Castle Fight")]
    [SerializeField] private GameObject heroPrefab;
    [SerializeField] private Transform[] team1SpawnPoints;
    [SerializeField] private Transform[] team2SpawnPoints;
    [SerializeField] private DamageTable damageTable;

    private readonly Dictionary<int, NetworkPlayer> players = new();

    public IReadOnlyDictionary<int, NetworkPlayer> Players => players;

    public static new NetworkGameManager singleton => (NetworkGameManager)NetworkManager.singleton;

    public override void OnStartServer()
    {
        base.OnStartServer();

        InitializeDamageSystem();
        InitializeCastles();

        GameManager.Instance?.SetState(GameState.Playing);
    }

    private void InitializeDamageSystem()
    {
        var table = damageTable;
        if (table == null)
            table = Resources.Load<DamageTable>("DamageTable");
        if (table != null)
            DamageSystem.Initialize(table);
        else
            Debug.LogWarning("[NetworkGameManager] No DamageTable found. Attack/armor multipliers will default to 1.0x.");
    }

    private void InitializeCastles()
    {
        var castles = FindObjectsByType<Castle>(FindObjectsSortMode.None);
        foreach (var castle in castles)
        {
            int team = castle.gameObject.name.Contains("Team0") ? 0 : 1;
            castle.Initialize(team, 5000);
            Debug.Log($"[NetworkGameManager] Initialized {castle.gameObject.name} as team {team}");
        }
    }

    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        int teamId = TeamManager.Instance.GetTeamWithFewestPlayers();
        Transform spawnPoint = GetSpawnPoint(teamId, TeamManager.Instance.GetTeamPlayerCount(teamId));

        GameObject heroObj = Instantiate(heroPrefab, spawnPoint.position, spawnPoint.rotation);

        var networkPlayer = heroObj.GetComponent<NetworkPlayer>();
        if (networkPlayer != null)
        {
            networkPlayer.Initialize(conn.connectionId, teamId);
            networkPlayer.SetRace("humans");
            players[conn.connectionId] = networkPlayer;
            TeamManager.Instance.AddPlayerToTeam(conn.connectionId, teamId);
        }

        NetworkServer.AddPlayerForConnection(conn, heroObj);
    }

    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        if (players.TryGetValue(conn.connectionId, out var player))
        {
            TeamManager.Instance.RemovePlayerFromTeam(conn.connectionId, player.TeamId);
            players.Remove(conn.connectionId);
        }
        base.OnServerDisconnect(conn);
    }

    private Transform GetSpawnPoint(int teamId, int playerIndex)
    {
        var points = teamId == 0 ? team1SpawnPoints : team2SpawnPoints;
        if (points == null || points.Length == 0) return transform;
        return points[playerIndex % points.Length];
    }
}
