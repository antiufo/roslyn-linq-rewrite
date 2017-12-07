using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CscWrapper
{
    class Program
    {
        static int Main(string[] args)
        {
            return Shaman.Roslyn.LinqRewrite.Program.Main(args);
        }
    }
}
