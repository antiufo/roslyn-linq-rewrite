using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynLinqRewrite
{
    class Lambda
    {

        public CSharpSyntaxNode Body { get; }
        public IReadOnlyList<ParameterSyntax> Parameters { get; }
        public AnonymousFunctionExpressionSyntax Syntax { get; }

        public Lambda(AnonymousFunctionExpressionSyntax lambda)
        {
            Body = lambda.Body;
            Syntax = lambda;
            if (lambda is ParenthesizedLambdaExpressionSyntax) Parameters = ((ParenthesizedLambdaExpressionSyntax)lambda).ParameterList.Parameters;
            if (lambda is AnonymousMethodExpressionSyntax) Parameters = ((AnonymousMethodExpressionSyntax)lambda).ParameterList.Parameters;
            if (lambda is SimpleLambdaExpressionSyntax) Parameters = new[] { ((SimpleLambdaExpressionSyntax)lambda).Parameter };
        }

        public Lambda(StatementSyntax statement, ParameterSyntax[] parameters)
        {
            Body = statement;
            Parameters = parameters;
        }
    }
}
