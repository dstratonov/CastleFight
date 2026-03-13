using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using System.Collections.Generic;

public class BuildMenuUI : MonoBehaviour
{
    [SerializeField] private Transform buildButtonContainer;
    [SerializeField] private GameObject buildButtonPrefab;

    private NetworkPlayer localPlayer;
    private HeroBuilder heroBuilder;
    private BuildingPlacer buildingPlacer;
    private readonly List<BuildMenuButton> buttons = new();
    private UIThemeData theme;
    private TMP_FontAsset cachedFontAsset;

    public void Init(Transform container, TMP_FontAsset sharedFont = null)
    {
        buildButtonContainer = container;
        theme = Resources.Load<UIThemeData>("UITheme");
        if (sharedFont != null)
            cachedFontAsset = sharedFont;
        else if (theme != null && theme.medievalFont != null)
            cachedFontAsset = TMP_FontAsset.CreateFontAsset(theme.medievalFont);
    }

    public void Initialize(NetworkPlayer player)
    {
        localPlayer = player;
        heroBuilder = player.GetComponent<HeroBuilder>();
        buildingPlacer = player.GetComponent<BuildingPlacer>();
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

        int hotkeyIndex = 1;
        foreach (var buildingData in race.buildings)
        {
            if (buildingData == null) continue;
            CreateBuildButton(buildingData, hotkeyIndex <= 9 ? hotkeyIndex : 0);
            hotkeyIndex++;
        }
    }

    private void CreateBuildButton(BuildingData data, int hotkey = 0)
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

        menuButton.Setup(data, OnBuildButtonClicked, hotkey);
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
        activePlacingData = (buildingPlacer != null && buildingPlacer.IsPlacing) ? data : null;
    }

    private BuildingData activePlacingData;

    private void Update()
    {
        UpdateButtonStates();
        HandleKeyboardShortcuts();
    }

    private void UpdateButtonStates()
    {
        if (localPlayer == null) return;

        if (activePlacingData != null)
        {
            if (buildingPlacer == null || !buildingPlacer.IsPlacing)
                activePlacingData = null;
        }

        for (int i = 0; i < buttons.Count; i++)
        {
            var button = buttons[i];
            if (button == null || button.Data == null) continue;
            bool canAfford = localPlayer.Gold >= button.Data.cost;
            button.SetInteractable(canAfford);
            button.SetActiveIndicator(activePlacingData != null && button.Data == activePlacingData);
        }
    }

    private void HandleKeyboardShortcuts()
    {
        if (localPlayer == null || heroBuilder == null) return;

        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        for (int i = 0; i < buttons.Count && i < 9; i++)
        {
            var key = GetNumberKey(keyboard, i + 1);
            if (key != null && key.wasPressedThisFrame)
            {
                var data = buttons[i]?.Data;
                if (data != null)
                    OnBuildButtonClicked(data);
                break;
            }
        }
    }

    private static UnityEngine.InputSystem.Controls.KeyControl GetNumberKey(Keyboard kb, int num)
    {
        return num switch
        {
            1 => kb.digit1Key,
            2 => kb.digit2Key,
            3 => kb.digit3Key,
            4 => kb.digit4Key,
            5 => kb.digit5Key,
            6 => kb.digit6Key,
            7 => kb.digit7Key,
            8 => kb.digit8Key,
            9 => kb.digit9Key,
            _ => null
        };
    }

    /// <summary>Sets which building is currently being placed, for UI highlight.</summary>
    public void SetActivePlacing(BuildingData data)
    {
        activePlacingData = data;
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
