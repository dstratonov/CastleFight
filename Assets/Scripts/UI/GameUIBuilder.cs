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
        rect.sizeDelta = new Vector2(320, 140);

        var bg = panelObj.AddComponent<Image>();
        if (theme != null && theme.infoPanelBackground != null)
        {
            bg.sprite = theme.infoPanelBackground;
            bg.type = Image.Type.Sliced;
            bg.color = new Color(1f, 1f, 1f, 0.95f);
        }
        else
        {
            bg.color = new Color(0.08f, 0.06f, 0.12f, 0.9f);
        }

        var portraitObj = CreatePanel("Portrait", panelObj.transform);
        var portraitRect = portraitObj.GetComponent<RectTransform>();
        portraitRect.anchorMin = new Vector2(0, 0);
        portraitRect.anchorMax = new Vector2(0, 1);
        portraitRect.pivot = new Vector2(0, 0.5f);
        portraitRect.anchoredPosition = new Vector2(10, 0);
        portraitRect.sizeDelta = new Vector2(80, -20);

        var portraitFrame = portraitObj.AddComponent<Image>();
        if (theme != null && theme.portraitFrame != null)
        {
            portraitFrame.sprite = theme.portraitFrame;
            portraitFrame.preserveAspect = true;
        }
        else
        {
            portraitFrame.color = new Color(0.3f, 0.25f, 0.2f);
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

        var infoArea = CreatePanel("InfoArea", panelObj.transform);
        var infoRect = infoArea.GetComponent<RectTransform>();
        infoRect.anchorMin = new Vector2(0, 0);
        infoRect.anchorMax = new Vector2(1, 1);
        infoRect.offsetMin = new Vector2(100, 10);
        infoRect.offsetMax = new Vector2(-10, -10);

        var nameText = CreateText("NameText", infoArea.transform, "Hero", 20, TextAlignmentOptions.TopLeft, 0);
        var nameRect = nameText.GetComponent<RectTransform>();
        nameRect.anchorMin = new Vector2(0, 0.7f);
        nameRect.anchorMax = new Vector2(1, 1);
        nameRect.offsetMin = Vector2.zero;
        nameRect.offsetMax = Vector2.zero;
        nameText.color = new Color(1f, 0.9f, 0.6f);
        nameText.fontStyle = FontStyles.Bold;

        var hpBarObj = CreatePanel("HPBar", infoArea.transform);
        var hpBarRect = hpBarObj.GetComponent<RectTransform>();
        hpBarRect.anchorMin = new Vector2(0, 0.5f);
        hpBarRect.anchorMax = new Vector2(1, 0.7f);
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
            hpFrameImg.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
        }

        var hpFillObj = CreatePanel("HPFill", hpBarObj.transform);
        var hpFillRect = hpFillObj.GetComponent<RectTransform>();
        hpFillRect.anchorMin = new Vector2(0.03f, 0.15f);
        hpFillRect.anchorMax = new Vector2(0.97f, 0.85f);
        hpFillRect.offsetMin = Vector2.zero;
        hpFillRect.offsetMax = Vector2.zero;
        var hpFill = hpFillObj.AddComponent<Image>();
        hpFill.color = new Color(0.2f, 0.8f, 0.2f);
        hpFill.type = Image.Type.Filled;
        hpFill.fillMethod = Image.FillMethod.Horizontal;
        if (theme != null && theme.hpFill != null)
            hpFill.sprite = theme.hpFill;

        var hpText = CreateText("HPText", hpBarObj.transform, "", 12, TextAlignmentOptions.Center, 0);
        var hpTextRect = hpText.GetComponent<RectTransform>();
        hpTextRect.anchorMin = Vector2.zero;
        hpTextRect.anchorMax = Vector2.one;
        hpTextRect.offsetMin = Vector2.zero;
        hpTextRect.offsetMax = Vector2.zero;
        hpText.color = Color.white;

        var statsText = CreateText("StatsText", infoArea.transform, "", 14, TextAlignmentOptions.TopLeft, 0);
        var statsRect = statsText.GetComponent<RectTransform>();
        statsRect.anchorMin = new Vector2(0, 0);
        statsRect.anchorMax = new Vector2(1, 0.48f);
        statsRect.offsetMin = Vector2.zero;
        statsRect.offsetMax = Vector2.zero;
        statsText.color = new Color(0.85f, 0.85f, 0.85f);

        infoPanelUI = panelObj.AddComponent<InfoPanelUI>();
        infoPanelUI.Init(panelObj, bg, portraitFrame, portraitIcon,
                         nameText, hpFrameImg, hpFill, hpText, statsText);
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
