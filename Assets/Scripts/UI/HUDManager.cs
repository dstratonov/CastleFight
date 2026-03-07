using UnityEngine;
using UnityEngine.UI;
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
    [SerializeField] private Image allyCastleHealthBar;
    [SerializeField] private Image enemyCastleHealthBar;
    [SerializeField] private TextMeshProUGUI allyCastleText;
    [SerializeField] private TextMeshProUGUI enemyCastleText;

    private float matchTimer;
    private NetworkPlayer localPlayer;
    private Castle allyCastle;
    private Castle enemyCastle;

    public void Init(TextMeshProUGUI gold, TextMeshProUGUI income,
                     TextMeshProUGUI timer, TextMeshProUGUI team,
                     Image allyBar, Image enemyBar,
                     TextMeshProUGUI allyText, TextMeshProUGUI enemyText)
    {
        goldText = gold;
        incomeText = income;
        timerText = timer;
        teamText = team;
        allyCastleHealthBar = allyBar;
        enemyCastleHealthBar = enemyBar;
        allyCastleText = allyText;
        enemyCastleText = enemyText;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
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

        if (allyCastle == null || enemyCastle == null)
            FindCastles();

        UpdateCastleHealthBars();
    }

    public void SetLocalPlayer(NetworkPlayer player)
    {
        localPlayer = player;
        UpdateGoldDisplay();
        FindCastles();
    }

    private void FindCastles()
    {
        if (localPlayer == null) return;

        var castles = FindObjectsByType<Castle>(FindObjectsSortMode.None);
        foreach (var c in castles)
        {
            if (c.TeamId == localPlayer.TeamId)
                allyCastle = c;
            else
                enemyCastle = c;
        }
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

    private void UpdateCastleHealthBars()
    {
        if (allyCastle != null && allyCastle.Health != null)
        {
            if (allyCastleHealthBar != null)
                allyCastleHealthBar.fillAmount = allyCastle.Health.HealthPercent;
            if (allyCastleText != null)
                allyCastleText.text = $"Ally  {allyCastle.Health.CurrentHealth:F0}/{allyCastle.Health.MaxHealth:F0}";
        }

        if (enemyCastle != null && enemyCastle.Health != null)
        {
            if (enemyCastleHealthBar != null)
                enemyCastleHealthBar.fillAmount = enemyCastle.Health.HealthPercent;
            if (enemyCastleText != null)
                enemyCastleText.text = $"Enemy  {enemyCastle.Health.CurrentHealth:F0}/{enemyCastle.Health.MaxHealth:F0}";
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
