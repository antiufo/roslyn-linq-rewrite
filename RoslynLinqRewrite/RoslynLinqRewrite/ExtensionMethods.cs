using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RoslynLinqRewrite
{
    public static class ExtensionMethods
    {
        public static MethodDeclarationSyntax WithStatic(this MethodDeclarationSyntax syntax, bool isStatic)
        {
            if (isStatic) return syntax.AddModifiers(SyntaxFactory.Token(SyntaxKind.StaticKeyword));
            else return syntax.WithModifiers(SyntaxFactory.TokenList());
        }
        public static ParameterSyntax WithRef(this ParameterSyntax syntax, bool isRef)
        {
            if (isRef) return syntax.AddModifiers(SyntaxFactory.Token(SyntaxKind.RefKeyword));
            else return syntax.WithModifiers(SyntaxFactory.TokenList());
        }
        public static ArgumentSyntax WithRef(this ArgumentSyntax syntax, bool isRef)
        {
            if (isRef) return syntax.WithRefOrOutKeyword(SyntaxFactory.Token(SyntaxKind.RefKeyword));
            else return syntax.WithRefOrOutKeyword(default(SyntaxToken));
        }
    }
}
