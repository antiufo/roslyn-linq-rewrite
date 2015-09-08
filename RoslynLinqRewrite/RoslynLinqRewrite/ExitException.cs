using System;

namespace RoslynLinqRewrite
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