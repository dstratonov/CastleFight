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
                             $"Speed: {data.moveSpeed}\nRange: {data.attackRange}\n" +
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

        Vector2 mousePos = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parentCanvas.transform as RectTransform,
            mousePos,
            parentCanvas.worldCamera,
            out Vector2 localPoint
        );

        panelRect.anchoredPosition = localPoint + offset;
    }
}
