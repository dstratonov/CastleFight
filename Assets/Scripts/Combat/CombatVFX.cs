using UnityEngine;

public class CombatVFX : MonoBehaviour
{
    public static CombatVFX Instance { get; private set; }

    private CombatVFXConfig config;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        config = Resources.Load<CombatVFXConfig>("CombatVFXConfig");
        if (config == null)
            Debug.LogWarning("[CombatVFX] CombatVFXConfig not found in Resources folder");
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
    }

    private void OnEnable()
    {
        // VFX events disabled for now -- will re-enable when skills are implemented
    }

    private void OnDisable()
    {
    }

    public void PlayMeleeHit(Vector3 position)
    {
        if (config == null || config.meleeHitEffects == null || config.meleeHitEffects.Length == 0) return;
        var prefab = config.meleeHitEffects[Random.Range(0, config.meleeHitEffects.Length)];
        SpawnEffect(prefab, position, config.hitEffectScale);
    }

    private void OnUnitKilled(UnitKilledEvent evt)
    {
        if (evt.Unit == null || config == null) return;
        if (config.unitDeathEffects == null || config.unitDeathEffects.Length == 0) return;

        var prefab = config.unitDeathEffects[Random.Range(0, config.unitDeathEffects.Length)];
        Vector3 pos = evt.Unit.transform.position + Vector3.up * 0.5f;
        SpawnEffect(prefab, pos, config.deathEffectScale);
    }

    private void OnBuildingDestroyed(BuildingDestroyedEvent evt)
    {
        if (evt.Building == null || config == null) return;
        if (config.buildingDestroyEffects == null || config.buildingDestroyEffects.Length == 0) return;

        var prefab = config.buildingDestroyEffects[Random.Range(0, config.buildingDestroyEffects.Length)];
        Vector3 pos = evt.Building.transform.position + Vector3.up * 1f;
        SpawnEffect(prefab, pos, config.buildingDestroyScale);
    }

    private void SpawnEffect(GameObject prefab, Vector3 position, float scale)
    {
        if (prefab == null) return;
        var instance = Instantiate(prefab, position, Quaternion.identity);
        instance.transform.localScale = Vector3.one * scale;
        Destroy(instance, config != null ? config.effectLifetime : 3f);
    }
}
