using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GameOverUI : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private TextMeshProUGUI resultText;
    [SerializeField] private TextMeshProUGUI statsText;
    [SerializeField] private Button returnToMenuButton;

    public void Init(GameObject panelRoot, TextMeshProUGUI result, TextMeshProUGUI stats, Button returnBtn)
    {
        panel = panelRoot;
        resultText = result;
        statsText = stats;
        returnToMenuButton = returnBtn;

        if (panel != null)
            panel.SetActive(false);
    }

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

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        var localPlayer = NetworkPlayer.Local;
        if (localPlayer != null && resultText != null)
        {
            bool won = localPlayer.TeamId == evt.WinningTeamId;
            resultText.text = won ? "VICTORY" : "DEFEAT";
            resultText.color = won ? new Color(0.2f, 1f, 0.3f) : new Color(1f, 0.25f, 0.2f);
        }

        if (statsText != null)
        {
            float matchTime = HUDManager.Instance != null
                ? HUDManager.Instance.MatchTimer
                : Time.timeSinceLevelLoad;
            int minutes = Mathf.FloorToInt(matchTime / 60f);
            int seconds = Mathf.FloorToInt(matchTime % 60f);
            statsText.text = $"Match Duration: {minutes:00}:{seconds:00}";
        }
    }

    private void ReturnToMenu()
    {
        if (Mirror.NetworkServer.active)
            NetworkGameManager.singleton?.StopHost();
        else
            NetworkGameManager.singleton?.StopClient();
    }
}
