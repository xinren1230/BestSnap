using System;
using System.Collections.Concurrent;
using System.Threading;

namespace BestSnap.Plugins
{
    /// <summary>Newest-wins queue: at most <see cref="MaxPending"/> items waiting. Drops oldest when lag grows.</summary>
    public sealed class BoundedJobQueue<T>
    {
        readonly ConcurrentQueue<T> _q = new ConcurrentQueue<T>();
        int _count;

        public BoundedJobQueue(int maxPending = WhisperDecodeBudget.LiveMaxPending)
        {
            MaxPending = maxPending < 1 ? 1 : maxPending;
        }

        public int MaxPending { get; }
        public int Count => Math.Max(0, Volatile.Read(ref _count));

        public int EnqueueNewest(T item, out int dropped)
        {
            dropped = 0;
            while (Count >= MaxPending && TryDequeue(out _))
                dropped++;
            _q.Enqueue(item);
            return Interlocked.Increment(ref _count);
        }

        public bool TryDequeue(out T item)
        {
            if (_q.TryDequeue(out item))
            {
                Interlocked.Decrement(ref _count);
                return true;
            }
            item = default;
            return false;
        }

        public void Clear()
        {
            while (TryDequeue(out _)) { }
        }
    }
}
