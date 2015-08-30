using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynLinqRewrite
{
    class CanRewrapForeachVisitor : CSharpSyntaxWalker
    {
        internal bool Fail;
        public override void VisitAwaitExpression(AwaitExpressionSyntax node)
        {
            Fail = true;
        }

        public override void VisitBreakStatement(BreakStatementSyntax node)
        {
            Fail = true;
        }

        public override void VisitContinueStatement(ContinueStatementSyntax node)
        {
            Fail = true;
        }

        public override void VisitReturnStatement(ReturnStatementSyntax node)
        {
            Fail = true;
        }

        public override void VisitGotoStatement(GotoStatementSyntax node)
        {
            Fail = true;
        }

        public override void VisitYieldStatement(YieldStatementSyntax node)
        {
            Fail = true;
        }

        public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
        {
        }

        public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
        {
        }

        public override void VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node)
        {
        }
    }
}
