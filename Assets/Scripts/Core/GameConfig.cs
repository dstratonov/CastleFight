using UnityEngine;

[CreateAssetMenu(menuName = "CastleFight/Game Config")]
public class GameConfig : ScriptableObject
{
    private static GameConfig cachedInstance;

    /// <summary>
    /// Cached accessor — loads from Resources once, then reuses.
    /// </summary>
    public static GameConfig Instance
    {
        get
        {
            if (cachedInstance == null)
                cachedInstance = Resources.Load<GameConfig>("GameConfig");
            return cachedInstance;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        cachedInstance = null;
    }

    [Header("Economy")]
    public int startingGold = 500;
    public int passiveIncomeAmount = 25;
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

    [Header("Castle")]
    public int castleMaxHealth = 5000;
}
