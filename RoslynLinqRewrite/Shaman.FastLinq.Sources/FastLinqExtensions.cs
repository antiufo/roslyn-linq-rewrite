using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace System.Linq
{
    internal static partial class FastLinqExtensions
    {
        public static TSource Single<TSource>(this TSource[] array)
        {
            if (array.Length > 1) throw new InvalidOperationException("Sequence contains more than one element");
            return array[0];
        }
        public static TSource Single<TSource>(this List<TSource> array)
        {
            if (array.Count > 1) throw new InvalidOperationException("Sequence contains more than one element");
            return array[0];
        }
        public static TSource First<TSource>(this TSource[] array)
        {
            return array[0];
        }
        public static TSource First<TSource>(this List<TSource> array)
        {
            return array[0];
        }
        public static TSource First<TSource>(this TSource[] array, Func<TSource, bool> condition)
        {
            for (int i = 0; i < array.Length; i++)
            {
                if (condition(array[i])) return array[i];
            }
            throw new InvalidOperationException("No items match the specified search criteria.");
        }

        public static TSource First<TSource>(this List<TSource> array, Func<TSource, bool> condition)
        {
            var len = array.Count;
            for (int i = 0; i < len; i++)
            {
                if (condition(array[i])) return array[i];
            }
            throw new InvalidOperationException("No items match the specified search criteria.");
        }
        public static TSource FirstOrDefault<TSource>(this TSource[] array, Func<TSource, bool> condition)
        {
            for (int i = 0; i < array.Length; i++)
            {
                if (condition(array[i])) return array[i];
            }
            return default(TSource);
        }
        public static TSource FirstOrDefault<TSource>(this List<TSource> array, Func<TSource, bool> condition)
        {
            var len = array.Count;
            for (int i = 0; i < len; i++)
            {
                if (condition(array[i])) return array[i];
            }
            return default(TSource);
        }

        public static bool Contains(this string str, char ch)
        {
            return str.IndexOf(ch) != -1;
        }

        public static TSource FirstOrDefault<TSource>(this TSource[] array)
        {
            return array.Length == 0 ? default(TSource) : array[0];
        }

        public static TSource FirstOrDefault<TSource>(this List<TSource> array)
        {
            return array.Count == 0 ? default(TSource) : array[0];
        }
        public static TSource SingleOrDefault<TSource>(this TSource[] array)
        {
            if (array.Length > 1) throw new InvalidOperationException("Sequence contains more than one element");
            return array.Length == 0 ? default(TSource) : array[0];
        }

        public static TSource SingleOrDefault<TSource>(this List<TSource> array)
        {
            if (array.Count > 1) throw new InvalidOperationException("Sequence contains more than one element");
            return array.Count == 0 ? default(TSource) : array[0];
        }
        public static TSource Last<TSource>(this List<TSource> array)
        {
            return array[array.Count - 1];
        }
        public static TSource Last<TSource>(this TSource[] array)
        {
            return array[array.Length - 1];
        }
        public static TSource LastOrDefault<TSource>(this List<TSource> array)
        {
            return array.Count == 0 ? default(TSource) : array[array.Count - 1];
        }
        public static TSource LastOrDefault<TSource>(this TSource[] array)
        {
            return array.Length == 0 ? default(TSource) : array[array.Length - 1];
        }
#if !LINQREWRITE
        public static bool Any<TSource>(this TSource[] array, Func<TSource, bool> condition)
        {
            for (int i = 0; i < array.Length; i++)
            {
                if (condition(array[i])) return true;
            }
            return false;
        }
#endif
        public static bool Any<TSource>(this List<TSource> array)
        {
            return array.Count != 0;
        }
        public static bool Any<TSource>(this TSource[] array)
        {
            return array.Length != 0;
        }
#if !LINQREWRITE
        public static bool Any<TSource>(this List<TSource> array, Func<TSource, bool> condition)
        {
            var len = array.Count;
            for (int i = 0; i < len; i++)
            {
                if (condition(array[i])) return true;
            }
            return false;
        }
        public static bool All<TSource>(this TSource[] array, Func<TSource, bool> condition)
        {
            for (int i = 0; i < array.Length; i++)
            {
                if (!condition(array[i])) return false;
            }
            return true;
        }
        public static bool All<TSource>(this List<TSource> array, Func<TSource, bool> condition)
        {
            var len = array.Count;
            for (int i = 0; i < len; i++)
            {
                if (!condition(array[i])) return false;
            }
            return true;
        }
#endif
        public static TSource[] ToArray<TSource>(this List<TSource> array)
        {
            var len = array.Count;
            var dest = new TSource[len];
            for (int i = 0; i < dest.Length; i++)
            {
                dest[i] = array[i];
            }
            return dest;
        }
        public static TSource[] ToArray<TSource>(this TSource[] array)
        {
            var dest = new TSource[array.Length];
            for (int i = 0; i < dest.Length; i++)
            {
                dest[i] = array[i];
            }
            return dest;
        }
        public static List<TSource> ToList<TSource>(this List<TSource> array)
        {
            var len = array.Count;
            var dest = new List<TSource>(len);
            for (int i = 0; i < len; i++)
            {
                dest.Add(array[i]);
            }
            return dest;
        }
        public static List<TSource> ToList<TSource>(this TSource[] array)
        {
            var dest = new List<TSource>(array.Length);
            for (int i = 0; i < array.Length; i++)
            {
                dest.Add(array[i]);
            }
            return dest;
        }


    }
}
