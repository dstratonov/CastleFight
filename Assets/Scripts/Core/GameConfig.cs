using UnityEngine;

[CreateAssetMenu(menuName = "CastleFight/Game Config")]
public class GameConfig : ScriptableObject
{
    [Header("Economy")]
    public int startingGold = 100;
    public int passiveIncomeAmount = 10;
    public float incomeTickInterval = 5f;

    [Header("Hero")]
    public float heroBuildRange = 5f;
    public float heroRespawnTime = 10f;
    public float heroMoveSpeed = 8f;

    [Header("Combat")]
    public float unitScanRadius = 10f;
    public float projectileSpeed = 15f;

    [Header("Match")]
    public float matchStartCountdown = 5f;
}
