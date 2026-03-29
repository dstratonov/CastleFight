using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

public class TooltipUI : MonoBehaviour
{
    public static TooltipUI Instance { get; private set; }

    [SerializeField] private GameObject tooltipPanel;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI descriptionText;
    [SerializeField] private TextMeshProUGUI statsText;
    [SerializeField] private Vector2 offset = new(15f, -15f);

    private RectTransform panelRect;
    private Canvas parentCanvas;

    public void Init(GameObject panel, TextMeshProUGUI title, TextMeshProUGUI desc, TextMeshProUGUI stats)
    {
        tooltipPanel = panel;
        titleText = title;
        descriptionText = desc;
        statsText = stats;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (tooltipPanel != null)
        {
            panelRect = tooltipPanel.GetComponent<RectTransform>();
            tooltipPanel.SetActive(false);
        }

        parentCanvas = GetComponentInParent<Canvas>();
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

    private void Update()
    {
        if (tooltipPanel != null && tooltipPanel.activeSelf)
            FollowMouse();
    }

    public void ShowBuildingTooltip(BuildingData data)
    {
        if (data == null) return;

        if (titleText != null) titleText.text = data.buildingName;
        if (descriptionText != null) descriptionText.text = data.description;
        if (statsText != null)
        {
            string stats = $"Cost: {data.cost} gold\nHP: {data.maxHealth}";
            if (data.spawnedUnit != null)
                stats += $"\nSpawns: {data.spawnedUnit.displayName} every {data.spawnInterval}s";
            statsText.text = stats;
        }

        ShowPanel();
    }

    public void ShowUnitTooltip(UnitData data)
    {
        if (data == null) return;

        if (titleText != null) titleText.text = data.displayName;
        if (descriptionText != null) descriptionText.text = data.description;
        if (statsText != null)
        {
            statsText.text = $"HP: {data.maxHealth}\nDamage: {data.attackDamage}\n" +
                             $"Speed: {data.moveSpeed}\nRange: {data.attackRangeCells}\n" +
                             $"Attack: {data.attackType} | Armor: {data.armorType}";
        }

        ShowPanel();
    }

    public void Hide()
    {
        if (tooltipPanel != null)
            tooltipPanel.SetActive(false);
    }

    private void ShowPanel()
    {
        if (tooltipPanel != null)
            tooltipPanel.SetActive(true);
        FollowMouse();
    }

    private void FollowMouse()
    {
        if (panelRect == null || parentCanvas == null) return;

        var canvasRect = parentCanvas.transform as RectTransform;
        if (canvasRect == null) return;

        Vector2 mousePos = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect,
            mousePos,
            parentCanvas.worldCamera,
            out Vector2 localPoint
        );

        Vector2 pos = localPoint + offset;

        Vector2 canvasSize = canvasRect.rect.size;
        Vector2 panelSize = panelRect.rect.size;
        Vector2 pivotOffset = panelRect.pivot;

        float minX = -canvasSize.x * 0.5f + panelSize.x * pivotOffset.x;
        float maxX = canvasSize.x * 0.5f - panelSize.x * (1f - pivotOffset.x);
        float minY = -canvasSize.y * 0.5f + panelSize.y * pivotOffset.y;
        float maxY = canvasSize.y * 0.5f - panelSize.y * (1f - pivotOffset.y);

        if (pos.x + panelSize.x * (1f - pivotOffset.x) > canvasSize.x * 0.5f)
            pos.x = localPoint.x - offset.x - panelSize.x;
        if (pos.y - panelSize.y * (1f - pivotOffset.y) < -canvasSize.y * 0.5f)
            pos.y = localPoint.y - offset.y + panelSize.y;

        pos.x = Mathf.Clamp(pos.x, minX, maxX);
        pos.y = Mathf.Clamp(pos.y, minY, maxY);

        panelRect.anchoredPosition = pos;
    }
}
