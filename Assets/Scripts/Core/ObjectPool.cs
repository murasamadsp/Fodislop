#nullable enable

using System.Collections.Generic;
using UnityEngine;

namespace Fodinae.Core;

public class ObjectPool<T>(T prefab, Transform? parent = null, int preload = 0)
    where T : Component
{
    private readonly Queue<T> _pool = InitializePool(prefab, parent, preload);

    public int CountInactive => _pool.Count;

    private static Queue<T> InitializePool(T prefab, Transform? parent, int preload)
    {
        var queue = new Queue<T>(preload > 0 ? preload : 4);
        for (int i = 0; i < preload; i++)
        {
            var obj = Object.Instantiate(prefab, parent);
            obj.gameObject.SetActive(false);
            queue.Enqueue(obj);
        }

        return queue;
    }

    public T Get()
    {
        while (_pool.Count > 0)
        {
            var obj = _pool.Dequeue();
            if (obj != null)
            {
                obj.gameObject.SetActive(true);
                return obj;
            }
        }

        return Object.Instantiate(prefab, parent);
    }

    public void Return(T obj)
    {
        if (obj == null)
        {
            return;
        }

        obj.gameObject.SetActive(false);
        _pool.Enqueue(obj);
    }

    public void Clear()
    {
        while (_pool.Count > 0)
        {
            var obj = _pool.Dequeue();
            if (obj != null)
            {
                Object.Destroy(obj.gameObject);
            }
        }
    }
}
