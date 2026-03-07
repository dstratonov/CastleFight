using UnityEngine;
using Mirror;
using System.Collections.Generic;

public class NetworkGameManager : NetworkManager
{
    [Header("Castle Fight")]
    [SerializeField] private GameObject heroPrefab;
    [SerializeField] private Transform[] team1SpawnPoints;
    [SerializeField] private Transform[] team2SpawnPoints;

    private readonly Dictionary<int, NetworkPlayer> players = new();

    public IReadOnlyDictionary<int, NetworkPlayer> Players => players;

    public static new NetworkGameManager singleton => (NetworkGameManager)NetworkManager.singleton;

    public override void OnStartServer()
    {
        base.OnStartServer();
        GameManager.Instance?.SetState(GameState.Lobby);
    }

    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        int teamId = TeamManager.Instance.GetTeamWithFewestPlayers();
        Transform spawnPoint = GetSpawnPoint(teamId, TeamManager.Instance.GetTeamPlayerCount(teamId));

        GameObject heroObj = Instantiate(heroPrefab, spawnPoint.position, spawnPoint.rotation);
        NetworkServer.AddPlayerForConnection(conn, heroObj);

        var networkPlayer = heroObj.GetComponent<NetworkPlayer>();
        if (networkPlayer != null)
        {
            networkPlayer.Initialize(conn.connectionId, teamId);
            players[conn.connectionId] = networkPlayer;
            TeamManager.Instance.AddPlayerToTeam(conn.connectionId, teamId);
        }
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
