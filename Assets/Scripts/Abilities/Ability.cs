using UnityEngine;

public abstract class Ability : MonoBehaviour
{
    [SerializeField] protected AbilityData data;

    public AbilityData Data => data;

    public abstract void Activate(GameObject caster, Vector3 targetPosition, GameObject targetObject = null);
    public abstract void Deactivate();

    public virtual bool CanActivate(GameObject caster)
    {
        return true;
    }
}
