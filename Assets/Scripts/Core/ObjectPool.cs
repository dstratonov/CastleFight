using System.Collections.Generic;
using UnityEngine;

public class ObjectPool : MonoBehaviour
{
    [SerializeField] private GameObject prefab;
    [SerializeField] private int initialSize = 20;
    [SerializeField] private Transform poolParent;

    private readonly Queue<GameObject> available = new();
    private readonly HashSet<GameObject> inUse = new();

    public int CountAvailable => available.Count;
    public int CountInUse => inUse.Count;

    private void Awake()
    {
        if (poolParent == null)
            poolParent = transform;

        if (prefab != null)
            Prewarm(initialSize);
    }

    public void Initialize(GameObject prefabToPool, int size)
    {
        prefab = prefabToPool;
        Prewarm(size);
    }

    private void Prewarm(int count)
    {
        if (prefab == null) return;

        for (int i = 0; i < count; i++)
        {
            var obj = CreateInstance();
            obj.SetActive(false);
            available.Enqueue(obj);
        }
    }

    public GameObject Get(Vector3 position, Quaternion rotation)
    {
        if (prefab == null) return null;

        GameObject obj;

        if (available.Count > 0)
        {
            obj = available.Dequeue();
        }
        else
        {
            obj = CreateInstance();
        }

        obj.transform.SetPositionAndRotation(position, rotation);
        obj.SetActive(true);
        inUse.Add(obj);
        return obj;
    }

    public void Return(GameObject obj)
    {
        if (obj == null) return;
        if (!inUse.Remove(obj)) return;

        obj.SetActive(false);
        obj.transform.SetParent(poolParent);
        available.Enqueue(obj);
    }

    public void ReturnAll()
    {
        var objects = new List<GameObject>(inUse);
        foreach (var obj in objects)
            Return(obj);
    }

    private GameObject CreateInstance()
    {
        var obj = Instantiate(prefab, poolParent);
        obj.name = prefab.name;
        return obj;
    }
}
