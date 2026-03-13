using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GameUIBuilder : MonoBehaviour
{
    public static GameUIBuilder Instance { get; private set; }

    private Canvas canvas;
    private HUDManager hudManager;
    private BuildMenuUI buildMenuUI;
    private InfoPanelUI infoPanelUI;
    private SelectionManager selectionManager;
    private TooltipUI tooltipUI;
    private GameOverUI gameOverUI;
    private UIThemeData theme;
    private TMP_FontAsset cachedFontAsset;

    public HUDManager HUD => hudManager;
    public BuildMenuUI BuildMenu => buildMenuUI;
    public InfoPanelUI InfoPanel => infoPanelUI;

    private static readonly Color COL_PANEL_BG = new(0.08f, 0.07f, 0.06f, 0.92f);
    private static readonly Color COL_GOLD = new(1f, 0.82f, 0.15f);
    private static readonly Color COL_INCOME = new(0.55f, 0.85f, 0.35f);
    private static readonly Color COL_TITLE = new(0.9f, 0.82f, 0.55f);
    private static readonly Color COL_NAME = new(0.95f, 0.92f, 0.85f);
    private static readonly Color COL_STAT = new(0.78f, 0.76f, 0.72f);
    private static readonly Color COL_HP_ALLY = new(0.2f, 0.72f, 0.2f);
    private static readonly Color COL_HP_ENEMY = new(0.82f, 0.22f, 0.18f);
    private static readonly Color COL_ALLY_CASTLE = new(0.25f, 0.65f, 0.95f);
    private static readonly Color COL_ENEMY_CASTLE = new(0.92f, 0.28f, 0.25f);
    private static readonly Color COL_FRAME = new(0.35f, 0.3f, 0.22f);
    private static readonly Color COL_SPRITE_DARK = new(0.18f, 0.15f, 0.12f, 0.95f);
    private static readonly Color COL_SPRITE_FRAME = new(0.45f, 0.38f, 0.28f);

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        theme = Resources.Load<UIThemeData>("UITheme");
        if (theme != null && theme.medievalFont != null)
            cachedFontAsset = TMP_FontAsset.CreateFontAsset(theme.medievalFont);
        CreateUI();
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

    private void CreateUI()
    {
        var canvasObj = new GameObject("GameCanvas");
        canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasObj.AddComponent<GraphicRaycaster>();

        CreateHUD(canvasObj.transform);
        CreateBuildPanel(canvasObj.transform);
        CreateInfoPanel(canvasObj.transform);
        CreateTooltip(canvasObj.transform);
        CreateGameOverPanel(canvasObj.transform);
        CreateSelectionManager();
        CreateDebugSystem(canvasObj.transform);
        CreateCombatVFX();
    }

    // ====================================================================
    // TOP BAR (HUD)
    // ====================================================================

    private void CreateHUD(Transform parent)
    {
        var hudObj = CreatePanel("HUD", parent);
        var rect = hudObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(0.5f, 1);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(0, 60);

        var bg = hudObj.AddComponent<Image>();
        if (theme != null && theme.topBarBackground != null)
        {
            bg.sprite = theme.topBarBackground;
            bg.type = Image.Type.Sliced;
            bg.color = COL_SPRITE_DARK;
        }
        else
        {
            bg.color = COL_PANEL_BG;
        }

        var layout = hudObj.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 8, 8);
        layout.spacing = 20;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = false;
        layout.childControlHeight = true;

        CreateGoldGroup(hudObj.transform, out var goldText, out var incomeText);

        var timerText = CreateText("TimerText", hudObj.transform, "00:00", 22, TextAlignmentOptions.Midline, 100);
        var teamText = CreateText("TeamText", hudObj.transform, "Team 0", 18, TextAlignmentOptions.MidlineLeft, 120);

        CreateCastleHealthGroup(hudObj.transform,
            out var allyCastleBar, out var enemyCastleBar,
            out var allyCastleText, out var enemyCastleText);

        hudManager = hudObj.AddComponent<HUDManager>();
        hudManager.Init(goldText, incomeText, timerText, teamText,
                        allyCastleBar, enemyCastleBar, allyCastleText, enemyCastleText);

        var notifText = CreateText("NotificationText", parent, "", 20, TextAlignmentOptions.Center, 0);
        var notifRect = notifText.GetComponent<RectTransform>();
        notifRect.anchorMin = new Vector2(0.3f, 1);
        notifRect.anchorMax = new Vector2(0.7f, 1);
        notifRect.pivot = new Vector2(0.5f, 1);
        notifRect.anchoredPosition = new Vector2(0, -65);
        notifRect.sizeDelta = new Vector2(0, 30);
        notifText.color = new Color(1f, 0.4f, 0.3f);
        notifText.fontStyle = FontStyles.Bold;
        notifText.enableAutoSizing = true;
        notifText.fontSizeMin = 14;
        notifText.fontSizeMax = 20;
        notifText.gameObject.SetActive(false);
        hudManager.SetNotificationText(notifText);
    }

    private void CreateGoldGroup(Transform parent, out TextMeshProUGUI goldText, out TextMeshProUGUI incomeText)
    {
        var group = CreatePanel("GoldGroup", parent);
        var le = group.AddComponent<LayoutElement>();
        le.preferredWidth = 220;

        var hLayout = group.AddComponent<HorizontalLayoutGroup>();
        hLayout.spacing = 6;
        hLayout.childAlignment = TextAnchor.MiddleLeft;
        hLayout.childControlWidth = false;
        hLayout.childControlHeight = true;

        if (theme != null && theme.iconGold != null)
        {
            var iconObj = CreatePanel("GoldIcon", group.transform);
            var iconImg = iconObj.AddComponent<Image>();
            iconImg.sprite = theme.iconGold;
            iconImg.preserveAspect = true;
            var iconLe = iconObj.AddComponent<LayoutElement>();
            iconLe.preferredWidth = 32;
            iconLe.preferredHeight = 32;
        }

        goldText = CreateText("GoldText", group.transform, "100", 22, TextAlignmentOptions.MidlineLeft, 100);
        goldText.color = COL_GOLD;

        incomeText = CreateText("IncomeText", group.transform, "+10", 16, TextAlignmentOptions.MidlineLeft, 70);
        incomeText.color = COL_INCOME;
    }

    private void CreateCastleHealthGroup(Transform parent,
        out Image allyBar, out Image enemyBar,
        out TextMeshProUGUI allyText, out TextMeshProUGUI enemyText)
    {
        var group = CreatePanel("CastleHealth", parent);
        var le = group.AddComponent<LayoutElement>();
        le.preferredWidth = 500;
        le.flexibleWidth = 1;

        var hLayout = group.AddComponent<HorizontalLayoutGroup>();
        hLayout.spacing = 20;
        hLayout.childAlignment = TextAnchor.MiddleCenter;
        hLayout.childControlWidth = false;
        hLayout.childControlHeight = true;

        allyBar = CreateHealthBar("AllyCastle", group.transform, COL_ALLY_CASTLE, out allyText);
        enemyBar = CreateHealthBar("EnemyCastle", group.transform, COL_ENEMY_CASTLE, out enemyText);
    }

    private Image CreateHealthBar(string name, Transform parent, Color fillColor, out TextMeshProUGUI labelText)
    {
        var barGroup = CreatePanel(name + "Bar", parent);
        var barLe = barGroup.AddComponent<LayoutElement>();
        barLe.preferredWidth = 220;

        var label = CreateText(name + "Label", barGroup.transform, name.Replace("Castle", " Castle"),
            14, TextAlignmentOptions.Midline, 0);
        label.enableAutoSizing = true;
        label.fontSizeMin = 10;
        label.fontSizeMax = 14;
        var labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0, 0.7f);
        labelRect.anchorMax = new Vector2(1, 1f);
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        var frameObj = CreatePanel(name + "Frame", barGroup.transform);
        var frameRect = frameObj.GetComponent<RectTransform>();
        frameRect.anchorMin = new Vector2(0, 0);
        frameRect.anchorMax = new Vector2(1, 0.65f);
        frameRect.offsetMin = Vector2.zero;
        frameRect.offsetMax = Vector2.zero;

        var frameBg = frameObj.AddComponent<Image>();
        if (theme != null && theme.hpFrame != null)
        {
            frameBg.sprite = theme.hpFrame;
            frameBg.type = Image.Type.Sliced;
            frameBg.color = COL_SPRITE_FRAME;
        }
        else
        {
            frameBg.color = new Color(0.12f, 0.12f, 0.12f, 0.8f);
        }

        var fillObj = CreatePanel(name + "Fill", frameObj.transform);
        var fillRect = fillObj.GetComponent<RectTransform>();
        fillRect.anchorMin = new Vector2(0.05f, 0.15f);
        fillRect.anchorMax = new Vector2(0.95f, 0.85f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        var fill = fillObj.AddComponent<Image>();
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        if (theme != null && theme.hpFill != null)
        {
            fill.sprite = theme.hpFill;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
        }
        fill.color = fillColor;
        fill.fillAmount = 1f;

        labelText = label;
        return fill;
    }

    // ====================================================================
    // BUILD PANEL (bottom bar, right of info panel)
    // ====================================================================

    private void CreateBuildPanel(Transform parent)
    {
        var panelObj = CreatePanel("BuildPanel", parent);
        var rect = panelObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 0);
        rect.anchorMax = new Vector2(1, 0);
        rect.pivot = new Vector2(0, 0);
        rect.anchoredPosition = new Vector2(480, 10);
        rect.sizeDelta = new Vector2(-490, 90);

        var bg = panelObj.AddComponent<Image>();
        if (theme != null && theme.buildPanelBackground != null)
        {
            bg.sprite = theme.buildPanelBackground;
            bg.type = Image.Type.Sliced;
            bg.color = COL_SPRITE_DARK;
        }
        else
        {
            bg.color = COL_PANEL_BG;
        }

        var containerObj = CreatePanel("BuildButtonContainer", panelObj.transform);
        var containerRect = containerObj.GetComponent<RectTransform>();
        containerRect.anchorMin = Vector2.zero;
        containerRect.anchorMax = Vector2.one;
        containerRect.offsetMin = new Vector2(8, 6);
        containerRect.offsetMax = new Vector2(-8, -6);

        var grid = containerObj.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(140, 36);
        grid.spacing = new Vector2(6, 4);
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.startAxis = GridLayoutGroup.Axis.Vertical;
        grid.childAlignment = TextAnchor.MiddleLeft;
        grid.constraint = GridLayoutGroup.Constraint.FixedRowCount;
        grid.constraintCount = 2;

        buildMenuUI = panelObj.AddComponent<BuildMenuUI>();
        buildMenuUI.Init(containerObj.transform, cachedFontAsset);
    }

    // ====================================================================
    // INFO PANEL (bottom-left)
    // ====================================================================

    private void CreateInfoPanel(Transform parent)
    {
        var panelObj = CreatePanel("InfoPanel", parent);
        var rect = panelObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 0);
        rect.anchorMax = new Vector2(0, 0);
        rect.pivot = new Vector2(0, 0);
        rect.anchoredPosition = new Vector2(10, 10);
        rect.sizeDelta = new Vector2(460, 200);

        var bgImg = panelObj.AddComponent<Image>();
        if (theme != null && theme.infoPanelBackground != null)
        {
            bgImg.sprite = theme.infoPanelBackground;
            bgImg.type = Image.Type.Sliced;
            bgImg.color = COL_SPRITE_DARK;
        }
        else
        {
            bgImg.color = COL_PANEL_BG;
        }

        var frameObj = CreatePanel("Frame", panelObj.transform);
        var frameRect = frameObj.GetComponent<RectTransform>();
        frameRect.anchorMin = Vector2.zero;
        frameRect.anchorMax = Vector2.one;
        frameRect.offsetMin = Vector2.zero;
        frameRect.offsetMax = Vector2.zero;
        var frameImg = frameObj.AddComponent<Image>();
        if (theme != null && theme.infoPanelFrame != null)
        {
            frameImg.sprite = theme.infoPanelFrame;
            frameImg.type = Image.Type.Sliced;
            frameImg.color = COL_SPRITE_FRAME;
        }
        else
        {
            frameImg.color = Color.clear;
        }

        // Portrait area
        var portraitObj = CreatePanel("Portrait", panelObj.transform);
        var portraitRect = portraitObj.GetComponent<RectTransform>();
        portraitRect.anchorMin = new Vector2(0, 0.5f);
        portraitRect.anchorMax = new Vector2(0, 0.5f);
        portraitRect.pivot = new Vector2(0, 0.5f);
        portraitRect.anchoredPosition = new Vector2(16, 0);
        portraitRect.sizeDelta = new Vector2(110, 110);

        if (theme != null && theme.portraitUnderlay != null)
        {
            var underlayObj = CreatePanel("PortraitUnderlay", portraitObj.transform);
            var ulRect = underlayObj.GetComponent<RectTransform>();
            ulRect.anchorMin = Vector2.zero;
            ulRect.anchorMax = Vector2.one;
            ulRect.offsetMin = new Vector2(-4, -4);
            ulRect.offsetMax = new Vector2(4, 4);
            var ulImg = underlayObj.AddComponent<Image>();
            ulImg.sprite = theme.portraitUnderlay;
            ulImg.preserveAspect = true;
            ulImg.color = new Color(0f, 0f, 0f, 0.5f);
        }

        var portraitIconObj = CreatePanel("PortraitIcon", portraitObj.transform);
        var piRect = portraitIconObj.GetComponent<RectTransform>();
        piRect.anchorMin = new Vector2(0.15f, 0.15f);
        piRect.anchorMax = new Vector2(0.85f, 0.85f);
        piRect.offsetMin = Vector2.zero;
        piRect.offsetMax = Vector2.zero;
        var portraitIcon = portraitIconObj.AddComponent<Image>();
        portraitIcon.preserveAspect = true;
        portraitIcon.color = new Color(0.5f, 0.5f, 0.5f, 0.3f);

        var portraitFrameObj = CreatePanel("PortraitFrame", portraitObj.transform);
        var pfRect = portraitFrameObj.GetComponent<RectTransform>();
        pfRect.anchorMin = Vector2.zero;
        pfRect.anchorMax = Vector2.one;
        pfRect.offsetMin = Vector2.zero;
        pfRect.offsetMax = Vector2.zero;
        var portraitFrame = portraitFrameObj.AddComponent<Image>();
        if (theme != null && theme.portraitFrame != null)
        {
            portraitFrame.sprite = theme.portraitFrame;
            portraitFrame.preserveAspect = true;
            portraitFrame.color = COL_SPRITE_FRAME;
        }
        else
        {
            portraitFrame.color = new Color(0.3f, 0.25f, 0.2f);
        }

        // Info area (right of portrait)
        var infoArea = CreatePanel("InfoArea", panelObj.transform);
        var infoRect = infoArea.GetComponent<RectTransform>();
        infoRect.anchorMin = new Vector2(0, 0);
        infoRect.anchorMax = new Vector2(1, 1);
        infoRect.offsetMin = new Vector2(140, 16);
        infoRect.offsetMax = new Vector2(-18, -16);

        var nameText = CreateText("NameText", infoArea.transform, "Hero", 22, TextAlignmentOptions.MidlineLeft, 0);
        var nameRect = nameText.GetComponent<RectTransform>();
        nameRect.anchorMin = new Vector2(0, 0.78f);
        nameRect.anchorMax = new Vector2(1, 1);
        nameRect.offsetMin = Vector2.zero;
        nameRect.offsetMax = Vector2.zero;
        nameText.color = COL_NAME;
        nameText.fontStyle = FontStyles.Bold;
        nameText.enableAutoSizing = true;
        nameText.fontSizeMin = 16;
        nameText.fontSizeMax = 22;

        // HP bar
        var hpBarObj = CreatePanel("HPBar", infoArea.transform);
        var hpBarRect = hpBarObj.GetComponent<RectTransform>();
        hpBarRect.anchorMin = new Vector2(0, 0.56f);
        hpBarRect.anchorMax = new Vector2(1, 0.78f);
        hpBarRect.offsetMin = Vector2.zero;
        hpBarRect.offsetMax = Vector2.zero;

        var hpFrameImg = hpBarObj.AddComponent<Image>();
        if (theme != null && theme.hpFrame != null)
        {
            hpFrameImg.sprite = theme.hpFrame;
            hpFrameImg.type = Image.Type.Sliced;
            hpFrameImg.color = COL_SPRITE_FRAME;
        }
        else
        {
            hpFrameImg.color = new Color(0.12f, 0.12f, 0.12f, 0.9f);
        }

        var hpFillObj = CreatePanel("HPFill", hpBarObj.transform);
        var hpFillRect = hpFillObj.GetComponent<RectTransform>();
        hpFillRect.anchorMin = new Vector2(0.03f, 0.1f);
        hpFillRect.anchorMax = new Vector2(0.97f, 0.9f);
        hpFillRect.offsetMin = Vector2.zero;
        hpFillRect.offsetMax = Vector2.zero;
        var hpFill = hpFillObj.AddComponent<Image>();
        hpFill.color = COL_HP_ALLY;
        hpFill.type = Image.Type.Filled;
        hpFill.fillMethod = Image.FillMethod.Horizontal;
        if (theme != null && theme.hpFill != null)
            hpFill.sprite = theme.hpFill;

        var hpText = CreateText("HPText", hpBarObj.transform, "", 13, TextAlignmentOptions.Center, 0);
        var hpTextRect = hpText.GetComponent<RectTransform>();
        hpTextRect.anchorMin = Vector2.zero;
        hpTextRect.anchorMax = Vector2.one;
        hpTextRect.offsetMin = new Vector2(4, 0);
        hpTextRect.offsetMax = new Vector2(-4, 0);
        hpText.color = Color.white;
        hpText.enableAutoSizing = true;
        hpText.fontSizeMin = 9;
        hpText.fontSizeMax = 13;

        // Stats area
        var statsArea = CreatePanel("StatsArea", infoArea.transform);
        var statsAreaRect = statsArea.GetComponent<RectTransform>();
        statsAreaRect.anchorMin = new Vector2(0, 0);
        statsAreaRect.anchorMax = new Vector2(1, 0.54f);
        statsAreaRect.offsetMin = new Vector2(0, 2);
        statsAreaRect.offsetMax = Vector2.zero;

        var statsLayout = statsArea.AddComponent<VerticalLayoutGroup>();
        statsLayout.spacing = 1;
        statsLayout.padding = new RectOffset(0, 0, 0, 0);
        statsLayout.childAlignment = TextAnchor.MiddleLeft;
        statsLayout.childControlWidth = true;
        statsLayout.childControlHeight = true;
        statsLayout.childForceExpandWidth = true;
        statsLayout.childForceExpandHeight = true;

        var dmgIcon = CreateStatRow("DmgRow", statsArea.transform,
            theme != null ? theme.iconSword : null, "Damage: --",
            out var dmgText);
        var armIcon = CreateStatRow("ArmRow", statsArea.transform,
            theme != null ? theme.iconArmor : null, "Armor: --",
            out var armText);
        var extraIcon = CreateStatRow("ExtraRow", statsArea.transform,
            theme != null ? theme.iconSpeed : null, "",
            out var extraText);

        // Spawn timer bar
        var spawnTimerObj = CreatePanel("SpawnTimer", panelObj.transform);
        var spawnTimerRect = spawnTimerObj.GetComponent<RectTransform>();
        spawnTimerRect.anchorMin = new Vector2(0, 0);
        spawnTimerRect.anchorMax = new Vector2(0, 0);
        spawnTimerRect.pivot = new Vector2(0, 0);
        spawnTimerRect.anchoredPosition = new Vector2(16, 12);
        spawnTimerRect.sizeDelta = new Vector2(110, 22);
        spawnTimerObj.SetActive(false);

        var spawnBarBg = spawnTimerObj.AddComponent<Image>();
        if (theme != null && theme.hpFrame != null)
        {
            spawnBarBg.sprite = theme.hpFrame;
            spawnBarBg.type = Image.Type.Sliced;
            spawnBarBg.color = COL_SPRITE_FRAME;
        }
        else
        {
            spawnBarBg.color = new Color(0.12f, 0.1f, 0.08f, 0.95f);
        }

        var spawnFillObj = CreatePanel("SpawnFill", spawnTimerObj.transform);
        var spawnFillRect = spawnFillObj.GetComponent<RectTransform>();
        spawnFillRect.anchorMin = new Vector2(0.03f, 0.1f);
        spawnFillRect.anchorMax = new Vector2(0.97f, 0.9f);
        spawnFillRect.offsetMin = Vector2.zero;
        spawnFillRect.offsetMax = Vector2.zero;
        var spawnFill = spawnFillObj.AddComponent<Image>();
        spawnFill.color = new Color(0.3f, 0.75f, 0.95f);
        spawnFill.type = Image.Type.Filled;
        spawnFill.fillMethod = Image.FillMethod.Horizontal;
        spawnFill.fillAmount = 0f;
        if (theme != null && theme.hpFill != null)
            spawnFill.sprite = theme.hpFill;

        var spawnIconObj = CreatePanel("SpawnIcon", spawnTimerObj.transform);
        var spawnIconRect = spawnIconObj.GetComponent<RectTransform>();
        spawnIconRect.anchorMin = new Vector2(0, 0.5f);
        spawnIconRect.anchorMax = new Vector2(0, 0.5f);
        spawnIconRect.pivot = new Vector2(1, 0.5f);
        spawnIconRect.anchoredPosition = new Vector2(-4, 0);
        spawnIconRect.sizeDelta = new Vector2(20, 20);
        var spawnIcon = spawnIconObj.AddComponent<Image>();
        spawnIcon.preserveAspect = true;
        spawnIcon.enabled = false;

        var spawnText = CreateText("SpawnText", spawnTimerObj.transform, "", 11, TextAlignmentOptions.Center, 0);
        var spawnTextRect = spawnText.GetComponent<RectTransform>();
        spawnTextRect.anchorMin = Vector2.zero;
        spawnTextRect.anchorMax = Vector2.one;
        spawnTextRect.offsetMin = Vector2.zero;
        spawnTextRect.offsetMax = Vector2.zero;
        spawnText.color = Color.white;
        spawnText.enableAutoSizing = true;
        spawnText.fontSizeMin = 8;
        spawnText.fontSizeMax = 11;

        infoPanelUI = panelObj.AddComponent<InfoPanelUI>();
        infoPanelUI.Init(panelObj, bgImg, portraitFrame, portraitIcon,
                         nameText, hpFrameImg, hpFill, hpText,
                         dmgIcon, dmgText, armIcon, armText, extraIcon, extraText);
        infoPanelUI.SetSpawnTimerUI(spawnTimerObj, spawnFill, spawnText, spawnIcon);
    }

    private Image CreateStatRow(string name, Transform parent, Sprite icon,
        string defaultText, out TextMeshProUGUI label)
    {
        var row = CreatePanel(name, parent);
        var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 6;
        rowLayout.padding = new RectOffset(0, 0, 0, 0);
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childControlWidth = false;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;

        var iconObj = CreatePanel(name + "Icon", row.transform);
        var iconImg = iconObj.AddComponent<Image>();
        iconImg.preserveAspect = true;
        if (icon != null)
        {
            iconImg.sprite = icon;
            iconImg.color = Color.white;
        }
        else
        {
            iconImg.color = new Color(0.4f, 0.4f, 0.4f, 0.3f);
        }
        var iconLe = iconObj.AddComponent<LayoutElement>();
        iconLe.preferredWidth = 22;
        iconLe.preferredHeight = 22;

        label = CreateText(name + "Text", row.transform, defaultText,
            16, TextAlignmentOptions.MidlineLeft, 0);
        label.enableAutoSizing = true;
        label.fontSizeMin = 12;
        label.fontSizeMax = 16;
        label.color = COL_STAT;
        var labelLe = label.gameObject.AddComponent<LayoutElement>();
        labelLe.flexibleWidth = 1;

        return iconImg;
    }

    // ====================================================================
    // TOOLTIP
    // ====================================================================

    private void CreateTooltip(Transform parent)
    {
        var tooltipObj = CreatePanel("Tooltip", parent);
        var rect = tooltipObj.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = new Vector2(0, 1);
        rect.sizeDelta = new Vector2(280, 160);

        var bg = tooltipObj.AddComponent<Image>();
        if (theme != null && theme.tooltipBackground != null)
        {
            bg.sprite = theme.tooltipBackground;
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.06f, 0.05f, 0.04f, 0.95f);
        }
        else
        {
            bg.color = new Color(0.06f, 0.05f, 0.04f, 0.95f);
        }

        var layout = tooltipObj.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 8, 8);
        layout.spacing = 4;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var csf = tooltipObj.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var titleTxt = CreateText("TooltipTitle", tooltipObj.transform, "", 16, TextAlignmentOptions.TopLeft, 0);
        titleTxt.color = COL_TITLE;
        titleTxt.fontStyle = FontStyles.Bold;
        var titleLe = titleTxt.gameObject.AddComponent<LayoutElement>();
        titleLe.preferredWidth = 256;

        var descTxt = CreateText("TooltipDesc", tooltipObj.transform, "", 13, TextAlignmentOptions.TopLeft, 0);
        descTxt.color = COL_STAT;
        var descLe = descTxt.gameObject.AddComponent<LayoutElement>();
        descLe.preferredWidth = 256;

        var statsTxt = CreateText("TooltipStats", tooltipObj.transform, "", 13, TextAlignmentOptions.TopLeft, 0);
        statsTxt.color = COL_INCOME;
        var statsLe = statsTxt.gameObject.AddComponent<LayoutElement>();
        statsLe.preferredWidth = 256;

        tooltipUI = tooltipObj.AddComponent<TooltipUI>();
        tooltipUI.Init(tooltipObj, titleTxt, descTxt, statsTxt);
    }

    // ====================================================================
    // GAME OVER
    // ====================================================================

    private void CreateGameOverPanel(Transform parent)
    {
        var panelObj = CreatePanel("GameOverPanel", parent);
        var rect = panelObj.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var dimBg = panelObj.AddComponent<Image>();
        dimBg.color = new Color(0f, 0f, 0f, 0.7f);

        var contentObj = CreatePanel("GameOverContent", panelObj.transform);
        var contentRect = contentObj.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0.5f, 0.5f);
        contentRect.anchorMax = new Vector2(0.5f, 0.5f);
        contentRect.pivot = new Vector2(0.5f, 0.5f);
        contentRect.sizeDelta = new Vector2(500, 280);

        var contentBg = contentObj.AddComponent<Image>();
        if (theme != null && theme.infoPanelBackground != null)
        {
            contentBg.sprite = theme.infoPanelBackground;
            contentBg.type = Image.Type.Sliced;
            contentBg.color = COL_SPRITE_DARK;
        }
        else
        {
            contentBg.color = COL_PANEL_BG;
        }

        var contentLayout = contentObj.AddComponent<VerticalLayoutGroup>();
        contentLayout.padding = new RectOffset(30, 30, 30, 30);
        contentLayout.spacing = 16;
        contentLayout.childAlignment = TextAnchor.MiddleCenter;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;

        var resultTxt = CreateText("ResultText", contentObj.transform, "VICTORY", 42, TextAlignmentOptions.Center, 0);
        resultTxt.fontStyle = FontStyles.Bold;
        resultTxt.color = Color.green;

        var statsTxt = CreateText("StatsText", contentObj.transform, "Match Duration: 00:00", 18, TextAlignmentOptions.Center, 0);
        statsTxt.color = COL_STAT;

        var btnObj = CreatePanel("ReturnButton", contentObj.transform);
        var btnBg = btnObj.AddComponent<Image>();
        if (theme != null && theme.buildButtonNormal != null)
        {
            btnBg.sprite = theme.buildButtonNormal;
            btnBg.type = Image.Type.Sliced;
            btnBg.color = new Color(0.22f, 0.19f, 0.15f);
        }
        else
        {
            btnBg.color = new Color(0.2f, 0.2f, 0.2f);
        }
        var btnLe = btnObj.AddComponent<LayoutElement>();
        btnLe.preferredHeight = 44;
        btnLe.preferredWidth = 200;
        var btn = btnObj.AddComponent<Button>();

        var btnLabel = CreateText("BtnLabel", btnObj.transform, "Return to Menu", 18, TextAlignmentOptions.Center, 0);
        btnLabel.color = COL_NAME;
        var labelRect = btnLabel.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        gameOverUI = panelObj.AddComponent<GameOverUI>();
        gameOverUI.Init(panelObj, resultTxt, statsTxt, btn);
    }

    // ====================================================================
    // SELECTION MANAGER
    // ====================================================================

    private void CreateSelectionManager()
    {
        if (SelectionManager.Instance != null) return;
        selectionManager = gameObject.AddComponent<SelectionManager>();
    }

    // ====================================================================
    // DEBUG SYSTEM
    // ====================================================================

    private void CreateDebugSystem(Transform canvasRoot)
    {
        var cam = Camera.main;
        if (cam == null) return;

        var overlay = cam.gameObject.AddComponent<DebugOverlay>();
        overlay.enabled = false;

        var debugPanel = canvasRoot.gameObject.AddComponent<DebugPanel>();
        debugPanel.Init(overlay);
    }

    // ====================================================================
    // COMBAT VFX
    // ====================================================================

    private void CreateCombatVFX()
    {
        if (CombatVFX.Instance != null) return;
        var vfxObj = new GameObject("CombatVFX");
        vfxObj.AddComponent<CombatVFX>();
    }

    // ====================================================================
    // HELPERS
    // ====================================================================

    private GameObject CreatePanel(string name, Transform parent)
    {
        var obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        return obj;
    }

    private TextMeshProUGUI CreateText(string name, Transform parent, string text,
        float fontSize, TextAlignmentOptions alignment, float width)
    {
        var obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);

        var tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = alignment;
        tmp.color = Color.white;

        if (cachedFontAsset != null)
            tmp.font = cachedFontAsset;

        if (width > 0)
        {
            var le = obj.AddComponent<LayoutElement>();
            le.preferredWidth = width;
        }

        return tmp;
    }
}
