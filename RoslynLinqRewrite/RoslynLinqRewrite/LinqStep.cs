using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RoslynLinqRewrite
{
    class LinqStep
    {
        public LinqStep(string methodName, IReadOnlyList<ExpressionSyntax> arguments)
        {
            this.MethodName = methodName;
            this.Arguments = arguments;
        }
        public string MethodName { get; }
        public IReadOnlyList<ExpressionSyntax> Arguments { get; }
    }
}
