using System;
using System.Collections.Generic;
using System.Linq;
namespace NuGet
{
	internal static class CollectionExtensions
	{
		public static void AddRange<T>(this ICollection<T> collection, IEnumerable<T> items)
		{
			foreach (T current in items)
			{
				collection.Add(current);
			}
		}
		public static int RemoveAll<T>(this ICollection<T> collection, Func<T, bool> match)
		{
			IList<T> list = collection.Where(match).ToList<T>();
			foreach (T current in list)
			{
				collection.Remove(current);
			}
			return list.Count;
		}
	}
}
