using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class BuildMenuButton : MonoBehaviour
{
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI costText;
    [SerializeField] private Button button;
    [SerializeField] private CanvasGroup canvasGroup;

    private BuildingData data;
    private Action<BuildingData> onClick;

    public BuildingData Data => data;

    public void Setup(BuildingData buildingData, Action<BuildingData> callback)
    {
        data = buildingData;
        onClick = callback;

        if (iconImage != null && data.icon != null)
            iconImage.sprite = data.icon;

        if (nameText != null)
            nameText.text = data.buildingName;

        if (costText != null)
            costText.text = data.cost.ToString();

        if (button == null)
            button = GetComponent<Button>();

        if (button != null)
            button.onClick.AddListener(HandleClick);
    }

    private void HandleClick()
    {
        onClick?.Invoke(data);
    }

    public void SetInteractable(bool interactable)
    {
        if (button != null)
            button.interactable = interactable;

        if (canvasGroup != null)
            canvasGroup.alpha = interactable ? 1f : 0.5f;
    }

    private void OnDestroy()
    {
        if (button != null)
            button.onClick.RemoveListener(HandleClick);
    }
}
