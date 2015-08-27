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
                currentMethodIsStatic = semantic.GetDeclaredSymbol(node.FirstAncestorOrSelf<MethodDeclarationSyntax>()).IsStatic;

                if (symbol.ContainingType.ToString() == "System.Linq.Enumerable")
                {
                    var lambda = node.ArgumentList.Arguments.FirstOrDefault()?.Expression as LambdaExpressionSyntax;
                    var arg = (lambda as SimpleLambdaExpressionSyntax)?.Parameter ?? (lambda as ParenthesizedLambdaExpressionSyntax)?.ParameterList.Parameters.FirstOrDefault();
                    var collection = ((MemberAccessExpressionSyntax)node.Expression).Expression;
                    var collectionType = semantic.GetTypeInfo(collection).Type;

                    var itemType = collectionType is IArrayTypeSymbol ? ((IArrayTypeSymbol)collectionType).ElementType : collectionType.AllInterfaces.Concat(new[] { collectionType }).OfType<INamedTypeSymbol>().First(x => x.IsGenericType && x.ConstructUnboundGenericType().ToString() == "System.Collections.Generic.IEnumerable<>").TypeArguments.First();
                    var dataFlow = lambda != null ? semantic.AnalyzeDataFlow(lambda.Body) : null;
                    currentFlow = dataFlow?.Captured.Select(x => new VariableCapture(x, dataFlow.WrittenInside.Contains(x))) ?? Enumerable.Empty<VariableCapture>();


                    if (GetMethodFullName(node) == AnyWithConditionMethod)
                    {
                      

                        string itemArg = null;
                        return RewriteAsLoop(
                            CreatePrimitiveType(SyntaxKind.BoolKeyword),
                            Enumerable.Empty<StatementSyntax>(),
                            arguments =>
                            {
                                return SyntaxFactory.IfStatement(
                                 InlineOrCreateMethod((CSharpSyntaxNode)Visit(lambda.Body), arguments, CreateParameter(arg.Identifier, itemType), out itemArg),
                                 SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression))
                             );
                            },
                            new[] { SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression)) },
                            () => itemArg,
                            collection
                        );
                    }

                    if (GetMethodFullName(node) == WhereMethod)
                    {

                        string itemArg = null;
                        return RewriteAsLoop(
                            SyntaxFactory.ParseTypeName("System.Collections.Generic.IEnumerable<" + itemType.ToDisplayString() + ">"),
                            Enumerable.Empty<StatementSyntax>(),
                            arguments =>
                            {
                                return SyntaxFactory.IfStatement(
                                 InlineOrCreateMethod((CSharpSyntaxNode)Visit(lambda.Body), arguments, CreateParameter(arg.Identifier, itemType), out itemArg),
                                 SyntaxFactory.YieldStatement(SyntaxKind.YieldReturnStatement, SyntaxFactory.IdentifierName(arg.Identifier))
                             );
                            },
                            Enumerable.Empty<StatementSyntax>(),
                            () => itemArg,
                            collection
                        );
                    }

                    if (GetMethodFullName(node) == SumWithSelectorMethod)
                    {

                        string itemArg = null;



                        var innerInvocation = collection as InvocationExpressionSyntax;
                        var innerMethodName = innerInvocation != null ? semantic.GetSymbolInfo(innerInvocation.Expression).Symbol : null;
                        if (innerMethodName.Name == "Where")
                        {

                            return RewriteAsLoop(
                                CreatePrimitiveType(SyntaxKind.IntKeyword),
                                new[] { CreateLocalVariableDeclaration("sum_", SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0))) },
                                arguments =>
                                {
                                    return SyntaxFactory.IfStatement(
                                    InlineOrCreateMethod((CSharpSyntaxNode)Visit(lambda.Body), arguments, CreateParameter(arg.Identifier, itemType), out itemArg),
                                    SyntaxFactory.ExpressionStatement(SyntaxFactory.AssignmentExpression(SyntaxKind.AddAssignmentExpression, SyntaxFactory.IdentifierName("sum_"),
                                         InlineOrCreateMethod((CSharpSyntaxNode)Visit(lambda.Body), arguments, CreateParameter(arg.Identifier, itemType), out itemArg)))
                                         );

                                },
                                new[] { SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName("sum_")) },
                                () => itemArg,
                                collection
                            );

                        }
                        else
                        {
                            return RewriteAsLoop(
                               CreatePrimitiveType(SyntaxKind.IntKeyword),
                               new[] { CreateLocalVariableDeclaration("sum_", SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0))) },
                               arguments =>
                               {
                                   return SyntaxFactory.ExpressionStatement(SyntaxFactory.AssignmentExpression(SyntaxKind.AddAssignmentExpression, SyntaxFactory.IdentifierName("sum_"),
                                        InlineOrCreateMethod((CSharpSyntaxNode)Visit(lambda.Body), arguments, CreateParameter(arg.Identifier, itemType), out itemArg)));

                               },
                               new[] { SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName("sum_")) },
                               () => itemArg,
                               collection
                           );
                        }


                    }


                    if (GetMethodFullName(node) == SumIntsMethod)
                    {
                        string itemArg = null;
                        return RewriteAsLoop(
                            CreatePrimitiveType(SyntaxKind.IntKeyword),
                            new[] { CreateLocalVariableDeclaration("sum_", SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0))) },
                            arguments =>
                            {
                                return SyntaxFactory.ExpressionStatement(SyntaxFactory.AssignmentExpression(SyntaxKind.AddAssignmentExpression, SyntaxFactory.IdentifierName("sum_"),
                                     InlineOrCreateMethod(SyntaxFactory.IdentifierName(ItemName), arguments, CreateParameter(ItemName, itemType), out itemArg)));

                            },
                            new[] { SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName("sum_")) },
                            () => itemArg,
                            collection
                        );
                    }

                }
            }

            return base.VisitInvocationExpression(node);
        }

        readonly static string AnyWithConditionMethod = "System.Collections.Generic.IEnumerable<TSource>.Any<TSource>(System.Func<TSource, bool>)";
        readonly static string SumWithSelectorMethod = "System.Collections.Generic.IEnumerable<TSource>.Sum<TSource>(System.Func<TSource, int>)";
        readonly static string SumIntsMethod = "System.Collections.Generic.IEnumerable<int>.Sum()";
        readonly static string WhereMethod = "System.Collections.Generic.IEnumerable<TSource>.Where<TSource>(System.Func<TSource, bool>)";

        ITypeSymbol GetSymbolType(VariableCapture x)
        {
            return GetSymbolType(x.Symbol);
        }

        private string GetMethodFullName(CSharpSyntaxNode syntax)
        {
            var invocation = syntax as InvocationExpressionSyntax;
            if (invocation != null)
            {
                return (semantic.GetSymbolInfo(invocation.Expression).Symbol as IMethodSymbol)?.ConstructedFrom.ToString();
            }
            return null;
        }

        const string ItemsName = "theitems";
        const string ItemName = "theitem";
        private class VariableCapture

        {
            public VariableCapture(ISymbol symbol, bool changes)
            {
                this.Symbol = symbol;
                this.Changes = changes;
            }
            public ISymbol Symbol { get; }
            public bool Changes { get; }
            public string Name
            {
                get { return Symbol.Name; }
            }
        }



        private ExpressionSyntax RewriteAsLoop(TypeSyntax returnType, IEnumerable<StatementSyntax> prologue, Func<ArgumentListSyntax, StatementSyntax> loopBody, IEnumerable<StatementSyntax> epilogue, Func<string> loopVariable, ExpressionSyntax collection)
        {
            var parameters = new[] { CreateParameter(ItemsName, semantic.GetTypeInfo(collection).Type) }.Concat(currentFlow.Select(x => CreateParameter(x.Name, GetSymbolType(x.Symbol)).WithRef(x.Changes)));

            var functionName = "ProceduralLinq" + ++lastId;
            var arguments = CreateArguments(new[] { SyntaxFactory.Argument(SyntaxFactory.IdentifierName(ItemName)) }.Concat(currentFlow.Select(x => SyntaxFactory.Argument(SyntaxFactory.IdentifierName(x.Name)).WithRef(x.Changes))));

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
                        .WithStatic(currentMethodIsStatic)
                        .NormalizeWhitespace();
            methodsToAddToCurrentType.Add(coreFunction);


            return SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName(functionName), CreateArguments(new[] { SyntaxFactory.Argument((ExpressionSyntax)Visit(collection)) }.Concat(arguments.Arguments.Skip(1))));

        }

        private static PredefinedTypeSyntax CreatePrimitiveType(SyntaxKind boolKeyword)
        {
            return SyntaxFactory.PredefinedType(SyntaxFactory.Token(boolKeyword));
        }

        private List<MethodDeclarationSyntax> methodsToAddToCurrentType = new List<MethodDeclarationSyntax>();
        private int lastId;
        private bool currentMethodIsStatic;
        private IEnumerable<VariableCapture> currentFlow;

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

        private ExpressionSyntax InlineOrCreateMethod(CSharpSyntaxNode body, ArgumentListSyntax arguments, ParameterSyntax arg, out string itemArg)
        {
            var fn = "ProceduralLinqHelper" + ++lastId;

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
                                    }.Union(currentFlow.Select(x => CreateParameter(x.Name, GetSymbolType(x)).WithRef(x.Changes)))
                                 )))
                                .WithBody(body as BlockSyntax ?? (body is StatementSyntax ? SyntaxFactory.Block((StatementSyntax)body) : SyntaxFactory.Block(SyntaxFactory.ReturnStatement((ExpressionSyntax)body))))
                                .WithStatic(currentMethodIsStatic)
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