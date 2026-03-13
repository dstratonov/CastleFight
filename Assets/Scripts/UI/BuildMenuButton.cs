using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System;

public class BuildMenuButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI costText;
    [SerializeField] private Button button;
    [SerializeField] private CanvasGroup canvasGroup;

    private BuildingData data;
    private Action<BuildingData> onClick;

    public BuildingData Data => data;

    public void Init(Button btn, CanvasGroup cg, TextMeshProUGUI name, TextMeshProUGUI cost)
    {
        button = btn;
        canvasGroup = cg;
        nameText = name;
        costText = cost;
    }

    public void Setup(BuildingData buildingData, Action<BuildingData> callback, int hotkey = 0)
    {
        data = buildingData;
        onClick = callback;

        if (iconImage != null && data.icon != null)
            iconImage.sprite = data.icon;

        if (nameText != null)
        {
            string prefix = hotkey > 0 && hotkey <= 9 ? $"<color=#FFD700>[{hotkey}]</color> " : "";
            nameText.text = prefix + data.buildingName;
        }

        if (costText != null)
            costText.text = data.cost + "g";

        if (button == null)
            button = GetComponent<Button>();

        if (button != null)
        {
            button.onClick.RemoveListener(HandleClick);
            button.onClick.AddListener(HandleClick);
        }
    }

    private void HandleClick()
    {
        onClick?.Invoke(data);
    }

    private bool isActive;

    public void SetInteractable(bool interactable)
    {
        if (button != null)
            button.interactable = interactable;

        if (canvasGroup != null)
            canvasGroup.alpha = interactable ? 1f : 0.5f;
    }

    /// <summary>Shows/hides a visual indicator that this building is currently being placed.</summary>
    public void SetActiveIndicator(bool active)
    {
        if (isActive == active) return;
        isActive = active;

        var bg = GetComponent<UnityEngine.UI.Image>();
        if (bg != null)
        {
            bg.color = active
                ? new Color(0.25f, 0.45f, 0.65f)
                : new Color(0.22f, 0.19f, 0.15f);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (data != null && TooltipUI.Instance != null)
            TooltipUI.Instance.ShowBuildingTooltip(data);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (TooltipUI.Instance != null)
            TooltipUI.Instance.Hide();
    }

    private void OnDestroy()
    {
        if (button != null)
            button.onClick.RemoveListener(HandleClick);
    }
}
