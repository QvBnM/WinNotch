using System;
using System.Collections.Generic;

namespace WinNotch.Core.Perf
{
    /// <summary>
    /// A fixed window of the most recent samples. Fixed on purpose: an hour of history must cost the same whether the
    /// app has been up for ten minutes or ten days, so nothing here grows and nothing is ever allocated after the
    /// start. Oldest first when read.
    /// </summary>
    public sealed class SampleRing<T>
    {
        private readonly T[] _items;
        private int _next;

        public SampleRing(int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            _items = new T[capacity];
        }

        public int Capacity => _items.Length;
        /// <summary>How many of the slots are filled (at most <see cref="Capacity"/>).</summary>
        public int Count { get; private set; }

        public void Add(T item)
        {
            _items[_next] = item;
            _next = (_next + 1) % _items.Length;
            if (Count < _items.Length) Count++;
        }

        public void Clear()
        {
            Array.Clear(_items, 0, _items.Length);
            _next = 0;
            Count = 0;
        }

        /// <summary>Index 0 is the oldest kept sample, <c>Count - 1</c> the newest.</summary>
        public T this[int i]
        {
            get
            {
                if (i < 0 || i >= Count) throw new ArgumentOutOfRangeException(nameof(i));
                int start = Count == _items.Length ? _next : 0;
                return _items[(start + i) % _items.Length];
            }
        }

        /// <summary>The newest sample, or <c>default</c> when nothing has been added yet.</summary>
        public T Last => Count > 0 ? this[Count - 1] : default;

        /// <summary>Oldest first. A snapshot: adding while the caller reads it cannot change what it sees.</summary>
        public List<T> ToList()
        {
            var list = new List<T>(Count);
            for (int i = 0; i < Count; i++) list.Add(this[i]);
            return list;
        }

        /// <summary>The last <paramref name="n"/> samples, oldest first (fewer if that is all there is).</summary>
        public List<T> Tail(int n)
        {
            int take = Math.Clamp(n, 0, Count);
            var list = new List<T>(take);
            for (int i = Count - take; i < Count; i++) list.Add(this[i]);
            return list;
        }
    }
}
