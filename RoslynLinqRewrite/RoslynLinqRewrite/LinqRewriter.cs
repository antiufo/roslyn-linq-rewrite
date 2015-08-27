using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Rename;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RoslynLinqRewrite
{
    internal class LinqRewriter : CSharpSyntaxRewriter
    {
        private SemanticModel semantic;

        private Project project;
        public LinqRewriter(Project project, SemanticModel semantic)
        {
            this.project = project;
            this.semantic = semantic;
        }



        private delegate bool IsMethodSequenceDelegate(
            string p0,
            string p1 = null,
            string p2 = null,
            string p3 = null,
            string p4 = null,
            string p5 = null
            );
        public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
        {

            var memberAccess = node.Expression as MemberAccessExpressionSyntax;
            if (memberAccess != null)
            {
                var symbol = semantic.GetSymbolInfo(memberAccess).Symbol as IMethodSymbol;
                currentMethodIsStatic = semantic.GetDeclaredSymbol(node.FirstAncestorOrSelf<MethodDeclarationSyntax>()).IsStatic;

                if (symbol.ContainingType.ToString() == "System.Linq.Enumerable")
                {
                    //var lambda = node.ArgumentList.Arguments.FirstOrDefault()?.Expression as LambdaExpressionSyntax;
                    //var arg = (lambda as SimpleLambdaExpressionSyntax)?.Parameter ?? (lambda as ParenthesizedLambdaExpressionSyntax)?.ParameterList.Parameters.FirstOrDefault();
                    var collection = ((MemberAccessExpressionSyntax)node.Expression).Expression;
                    var collectionType = semantic.GetTypeInfo(collection).Type;
                    var firstArg = node.ArgumentList.Arguments.FirstOrDefault()?.Expression;
                    var itemType = collectionType is IArrayTypeSymbol ? ((IArrayTypeSymbol)collectionType).ElementType : collectionType.AllInterfaces.Concat(new[] { collectionType }).OfType<INamedTypeSymbol>().First(x => x.IsGenericType && x.ConstructUnboundGenericType().ToString() == "System.Collections.Generic.IEnumerable<>").TypeArguments.First();
                    var dataFlow = semantic.AnalyzeDataFlow(node);
                    var chain = new List<InvocationExpressionSyntax>();
                    chain.Add(node);
                    var c = node;
                    while (c.Expression is MemberAccessExpressionSyntax)
                    {
                        c = ((MemberAccessExpressionSyntax)c.Expression).Expression as InvocationExpressionSyntax;
                        if (c != null) chain.Add(c);
                        else break;
                    }


                    var methodNames = Enumerable.Range(0, 5).Select(x => x < chain.Count ? GetMethodFullName(chain[x]) : null).ToList();
                    IsMethodSequenceDelegate IsMethodSequence = (p0, p1, p2, p3, p4, p5) =>
                    {
                        if (p0 != null && methodNames[0] != p0) return false;
                        if (p1 != null && methodNames[1] != p1) return false;
                        if (p2 != null && methodNames[2] != p2) return false;
                        if (p3 != null && methodNames[3] != p3) return false;
                        if (p4 != null && methodNames[4] != p4) return false;
                        if (p5 != null && methodNames[5] != p5) return false;

                        return true;
                    };
                    currentFlow = dataFlow?.Captured.Select(x => new VariableCapture(x, dataFlow.WrittenInside.Contains(x))) ?? Enumerable.Empty<VariableCapture>();



                    var aggregationMethod = methodNames[0];


                    if (aggregationMethod == SumIntsMethod)
                    {
                        return RewriteAsLoop(
                            CreatePrimitiveType(SyntaxKind.IntKeyword),
                            new[] { CreateLocalVariableDeclaration("sum_", SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0))) },
                            (itemName, arguments) =>
                            {
                                return CreateProcessingStep(chain, chain.Count - 1, itemType, ItemName, arguments,
                                    x => SyntaxFactory.ExpressionStatement(SyntaxFactory.AssignmentExpression(SyntaxKind.AddAssignmentExpression, SyntaxFactory.IdentifierName("sum_"), x))
                                    );
                            },
                            new[] { SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName("sum_")) },
                            collection
                        );
                    }
                    if (aggregationMethod == AnyWithConditionMethod)
                    {

                        return RewriteAsLoop(
                            CreatePrimitiveType(SyntaxKind.BoolKeyword),
                            Enumerable.Empty<StatementSyntax>(),
                            (itemName, arguments) => CreateProcessingStep(chain, chain.Count - 1, itemType, ItemName, arguments, x =>
                            {
                                var lambda = RenameSymbol((LambdaExpressionSyntax)firstArg, 0, itemName);
                                return SyntaxFactory.IfStatement(InlineOrCreateMethod((CSharpSyntaxNode)Visit(lambda.Body), arguments, CreateParameter(itemName, itemType)),
                                 SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)
                                 ));
                            }), 
                            new[] { SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression)) },
                            collection
                        );
                    }


#if false


                    if (IsMethodSequence(SumWithSelectorMethod, WhereMethod))
                    {
                        string itemArg = null;
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
#endif
                }
            }

            return base.VisitInvocationExpression(node);
        }

        private StatementSyntax CreateProcessingStep(List<InvocationExpressionSyntax> chain, int chainIndex, ITypeSymbol itemType, string itemName, ArgumentListSyntax arguments, Func<ExpressionSyntax, StatementSyntax> finalStatement)
        {
            var invocationExpressionSyntax = chain[chainIndex];
            var method = GetMethodFullName(invocationExpressionSyntax);


            if (method == WhereMethod)
            {
                var lambda = (LambdaExpressionSyntax)invocationExpressionSyntax.ArgumentList.Arguments[0].Expression;

                lambda = RenameSymbol(lambda, 0, itemName);
                var check = InlineOrCreateMethod(lambda.Body, arguments, CreateParameter(itemName, itemType));
                var next = chainIndex == 1 ? finalStatement(SyntaxFactory.IdentifierName(itemName)) : CreateProcessingStep(chain, chainIndex - 1, itemType, itemName, arguments, finalStatement);
                return SyntaxFactory.IfStatement(check, next is BlockSyntax ? next : SyntaxFactory.Block(next));
            }


            if (method == SelectMethod)
            {
                var lambda = (LambdaExpressionSyntax)invocationExpressionSyntax.ArgumentList.Arguments[0].Expression;

                var newname = "gattone" + ++lastId;
                lambda = RenameSymbol(lambda, 0, itemName);
                var local = CreateLocalVariableDeclaration(newname, InlineOrCreateMethod(lambda.Body, arguments, CreateParameter(itemName, itemType)));


                var next = chainIndex == 1 ? finalStatement(SyntaxFactory.IdentifierName(newname)) : CreateProcessingStep(chain, chainIndex - 1, itemType, newname, arguments, finalStatement);
                var nexts = next is BlockSyntax ? ((BlockSyntax)next).Statements : (IEnumerable<StatementSyntax>)new[] { next };
                return SyntaxFactory.Block(new[] { local }.Concat(nexts));
            }



            throw new NotImplementedException();
        }

        private LambdaExpressionSyntax RenameSymbol(LambdaExpressionSyntax container, int argIndex, string newname)
        {

            var doc = project.Documents.Single();
            var docid = doc.Id;

            var annot = new SyntaxAnnotation("RenamedLambda");
            var annotated = container.WithAdditionalAnnotations(annot);
            var root = project.GetDocument(docid).GetSyntaxRootAsync().Result.ReplaceNode(container, annotated).SyntaxTree;
            var proj = project.GetDocument(docid).WithSyntaxRoot(root.GetRoot()).Project;
            doc = proj.GetDocument(docid);
            var syntaxTree = doc.GetSyntaxTreeAsync().Result;
            var modifiedSemantic = proj.GetCompilationAsync().Result.GetSemanticModel(syntaxTree);
            annotated = (LambdaExpressionSyntax)doc.GetSyntaxRootAsync().Result.GetAnnotatedNodes(annot).First();
            var parameter = (annotated as SimpleLambdaExpressionSyntax)?.Parameter ?? ((ParenthesizedLambdaExpressionSyntax)annotated).ParameterList.Parameters[argIndex];
            var renamed = Renamer.RenameSymbolAsync(proj.Solution, modifiedSemantic.GetDeclaredSymbol(parameter), newname, null).Result;
            //var renamed =Renamer.RenameSymbolAsync(project. RenameSymbolAsync(project.Documents.Single(), lambda, parameters.First().Identifier, "gattone", CancellationToken.None).Result;
            annotated = (LambdaExpressionSyntax)renamed.GetDocument(doc.Id).GetSyntaxRootAsync().Result.GetAnnotatedNodes(annot).First();
            return annotated.WithoutAnnotations();
        }

        readonly static string AnyWithConditionMethod = "System.Collections.Generic.IEnumerable<TSource>.Any<TSource>(System.Func<TSource, bool>)";
        readonly static string SumWithSelectorMethod = "System.Collections.Generic.IEnumerable<TSource>.Sum<TSource>(System.Func<TSource, int>)";
        readonly static string SumIntsMethod = "System.Collections.Generic.IEnumerable<int>.Sum()";
        readonly static string WhereMethod = "System.Collections.Generic.IEnumerable<TSource>.Where<TSource>(System.Func<TSource, bool>)";
        readonly static string SelectMethod = "System.Collections.Generic.IEnumerable<TSource>.Select<TSource, TResult>(System.Func<TSource, TResult>)";

        //public static async Task<Solution> RenameSymbolAsync(Document document, SyntaxNode root, SyntaxToken declarationToken, string newName, CancellationToken cancellationToken)
        //{
        //    var annotation = RenameAnnotation.Create();
        //    var annotatedRoot = root.ReplaceToken(declarationToken, declarationToken.WithAdditionalAnnotations());
        //    var annotatedSolution = document.Project.Solution.WithDocumentSyntaxRoot(document.Id, annotatedRoot);
        //    var annotatedDocument = annotatedSolution.GetDocument(document.Id);

        //    annotatedRoot = await annotatedDocument.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        //    var annotatedToken = annotatedRoot.FindToken(declarationToken.SpanStart);

        //    var semanticModel = await annotatedDocument.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        //    var symbol = semanticModel?.GetDeclaredSymbol(annotatedToken.Parent, cancellationToken);

        //    var newSolution = await Renamer.RenameSymbolAsync(annotatedSolution, symbol, newName, null, cancellationToken).ConfigureAwait(false);
        //    return newSolution;
        //}

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



        private ExpressionSyntax RewriteAsLoop(TypeSyntax returnType, IEnumerable<StatementSyntax> prologue, Func<string, ArgumentListSyntax, StatementSyntax> loopBody, IEnumerable<StatementSyntax> epilogue, ExpressionSyntax collection)
        {
            var parameters = new[] { CreateParameter(ItemsName, semantic.GetTypeInfo(collection).Type) }.Concat(currentFlow.Select(x => CreateParameter(x.Name, GetSymbolType(x.Symbol)).WithRef(x.Changes)));

            var functionName = "ProceduralLinq" + ++lastId;
            var arguments = CreateArguments(new[] { SyntaxFactory.Argument(SyntaxFactory.IdentifierName(ItemName)) }.Concat(currentFlow.Select(x => SyntaxFactory.Argument(SyntaxFactory.IdentifierName(x.Name)).WithRef(x.Changes))));

            var body = loopBody("aerr", arguments);
            var loop = SyntaxFactory.ForEachStatement(
                SyntaxFactory.IdentifierName("var"),
                ItemName,
                SyntaxFactory.IdentifierName(ItemsName),
                SyntaxFactory.Block(body));
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

        private ExpressionSyntax InlineOrCreateMethod(CSharpSyntaxNode body, ArgumentListSyntax arguments, ParameterSyntax arg)
        {
            var fn = "ProceduralLinqHelper" + ++lastId;

            if (body is ExpressionSyntax)
            {
                return (ExpressionSyntax)body;
            }
            else
            {
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