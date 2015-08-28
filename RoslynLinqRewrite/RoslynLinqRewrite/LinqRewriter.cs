using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Rename;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace RoslynLinqRewrite
{
    internal class LinqRewriter : CSharpSyntaxRewriter
    {
        private SemanticModel semantic;

        private Project project;
        public LinqRewriter(Project project, SemanticModel semantic, DocumentId docid)
        {
            this.docid = docid;
            this.project = project;
            this.semantic = semantic;
        }
        static LinqRewriter()
        {

            KnownMethods = typeof(LinqRewriter).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(x => x.Name.EndsWith("Method") && x.FieldType == typeof(string))
                .Select(x => (string)x.GetValue(null))
                .ToList();
        }

        private readonly static List<string> KnownMethods;
        public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var methodIdx = methodsToAddToCurrentType.Count;
            try
            {
                var k = TryVisitInvocationExpression(node);
                if (k != null) return k;
            }
            catch (Exception ex) when (ex is InvalidCastException || ex is NotSupportedException)
            {
                methodsToAddToCurrentType = methodsToAddToCurrentType.Take(methodIdx).ToList();
            }
            return base.VisitInvocationExpression(node);
        }

        private SyntaxNode TryVisitInvocationExpression(InvocationExpressionSyntax node)
        {

            var memberAccess = node.Expression as MemberAccessExpressionSyntax;
            if (memberAccess != null)
            {
                var symbol = semantic.GetSymbolInfo(memberAccess).Symbol as IMethodSymbol;
                var owner = node.AncestorsAndSelf().FirstOrDefault(x => x is MethodDeclarationSyntax);
                if (owner == null) return null;
                currentMethodIsStatic = semantic.GetDeclaredSymbol((MethodDeclarationSyntax)owner).IsStatic;
                currentMethodTypeParameters = ((MethodDeclarationSyntax)owner).TypeParameterList;
                currentMethodConstraintClauses = ((MethodDeclarationSyntax)owner).ConstraintClauses;


                if (KnownMethods.Contains(GetMethodFullName(node)))
                {
                    var chain = new List<InvocationExpressionSyntax>();
                    chain.Add(node);
                    var c = node;
                    while (c.Expression is MemberAccessExpressionSyntax)
                    {
                        c = ((MemberAccessExpressionSyntax)c.Expression).Expression as InvocationExpressionSyntax;
                        if (c != null && KnownMethods.Contains(GetMethodFullName(c))) chain.Add(c);
                        else break;
                    }


                    var flowsIn = new List<ISymbol>();
                    var flowsOut = new List<ISymbol>();
                    foreach (var item in chain)
                    {
                        foreach (var arg in item.ArgumentList.Arguments)
                        {
                            var dataFlow = semantic.AnalyzeDataFlow(arg.Expression);
                            foreach (var k in dataFlow.DataFlowsIn)
                            {
                                if (!flowsIn.Contains(k)) flowsIn.Add(k);
                            }
                            foreach (var k in dataFlow.DataFlowsOut)
                            {
                                if (!flowsOut.Contains(k)) flowsOut.Add(k);
                            }

                        }
                    }

                    currentFlow = flowsIn
                        .Union(flowsOut)
                        .Where(x => (x as IParameterSymbol)?.IsThis != true)
                        .Select(x => new VariableCapture(x, flowsOut.Contains(x))) ?? Enumerable.Empty<VariableCapture>();




                    var collection = ((MemberAccessExpressionSyntax)chain.Last().Expression).Expression;

                    if (IsAnonymousType(semantic.GetTypeInfo(collection).Type)) return null;

                    var methodNames = Enumerable.Range(0, 5).Select(x => x < chain.Count ? GetMethodFullName(chain[x]) : null).ToList();


                    var semanticReturnType = semantic.GetTypeInfo(node).Type;
                    if (IsAnonymousType(semanticReturnType) || currentFlow.Any(x => IsAnonymousType(GetSymbolType(x.Symbol)))) return null;




                    var returnType = SyntaxFactory.ParseTypeName(semanticReturnType.ToDisplayString());

                    var aggregationMethod = methodNames[0];

                    if (aggregationMethod == WhereMethod || aggregationMethod == SelectMethod)
                    {
                        return RewriteAsLoop(
                            returnType,
                            Enumerable.Empty<StatementSyntax>(),
                            Enumerable.Empty<StatementSyntax>(),
                            collection,
                            chain,
                            (inv, arguments, param) =>
                            {
                                return SyntaxFactory.YieldStatement(SyntaxKind.YieldReturnStatement, SyntaxFactory.IdentifierName(param.Identifier.ValueText));
                            },
                            true
                        );
                    }

                    if (aggregationMethod == SumIntsMethod)
                    {
                        return RewriteAsLoop(
                            CreatePrimitiveType(SyntaxKind.IntKeyword),
                            new[] { CreateLocalVariableDeclaration("sum_", SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0))) },
                            new[] { SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName("sum_")) },
                            collection,
                            chain,
                            (inv, arguments, param) =>
                            {

                                return SyntaxFactory.ExpressionStatement(SyntaxFactory.AssignmentExpression(SyntaxKind.AddAssignmentExpression, SyntaxFactory.IdentifierName("sum_"), SyntaxFactory.IdentifierName(param.Identifier.ValueText)));
                            }
                        );
                    }
                    if (aggregationMethod == AnyWithConditionMethod)
                    {

                        return RewriteAsLoop(
                            CreatePrimitiveType(SyntaxKind.BoolKeyword),
                            Enumerable.Empty<StatementSyntax>(),
                            new[] { SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression)) },
                            collection,
                            chain,
                            (inv, arguments, param) =>
                            {
                                var lambda = (LambdaExpressionSyntax)inv.ArgumentList.Arguments.First().Expression;

                                return SyntaxFactory.IfStatement(InlineOrCreateMethod(lambda, CreatePrimitiveType(SyntaxKind.BoolKeyword), arguments, param),
                                 SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)
                                 ));
                            }
                        );
                    }
                    if (aggregationMethod == AnyMethod)
                    {

                        return RewriteAsLoop(
                            CreatePrimitiveType(SyntaxKind.BoolKeyword),
                            Enumerable.Empty<StatementSyntax>(),
                            new[] { SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression)) },
                            collection,
                            chain,
                            (inv, arguments, param) =>
                            {
                                return SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression));
                            }
                        );
                    }



                    if (aggregationMethod == FirstMethod)
                    {
                        return RewriteAsLoop(
                            SyntaxFactory.ParseTypeName(semantic.GetTypeInfo(node).ConvertedType.ToDisplayString()),
                            Enumerable.Empty<StatementSyntax>(),
                            new[] { CreateThrowException("System.InvalidOperationException", "The sequence did not contain any elements.") },
                            collection,
                            chain,
                            (inv, arguments, param) =>
                            {
                                return SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName(param.Identifier.ValueText));
                            }
                        );
                    }



                    if (aggregationMethod == FirstOrDefaultMethod)
                    {
                        return RewriteAsLoop(
                            returnType,
                            Enumerable.Empty<StatementSyntax>(),
                            new[] { SyntaxFactory.ReturnStatement(SyntaxFactory.DefaultExpression(returnType)) },
                            collection,
                            chain,
                            (inv, arguments, param) =>
                            {
                                return SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName(param.Identifier.ValueText));
                            }
                        );
                    }

                    if (aggregationMethod == LastOrDefaultMethod)
                    {
                        return RewriteAsLoop(
                            returnType,
                            new[] { CreateLocalVariableDeclaration("_last", SyntaxFactory.DefaultExpression(returnType)) },
                            new[] { SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName("_last")) },
                            collection,
                            chain,
                            (inv, arguments, param) =>
                            {
                                return SyntaxFactory.ExpressionStatement(SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, SyntaxFactory.IdentifierName("_last"), SyntaxFactory.IdentifierName(param.Identifier.ValueText)));
                            }
                        );
                    }


                    if (aggregationMethod == ToListMethod)
                    {
                        var listIdentifier = SyntaxFactory.IdentifierName("_list");
                        return RewriteAsLoop(
                            returnType,
                            new[] { CreateLocalVariableDeclaration("_list", SyntaxFactory.ObjectCreationExpression(returnType, CreateArguments(Enumerable.Empty<ArgumentSyntax>()), null)) },
                            new[] { SyntaxFactory.ReturnStatement(listIdentifier) },
                            collection,
                            chain,
                            (inv, arguments, param) =>
                            {
                                return CreateStatement(SyntaxFactory.InvocationExpression(SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, listIdentifier, SyntaxFactory.IdentifierName("Add")), CreateArguments(new[] { SyntaxFactory.IdentifierName(param.Identifier.ValueText) })));
                            }
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
            return null;
        }

        private StatementSyntax CreateStatement(ExpressionSyntax expression)
        {
            return SyntaxFactory.ExpressionStatement(expression);
        }



        private bool IsAnonymousType(ITypeSymbol t)
        {
            return (t.ToDisplayString().Contains("anonymous type:"));
        }

        private ThrowStatementSyntax CreateThrowException(string type, string message)
        {
            return SyntaxFactory.ThrowStatement(SyntaxFactory.ObjectCreationExpression(SyntaxFactory.ParseTypeName(type), CreateArguments(new[] { SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(message)) }), null));
        }

        private static ParameterSyntax GetLambdaParameter(AnonymousFunctionExpressionSyntax lambda, int index)
        {
            return
                (lambda as SimpleLambdaExpressionSyntax)?.Parameter ??
                (lambda as ParenthesizedLambdaExpressionSyntax)?.ParameterList.Parameters[index] ??
                (lambda as AnonymousMethodExpressionSyntax)?.ParameterList.Parameters[index];
        }

        private StatementSyntax CreateProcessingStep(List<InvocationExpressionSyntax> chain, int chainIndex, TypeSyntax itemType, string itemName, ArgumentListSyntax arguments, bool noAggregation)
        {

            if (chainIndex == 0 && !noAggregation || chainIndex == -1)
            {
                return currentAggregation(chain[0], arguments, CreateParameter(itemName, itemType));
            }

            var invocationExpressionSyntax = chain[chainIndex];


            var method = GetMethodFullName(invocationExpressionSyntax);



            if (method == WhereMethod)
            {
                var lambda = (AnonymousFunctionExpressionSyntax)invocationExpressionSyntax.ArgumentList.Arguments[0].Expression;

                var check = InlineOrCreateMethod(lambda, CreatePrimitiveType(SyntaxKind.BoolKeyword), arguments, CreateParameter(itemName, itemType));
                var next = CreateProcessingStep(chain, chainIndex - 1, itemType, itemName, arguments, noAggregation);
                return SyntaxFactory.IfStatement(check, next is BlockSyntax ? next : SyntaxFactory.Block(next));
            }


            if (method == SelectMethod)
            {
                var lambda = (LambdaExpressionSyntax)invocationExpressionSyntax.ArgumentList.Arguments[0].Expression;

                var newname = "gattone" + ++lastId;
                var lambdaType = (INamedTypeSymbol)semantic.GetTypeInfo(lambda).ConvertedType;
                var lambdaBodyType = lambdaType.TypeArguments.Last();
                var newtype = IsAnonymousType(lambdaBodyType) ? null : SyntaxFactory.ParseTypeName(lambdaBodyType.ToDisplayString());


                var local = CreateLocalVariableDeclaration(newname, InlineOrCreateMethod(lambda, newtype, arguments, CreateParameter(itemName, itemType)));


                var next = CreateProcessingStep(chain, chainIndex - 1, newtype, newname, arguments, noAggregation);
                var nexts = next is BlockSyntax ? ((BlockSyntax)next).Statements : (IEnumerable<StatementSyntax>)new[] { next };
                return SyntaxFactory.Block(new[] { local }.Concat(nexts));
            }



            throw new NotImplementedException();
        }

        private AnonymousFunctionExpressionSyntax RenameSymbol(AnonymousFunctionExpressionSyntax container, int argIndex, string newname)
        {
            var oldparameter = GetLambdaParameter(container, argIndex);
            var oldsymbol = semantic.GetDeclaredSymbol(oldparameter);
            var tokensToRename = container.DescendantNodesAndSelf().Where(x =>
            {
                var sem = semantic.GetSymbolInfo(x);
                if (sem.Symbol == oldsymbol) return true;
                return false;
            });
            return container.ReplaceNodes(tokensToRename, (a, b) =>
            {
                var ide = b as IdentifierNameSyntax;
                if (ide != null) return ide.WithIdentifier(SyntaxFactory.Identifier(newname));
                throw new NotImplementedException();
            });
            //var doc = project.GetDocument(docid);

            //var annot = new SyntaxAnnotation("RenamedLambda");
            //var annotated = container.WithAdditionalAnnotations(annot);
            //var root = project.GetDocument(docid).GetSyntaxRootAsync().Result.ReplaceNode(container, annotated).SyntaxTree;
            //var proj = project.GetDocument(docid).WithSyntaxRoot(root.GetRoot()).Project;
            //doc = proj.GetDocument(docid);
            //var syntaxTree = doc.GetSyntaxTreeAsync().Result;
            //var modifiedSemantic = proj.GetCompilationAsync().Result.GetSemanticModel(syntaxTree);
            //annotated = (AnonymousFunctionExpressionSyntax)doc.GetSyntaxRootAsync().Result.GetAnnotatedNodes(annot).First();
            //var parameter = GetLambdaParameter(annotated, 0);
            //var renamed = Renamer.RenameSymbolAsync(proj.Solution, modifiedSemantic.GetDeclaredSymbol(parameter), newname, null).Result;
            //annotated = (AnonymousFunctionExpressionSyntax)renamed.GetDocument(doc.Id).GetSyntaxRootAsync().Result.GetAnnotatedNodes(annot).First();
            //return annotated.WithoutAnnotations();
        }

        readonly static string ToListMethod = "System.Collections.Generic.IEnumerable<TSource>.ToList<TSource>()";
        readonly static string FirstMethod = "System.Collections.Generic.IEnumerable<TSource>.First<TSource>()";
        readonly static string FirstOrDefaultMethod = "System.Collections.Generic.IEnumerable<TSource>.FirstOrDefault<TSource>()";
        readonly static string LastOrDefaultMethod = "System.Collections.Generic.IEnumerable<TSource>.LastOrDefault<TSource>()";
        readonly static string AnyMethod = "System.Collections.Generic.IEnumerable<TSource>.Any<TSource>()";
        readonly static string AnyWithConditionMethod = "System.Collections.Generic.IEnumerable<TSource>.Any<TSource>(System.Func<TSource, bool>)";
        readonly static string SumWithSelectorMethod = "System.Collections.Generic.IEnumerable<TSource>.Sum<TSource>(System.Func<TSource, int>)";
        readonly static string SumIntsMethod = "System.Collections.Generic.IEnumerable<int>.Sum()";
        readonly static string WhereMethod = "System.Collections.Generic.IEnumerable<TSource>.Where<TSource>(System.Func<TSource, bool>)";
        readonly static string SelectMethod = "System.Collections.Generic.IEnumerable<TSource>.Select<TSource, TResult>(System.Func<TSource, TResult>)";


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

        delegate StatementSyntax AggregationDelegate(InvocationExpressionSyntax invocation, ArgumentListSyntax arguments, ParameterSyntax param);
        private AggregationDelegate currentAggregation;
        private ExpressionSyntax RewriteAsLoop(TypeSyntax returnType, IEnumerable<StatementSyntax> prologue, IEnumerable<StatementSyntax> epilogue, ExpressionSyntax collection, List<InvocationExpressionSyntax> chain, AggregationDelegate k, bool noaggregation = false)
        {
            var old = currentAggregation;
            currentAggregation = k;
            var parameters = new[] { CreateParameter(ItemsName, semantic.GetTypeInfo(collection).Type) }.Concat(currentFlow.Select(x => CreateParameter(x.Name, GetSymbolType(x.Symbol)).WithRef(x.Changes)));

            var collectionType = semantic.GetTypeInfo(collection).Type;
            var collectionItemType = collectionType is IArrayTypeSymbol ? ((IArrayTypeSymbol)collectionType).ElementType : collectionType.AllInterfaces.Concat(new[] { collectionType }).OfType<INamedTypeSymbol>().First(x => x.IsGenericType && x.ConstructUnboundGenericType().ToString() == "System.Collections.Generic.IEnumerable<>").TypeArguments.First();

            var functionName = "ProceduralLinq" + ++lastId;
            var arguments = CreateArguments(new[] { SyntaxFactory.Argument(SyntaxFactory.IdentifierName(ItemName)) }.Concat(currentFlow.Select(x => SyntaxFactory.Argument(SyntaxFactory.IdentifierName(x.Name)).WithRef(x.Changes))));

            var loopContent = CreateProcessingStep(chain, chain.Count - 1, SyntaxFactory.ParseTypeName(collectionItemType.ToDisplayString()), ItemName, arguments, noaggregation);
            var foreachStatement = SyntaxFactory.ForEachStatement(
                SyntaxFactory.IdentifierName("var"),
                ItemName,
                SyntaxFactory.IdentifierName(ItemsName),
                loopContent);
            var coreFunction = SyntaxFactory.MethodDeclaration(returnType, functionName)
                        .WithParameterList(CreateParameters(parameters))
                        .WithBody(SyntaxFactory.Block(prologue.Concat(new[] {
                            foreachStatement
                        }).Concat(epilogue)))
                        .WithStatic(currentMethodIsStatic)
                        .WithTypeParameterList(currentMethodTypeParameters)
                        .WithConstraintClauses(currentMethodConstraintClauses)
                        .NormalizeWhitespace();
            methodsToAddToCurrentType.Add(Tuple.Create(currentType, coreFunction));


            var inv = SyntaxFactory.InvocationExpression(GetMethodNameSyntaxWithCurrentTypeParameters(functionName), CreateArguments(new[] { SyntaxFactory.Argument((ExpressionSyntax)Visit(collection)) }.Concat(arguments.Arguments.Skip(1))));

            currentAggregation = old;
            return inv;
        }

        private static PredefinedTypeSyntax CreatePrimitiveType(SyntaxKind keyword)
        {
            return SyntaxFactory.PredefinedType(SyntaxFactory.Token(keyword));
        }

        private List<Tuple<TypeDeclarationSyntax, MethodDeclarationSyntax>> methodsToAddToCurrentType = new List<Tuple<TypeDeclarationSyntax, MethodDeclarationSyntax>>();
        private int lastId;
        private bool currentMethodIsStatic;
        private IEnumerable<VariableCapture> currentFlow;
        private DocumentId docid;

        public override SyntaxNode VisitStructDeclaration(StructDeclarationSyntax node)
        {
            return VisitTypeDeclaration(node);
        }

        private SyntaxNode VisitTypeDeclaration(TypeDeclarationSyntax node)
        {
            var old = currentType;
            currentType = node;
            var changed = (TypeDeclarationSyntax)(node is ClassDeclarationSyntax ? base.VisitClassDeclaration((ClassDeclarationSyntax)node) : base.VisitStructDeclaration((StructDeclarationSyntax)node));
            if (methodsToAddToCurrentType.Count != 0)
            {
                var newmembers = methodsToAddToCurrentType.Where(x => x.Item1 == currentType).Select(x => x.Item2).ToArray();
                var withMethods = changed is ClassDeclarationSyntax ? (TypeDeclarationSyntax)((ClassDeclarationSyntax)changed).AddMembers(newmembers) : ((StructDeclarationSyntax)changed).AddMembers(newmembers);
                methodsToAddToCurrentType.RemoveAll(x => x.Item1 == currentType);
                currentType = old;
                return withMethods.NormalizeWhitespace();
            }
            currentType = old;
            return changed;
        }

        private ExpressionSyntax InlineOrCreateMethod(AnonymousFunctionExpressionSyntax lambda, TypeSyntax returnType, ArgumentListSyntax arguments, ParameterSyntax param)
        {
            var lambdaParameter = semantic.GetDeclaredSymbol(GetLambdaParameter(lambda, 0));
            var currentFlow = semantic.AnalyzeDataFlow(lambda.Body);
            var currentCaptures = currentFlow
                .DataFlowsOut
                .Union(currentFlow.DataFlowsIn)
                .Where(x => x != lambdaParameter && (x as IParameterSymbol)?.IsThis != true)
                .Select(x => new VariableCapture(x, currentFlow.DataFlowsOut.Contains(x)))
                .ToList();
            lambda = RenameSymbol(lambda, 0, param.Identifier.ValueText);


            return InlineOrCreateMethod(lambda.Body, returnType, param, currentCaptures);
        }

        private ExpressionSyntax InlineOrCreateMethod(CSharpSyntaxNode body, TypeSyntax returnType, ParameterSyntax param, IEnumerable<VariableCapture> captures)
        {

            var fn = "ProceduralLinqHelper" + ++lastId;

            if (body is ExpressionSyntax && true)
            {
                return (ExpressionSyntax)body;
            }
            else
            {
                if (captures.Any(x => IsAnonymousType(GetSymbolType(x.Symbol)))) throw new NotSupportedException();
                if (returnType == null) throw new NotSupportedException(); // Anonymous type
                var method = SyntaxFactory.MethodDeclaration(returnType, fn)
                                .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(
                                    new[] {
                                    param
                                    }.Union(captures.Select(x => CreateParameter(x.Name, GetSymbolType(x)).WithRef(x.Changes)))
                                 )))
                                .WithBody(body as BlockSyntax ?? (body is StatementSyntax ? SyntaxFactory.Block((StatementSyntax)body) : SyntaxFactory.Block(SyntaxFactory.ReturnStatement((ExpressionSyntax)body))))
                                .WithStatic(currentMethodIsStatic)
                                .WithTypeParameterList(currentMethodTypeParameters)
                                .WithConstraintClauses(currentMethodConstraintClauses)
                                .NormalizeWhitespace();

                methodsToAddToCurrentType.Add(Tuple.Create(currentType, method));


                return SyntaxFactory.InvocationExpression(GetMethodNameSyntaxWithCurrentTypeParameters(fn), CreateArguments(new[] { SyntaxFactory.Argument(SyntaxFactory.IdentifierName(param.Identifier.ValueText)) }.Union(captures.Select(x => SyntaxFactory.Argument(SyntaxFactory.IdentifierName(x.Name)).WithRef(x.Changes)))));
            }
        }

        private ExpressionSyntax GetMethodNameSyntaxWithCurrentTypeParameters(string fn)
        {
            return (currentMethodTypeParameters?.Parameters.Count).GetValueOrDefault() != 0 ? SyntaxFactory.GenericName(SyntaxFactory.Identifier(fn), SyntaxFactory.TypeArgumentList(CreateSeparatedList(currentMethodTypeParameters.Parameters.Select(x => SyntaxFactory.ParseTypeName(x.Identifier.ValueText))))) : (NameSyntax)SyntaxFactory.IdentifierName(fn);
        }

        private TypeDeclarationSyntax currentType;
        private TypeParameterListSyntax currentMethodTypeParameters;
        private SyntaxList<TypeParameterConstraintClauseSyntax> currentMethodConstraintClauses;

        public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            return VisitTypeDeclaration(node);
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
        private static ParameterSyntax CreateParameter(SyntaxToken name, TypeSyntax type)
        {
            return SyntaxFactory.Parameter(name).WithType(type);
        }
        private static ParameterSyntax CreateParameter(string name, ITypeSymbol type)
        {
            return CreateParameter(SyntaxFactory.Identifier(name), type);
        }
        private static ParameterSyntax CreateParameter(string name, TypeSyntax type)
        {
            return CreateParameter(SyntaxFactory.Identifier(name), type);
        }


    }


}