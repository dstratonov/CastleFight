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
    private float castleSearchCooldown;

    public float MatchTimer => matchTimer;

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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
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

    private int lastDisplayedIncome = -1;

    private void Update()
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Playing)
        {
            matchTimer += Time.deltaTime;
            UpdateTimerDisplay();
        }

        if (allyCastle == null || enemyCastle == null)
        {
            castleSearchCooldown -= Time.deltaTime;
            if (castleSearchCooldown <= 0f)
            {
                castleSearchCooldown = 1f;
                FindCastles();
            }
        }

        if (localPlayer != null && localPlayer.Income != lastDisplayedIncome)
        {
            lastDisplayedIncome = localPlayer.Income;
            if (incomeText != null) incomeText.text = $"+{localPlayer.Income}";
        }

        UpdateCastleHealthBars();
        UpdateNotification();
    }

    public void SetLocalPlayer(NetworkPlayer player)
    {
        localPlayer = player;
        UpdateGoldDisplay();
        UpdateTeamDisplay();
        FindCastles();
    }

    private void FindCastles()
    {
        if (localPlayer == null) return;

        var castles = GameRegistry.Castles;
        foreach (var c in castles)
        {
            if (c == null) continue;
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

    private void UpdateTeamDisplay()
    {
        if (localPlayer == null || teamText == null) return;
        string teamLabel = localPlayer.TeamId == 0 ? "Blue" : "Red";
        teamText.text = $"Team: {teamLabel}";
    }

    private void UpdateTimerDisplay()
    {
        if (timerText == null) return;
        int minutes = Mathf.FloorToInt(matchTimer / 60f);
        int seconds = Mathf.FloorToInt(matchTimer % 60f);
        timerText.text = $"{minutes:00}:{seconds:00}";
    }

    private static readonly Color COL_ALLY_CASTLE = new(0.25f, 0.65f, 0.95f);
    private static readonly Color COL_ENEMY_CASTLE = new(0.92f, 0.28f, 0.25f);
    private static readonly Color COL_CRITICAL = new(1f, 0.15f, 0.1f);

    private void UpdateCastleHealthBars()
    {
        if (allyCastle != null && allyCastle.Health != null)
        {
            float pct = allyCastle.Health.HealthPercent;
            if (allyCastleHealthBar != null)
            {
                allyCastleHealthBar.fillAmount = pct;
                allyCastleHealthBar.color = GetCastleBarColor(pct, COL_ALLY_CASTLE);
            }
            if (allyCastleText != null)
                allyCastleText.text = $"Ally  {allyCastle.Health.CurrentHealth:F0}/{allyCastle.Health.MaxHealth:F0}";
        }

        if (enemyCastle != null && enemyCastle.Health != null)
        {
            float pct = enemyCastle.Health.HealthPercent;
            if (enemyCastleHealthBar != null)
            {
                enemyCastleHealthBar.fillAmount = pct;
                enemyCastleHealthBar.color = GetCastleBarColor(pct, COL_ENEMY_CASTLE);
            }
            if (enemyCastleText != null)
                enemyCastleText.text = $"Enemy  {enemyCastle.Health.CurrentHealth:F0}/{enemyCastle.Health.MaxHealth:F0}";
        }
    }

    private Color GetCastleBarColor(float pct, Color baseColor)
    {
        if (pct > 0.3f) return baseColor;
        float pulse = (Mathf.Sin(Time.time * 4f) + 1f) * 0.5f;
        return Color.Lerp(baseColor, COL_CRITICAL, pulse * (1f - pct / 0.3f));
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

    // ====================================================================
    // Notification
    // ====================================================================

    private TextMeshProUGUI notificationText;
    private float notificationTimer;

    public void SetNotificationText(TextMeshProUGUI text) => notificationText = text;

    /// <summary>Shows a brief notification message below the HUD bar.</summary>
    public void ShowNotification(string message)
    {
        if (notificationText == null) return;
        notificationText.text = message;
        notificationText.gameObject.SetActive(true);
        notificationTimer = 2f;
    }

    private void UpdateNotification()
    {
        if (notificationText == null || !notificationText.gameObject.activeSelf) return;

        notificationTimer -= Time.deltaTime;
        if (notificationTimer <= 0f)
        {
            notificationText.gameObject.SetActive(false);
        }
        else if (notificationTimer < 0.5f)
        {
            var c = notificationText.color;
            c.a = notificationTimer / 0.5f;
            notificationText.color = c;
        }
        else
        {
            var c = notificationText.color;
            c.a = 1f;
            notificationText.color = c;
        }
    }
}
