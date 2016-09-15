using System;
using System.Collections.Generic;
using System.Linq;

static class Example2
{
    static void Main()
    {
        var arr = Enumerable.Range(0, 10).ToList();
        var m1 = arr.First(x => x > 4);
        var m2 = arr.All(x => x > 4);
        var m3 = arr.Any(x => x > 4);
        var m4 = arr.FirstOrDefault(x => x > 4);
        var m5 = arr.SingleOrDefault(x => x > 4);
        var m6 = arr.First(x => x > 4);
        var m7 = arr.First();
        var m8 = arr.Any();
        var m9 = arr.Single();

    }

}