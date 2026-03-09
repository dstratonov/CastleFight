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
    private UIThemeData theme;
    private TMP_FontAsset cachedFontAsset;

    public void Init(Transform container)
    {
        buildButtonContainer = container;
        theme = Resources.Load<UIThemeData>("UITheme");
        if (theme != null && theme.medievalFont != null)
            cachedFontAsset = TMP_FontAsset.CreateFontAsset(theme.medievalFont);
    }

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

        var bg = buttonObj.AddComponent<Image>();
        if (theme != null && theme.buildButtonNormal != null)
        {
            bg.sprite = theme.buildButtonNormal;
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.22f, 0.19f, 0.15f);
        }
        else
        {
            bg.color = new Color(0.14f, 0.13f, 0.11f, 0.92f);
        }

        var btn = buttonObj.AddComponent<Button>();
        if (theme != null && theme.buildButtonNormal != null)
        {
            var spriteState = new SpriteState();
            spriteState.highlightedSprite = theme.buildButtonHighlight;
            spriteState.pressedSprite = theme.buildButtonHighlight;
            spriteState.disabledSprite = theme.buildButtonRed;
            btn.spriteState = spriteState;
            btn.transition = Selectable.Transition.SpriteSwap;
        }
        else
        {
            var colors = btn.colors;
            colors.highlightedColor = new Color(0.3f, 0.4f, 0.6f);
            colors.pressedColor = new Color(0.2f, 0.3f, 0.5f);
            colors.disabledColor = new Color(0.15f, 0.15f, 0.15f, 0.8f);
            btn.colors = colors;
        }

        var cg = buttonObj.AddComponent<CanvasGroup>();

        var nameObj = new GameObject("Name", typeof(RectTransform));
        nameObj.transform.SetParent(buttonObj.transform, false);
        var nameRect = nameObj.GetComponent<RectTransform>();
        nameRect.anchorMin = new Vector2(0, 0);
        nameRect.anchorMax = new Vector2(0.6f, 1);
        nameRect.offsetMin = new Vector2(6, 0);
        nameRect.offsetMax = Vector2.zero;
        var nameTmp = nameObj.AddComponent<TextMeshProUGUI>();
        nameTmp.text = data.buildingName;
        nameTmp.fontSize = 12;
        nameTmp.alignment = TextAlignmentOptions.MidlineLeft;
        nameTmp.color = new Color(0.88f, 0.85f, 0.8f);
        nameTmp.enableAutoSizing = true;
        nameTmp.fontSizeMin = 9;
        nameTmp.fontSizeMax = 12;
        nameTmp.overflowMode = TextOverflowModes.Ellipsis;
        if (cachedFontAsset != null) nameTmp.font = cachedFontAsset;

        var costObj = new GameObject("Cost", typeof(RectTransform));
        costObj.transform.SetParent(buttonObj.transform, false);
        var costRect = costObj.GetComponent<RectTransform>();
        costRect.anchorMin = new Vector2(0.6f, 0);
        costRect.anchorMax = new Vector2(1, 1);
        costRect.offsetMin = Vector2.zero;
        costRect.offsetMax = new Vector2(-6, 0);
        var costTmp = costObj.AddComponent<TextMeshProUGUI>();
        costTmp.text = data.cost + "g";
        costTmp.fontSize = 12;
        costTmp.alignment = TextAlignmentOptions.MidlineRight;
        costTmp.color = new Color(1f, 0.85f, 0.3f);
        costTmp.fontStyle = FontStyles.Bold;
        costTmp.enableAutoSizing = true;
        costTmp.fontSizeMin = 9;
        costTmp.fontSizeMax = 12;
        if (cachedFontAsset != null) costTmp.font = cachedFontAsset;

        var menuButton = buttonObj.GetComponent<BuildMenuButton>();
        if (menuButton == null) menuButton = buttonObj.AddComponent<BuildMenuButton>();
        menuButton.Init(btn, cg, nameTmp, costTmp);

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
