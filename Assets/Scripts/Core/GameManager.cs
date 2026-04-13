using UnityEngine;
using Mirror;
using System;

public enum GameState
{
    MainMenu,
    Lobby,
    RaceSelect,
    Loading,
    Playing,
    GameOver
}

public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    [SyncVar(hook = nameof(OnGameStateChanged))]
    private GameState currentState = GameState.MainMenu;

    public GameState CurrentState => currentState;

    public event Action<GameState, GameState> OnStateChanged;

    private void Awake()
    {
        EnsureSingleton();
    }

    private void EnsureSingleton()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnEnable()
    {
        EnsureSingleton();
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

    private void OnGameStateChanged(GameState oldState, GameState newState)
    {
        OnStateChanged?.Invoke(oldState, newState);
        EventBus.Raise(new GameStateChangedEvent(oldState, newState));
    }

    [Server]
    public void SetState(GameState newState)
    {
        if (currentState == newState) return;
        var old = currentState;
        Debug.Log($"[Game] State: {old} -> {newState}");
        currentState = newState;
    }

    [Server]
    public void StartMatch()
    {
        // Pathfinding is handled by A* Pathfinding Project Pro (AstarPath).
        // No manual initialization needed — Recast Graph auto-scans.
        SetState(GameState.Playing);
    }

    [Server]
    public void EndMatch(int winningTeamId)
    {
        Debug.Log($"[Game] Match ended! Team {winningTeamId} wins at t={Time.timeSinceLevelLoad:F0}s");
        SetState(GameState.GameOver);
        RpcNotifyGameOver(winningTeamId);
    }

    [ClientRpc]
    private void RpcNotifyGameOver(int winningTeamId)
    {
        EventBus.Raise(new GameOverEvent(winningTeamId));
    }
}
