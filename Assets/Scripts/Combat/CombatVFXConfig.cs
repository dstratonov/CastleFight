using UnityEngine;

[CreateAssetMenu(menuName = "CastleFight/Combat VFX Config")]
public class CombatVFXConfig : ScriptableObject
{
    [Header("Hit Effects (spawned at target on melee hit)")]
    public GameObject[] meleeHitEffects;

    [Header("Death Effects (spawned when unit dies)")]
    public GameObject[] unitDeathEffects;

    [Header("Building Destruction (spawned when building is destroyed)")]
    public GameObject[] buildingDestroyEffects;

    [Header("Settings")]
    [Tooltip("Auto-destroy spawned VFX after this many seconds")]
    public float effectLifetime = 3f;

    [Tooltip("Scale multiplier for hit effects")]
    public float hitEffectScale = 0.5f;

    [Tooltip("Scale multiplier for death effects")]
    public float deathEffectScale = 0.7f;

    [Tooltip("Scale multiplier for building destruction effects")]
    public float buildingDestroyScale = 1.2f;
}
