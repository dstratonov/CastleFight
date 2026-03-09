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

    [Header("Stat Rows")]
    [SerializeField] private Image damageIcon;
    [SerializeField] private TextMeshProUGUI damageText;
    [SerializeField] private Image armorIcon;
    [SerializeField] private TextMeshProUGUI armorText;
    [SerializeField] private Image extraIcon;
    [SerializeField] private TextMeshProUGUI extraText;

    [Header("Spawn Timer")]
    [SerializeField] private GameObject spawnTimerRoot;
    [SerializeField] private Image spawnBarFill;
    [SerializeField] private TextMeshProUGUI spawnTimerText;
    [SerializeField] private Image spawnUnitIcon;

    private UIThemeData theme;
    private GameObject trackedTarget;
    private Health trackedHealth;
    private Spawner trackedSpawner;
    private bool showingHero;

    public void Init(GameObject panel, Image bg, Image portrait, Image portIcon,
                     TextMeshProUGUI nameTxt, Image hpFrame, Image hpFill,
                     TextMeshProUGUI hpTxt,
                     Image dmgIcon, TextMeshProUGUI dmgTxt,
                     Image armIcon, TextMeshProUGUI armTxt,
                     Image extIcon, TextMeshProUGUI extTxt)
    {
        panelRoot = panel;
        panelBackground = bg;
        portraitFrame = portrait;
        portraitIcon = portIcon;
        nameText = nameTxt;
        hpBarFrame = hpFrame;
        hpBarFill = hpFill;
        hpText = hpTxt;
        damageIcon = dmgIcon;
        damageText = dmgTxt;
        armorIcon = armIcon;
        armorText = armTxt;
        extraIcon = extIcon;
        extraText = extTxt;
    }

    public void SetSpawnTimerUI(GameObject root, Image barFill, TextMeshProUGUI timerTxt, Image unitIcon)
    {
        spawnTimerRoot = root;
        spawnBarFill = barFill;
        spawnTimerText = timerTxt;
        spawnUnitIcon = unitIcon;
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
        UpdateSpawnTimer();
    }

    public void TryShowHero()
    {
        var local = NetworkPlayer.Local;
        if (local == null) return;

        trackedTarget = local.gameObject;
        trackedHealth = local.GetComponent<Health>();
        trackedSpawner = null;
        showingHero = true;

        if (nameText != null) nameText.text = "Hero";

        var autoAttack = local.GetComponent<HeroAutoAttack>();
        if (autoAttack != null)
        {
            SetStatRow(damageIcon, damageText, theme?.iconSword, $"Damage: {autoAttack.AttackDamage:F0}");
            SetStatRow(armorIcon, armorText, theme?.iconArmor, $"Range: {autoAttack.AttackRange:F0}");
            SetStatRow(extraIcon, extraText, theme?.iconSpeed, "Type: Hero");
        }
        else
        {
            ClearStatRows();
        }

        SetPortrait(theme != null ? theme.iconHero : null, new Color(0.3f, 0.7f, 1f));
        HideSpawnTimer();
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
        if (unit != null) { ShowUnit(unit); return; }

        var building = evt.Selected.GetComponent<Building>();
        if (building != null) { ShowBuilding(building); return; }

        var castle = evt.Selected.GetComponent<Castle>();
        if (castle != null) { ShowCastle(castle); return; }

        TryShowHero();
    }

    private void ShowUnit(Unit unit)
    {
        trackedHealth = unit.GetComponent<Health>();
        trackedSpawner = null;
        var data = unit.Data;

        if (nameText != null)
            nameText.text = data != null ? data.displayName : "Unit";

        if (data != null)
        {
            SetStatRow(damageIcon, damageText, theme?.iconSword, $"Damage: {data.attackDamage:F0}");
            SetStatRow(armorIcon, armorText, theme?.iconArmor, $"Armor: {data.armorType}");
            SetStatRow(extraIcon, extraText, theme?.iconSpeed, $"Speed: {data.moveSpeed:F0}");
        }
        else
        {
            ClearStatRows();
        }

        bool isEnemy = NetworkPlayer.Local != null && unit.TeamId != NetworkPlayer.Local.TeamId;
        Sprite portrait = data != null && data.icon != null ? data.icon : theme?.iconUnit;
        SetPortrait(portrait, isEnemy ? new Color(1f, 0.3f, 0.3f) : new Color(0.3f, 1f, 0.5f));
        HideSpawnTimer();
        UpdateHealthBar();
    }

    private void ShowBuilding(Building building)
    {
        trackedHealth = building.GetComponent<Health>();
        trackedSpawner = building.GetComponent<Spawner>();
        var data = building.Data;

        if (nameText != null)
            nameText.text = data != null ? data.buildingName : "Building";

        if (data != null)
        {
            SetStatRow(damageIcon, damageText, theme?.iconBuild,  $"Tier: {data.tier}");
            SetStatRow(armorIcon, armorText, theme?.iconArmor, $"Armor: {data.armorType}");
            string extra = data.spawnedUnit != null
                ? $"Spawns: {data.spawnedUnit.displayName}"
                : "";
            SetStatRow(extraIcon, extraText, theme?.iconSpeed, extra);
        }
        else
        {
            ClearStatRows();
        }

        bool isEnemy = NetworkPlayer.Local != null && building.TeamId != NetworkPlayer.Local.TeamId;
        Sprite portrait = data != null && data.icon != null ? data.icon : theme?.iconBuild;
        SetPortrait(portrait, isEnemy ? new Color(1f, 0.4f, 0.2f) : new Color(0.4f, 0.8f, 1f));

        if (trackedSpawner != null && data?.spawnedUnit != null)
            ShowSpawnTimer(data.spawnedUnit);
        else
            HideSpawnTimer();

        UpdateHealthBar();
    }

    private void ShowCastle(Castle castle)
    {
        trackedHealth = castle.Health;
        trackedSpawner = null;

        bool isEnemy = NetworkPlayer.Local != null && castle.TeamId != NetworkPlayer.Local.TeamId;
        if (nameText != null)
            nameText.text = isEnemy ? "Enemy Castle" : "Allied Castle";

        SetStatRow(damageIcon, damageText, theme?.iconCastle, $"Team: {castle.TeamId}");
        SetStatRow(armorIcon, armorText, theme?.iconArmor, "Armor: Fortified");
        SetStatRow(extraIcon, extraText, null, "");

        SetPortrait(theme != null ? theme.iconCastle : null,
            isEnemy ? new Color(1f, 0.2f, 0.2f) : new Color(0.2f, 0.6f, 1f));
        HideSpawnTimer();
        UpdateHealthBar();
    }

    private void ShowSpawnTimer(UnitData unitData)
    {
        if (spawnTimerRoot != null)
            spawnTimerRoot.SetActive(true);

        if (spawnUnitIcon != null && unitData.icon != null)
        {
            spawnUnitIcon.sprite = unitData.icon;
            spawnUnitIcon.color = Color.white;
            spawnUnitIcon.enabled = true;
        }
        else if (spawnUnitIcon != null)
        {
            spawnUnitIcon.enabled = false;
        }
    }

    private void HideSpawnTimer()
    {
        trackedSpawner = null;
        if (spawnTimerRoot != null)
            spawnTimerRoot.SetActive(false);
    }

    private void UpdateSpawnTimer()
    {
        if (trackedSpawner == null) return;

        float progress = trackedSpawner.SpawnProgress;

        if (spawnBarFill != null)
            spawnBarFill.fillAmount = progress;

        if (spawnTimerText != null)
        {
            float remaining = trackedSpawner.SpawnInterval * (1f - progress);
            spawnTimerText.text = $"{remaining:F1}s";
        }
    }

    private void SetStatRow(Image icon, TextMeshProUGUI text, Sprite sprite, string value)
    {
        bool hasContent = !string.IsNullOrEmpty(value);

        if (icon != null)
        {
            icon.gameObject.transform.parent.gameObject.SetActive(hasContent);
            if (hasContent && sprite != null)
            {
                icon.sprite = sprite;
                icon.color = Color.white;
                icon.enabled = true;
            }
            else
            {
                icon.enabled = false;
            }
        }

        if (text != null)
            text.text = hasContent ? value : "";
    }

    private void ClearStatRows()
    {
        SetStatRow(damageIcon, damageText, null, "");
        SetStatRow(armorIcon, armorText, null, "");
        SetStatRow(extraIcon, extraText, null, "");
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
        trackedSpawner = null;
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
