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
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
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
        currentState = newState;
    }

    [Server]
    public void StartMatch()
    {
        SetState(GameState.Playing);
    }

    [Server]
    public void EndMatch(int winningTeamId)
    {
        SetState(GameState.GameOver);
        RpcNotifyGameOver(winningTeamId);
    }

    [ClientRpc]
    private void RpcNotifyGameOver(int winningTeamId)
    {
        EventBus.Raise(new GameOverEvent(winningTeamId));
    }
}
