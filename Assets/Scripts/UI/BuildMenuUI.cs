using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class BuildMenuUI : MonoBehaviour
{
    [SerializeField] private Transform buildButtonContainer;
    [SerializeField] private GameObject buildButtonPrefab;

    private NetworkPlayer localPlayer;
    private HeroBuilder heroBuilder;
    private readonly List<BuildMenuButton> buttons = new();

    public void Initialize(NetworkPlayer player)
    {
        localPlayer = player;
        heroBuilder = player.GetComponent<HeroBuilder>();
        RefreshBuildMenu();
    }

    public void RefreshBuildMenu()
    {
        ClearButtons();
        if (localPlayer == null) return;

        var raceDb = RaceDatabase.Instance;
        if (raceDb == null) return;

        var race = raceDb.GetRace(localPlayer.SelectedRaceId);
        if (race == null || race.buildings == null) return;

        foreach (var buildingData in race.buildings)
        {
            if (buildingData == null) continue;
            CreateBuildButton(buildingData);
        }
    }

    private void CreateBuildButton(BuildingData data)
    {
        if (buildButtonContainer == null) return;

        GameObject buttonObj;
        if (buildButtonPrefab != null)
        {
            buttonObj = Instantiate(buildButtonPrefab, buildButtonContainer);
        }
        else
        {
            buttonObj = CreateButtonRuntime(data);
        }

        var menuButton = buttonObj.GetComponent<BuildMenuButton>();
        if (menuButton == null)
            menuButton = buttonObj.AddComponent<BuildMenuButton>();

        menuButton.Setup(data, OnBuildButtonClicked);
        buttons.Add(menuButton);
    }

    private GameObject CreateButtonRuntime(BuildingData data)
    {
        var buttonObj = new GameObject(data.buildingName, typeof(RectTransform));
        buttonObj.transform.SetParent(buildButtonContainer, false);

        var le = buttonObj.AddComponent<UnityEngine.UI.LayoutElement>();
        le.preferredHeight = 50;

        var bg = buttonObj.AddComponent<UnityEngine.UI.Image>();
        bg.color = new Color(0.2f, 0.2f, 0.3f, 0.9f);

        var btn = buttonObj.AddComponent<UnityEngine.UI.Button>();
        var colors = btn.colors;
        colors.highlightedColor = new Color(0.3f, 0.4f, 0.6f);
        colors.pressedColor = new Color(0.2f, 0.3f, 0.5f);
        colors.disabledColor = new Color(0.15f, 0.15f, 0.15f, 0.8f);
        btn.colors = colors;

        var cg = buttonObj.AddComponent<CanvasGroup>();

        var nameObj = new GameObject("Name", typeof(RectTransform));
        nameObj.transform.SetParent(buttonObj.transform, false);
        var nameRect = nameObj.GetComponent<RectTransform>();
        nameRect.anchorMin = new Vector2(0, 0);
        nameRect.anchorMax = new Vector2(0.65f, 1);
        nameRect.offsetMin = new Vector2(10, 0);
        nameRect.offsetMax = Vector2.zero;
        var nameTmp = nameObj.AddComponent<TMPro.TextMeshProUGUI>();
        nameTmp.text = data.buildingName;
        nameTmp.fontSize = 16;
        nameTmp.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
        nameTmp.color = Color.white;

        var costObj = new GameObject("Cost", typeof(RectTransform));
        costObj.transform.SetParent(buttonObj.transform, false);
        var costRect = costObj.GetComponent<RectTransform>();
        costRect.anchorMin = new Vector2(0.65f, 0);
        costRect.anchorMax = new Vector2(1, 1);
        costRect.offsetMin = Vector2.zero;
        costRect.offsetMax = new Vector2(-10, 0);
        var costTmp = costObj.AddComponent<TMPro.TextMeshProUGUI>();
        costTmp.text = data.cost.ToString() + "g";
        costTmp.fontSize = 16;
        costTmp.alignment = TMPro.TextAlignmentOptions.MidlineRight;
        costTmp.color = new Color(1f, 0.85f, 0f);

        var field = typeof(BuildMenuButton).GetField("button",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field != null)
        {
            var menuButton = buttonObj.GetComponent<BuildMenuButton>();
            if (menuButton == null) menuButton = buttonObj.AddComponent<BuildMenuButton>();
            field.SetValue(menuButton, btn);

            var cgField = typeof(BuildMenuButton).GetField("canvasGroup",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            cgField?.SetValue(menuButton, cg);

            var nameField = typeof(BuildMenuButton).GetField("nameText",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            nameField?.SetValue(menuButton, nameTmp);

            var costField = typeof(BuildMenuButton).GetField("costText",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            costField?.SetValue(menuButton, costTmp);
        }

        return buttonObj;
    }

    private void OnBuildButtonClicked(BuildingData data)
    {
        if (localPlayer == null || heroBuilder == null) return;
        heroBuilder.StartBuilding(data);
    }

    private void Update()
    {
        UpdateButtonStates();
    }

    private void UpdateButtonStates()
    {
        if (localPlayer == null) return;

        foreach (var button in buttons)
        {
            if (button == null || button.Data == null) continue;
            bool canAfford = localPlayer.Gold >= button.Data.cost;
            button.SetInteractable(canAfford);
        }
    }

    private void ClearButtons()
    {
        foreach (var button in buttons)
        {
            if (button != null)
                Destroy(button.gameObject);
        }
        buttons.Clear();
    }
}
