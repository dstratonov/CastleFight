using UnityEngine;
using Mirror;
using System.Collections.Generic;

public class LobbyManager : NetworkBehaviour
{
    public static LobbyManager Instance { get; private set; }

    private readonly SyncDictionary<int, bool> readyStates = new();

    public event System.Action OnReadyStatesChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public override void OnStartClient()
    {
        readyStates.OnChange += OnReadyStateChanged;
    }

    private void OnReadyStateChanged(SyncIDictionary<int, bool>.Operation op, int key, bool item)
    {
        OnReadyStatesChanged?.Invoke();
    }

    [Command(requiresAuthority = false)]
    public void CmdSetReady(bool ready, NetworkConnectionToClient sender = null)
    {
        if (sender == null) return;
        readyStates[sender.connectionId] = ready;
        CheckAllReady();
    }

    [Command(requiresAuthority = false)]
    public void CmdRequestTeamSwitch(NetworkConnectionToClient sender = null)
    {
        if (sender == null) return;
        var player = sender.identity.GetComponent<NetworkPlayer>();
        if (player == null) return;

        int currentTeam = player.TeamId;
        int newTeam = currentTeam == 0 ? 1 : 0;

        TeamManager.Instance.RemovePlayerFromTeam(sender.connectionId, currentTeam);
        TeamManager.Instance.AddPlayerToTeam(sender.connectionId, newTeam);
        player.Initialize(sender.connectionId, newTeam);
    }

    public bool IsPlayerReady(int connectionId)
    {
        return readyStates.TryGetValue(connectionId, out bool ready) && ready;
    }

    [Server]
    private void CheckAllReady()
    {
        if (readyStates.Count < 2) return;

        foreach (var kvp in readyStates)
        {
            if (!kvp.Value) return;
        }

        StartMatch();
    }

    [Server]
    private void StartMatch()
    {
        GameManager.Instance?.StartMatch();
    }
}
