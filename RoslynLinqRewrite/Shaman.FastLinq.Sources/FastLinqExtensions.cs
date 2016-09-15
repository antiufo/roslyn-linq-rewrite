using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace System.Linq
{
    internal static partial class FastLinqExtensions
    {
        public static T First<T>(this T[] array)
        {
            return array[0];
        }
        public static T First<T>(this List<T> array)
        {
            return array[0];
        }
#if !LINQREWRITE
        public static T First<T>(this T[] array, Func<T, bool> condition)
        {
            for (int i = 0; i < array.Length; i++)
            {
                if (condition(array[i])) return array[i];
            }
            throw new InvalidOperationException("No items match the specified search criteria.");
        }

        public static T First<T>(this List<T> array, Func<T, bool> condition)
        {
            var len = array.Count;
            for (int i = 0; i < len; i++)
            {
                if (condition(array[i])) return array[i];
            }
            throw new InvalidOperationException("No items match the specified search criteria.");
        }
        public static T FirstOrDefault<T>(this T[] array, Func<T, bool> condition)
        {
            for (int i = 0; i < array.Length; i++)
            {
                if (condition(array[i])) return array[i];
            }
            return default(T);
        }
        public static T FirstOrDefault<T>(this List<T> array, Func<T, bool> condition)
        {
            var len = array.Count;
            for (int i = 0; i < len; i++)
            {
                if (condition(array[i])) return array[i];
            }
            return default(T);
        }
#endif

        public static bool Contains(this string str, char ch)
        {
            return str.IndexOf(ch) != -1;
        }

        public static T FirstOrDefault<T>(this T[] array)
        {
            return array.Length == 0 ? default(T) : array[0];
        }

        public static T FirstOrDefault<T>(this List<T> array)
        {
            return array.Count == 0 ? default(T) : array[0];
        }
        public static T Last<T>(this List<T> array)
        {
            return array[array.Count - 1];
        }
        public static T Last<T>(this T[] array)
        {
            return array[array.Length - 1];
        }
        public static T LastOrDefault<T>(this List<T> array)
        {
            return array.Count == 0 ? default(T) : array[array.Count - 1];
        }
        public static T LastOrDefault<T>(this T[] array)
        {
            return array.Length == 0 ? default(T) : array[array.Length - 1];
        }
#if !LINQREWRITE
        public static bool Any<T>(this T[] array, Func<T, bool> condition)
        {
            for (int i = 0; i < array.Length; i++)
            {
                if (condition(array[i])) return true;
            }
            return false;
        }
#endif
        public static bool Any<T>(this List<T> array)
        {
            return array.Count != 0;
        }
        public static bool Any<T>(this T[] array)
        {
            return array.Length != 0;
        }
#if !LINQREWRITE
        public static bool Any<T>(this List<T> array, Func<T, bool> condition)
        {
            var len = array.Count;
            for (int i = 0; i < len; i++)
            {
                if (condition(array[i])) return true;
            }
            return false;
        }
        public static bool All<T>(this T[] array, Func<T, bool> condition)
        {
            for (int i = 0; i < array.Length; i++)
            {
                if (!condition(array[i])) return false;
            }
            return true;
        }
        public static bool All<T>(this List<T> array, Func<T, bool> condition)
        {
            var len = array.Count;
            for (int i = 0; i < len; i++)
            {
                if (!condition(array[i])) return false;
            }
            return true;
        }
#endif
        public static T[] ToArray<T>(this List<T> array)
        {
            var len = array.Count;
            var dest = new T[len];
            for (int i = 0; i < dest.Length; i++)
            {
                dest[i] = array[i];
            }
            return dest;
        }
        public static T[] ToArray<T>(this T[] array)
        {
            var dest = new T[array.Length];
            for (int i = 0; i < dest.Length; i++)
            {
                dest[i] = array[i];
            }
            return dest;
        }
        public static List<T> ToList<T>(this List<T> array)
        {
            var len = array.Count;
            var dest = new List<T>(len);
            for (int i = 0; i < len; i++)
            {
                dest.Add(array[i]);
            }
            return dest;
        }
        public static List<T> ToList<T>(this T[] array)
        {
            var dest = new List<T>(array.Length);
            for (int i = 0; i < array.Length; i++)
            {
                dest.Add(array[i]);
            }
            return dest;
        }


    }
}
