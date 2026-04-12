using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

public class DebugPanel : MonoBehaviour
{
    private DebugOverlay overlay;
    private GameObject panelRoot;
    private bool debugActive;

    private static readonly Color BG_COLOR = new(0.05f, 0.05f, 0.05f, 0.85f);
    private static readonly Color TOGGLE_ON = new(0.2f, 0.9f, 0.3f, 1f);
    private static readonly Color TOGGLE_OFF = new(0.6f, 0.6f, 0.6f, 1f);
    private static readonly Color LABEL_COLOR = new(0.9f, 0.9f, 0.9f, 1f);

    public void Init(DebugOverlay debugOverlay)
    {
        overlay = debugOverlay;
        BuildPanel();
        SetDebugActive(false);
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.f1Key.wasPressedThisFrame)
            SetDebugActive(!debugActive);
    }

    private void SetDebugActive(bool active)
    {
        debugActive = active;
        if (panelRoot != null)
            panelRoot.SetActive(active);
        if (overlay != null)
            overlay.enabled = active;
    }

    private void BuildPanel()
    {
        panelRoot = new GameObject("DebugPanelRoot");
        panelRoot.transform.SetParent(transform, false);

        var rect = panelRoot.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(10, -70);
        rect.sizeDelta = new Vector2(200, 320);

        var bg = panelRoot.AddComponent<Image>();
        bg.color = BG_COLOR;

        var layout = panelRoot.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 8, 8);
        layout.spacing = 4;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childControlHeight = true;

        CreateHeader(panelRoot.transform);

        CreateToggle(panelRoot.transform, "NavMesh",        overlay.showNavMesh,            v => overlay.showNavMesh = v);
        CreateToggle(panelRoot.transform, "Unit Paths",     overlay.showPaths,              v => overlay.showPaths = v);
        CreateToggle(panelRoot.transform, "Attack Range",   overlay.showAttackRange,        v => overlay.showAttackRange = v);
        CreateToggle(panelRoot.transform, "Aggro Range",    overlay.showAggroRange,         v => overlay.showAggroRange = v);
        CreateToggle(panelRoot.transform, "Velocities",     overlay.showVelocities,         v => overlay.showVelocities = v);
        CreateToggle(panelRoot.transform, "Unit Radius",    overlay.showUnitRadius,         v => overlay.showUnitRadius = v);
        CreateToggle(panelRoot.transform, "Attack Slots",   overlay.showAttackSlots,        v => overlay.showAttackSlots = v);
        CreateToggle(panelRoot.transform, "Buildings",      overlay.showBuildingFootprints, v => overlay.showBuildingFootprints = v);
        CreateToggle(panelRoot.transform, "Build Zones",    overlay.showBuildZones,         v => overlay.showBuildZones = v);
    }

    private void CreateHeader(Transform parent)
    {
        var obj = new GameObject("Header");
        obj.transform.SetParent(parent, false);

        var le = obj.AddComponent<LayoutElement>();
        le.preferredHeight = 28;

        var text = obj.AddComponent<TextMeshProUGUI>();
        text.text = "DEBUG  <size=12><color=#888>(F1 to hide)</color></size>";
        text.fontSize = 18;
        text.fontStyle = FontStyles.Bold;
        text.color = new Color(1f, 0.85f, 0.3f);
        text.alignment = TextAlignmentOptions.Left;
        text.enableAutoSizing = false;
    }

    private void CreateToggle(Transform parent, string label, bool initialState, System.Action<bool> onChanged)
    {
        var row = new GameObject($"Toggle_{label}");
        row.transform.SetParent(parent, false);

        var le = row.AddComponent<LayoutElement>();
        le.preferredHeight = 30;

        var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 8;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childAlignment = TextAnchor.MiddleLeft;

        // Checkbox button
        var btnObj = new GameObject("Btn");
        btnObj.transform.SetParent(row.transform, false);

        var btnLe = btnObj.AddComponent<LayoutElement>();
        btnLe.preferredWidth = 28;
        btnLe.preferredHeight = 28;

        var btnImg = btnObj.AddComponent<Image>();
        btnImg.color = initialState ? TOGGLE_ON : TOGGLE_OFF;

        var btn = btnObj.AddComponent<Button>();

        var checkObj = new GameObject("Check");
        checkObj.transform.SetParent(btnObj.transform, false);
        var checkRect = checkObj.AddComponent<RectTransform>();
        checkRect.anchorMin = Vector2.zero;
        checkRect.anchorMax = Vector2.one;
        checkRect.offsetMin = Vector2.zero;
        checkRect.offsetMax = Vector2.zero;
        var checkText = checkObj.AddComponent<TextMeshProUGUI>();
        checkText.text = initialState ? "X" : "";
        checkText.fontSize = 18;
        checkText.fontStyle = FontStyles.Bold;
        checkText.color = Color.white;
        checkText.alignment = TextAlignmentOptions.Center;

        bool state = initialState;
        btn.onClick.AddListener(() =>
        {
            state = !state;
            onChanged(state);
            btnImg.color = state ? TOGGLE_ON : TOGGLE_OFF;
            checkText.text = state ? "X" : "";
        });

        // Label
        var labelObj = new GameObject("Label");
        labelObj.transform.SetParent(row.transform, false);

        var labelLe = labelObj.AddComponent<LayoutElement>();
        labelLe.flexibleWidth = 1;
        labelLe.preferredHeight = 28;

        var labelTmp = labelObj.AddComponent<TextMeshProUGUI>();
        labelTmp.text = label;
        labelTmp.fontSize = 16;
        labelTmp.color = LABEL_COLOR;
        labelTmp.alignment = TextAlignmentOptions.Left;
        labelTmp.enableAutoSizing = false;
    }
}
