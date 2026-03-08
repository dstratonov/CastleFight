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

    [Header("AI")]
    [SerializeField] private bool enableAI = true;
    [SerializeField] private bool aiVsAiMode = true;
    [SerializeField] private string aiRaceId = "";

    private readonly Dictionary<int, NetworkPlayer> players = new();
    private AIPlayer aiPlayerTeam0;
    private AIPlayer aiPlayerTeam1;

    public IReadOnlyDictionary<int, NetworkPlayer> Players => players;

    public static new NetworkGameManager singleton => (NetworkGameManager)NetworkManager.singleton;

    public override void Awake()
    {
        base.Awake();
        RegisterUnitPrefabs();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        InitializeDamageSystem();

        GameManager.Instance?.SetState(GameState.Playing);

        if (enableAI)
            SpawnAIPlayer();
    }

    private void SpawnAIPlayer()
    {
        if (aiVsAiMode)
        {
            var aiObj0 = new GameObject("AIPlayer_Team0");
            aiPlayerTeam0 = aiObj0.AddComponent<AIPlayer>();
            aiPlayerTeam0.Initialize(0, "");

            var aiObj1 = new GameObject("AIPlayer_Team1");
            aiPlayerTeam1 = aiObj1.AddComponent<AIPlayer>();
            aiPlayerTeam1.Initialize(1, "");

            Debug.Log("[NetworkGameManager] AI vs AI mode: spawned bots on both teams");
        }
        else
        {
            var aiObj = new GameObject("AIPlayer_Team1");
            aiPlayerTeam1 = aiObj.AddComponent<AIPlayer>();
            aiPlayerTeam1.Initialize(1, aiRaceId);
            Debug.Log("[NetworkGameManager] AI opponent spawned on team 1");
        }
    }

    private void RegisterUnitPrefabs()
    {
        var raceDb = RaceDatabase.Instance;
        if (raceDb == null || raceDb.AllRaces == null) return;

        foreach (var race in raceDb.AllRaces)
        {
            if (race == null || race.buildings == null) continue;
            foreach (var building in race.buildings)
            {
                if (building == null) continue;

                if (building.prefab != null && !spawnPrefabs.Contains(building.prefab))
                    spawnPrefabs.Add(building.prefab);

                if (building.spawnedUnit?.prefab != null && !spawnPrefabs.Contains(building.spawnedUnit.prefab))
                    spawnPrefabs.Add(building.spawnedUnit.prefab);
            }
        }
        Debug.Log($"[NetworkGameManager] Registered {spawnPrefabs.Count} prefabs for network spawning");
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

    public override void OnStopServer()
    {
        base.OnStopServer();
        EventBus.Clear();
    }

    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        if (aiVsAiMode)
        {
            var spectator = new GameObject("Spectator");
            spectator.AddComponent<NetworkIdentity>();
            NetworkServer.AddPlayerForConnection(conn, spectator);
            Debug.Log("[NetworkGameManager] AI vs AI mode: added spectator (no hero)");
            return;
        }

        if (TeamManager.Instance == null)
        {
            Debug.LogError("[NetworkGameManager] TeamManager.Instance is null when adding player");
            return;
        }
        int teamId = TeamManager.Instance.GetTeamWithFewestPlayers();
        Transform spawnPoint = GetSpawnPoint(teamId, TeamManager.Instance.GetTeamPlayerCount(teamId));

        GameObject heroObj = Instantiate(heroPrefab, spawnPoint.position, spawnPoint.rotation);

        var networkPlayer = heroObj.GetComponent<NetworkPlayer>();
        if (networkPlayer != null)
        {
            networkPlayer.Initialize(conn.connectionId, teamId);
            networkPlayer.SetRace("horde");
            players[conn.connectionId] = networkPlayer;
            TeamManager.Instance.AddPlayerToTeam(conn.connectionId, teamId);
        }

        NetworkServer.AddPlayerForConnection(conn, heroObj);
    }

    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        if (players.TryGetValue(conn.connectionId, out var player))
        {
            TeamManager.Instance?.RemovePlayerFromTeam(conn.connectionId, player.TeamId);
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
