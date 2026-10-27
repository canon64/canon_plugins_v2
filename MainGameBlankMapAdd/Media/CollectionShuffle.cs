using System;
using System.Collections.Generic;

namespace MainGameBlankMapAdd.Media
{
    internal static class CollectionShuffle
    {
        internal static void Shuffle<T>(IList<T> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            var random = new Random();
            for (int i = items.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                T value = items[i];
                items[i] = items[j];
                items[j] = value;
            }
        }
    }
}
