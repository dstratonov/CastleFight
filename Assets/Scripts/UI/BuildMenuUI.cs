using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class BuildMenuUI : MonoBehaviour
{
    [SerializeField] private Transform buildButtonContainer;
    [SerializeField] private GameObject buildButtonPrefab;
    [SerializeField] private BuildingPlacer buildingPlacer;

    private NetworkPlayer localPlayer;
    private readonly List<BuildMenuButton> buttons = new();

    public void Initialize(NetworkPlayer player)
    {
        localPlayer = player;
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
        if (buildButtonPrefab == null || buildButtonContainer == null) return;

        var buttonObj = Instantiate(buildButtonPrefab, buildButtonContainer);
        var menuButton = buttonObj.GetComponent<BuildMenuButton>();

        if (menuButton == null)
            menuButton = buttonObj.AddComponent<BuildMenuButton>();

        menuButton.Setup(data, OnBuildButtonClicked);
        buttons.Add(menuButton);
    }

    private void OnBuildButtonClicked(BuildingData data)
    {
        if (localPlayer == null || buildingPlacer == null) return;

        if (localPlayer.Gold < data.cost) return;

        buildingPlacer.StartPlacing(data);
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
