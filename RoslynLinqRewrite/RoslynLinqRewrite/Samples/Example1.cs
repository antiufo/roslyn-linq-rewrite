using System;
using System.Collections.Generic;
using System.Linq;

static class Meow
{
    static void Main()
    {
        var sdfsdf = (new Exception()).RecursiveEnumeration(x => x.InnerException).Last();
        var arr = new[] { 5, 457, 7464, 66 };
        var arr2 = new[] { "a", "b" };
        var capture = 5;
        var meow = 2;
        var k = arr2.Where(x => x.StartsWith("t")).Select(x => x == "miao").LastOrDefault();
        //var k = arr.Where(x =>x > capture).Where(x =>x != 0).Select(x =>{return (double)x - 4;}).Where(x => x < 99).Any(x => x == 100);
        // var ka = arr.Sum();
    }
    public static IEnumerable<T> RecursiveEnumeration<T>(this T first, Func<T, T> parent)
    {
        var current = first;
        while (current != null)
        {
            yield return current;
            current = parent(current);
        }
    }
}