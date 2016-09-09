using System;

namespace Shaman.Roslyn.LinqRewrite
{
    internal class ExitException : Exception
    {
        public int Code { get; }
        

        public ExitException(int v)
        {
            this.Code = v;
        }
        
    }
}