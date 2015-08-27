using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RoslynLinqRewrite
{
    internal class LinqRewriter : CSharpSyntaxRewriter
    {
        private SemanticModel semantic;
        public LinqRewriter(SemanticModel semantic)
        {
            this.semantic = semantic;
        }


        public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
        {

            var memberAccess = node.Expression as MemberAccessExpressionSyntax;
            if (memberAccess != null)
            {
                var symbol = semantic.GetSymbolInfo(memberAccess).Symbol as IMethodSymbol;
                if (symbol?.Name == "Any")
                {
                    var lambda = node.ArgumentList.Arguments.Single().Expression as LambdaExpressionSyntax;
                    if (lambda != null)
                    {
                        lastId++;


                        var isStatic = semantic.GetDeclaredSymbol(node.FirstAncestorOrSelf<MethodDeclarationSyntax>()).IsStatic;

                        var arg = (lambda as SimpleLambdaExpressionSyntax)?.Parameter ?? ((ParenthesizedLambdaExpressionSyntax)lambda).ParameterList.Parameters.Single();
                        var itemType = semantic.GetDeclaredSymbol(arg).Type;

                        var flow = semantic.AnalyzeDataFlow(lambda.Body);



                        var collection = ((MemberAccessExpressionSyntax)node.Expression).Expression;

                        string itemArg = null;
                        return RewriteAsLoop(
                            "Any" + lastId,
                            new[] { CreateParameter(ItemsName, semantic.GetTypeInfo(collection).Type) }.Concat(flow.Captured.Select(x => CreateParameter(x.Name, GetSymbolType(x)).WithRef(flow.WrittenInside.Contains(x)))),
                            CreatePrimitiveType(SyntaxKind.BoolKeyword),
                            Enumerable.Empty<StatementSyntax>(),
                            arguments =>
                            {
                                return SyntaxFactory.IfStatement(
                                 InlineOrCreateMethod((CSharpSyntaxNode)Visit(lambda.Body), arguments, flow, CreateParameter(arg.Identifier, itemType), isStatic, out itemArg),
                                 SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression))
                             );
                            },
                            new[] { SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression)) },
                            isStatic,
                            () => itemArg,
                            collection,
                            flow
                        );


                    }
                }
            }

            return base.VisitInvocationExpression(node);
        }
        const string ItemsName = "theitems";
        const string ItemName = "theitem";
        private ExpressionSyntax RewriteAsLoop(string functionName, IEnumerable<ParameterSyntax> parameters, TypeSyntax returnType, IEnumerable<StatementSyntax> prologue, Func<ArgumentListSyntax, StatementSyntax> loopBody, IEnumerable<StatementSyntax> epilogue, bool isStatic, Func<string> loopVariable, ExpressionSyntax collection, DataFlowAnalysis flow)
        {
            var arguments = CreateArguments(new[] { SyntaxFactory.Argument(SyntaxFactory.IdentifierName(ItemName)) }.Concat(flow.Captured.Select(x => SyntaxFactory.Argument(SyntaxFactory.IdentifierName(x.Name)).WithRef(flow.WrittenInside.Contains(x)))));

            var foreachBody = loopBody;
            var body = foreachBody(arguments);
            var loop = SyntaxFactory.ForEachStatement(
                SyntaxFactory.IdentifierName("var"),
                loopVariable(),
                SyntaxFactory.IdentifierName(ItemsName),
                body);
            var coreFunction = SyntaxFactory.MethodDeclaration(returnType, functionName)
                        .WithParameterList(CreateParameters(parameters))
                        .WithBody(SyntaxFactory.Block(prologue.Concat(new[] {
                            loop
                        }).Concat(epilogue)))
                        .WithStatic(isStatic)
                        .NormalizeWhitespace();
            methodsToAddToCurrentType.Add(coreFunction);


            return SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName(functionName), CreateArguments(new[] { SyntaxFactory.Argument(collection) }.Concat(arguments.Arguments.Skip(1))));

        }

        private static PredefinedTypeSyntax CreatePrimitiveType(SyntaxKind boolKeyword)
        {
            return SyntaxFactory.PredefinedType(SyntaxFactory.Token(boolKeyword));
        }

        private List<MethodDeclarationSyntax> methodsToAddToCurrentType = new List<MethodDeclarationSyntax>();
        private int lastId;

        public override SyntaxNode VisitStructDeclaration(StructDeclarationSyntax node)
        {
            var changed = (StructDeclarationSyntax)base.VisitStructDeclaration(node);
            if (methodsToAddToCurrentType.Count != 0)
            {
                var withMethods = changed.AddMembers(methodsToAddToCurrentType.ToArray());
                methodsToAddToCurrentType.Clear();
                return withMethods;
            }
            return changed;
        }

        private ExpressionSyntax InlineOrCreateMethod(CSharpSyntaxNode body, ArgumentListSyntax arguments, DataFlowAnalysis flow, ParameterSyntax arg, bool isStatic, out string itemArg)
        {
            var fn = "Check" + lastId;

            if (body is ExpressionSyntax)
            {
                itemArg = arg.Identifier.ValueText;
                return (ExpressionSyntax)body;
            }
            else
            {
                itemArg = ItemName;
                var method = SyntaxFactory.MethodDeclaration(CreatePrimitiveType(SyntaxKind.BoolKeyword), fn)
                                .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(
                                    new[] {
                                    arg
                                    }.Union(flow.Captured.Select(x => CreateParameter(x.Name, GetSymbolType(x)).WithRef(flow.WrittenInside.Contains(x))))
                                 )))
                                .WithBody(body as BlockSyntax ?? (body is StatementSyntax ? SyntaxFactory.Block((StatementSyntax)body) : SyntaxFactory.Block(SyntaxFactory.ReturnStatement((ExpressionSyntax)body))))
                                .WithStatic(isStatic)
                                .NormalizeWhitespace();


                methodsToAddToCurrentType.Add(method);
                return SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName(fn), arguments);
            }
        }

        public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            var changed = (ClassDeclarationSyntax)base.VisitClassDeclaration(node);
            if (methodsToAddToCurrentType.Count != 0)
            {
                var withMethods = changed.AddMembers(methodsToAddToCurrentType.ToArray());
                methodsToAddToCurrentType.Clear();
                return withMethods.NormalizeWhitespace();
            }
            return changed;
        }

        private LocalDeclarationStatementSyntax CreateLocalVariableDeclaration(string name, ExpressionSyntax value)
        {
            return SyntaxFactory.LocalDeclarationStatement(SyntaxFactory.VariableDeclaration(SyntaxFactory.IdentifierName("var"), CreateSeparatedList(new[] { SyntaxFactory.VariableDeclarator(name).WithInitializer(SyntaxFactory.EqualsValueClause(value)) })));
        }

        private ITypeSymbol GetSymbolType(ISymbol x)
        {
            var local = x as ILocalSymbol;
            if (local != null) return local.Type;

            var param = x as IParameterSymbol;
            if (param != null) return param.Type;

            throw new NotImplementedException();
        }
        private static SeparatedSyntaxList<T> CreateSeparatedList<T>(IEnumerable<T> items) where T : SyntaxNode
        {
            return SyntaxFactory.SeparatedList<T>(items);
        }
        private static ArgumentListSyntax CreateArguments(IEnumerable<ExpressionSyntax> items)
        {
            return CreateArguments(items.Select(x => SyntaxFactory.Argument(x)));
        }
        private static ArgumentListSyntax CreateArguments(IEnumerable<ArgumentSyntax> items)
        {
            return SyntaxFactory.ArgumentList(CreateSeparatedList(items));
        }
        private static ParameterListSyntax CreateParameters(IEnumerable<ParameterSyntax> items)
        {
            return SyntaxFactory.ParameterList(CreateSeparatedList(items));
        }
        private static ParameterSyntax CreateParameter(SyntaxToken name, ITypeSymbol type)
        {
            return SyntaxFactory.Parameter(name).WithType(SyntaxFactory.ParseTypeName(type.ToDisplayString()));
        }
        private static ParameterSyntax CreateParameter(string name, ITypeSymbol type)
        {
            return CreateParameter(SyntaxFactory.Identifier(name), type);
        }


    }


}