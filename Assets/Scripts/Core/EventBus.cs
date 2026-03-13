using System;
using System.Collections.Generic;
using UnityEngine;

public static class EventBus
{
    private static readonly Dictionary<Type, List<Delegate>> subscribers = new();
    private static readonly object lockObj = new();

    public static void Subscribe<T>(Action<T> handler) where T : struct
    {
        lock (lockObj)
        {
            var type = typeof(T);
            if (!subscribers.ContainsKey(type))
                subscribers[type] = new List<Delegate>();
            subscribers[type].Add(handler);
        }
    }

    public static void Unsubscribe<T>(Action<T> handler) where T : struct
    {
        lock (lockObj)
        {
            var type = typeof(T);
            if (subscribers.ContainsKey(type))
                subscribers[type].Remove(handler);
        }
    }

    public static void Raise<T>(T evt) where T : struct
    {
        List<Delegate> handlersCopy;
        lock (lockObj)
        {
            var type = typeof(T);
            if (!subscribers.ContainsKey(type)) return;
            handlersCopy = new List<Delegate>(subscribers[type]);
        }

        foreach (var handler in handlersCopy)
        {
            try
            {
                ((Action<T>)handler)?.Invoke(evt);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        lock (lockObj)
        {
            subscribers.Clear();
        }
    }

    public static void Clear()
    {
        lock (lockObj)
        {
            subscribers.Clear();
        }
    }
}
