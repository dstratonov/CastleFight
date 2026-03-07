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
    private UIThemeData theme;
    private TMP_FontAsset cachedFontAsset;

    public HUDManager HUD => hudManager;
    public BuildMenuUI BuildMenu => buildMenuUI;
    public InfoPanelUI InfoPanel => infoPanelUI;

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
        CreateSelectionManager();
        CreateDebugSystem(canvasObj.transform);
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
            bg.color = Color.white;
        }
        else
        {
            bg.color = new Color(0.08f, 0.06f, 0.12f, 0.9f);
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
        goldText.color = new Color(1f, 0.85f, 0.2f);

        incomeText = CreateText("IncomeText", group.transform, "+10", 16, TextAlignmentOptions.MidlineLeft, 70);
        incomeText.color = new Color(0.7f, 0.9f, 0.5f);
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

        allyBar = CreateHealthBar("AllyCastle", group.transform, new Color(0.2f, 0.7f, 1f), out allyText);
        enemyBar = CreateHealthBar("EnemyCastle", group.transform, new Color(1f, 0.3f, 0.3f), out enemyText);
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
        }
        else
        {
            frameBg.color = new Color(0.15f, 0.15f, 0.15f, 0.8f);
        }

        var fillObj = CreatePanel(name + "Fill", frameObj.transform);
        var fillRect = fillObj.GetComponent<RectTransform>();
        fillRect.anchorMin = new Vector2(0.05f, 0.15f);
        fillRect.anchorMax = new Vector2(0.95f, 0.85f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        var fill = fillObj.AddComponent<Image>();
        if (theme != null && theme.hpFill != null)
        {
            fill.sprite = theme.hpFill;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
        }
        else
        {
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
        }
        fill.color = fillColor;
        fill.fillAmount = 1f;

        labelText = label;
        return fill;
    }

    // ====================================================================
    // BUILD PANEL (right side)
    // ====================================================================

    private void CreateBuildPanel(Transform parent)
    {
        var panelObj = CreatePanel("BuildPanel", parent);
        var rect = panelObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1, 0);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(1, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(240, -70);
        rect.offsetMin = new Vector2(rect.offsetMin.x, 10);
        rect.offsetMax = new Vector2(rect.offsetMax.x, -70);

        var bg = panelObj.AddComponent<Image>();
        if (theme != null && theme.buildPanelBackground != null)
        {
            bg.sprite = theme.buildPanelBackground;
            bg.type = Image.Type.Sliced;
            bg.color = new Color(1f, 1f, 1f, 0.95f);
        }
        else
        {
            bg.color = new Color(0.08f, 0.06f, 0.12f, 0.85f);
        }

        var titleObj = CreatePanel("Title", panelObj.transform);
        var titleRect = titleObj.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0, 1);
        titleRect.anchorMax = new Vector2(1, 1);
        titleRect.pivot = new Vector2(0.5f, 1);
        titleRect.anchoredPosition = Vector2.zero;
        titleRect.sizeDelta = new Vector2(0, 36);

        var titleText = CreateText("TitleText", titleObj.transform, "BUILD", 18, TextAlignmentOptions.Center, 0);
        var titleTextRect = titleText.GetComponent<RectTransform>();
        titleTextRect.anchorMin = Vector2.zero;
        titleTextRect.anchorMax = Vector2.one;
        titleTextRect.sizeDelta = Vector2.zero;
        titleText.color = new Color(0.95f, 0.85f, 0.4f);
        titleText.fontStyle = FontStyles.Bold;

        var containerObj = CreatePanel("BuildButtonContainer", panelObj.transform);
        var containerRect = containerObj.GetComponent<RectTransform>();
        containerRect.anchorMin = new Vector2(0, 0);
        containerRect.anchorMax = new Vector2(1, 1);
        containerRect.pivot = new Vector2(0.5f, 1);
        containerRect.offsetMin = new Vector2(8, 8);
        containerRect.offsetMax = new Vector2(-8, -40);

        var vlg = containerObj.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(4, 4, 4, 4);
        vlg.spacing = 6;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        buildMenuUI = panelObj.AddComponent<BuildMenuUI>();
        buildMenuUI.Init(containerObj.transform);
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

        // Solid dark background with slight transparency
        var bgImg = panelObj.AddComponent<Image>();
        bgImg.color = new Color(0.08f, 0.07f, 0.06f, 0.94f);

        // Ornate border frame on top of solid bg
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
            frameImg.color = new Color(0.85f, 0.8f, 0.7f);
        }
        else
        {
            frameImg.color = Color.clear;
        }

        // Portrait -- centered vertically, left side
        var portraitObj = CreatePanel("Portrait", panelObj.transform);
        var portraitRect = portraitObj.GetComponent<RectTransform>();
        portraitRect.anchorMin = new Vector2(0, 0.5f);
        portraitRect.anchorMax = new Vector2(0, 0.5f);
        portraitRect.pivot = new Vector2(0, 0.5f);
        portraitRect.anchoredPosition = new Vector2(16, 0);
        portraitRect.sizeDelta = new Vector2(110, 110);

        // Entity icon (centered inside portrait area)
        var portraitIconObj = CreatePanel("PortraitIcon", portraitObj.transform);
        var piRect = portraitIconObj.GetComponent<RectTransform>();
        piRect.anchorMin = new Vector2(0.2f, 0.2f);
        piRect.anchorMax = new Vector2(0.8f, 0.8f);
        piRect.offsetMin = Vector2.zero;
        piRect.offsetMax = Vector2.zero;
        var portraitIcon = portraitIconObj.AddComponent<Image>();
        portraitIcon.preserveAspect = true;
        portraitIcon.color = new Color(0.5f, 0.5f, 0.5f, 0.3f);

        // Ornate circular frame overlay
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
            portraitFrame.color = new Color(0.9f, 0.85f, 0.75f);
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

        // Entity name -- top strip
        var nameText = CreateText("NameText", infoArea.transform, "Hero", 22, TextAlignmentOptions.MidlineLeft, 0);
        var nameRect = nameText.GetComponent<RectTransform>();
        nameRect.anchorMin = new Vector2(0, 0.78f);
        nameRect.anchorMax = new Vector2(1, 1);
        nameRect.offsetMin = Vector2.zero;
        nameRect.offsetMax = Vector2.zero;
        nameText.color = new Color(1f, 0.92f, 0.65f);
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
        }
        else
        {
            hpFrameImg.color = new Color(0.15f, 0.15f, 0.15f, 0.9f);
        }

        var hpFillObj = CreatePanel("HPFill", hpBarObj.transform);
        var hpFillRect = hpFillObj.GetComponent<RectTransform>();
        hpFillRect.anchorMin = new Vector2(0.03f, 0.1f);
        hpFillRect.anchorMax = new Vector2(0.97f, 0.9f);
        hpFillRect.offsetMin = Vector2.zero;
        hpFillRect.offsetMax = Vector2.zero;
        var hpFill = hpFillObj.AddComponent<Image>();
        hpFill.color = new Color(0.2f, 0.75f, 0.2f);
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

        // Stats area with icon+label rows
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

        infoPanelUI = panelObj.AddComponent<InfoPanelUI>();
        infoPanelUI.Init(panelObj, bgImg, portraitFrame, portraitIcon,
                         nameText, hpFrameImg, hpFill, hpText,
                         dmgIcon, dmgText, armIcon, armText, extraIcon, extraText);
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
        label.color = new Color(0.88f, 0.85f, 0.78f);
        var labelLe = label.gameObject.AddComponent<LayoutElement>();
        labelLe.flexibleWidth = 1;

        return iconImg;
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
