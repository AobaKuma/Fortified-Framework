using System.Collections;
using System.Collections.Generic;
using Verse;

namespace Fortified
{
    /// <summary>
    /// 裝置註冊表（雷達、發射架、CIWS、干擾機……）：不重複、依所屬 Thing 的 <see cref="Thing.thingIDNumber"/> 排序。
    /// <para>
    /// 取代原本的 <c>HashSet</c>。HashSet 的走訪順序取決於物件的雜湊值，每台電腦、每次啟動都不同；
    /// 防空有「第一個符合的防禦方」「同分時先找到的雷達」「CIWS 依序分配連射」這類依順序決定結果的地方，
    /// 在 Multiplayer 這種要求每台電腦算出同樣結果的環境會不同步。thingIDNumber 存在存檔裡，各台電腦一致。
    /// </para>
    /// </summary>
    public sealed class OrderedCompSet<T> : IReadOnlyCollection<T> where T : ThingComp
    {
        private readonly List<T> items = new List<T>();

        public int Count => items.Count;

        public T this[int index] => items[index];

        private static int KeyOf(T comp) => comp?.parent?.thingIDNumber ?? int.MinValue;

        /// <summary>加入；已經在裡面時回傳 false。</summary>
        public bool Add(T comp)
        {
            if (comp == null || items.Contains(comp)) { return false; }
            int key = KeyOf(comp);
            int lo = 0, hi = items.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (KeyOf(items[mid]) <= key) { lo = mid + 1; } else { hi = mid; }
            }
            items.Insert(lo, comp);
            return true;
        }

        public bool Remove(T comp) => items.Remove(comp);

        public bool Contains(T comp) => items.Contains(comp);

        public void Clear() => items.Clear();

        public List<T>.Enumerator GetEnumerator() => items.GetEnumerator();

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => items.GetEnumerator();
    }
}
