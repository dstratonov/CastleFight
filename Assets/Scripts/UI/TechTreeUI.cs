using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class TechTreeUI : MonoBehaviour
{
    [SerializeField] private Transform nodeContainer;
    [SerializeField] private GameObject nodePrefab;
    [SerializeField] private GameObject connectionLinePrefab;
    [SerializeField] private float horizontalSpacing = 200f;
    [SerializeField] private float verticalSpacing = 120f;

    private RaceData currentRace;
    private readonly Dictionary<string, RectTransform> nodePositions = new();

    public void ShowTechTree(RaceData race)
    {
        ClearTree();
        currentRace = race;
        if (race == null || race.techTree == null) return;

        var tiers = new Dictionary<int, List<TechTreeNode>>();

        foreach (var node in race.techTree)
        {
            if (!tiers.ContainsKey(node.tier))
                tiers[node.tier] = new List<TechTreeNode>();
            tiers[node.tier].Add(node);
        }

        foreach (var kvp in tiers)
        {
            int tier = kvp.Key;
            var nodes = kvp.Value;

            for (int i = 0; i < nodes.Count; i++)
            {
                CreateNode(nodes[i], tier, i, nodes.Count);
            }
        }

        foreach (var node in race.techTree)
        {
            if (node.prerequisites == null) continue;
            foreach (var prereq in node.prerequisites)
            {
                DrawConnection(prereq, node.buildingId);
            }
        }
    }

    private void CreateNode(TechTreeNode node, int tier, int index, int totalInTier)
    {
        if (nodePrefab == null || nodeContainer == null) return;

        var nodeObj = Instantiate(nodePrefab, nodeContainer);
        var rect = nodeObj.GetComponent<RectTransform>();

        float xPos = tier * horizontalSpacing;
        float yOffset = (totalInTier - 1) * verticalSpacing * 0.5f;
        float yPos = index * verticalSpacing - yOffset;

        rect.anchoredPosition = new Vector2(xPos, yPos);
        nodePositions[node.buildingId] = rect;

        var building = currentRace?.GetBuilding(node.buildingId);
        if (building != null)
        {
            var nameText = nodeObj.GetComponentInChildren<TextMeshProUGUI>();
            if (nameText != null) nameText.text = building.buildingName;

            var icon = nodeObj.transform.Find("Icon")?.GetComponent<Image>();
            if (icon != null && building.icon != null) icon.sprite = building.icon;
        }
    }

    private void DrawConnection(string fromId, string toId)
    {
        if (!nodePositions.TryGetValue(fromId, out var from)) return;
        if (!nodePositions.TryGetValue(toId, out var to)) return;
        if (connectionLinePrefab == null) return;

        var lineObj = Instantiate(connectionLinePrefab, nodeContainer);
        var rect = lineObj.GetComponent<RectTransform>();

        Vector2 fromPos = from.anchoredPosition;
        Vector2 toPos = to.anchoredPosition;
        Vector2 midpoint = (fromPos + toPos) / 2f;

        rect.anchoredPosition = midpoint;
        Vector2 diff = toPos - fromPos;
        rect.sizeDelta = new Vector2(diff.magnitude, 2f);
        float angle = Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg;
        rect.localRotation = Quaternion.Euler(0, 0, angle);
    }

    public void UpdateUnlockStates(List<string> unlockedBuildingIds)
    {
        foreach (var kvp in nodePositions)
        {
            bool unlocked = unlockedBuildingIds.Contains(kvp.Key);
            var canvasGroup = kvp.Value.GetComponent<CanvasGroup>();
            if (canvasGroup != null)
                canvasGroup.alpha = unlocked ? 1f : 0.4f;
        }
    }

    private void ClearTree()
    {
        nodePositions.Clear();
        if (nodeContainer == null) return;
        foreach (Transform child in nodeContainer)
            Destroy(child.gameObject);
    }

    public void Toggle()
    {
        gameObject.SetActive(!gameObject.activeSelf);
    }
}
