using UnityEngine;
using Mirror;
using System.Collections.Generic;

public class BuffSystem : NetworkBehaviour
{
    private readonly List<Buff> activeBuffs = new();
    private readonly Dictionary<string, Buff> buffLookup = new();

    public IReadOnlyList<Buff> ActiveBuffs => activeBuffs;

    private void Update()
    {
        if (!isServer) return;

        for (int i = activeBuffs.Count - 1; i >= 0; i--)
        {
            activeBuffs[i].remainingDuration -= Time.deltaTime;
            if (activeBuffs[i].remainingDuration <= 0f)
            {
                RemoveBuff(activeBuffs[i]);
                activeBuffs.RemoveAt(i);
            }
        }
    }

    [Server]
    public void ApplyBuff(Buff buff)
    {
        if (buffLookup.TryGetValue(buff.id, out var existing))
        {
            existing.remainingDuration = buff.remainingDuration;
            existing.value = buff.value;
            return;
        }

        activeBuffs.Add(buff);
        buffLookup[buff.id] = buff;
        OnBuffApplied(buff);
    }

    [Server]
    public void RemoveBuffById(string buffId)
    {
        if (!buffLookup.TryGetValue(buffId, out var buff)) return;

        RemoveBuff(buff);
        activeBuffs.Remove(buff);
    }

    private void RemoveBuff(Buff buff)
    {
        buffLookup.Remove(buff.id);
        OnBuffRemoved(buff);
    }

    private void OnBuffApplied(Buff buff)
    {
        // Apply stat modifications
    }

    private void OnBuffRemoved(Buff buff)
    {
        // Revert stat modifications
    }

    [Server]
    public void ClearAllBuffs()
    {
        foreach (var buff in activeBuffs)
            OnBuffRemoved(buff);
        activeBuffs.Clear();
        buffLookup.Clear();
    }

    public float GetBuffValue(string buffId)
    {
        return buffLookup.TryGetValue(buffId, out var buff) ? buff.value : 0f;
    }

    public bool HasBuff(string buffId)
    {
        return buffLookup.ContainsKey(buffId);
    }
}

[System.Serializable]
public class Buff
{
    public string id;
    public float remainingDuration;
    public float value;
    public bool isAura;

    public Buff(string id, float duration, float value, bool isAura = false)
    {
        this.id = id;
        this.remainingDuration = duration;
        this.value = value;
        this.isAura = isAura;
    }
}
