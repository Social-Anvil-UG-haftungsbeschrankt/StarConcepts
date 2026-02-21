using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class RequestQueue : MonoBehaviour
{
    public int maxCapturesPerFrame = 1;

    private readonly Queue<IconKey> queue = new Queue<IconKey>();
    private readonly HashSet<IconKey> enqueued = new HashSet<IconKey>();

    public event Action<int, int> OnProgress; // (pending, total)

    private int totalEverQueued;

    public void Enqueue(IconKey key)
    {
        if (enqueued.Contains(key)) return;
        enqueued.Add(key);
        queue.Enqueue(key);
        totalEverQueued++;
        OnProgress?.Invoke(queue.Count, totalEverQueued);
    }

    public bool TryDequeue(out IconKey key)
    {
        if (queue.Count > 0)
        {
            key = queue.Dequeue();
            OnProgress?.Invoke(queue.Count, totalEverQueued);
            return true;
        }
        key = default;
        return false;
    }

    public void MarkDone(IconKey key)
    {
        enqueued.Remove(key);
        OnProgress?.Invoke(queue.Count, totalEverQueued);
    }

    public int PendingCount => queue.Count;
}
