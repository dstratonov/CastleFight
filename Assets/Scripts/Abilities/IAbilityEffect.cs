using UnityEngine;

public interface IAbilityEffect
{
    void Apply(GameObject target, float value, float duration);
    void Remove(GameObject target);
}
