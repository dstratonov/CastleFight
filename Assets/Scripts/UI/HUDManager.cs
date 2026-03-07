using UnityEngine;
using TMPro;

public class HUDManager : MonoBehaviour
{
    public static HUDManager Instance { get; private set; }

    [Header("Gold")]
    [SerializeField] private TextMeshProUGUI goldText;
    [SerializeField] private TextMeshProUGUI incomeText;

    [Header("Match")]
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private TextMeshProUGUI teamText;

    [Header("Castle Health")]
    [SerializeField] private UnityEngine.UI.Slider allyCastleHealthBar;
    [SerializeField] private UnityEngine.UI.Slider enemyCastleHealthBar;

    private float matchTimer;
    private NetworkPlayer localPlayer;

    private void Awake()
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
        EventBus.Subscribe<GoldChangedEvent>(OnGoldChanged);
        EventBus.Subscribe<GameStateChangedEvent>(OnGameStateChanged);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<GoldChangedEvent>(OnGoldChanged);
        EventBus.Unsubscribe<GameStateChangedEvent>(OnGameStateChanged);
    }

    private void Update()
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Playing)
        {
            matchTimer += Time.deltaTime;
            UpdateTimerDisplay();
        }

        UpdateCastleHealth();
    }

    public void SetLocalPlayer(NetworkPlayer player)
    {
        localPlayer = player;
        UpdateGoldDisplay();
    }

    private void UpdateGoldDisplay()
    {
        if (localPlayer == null) return;
        if (goldText != null) goldText.text = localPlayer.Gold.ToString();
        if (incomeText != null) incomeText.text = $"+{localPlayer.Income}";
    }

    private void UpdateTimerDisplay()
    {
        if (timerText == null) return;
        int minutes = Mathf.FloorToInt(matchTimer / 60f);
        int seconds = Mathf.FloorToInt(matchTimer % 60f);
        timerText.text = $"{minutes:00}:{seconds:00}";
    }

    private void UpdateCastleHealth()
    {
        var castles = FindObjectsByType<Castle>(FindObjectsSortMode.None);
        foreach (var castle in castles)
        {
            if (localPlayer == null) continue;

            var slider = castle.TeamId == localPlayer.TeamId ? allyCastleHealthBar : enemyCastleHealthBar;
            if (slider != null && castle.Health != null)
                slider.value = castle.Health.HealthPercent;
        }
    }

    private void OnGoldChanged(GoldChangedEvent evt)
    {
        if (localPlayer != null && evt.PlayerId == localPlayer.PlayerId)
            UpdateGoldDisplay();
    }

    private void OnGameStateChanged(GameStateChangedEvent evt)
    {
        if (evt.NewState == GameState.Playing)
            matchTimer = 0f;
    }
}
