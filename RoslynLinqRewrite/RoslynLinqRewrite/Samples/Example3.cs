using System;
using System.Collections.Generic;
using System.Linq;

static class Example3
{
    static void Main()
    {
        var arr = new[] { 1, 2, 3, 4 };
        var q = 2;
        var l = arr.Where(x => x > q).Select(x => (q += x) + 3).Last();
        Console.WriteLine("q: {0}, l: {1}", q, l);
    }
}