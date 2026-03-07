using UnityEngine;

public enum AbilityType
{
    Passive,
    Active,
    Aura
}

public enum AbilityTargetType
{
    Self,
    SingleEnemy,
    SingleAlly,
    AreaEnemy,
    AreaAlly,
    AreaAll
}

[CreateAssetMenu(menuName = "CastleFight/Ability Data")]
public class AbilityData : ScriptableObject
{
    public string abilityId;
    public string abilityName;
    [TextArea] public string description;
    public Sprite icon;
    public AbilityType abilityType = AbilityType.Passive;
    public AbilityTargetType targetType = AbilityTargetType.Self;

    [Header("Cooldown")]
    public float cooldown = 10f;

    [Header("Effect")]
    public float radius = 5f;
    public float duration = 5f;
    public float value = 10f;

    [Header("Visuals")]
    public GameObject effectPrefab;
}
