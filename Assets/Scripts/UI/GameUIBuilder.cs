using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GameUIBuilder : MonoBehaviour
{
    public static GameUIBuilder Instance { get; private set; }

    private Canvas canvas;
    private HUDManager hudManager;
    private BuildMenuUI buildMenuUI;

    public HUDManager HUD => hudManager;
    public BuildMenuUI BuildMenu => buildMenuUI;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        CreateUI();
    }

    private void CreateUI()
    {
        var canvasObj = new GameObject("GameCanvas");
        canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        canvasObj.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasObj.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920, 1080);
        canvasObj.AddComponent<GraphicRaycaster>();

        CreateHUD(canvasObj.transform);
        CreateBuildPanel(canvasObj.transform);
    }

    private void CreateHUD(Transform parent)
    {
        var hudObj = CreatePanel("HUD", parent);
        var rect = hudObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(0.5f, 1);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(0, 50);

        var bg = hudObj.AddComponent<Image>();
        bg.color = new Color(0, 0, 0, 0.6f);

        var layout = hudObj.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 5, 5);
        layout.spacing = 30;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = false;
        layout.childControlHeight = true;

        var goldText = CreateTMPText("GoldText", hudObj.transform, "Gold: 100", 20, TextAlignmentOptions.MidlineLeft, 200);
        var incomeText = CreateTMPText("IncomeText", hudObj.transform, "+10", 18, TextAlignmentOptions.MidlineLeft, 100);
        var timerText = CreateTMPText("TimerText", hudObj.transform, "00:00", 20, TextAlignmentOptions.MidlineLeft, 100);
        var teamText = CreateTMPText("TeamText", hudObj.transform, "Team 0", 18, TextAlignmentOptions.MidlineLeft, 120);

        hudManager = hudObj.AddComponent<HUDManager>();
        SetPrivateField(hudManager, "goldText", goldText);
        SetPrivateField(hudManager, "incomeText", incomeText);
        SetPrivateField(hudManager, "timerText", timerText);
        SetPrivateField(hudManager, "teamText", teamText);
    }

    private void CreateBuildPanel(Transform parent)
    {
        var panelObj = CreatePanel("BuildPanel", parent);
        var rect = panelObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1, 0);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(1, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(220, -60);
        rect.offsetMin = new Vector2(rect.offsetMin.x, 10);
        rect.offsetMax = new Vector2(rect.offsetMax.x, -60);

        var bg = panelObj.AddComponent<Image>();
        bg.color = new Color(0, 0, 0, 0.5f);

        var titleObj = CreatePanel("Title", panelObj.transform);
        var titleRect = titleObj.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0, 1);
        titleRect.anchorMax = new Vector2(1, 1);
        titleRect.pivot = new Vector2(0.5f, 1);
        titleRect.anchoredPosition = Vector2.zero;
        titleRect.sizeDelta = new Vector2(0, 30);
        var titleText = CreateTMPText("TitleText", titleObj.transform, "BUILD", 16, TextAlignmentOptions.Center, 0);
        titleText.GetComponent<RectTransform>().anchorMin = Vector2.zero;
        titleText.GetComponent<RectTransform>().anchorMax = Vector2.one;
        titleText.GetComponent<RectTransform>().sizeDelta = Vector2.zero;
        titleText.color = Color.yellow;
        titleText.fontStyle = FontStyles.Bold;

        var containerObj = CreatePanel("BuildButtonContainer", panelObj.transform);
        var containerRect = containerObj.GetComponent<RectTransform>();
        containerRect.anchorMin = new Vector2(0, 0);
        containerRect.anchorMax = new Vector2(1, 1);
        containerRect.pivot = new Vector2(0.5f, 1);
        containerRect.offsetMin = new Vector2(5, 5);
        containerRect.offsetMax = new Vector2(-5, -35);

        var vlg = containerObj.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(5, 5, 5, 5);
        vlg.spacing = 5;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        buildMenuUI = panelObj.AddComponent<BuildMenuUI>();
        SetPrivateField(buildMenuUI, "buildButtonContainer", containerObj.transform);
    }

    private GameObject CreatePanel(string name, Transform parent)
    {
        var obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        return obj;
    }

    private TextMeshProUGUI CreateTMPText(string name, Transform parent, string text, float fontSize, TextAlignmentOptions alignment, float width)
    {
        var obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);

        var tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = alignment;
        tmp.color = Color.white;

        if (width > 0)
        {
            var le = obj.AddComponent<LayoutElement>();
            le.preferredWidth = width;
        }

        return tmp;
    }

    private void SetPrivateField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(fieldName,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field != null)
            field.SetValue(target, value);
    }
}
