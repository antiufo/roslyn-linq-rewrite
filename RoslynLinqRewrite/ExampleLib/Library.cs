using System;
using System.Linq;

namespace ClassLibrary
{
    public class Class1
    {
        public void Method1()
        {

            var k = new[] { 1, 2, 3, 4 };
            var m = k.Where(x => x > 2).Sum();
        }
    }
}
