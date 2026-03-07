using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InfoPanelUI : MonoBehaviour
{
    public static InfoPanelUI Instance { get; private set; }

    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Image panelBackground;

    [Header("Portrait")]
    [SerializeField] private Image portraitFrame;
    [SerializeField] private Image portraitIcon;

    [Header("Info")]
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private Image hpBarFrame;
    [SerializeField] private Image hpBarFill;
    [SerializeField] private TextMeshProUGUI hpText;
    [SerializeField] private TextMeshProUGUI statsText;

    private UIThemeData theme;
    private GameObject trackedTarget;
    private Health trackedHealth;
    private bool showingHero;

    public void Init(GameObject panel, Image bg, Image portrait, Image portIcon,
                     TextMeshProUGUI nameTxt, Image hpFrame, Image hpFill,
                     TextMeshProUGUI hpTxt, TextMeshProUGUI statsTxt)
    {
        panelRoot = panel;
        panelBackground = bg;
        portraitFrame = portrait;
        portraitIcon = portIcon;
        nameText = nameTxt;
        hpBarFrame = hpFrame;
        hpBarFill = hpFill;
        hpText = hpTxt;
        statsText = statsTxt;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        theme = Resources.Load<UIThemeData>("UITheme");
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnEnable()
    {
        EventBus.Subscribe<SelectionChangedEvent>(OnSelectionChanged);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<SelectionChangedEvent>(OnSelectionChanged);
        UntrackHealth();
    }

    private void Update()
    {
        if (showingHero && trackedTarget == null)
            TryShowHero();

        UpdateHealthBar();
    }

    public void TryShowHero()
    {
        var local = NetworkPlayer.Local;
        if (local == null) return;

        trackedTarget = local.gameObject;
        trackedHealth = local.GetComponent<Health>();
        showingHero = true;

        if (nameText != null) nameText.text = "Hero";
        if (statsText != null)
        {
            var autoAttack = local.GetComponent<HeroAutoAttack>();
            string stats = "";
            if (autoAttack != null)
                stats = $"Damage: {autoAttack.AttackDamage:F0}\nRange: {autoAttack.AttackRange:F0}\nType: Hero";
            statsText.text = stats;
        }

        SetPortrait(theme != null ? theme.iconHero : null, new Color(0.3f, 0.7f, 1f));
        UpdateHealthBar();
    }

    private void OnSelectionChanged(SelectionChangedEvent evt)
    {
        UntrackHealth();

        if (evt.Selected == null)
        {
            TryShowHero();
            return;
        }

        showingHero = false;
        trackedTarget = evt.Selected;

        var unit = evt.Selected.GetComponent<Unit>();
        if (unit != null)
        {
            ShowUnit(unit);
            return;
        }

        var building = evt.Selected.GetComponent<Building>();
        if (building != null)
        {
            ShowBuilding(building);
            return;
        }

        var castle = evt.Selected.GetComponent<Castle>();
        if (castle != null)
        {
            ShowCastle(castle);
            return;
        }

        TryShowHero();
    }

    private void ShowUnit(Unit unit)
    {
        trackedHealth = unit.GetComponent<Health>();
        var data = unit.Data;

        if (nameText != null)
            nameText.text = data != null ? data.displayName : "Unit";

        if (statsText != null && data != null)
        {
            statsText.text = $"Damage: {data.attackDamage:F0}\n" +
                             $"Speed: {data.moveSpeed:F0}\n" +
                             $"Attack: {data.attackType}\n" +
                             $"Armor: {data.armorType}";
        }

        bool isEnemy = NetworkPlayer.Local != null && unit.TeamId != NetworkPlayer.Local.TeamId;
        SetPortrait(theme != null ? theme.iconUnit : null,
            isEnemy ? new Color(1f, 0.3f, 0.3f) : new Color(0.3f, 1f, 0.5f));
        UpdateHealthBar();
    }

    private void ShowBuilding(Building building)
    {
        trackedHealth = building.GetComponent<Health>();
        var data = building.Data;

        if (nameText != null)
            nameText.text = data != null ? data.buildingName : "Building";

        if (statsText != null && data != null)
        {
            string info = $"Tier: {data.tier}\nArmor: {data.armorType}";
            if (data.spawnedUnit != null)
                info += $"\nSpawns: {data.spawnedUnit.displayName}\nEvery: {data.spawnInterval:F0}s";
            statsText.text = info;
        }

        bool isEnemy = NetworkPlayer.Local != null && building.TeamId != NetworkPlayer.Local.TeamId;
        SetPortrait(theme != null ? theme.iconBuild : null,
            isEnemy ? new Color(1f, 0.4f, 0.2f) : new Color(0.4f, 0.8f, 1f));
        UpdateHealthBar();
    }

    private void ShowCastle(Castle castle)
    {
        trackedHealth = castle.Health;

        bool isEnemy = NetworkPlayer.Local != null && castle.TeamId != NetworkPlayer.Local.TeamId;
        if (nameText != null)
            nameText.text = isEnemy ? "Enemy Castle" : "Allied Castle";

        if (statsText != null)
            statsText.text = $"Team: {castle.TeamId}\nArmor: Fortified";

        SetPortrait(theme != null ? theme.iconCastle : null,
            isEnemy ? new Color(1f, 0.2f, 0.2f) : new Color(0.2f, 0.6f, 1f));
        UpdateHealthBar();
    }

    private void UpdateHealthBar()
    {
        if (trackedHealth == null) return;

        float pct = trackedHealth.HealthPercent;
        if (hpBarFill != null)
            hpBarFill.fillAmount = pct;

        if (hpText != null)
            hpText.text = $"{trackedHealth.CurrentHealth:F0} / {trackedHealth.MaxHealth:F0}";
    }

    private void UntrackHealth()
    {
        trackedHealth = null;
        trackedTarget = null;
    }

    private void SetPortrait(Sprite icon, Color fallbackColor)
    {
        if (portraitIcon == null) return;

        if (icon != null)
        {
            portraitIcon.sprite = icon;
            portraitIcon.color = Color.white;
        }
        else
        {
            portraitIcon.sprite = null;
            portraitIcon.color = fallbackColor;
        }
    }
}
