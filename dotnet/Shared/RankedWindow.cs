using System;
using System.Collections.Generic;

namespace SkyKeyYuyan.Internal
{
    internal sealed class RankedWindow<T>
    {
        private readonly struct Entry
        {
            internal readonly T Value;
            internal readonly long Sequence;
            internal Entry(T value, long sequence) { Value = value; Sequence = sequence; }
        }
        private readonly long _capacity;
        private readonly Comparison<T> _comparison;
        private readonly List<Entry> _heap;
        internal long Seen { get; private set; }
        internal RankedWindow(long capacity, Comparison<T> comparison)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity; _comparison = comparison;
            _heap = new List<Entry>((int)Math.Min(capacity, 32));
        }
        private int Compare(Entry left, Entry right)
        {
            int result = _comparison(left.Value, right.Value);
            return result != 0 ? result : left.Sequence.CompareTo(right.Sequence);
        }
        internal void Add(T value)
        {
            var entry = new Entry(value, Seen++);
            // 堆顶始终是窗口内最差的记录，同分时保留先遇到的记录。
            if (_heap.Count < _capacity)
            {
                int index = _heap.Count; _heap.Add(entry);
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (Compare(entry, _heap[parent]) <= 0) break;
                    _heap[index] = _heap[parent]; index = parent;
                }
                _heap[index] = entry;
            }
            else if (Compare(entry, _heap[0]) < 0)
            {
                int index = 0;
                while (index < _heap.Count / 2)
                {
                    int child = index * 2 + 1;
                    if (child + 1 < _heap.Count && Compare(_heap[child + 1], _heap[child]) > 0) child++;
                    if (Compare(entry, _heap[child]) >= 0) break;
                    _heap[index] = _heap[child]; index = child;
                }
                _heap[index] = entry;
            }
        }
        internal T[] GetPage(int offset, int pageSize)
        {
            if (offset >= _heap.Count) return Array.Empty<T>();
            var sorted = _heap.ToArray(); Array.Sort(sorted, Compare);
            var result = new T[Math.Min(pageSize, sorted.Length - offset)];
            for (int index = 0; index < result.Length; index++) result[index] = sorted[offset + index].Value;
            return result;
        }
    }
}
