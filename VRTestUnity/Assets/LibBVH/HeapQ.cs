using System;
using System.Collections.Generic;


namespace BVH
{
    public class HeapQ<T> where T : IComparable<T>
    {
        List<T> items;

        public HeapQ()
        {
            items = new List<T>();
        }

        public bool Empty
        {
            get { return items.Count == 0; }
        }

        public T First
        {
            get { return items[0]; }
        }

        public void Push(T item)
        {
            items.Add(item);
            SiftDown(0, items.Count - 1);
        }

        public T Pop()
        {
            T item;
            var last = items[items.Count - 1];
            items.RemoveAt(items.Count - 1);
            if (items.Count > 0)
            {
                item = items[0];
                items[0] = last;
                SiftUp(0);
            }
            else
            {
                item = last;
            }
            return item;
        }

        void SiftDown(int startpos, int pos)
        {
            var newitem = items[pos];
            while (pos > startpos)
            {
                var parentpos = (pos - 1) >> 1;
                var parent = items[parentpos];
                if (parent.CompareTo(newitem) <= 0)
                    break;
                items[pos] = parent;
                pos = parentpos;
            }
            items[pos] = newitem;
        }

        void SiftUp(int pos)
        {
            var endpos = items.Count;
            var startpos = pos;
            var newitem = items[pos];
            var childpos = 2 * pos + 1;
            while (childpos < endpos)
            {
                var rightpos = childpos + 1;
                if (rightpos < endpos && items[rightpos].CompareTo(items[childpos]) <= 0)
                    childpos = rightpos;
                items[pos] = items[childpos];
                pos = childpos;
                childpos = 2 * pos + 1;
            }
            items[pos] = newitem;
            SiftDown(startpos, pos);
        }
    }
}
