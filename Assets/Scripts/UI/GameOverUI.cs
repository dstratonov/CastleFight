using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GameOverUI : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private TextMeshProUGUI resultText;
    [SerializeField] private TextMeshProUGUI statsText;
    [SerializeField] private Button returnToMenuButton;

    private void OnEnable()
    {
        EventBus.Subscribe<GameOverEvent>(OnGameOver);
        if (returnToMenuButton != null)
            returnToMenuButton.onClick.AddListener(ReturnToMenu);

        if (panel != null)
            panel.SetActive(false);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<GameOverEvent>(OnGameOver);
        if (returnToMenuButton != null)
            returnToMenuButton.onClick.RemoveListener(ReturnToMenu);
    }

    private void OnGameOver(GameOverEvent evt)
    {
        if (panel != null)
            panel.SetActive(true);

        var localPlayer = FindLocalPlayer();
        if (localPlayer != null && resultText != null)
        {
            bool won = localPlayer.TeamId == evt.WinningTeamId;
            resultText.text = won ? "VICTORY" : "DEFEAT";
            resultText.color = won ? Color.green : Color.red;
        }

        if (statsText != null)
        {
            float matchTime = Time.timeSinceLevelLoad;
            int minutes = Mathf.FloorToInt(matchTime / 60f);
            int seconds = Mathf.FloorToInt(matchTime % 60f);
            statsText.text = $"Match Duration: {minutes:00}:{seconds:00}";
        }
    }

    private NetworkPlayer FindLocalPlayer()
    {
        foreach (var player in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            if (player.isLocalPlayer) return player;
        }
        return null;
    }

    private void ReturnToMenu()
    {
        if (Mirror.NetworkServer.active)
            NetworkGameManager.singleton?.StopHost();
        else
            NetworkGameManager.singleton?.StopClient();
    }
}
