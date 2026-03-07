using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Mirror;
using System.Collections.Generic;

public class LobbyUI : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private Transform playerListContainer;
    [SerializeField] private GameObject playerEntryPrefab;
    [SerializeField] private Button readyButton;
    [SerializeField] private Button switchTeamButton;
    [SerializeField] private TextMeshProUGUI readyButtonText;

    private bool isReady;
    private readonly List<GameObject> playerEntries = new();

    private void OnEnable()
    {
        if (readyButton != null)
            readyButton.onClick.AddListener(ToggleReady);
        if (switchTeamButton != null)
            switchTeamButton.onClick.AddListener(SwitchTeam);

        EventBus.Subscribe<GameStateChangedEvent>(OnGameStateChanged);
    }

    private void OnDisable()
    {
        if (readyButton != null)
            readyButton.onClick.RemoveListener(ToggleReady);
        if (switchTeamButton != null)
            switchTeamButton.onClick.RemoveListener(SwitchTeam);

        EventBus.Unsubscribe<GameStateChangedEvent>(OnGameStateChanged);
    }

    private void Update()
    {
        if (panel != null && panel.activeSelf)
            RefreshPlayerList();
    }

    public void Show()
    {
        if (panel != null) panel.SetActive(true);
    }

    public void Hide()
    {
        if (panel != null) panel.SetActive(false);
    }

    private void RefreshPlayerList()
    {
        ClearEntries();

        if (playerEntryPrefab == null || playerListContainer == null) return;

        var players = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None);
        foreach (var player in players)
        {
            var entry = Instantiate(playerEntryPrefab, playerListContainer);
            playerEntries.Add(entry);

            var nameText = entry.GetComponentInChildren<TextMeshProUGUI>();
            if (nameText != null)
            {
                string teamLabel = player.TeamId == 0 ? "Blue" : "Red";
                bool ready = LobbyManager.Instance != null && LobbyManager.Instance.IsPlayerReady(player.PlayerId);
                string readyLabel = ready ? " [Ready]" : "";
                nameText.text = $"Player {player.PlayerId} - {teamLabel}{readyLabel}";
            }
        }
    }

    private void ToggleReady()
    {
        isReady = !isReady;
        LobbyManager.Instance?.CmdSetReady(isReady);
        if (readyButtonText != null)
            readyButtonText.text = isReady ? "Not Ready" : "Ready";
    }

    private void SwitchTeam()
    {
        LobbyManager.Instance?.CmdRequestTeamSwitch();
    }

    private void OnGameStateChanged(GameStateChangedEvent evt)
    {
        if (evt.NewState == GameState.Playing)
            Hide();
        else if (evt.NewState == GameState.Lobby)
            Show();
    }

    private void ClearEntries()
    {
        foreach (var entry in playerEntries)
        {
            if (entry != null) Destroy(entry);
        }
        playerEntries.Clear();
    }
}
