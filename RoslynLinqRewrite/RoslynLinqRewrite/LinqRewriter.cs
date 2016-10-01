using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Shaman.Roslyn.LinqRewrite
{
    public partial class LinqRewriter : CSharpSyntaxRewriter
    {
        private SemanticModel semantic;

        
        public LinqRewriter(SemanticModel semantic)
        {
            this.semantic = semantic;
        }
        public int RewrittenMethods { get; private set; }
        public int RewrittenLinqQueries { get; private set; }
        static LinqRewriter()
        {

            KnownMethods = typeof(LinqRewriter).GetTypeInfo().DeclaredFields
                .Where(x => x.Name.EndsWith("Method") && x.FieldType == typeof(string))
                .Select(x => (string)x.GetValue(null))
                .ToList();
        }

        private readonly static List<string> KnownMethods;
        public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            return TryCatchVisitInvocationExpression(node, null) ?? base.VisitInvocationExpression(node);
        }

        private bool insideConditionalExpression;
        public override SyntaxNode VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node)
        {
            var old = insideConditionalExpression;
            insideConditionalExpression = true;
            try
            {
                return base.VisitConditionalAccessExpression(node);
            }
            finally
            {
                insideConditionalExpression = old;
            }
            
        }

        private ExpressionSyntax TryCatchVisitInvocationExpression(InvocationExpressionSyntax node, ForEachStatementSyntax containingForEach)
        {
            if (insideConditionalExpression) return null;
            var methodIdx = methodsToAddToCurrentType.Count;
            try
            {
                var k = TryVisitInvocationExpression(node, containingForEach);
                if (k != null)
                {
                    RewrittenLinqQueries++;
                    return k;
                }
            }
            catch (Exception ex) when (ex is InvalidCastException || ex is NotSupportedException)
            {
                methodsToAddToCurrentType.RemoveRange(methodIdx, methodsToAddToCurrentType.Count - methodIdx);
            }
            return null;
        }

        private ExpressionSyntax TryVisitInvocationExpression(InvocationExpressionSyntax node, ForEachStatementSyntax containingForEach)
        {

            var memberAccess = node.Expression as MemberAccessExpressionSyntax;
            if (memberAccess != null)
            {
                var symbol = semantic.GetSymbolInfo(memberAccess).Symbol as IMethodSymbol;
                var owner = node.AncestorsAndSelf().FirstOrDefault(x => x is MethodDeclarationSyntax);
                if (owner == null) return null;
                currentMethodIsStatic = semantic.GetDeclaredSymbol((MethodDeclarationSyntax)owner).IsStatic;
                currentMethodName = ((MethodDeclarationSyntax)owner).Identifier.ValueText;
                currentMethodTypeParameters = ((MethodDeclarationSyntax)owner).TypeParameterList;
                currentMethodConstraintClauses = ((MethodDeclarationSyntax)owner).ConstraintClauses;

          
                if (IsSupportedMethod(node))
                {
                    var chain = new List<LinqStep>();
                    chain.Add(new LinqStep(GetMethodFullName(node), node.ArgumentList.Arguments.Select(x => x.Expression).ToList(), node));
                    var c = node;
                    var lastNode = node;
                    while (c.Expression is MemberAccessExpressionSyntax)
                    {
                        c = ((MemberAccessExpressionSyntax)c.Expression).Expression as InvocationExpressionSyntax;
                        if (c != null && IsSupportedMethod(c))
                        {
                            chain.Add(new LinqStep(GetMethodFullName(c), c.ArgumentList.Arguments.Select(x => x.Expression).ToList(), c));
                            lastNode = c;
                        }
                        else break;
                    }
                    if (containingForEach != null)
                    {
                        chain.Insert(0, new LinqStep(IEnumerableForEachMethod, new[] { SyntaxFactory.SimpleLambdaExpression(SyntaxFactory.Parameter(containingForEach.Identifier), containingForEach.Statement) })
                        {
                            Lambda = new Lambda(containingForEach.Statement, new[] { CreateParameter(containingForEach.Identifier, semantic.GetTypeInfo(containingForEach.Type).ConvertedType) })
                        });
                    }
                    if (!chain.Any(x => x.Arguments.Any(y => y is AnonymousFunctionExpressionSyntax))) return null;
                    if (chain.Count == 1 && RootMethodsThatRequireYieldReturn.Contains(chain[0].MethodName)) return null;


                    var flowsIn = new List<ISymbol>();
                    var flowsOut = new List<ISymbol>();
                    foreach (var item in chain)
                    {
                        foreach (var arg in item.Arguments)
                        {
                            if (item.Lambda != null)
                            {
                                var dataFlow = semantic.AnalyzeDataFlow(item.Lambda.Body);
                                var pname = item.Lambda.Parameters.Single().Identifier.ValueText;
                                foreach (var k in dataFlow.DataFlowsIn)
                                {
                                    if (k.Name == pname) continue;
                                    if (!flowsIn.Contains(k)) flowsIn.Add(k);
                                }
                                foreach (var k in dataFlow.DataFlowsOut)
                                {
                                    if (k.Name == pname) continue;
                                    if (!flowsOut.Contains(k)) flowsOut.Add(k);
                                }
                            }
                            else
                            {
                                var dataFlow = semantic.AnalyzeDataFlow(arg);
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
                    }

                    currentFlow = flowsIn
                        .Union(flowsOut)
                        .Where(x => (x as IParameterSymbol)?.IsThis != true)
                        .Select(x => CreateVariableCapture(x, flowsOut)) ?? Enumerable.Empty<VariableCapture>();

                    var collection = ((MemberAccessExpressionSyntax)lastNode.Expression).Expression;

                    if (IsAnonymousType(semantic.GetTypeInfo(collection).Type)) return null;


                    var semanticReturnType = semantic.GetTypeInfo(node).Type;
                    if (IsAnonymousType(semanticReturnType) || currentFlow.Any(x => IsAnonymousType(GetSymbolType(x.Symbol)))) return null;






                    return TryRewrite(chain.First().MethodName, collection, semanticReturnType, chain, node)
                        .WithLeadingTrivia(((CSharpSyntaxNode)containingForEach ?? node).GetLeadingTrivia())
                        .WithTrailingTrivia(((CSharpSyntaxNode)containingForEach ?? node).GetTrailingTrivia());

                }
            }
            return null;
        }

        private VariableCapture CreateVariableCapture(ISymbol symbol, IReadOnlyList<ISymbol> flowsOut)
        {
            var changes = flowsOut.Contains(symbol);
            if (!changes)
            {
                var local = symbol as ILocalSymbol;
                if (local != null)
                {
                    var type = local.Type;
                    if (type.IsValueType)
                    {
                        // Pass big structs by ref for performance.
                        var size = GetStructSize(type);
                        if (size > MaximumSizeForByValStruct)
                            changes = true;
                    }
                }
            }
            return new LinqRewrite.LinqRewriter.VariableCapture(symbol, changes);
        }

        private const int MaximumSizeForByValStruct = 128 / 8; // eg. two longs, or two references

        private Dictionary<ITypeSymbol, int> structSizeCache = new Dictionary<ITypeSymbol, int>();

        private int GetStructSize(ITypeSymbol type)
        {
            
            switch (type.SpecialType)
            {
                case SpecialType.System_Boolean: return 4;
                case SpecialType.System_Char: return 2;
                case SpecialType.System_SByte: return 1;
                case SpecialType.System_Byte: return 1;
                case SpecialType.System_Int16: return 2;
                case SpecialType.System_UInt16: return 2;
                case SpecialType.System_Int32: return 4;
                case SpecialType.System_UInt32: return 4;
                case SpecialType.System_Int64: return 8;
                case SpecialType.System_UInt64: return 8;
                case SpecialType.System_Single: return 4;
                case SpecialType.System_Double: return 8;
                case SpecialType.System_IntPtr: return 8;
                case SpecialType.System_UIntPtr: return 8;
                default: break;
            }
            int size;
            if (structSizeCache.TryGetValue(type, out size)) return size;


            size = 0;
            foreach (var item in type.GetMembers())
            {
                if (item.Kind == SymbolKind.Field && !item.IsStatic)
                {
                    var field = (IFieldSymbol)item;
                    if (field.Type.IsValueType)
                    {
                        if (field.Type == type)
                        {
                            // This is a primitive-like type, it "contains" itself.
                            // An unknown one, since we already ruled out some well know ones above.
                            size = 8;
                            break;
                        }
                        size += GetStructSize(field.Type);
                    }
                    else
                    {
                        size += 64 / 8;
                    }
                }
            }
            structSizeCache[type] = size;
            return size;

        }

        private bool IsSupportedMethod(InvocationExpressionSyntax invocation)
        {
            var name = GetMethodFullName(invocation);
            if (!IsSupportedMethod(name)) return false;
            if (invocation.ArgumentList.Arguments.Count != 0)
            {
                if (name != ElementAtMethod && name != ElementAtOrDefaultMethod && name != ContainsMethod)
                {
                    // Passing things like .Select(Method) is not supported.
                    if (invocation.ArgumentList.Arguments.Any(x => !(x.Expression is AnonymousFunctionExpressionSyntax)))
                        return false;
                }
            }
            return true;
        }

        private bool IsSupportedMethod(string v)
        {
            if (v == null) return false;
            if (KnownMethods.Contains(v)) return true;
            if (!v.StartsWith("System.Collections.Generic.IEnumerable<")) return false;
            var k = v.Replace("<", "(");
            if (!k.Contains(">.Sum(") && !k.Contains(">.Average(") && !k.Contains(">.Min(") && !k.Contains(">.Max(")) return false;
            if (k.Contains("TResult")) return false;
            if (v == "System.Collections.Generic.IEnumerable<TSource>.Min()") return false;
            if (v == "System.Collections.Generic.IEnumerable<TSource>.Max()") return false;
            return true;
        }

        private StatementSyntax CreateStatement(ExpressionSyntax expression)
        {
            return SyntaxFactory.ExpressionStatement(expression);
        }

        public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            if (HasNoRewriteAttribute(node.AttributeLists)) return node;
            var old = RewrittenLinqQueries;
            var k = base.VisitMethodDeclaration(node);
            if (RewrittenLinqQueries != old)
            {
                RewrittenMethods++;
            }
            return k;
        }

        private bool HasNoRewriteAttribute(SyntaxList<AttributeListSyntax> attributeLists)
        {
            return attributeLists.Any(x => x.Attributes.Any(y =>
            {
                var symbol = ((IMethodSymbol)semantic.GetSymbolInfo(y).Symbol).ContainingType;
                return symbol.ToDisplayString() == "Shaman.Runtime.NoLinqRewriteAttribute";
            }));
        }

        private bool IsAnonymousType(ITypeSymbol t)
        {
            return (t.ToDisplayString().Contains("anonymous type:"));
        }

        private ThrowStatementSyntax CreateThrowException(string type, string message = null)
        {
            return SyntaxFactory.ThrowStatement(SyntaxFactory.ObjectCreationExpression(SyntaxFactory.ParseTypeName(type), CreateArguments(message!=null? new[] { SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(message)) } :  new ExpressionSyntax[] { }), null));
        }

        private static ParameterSyntax GetLambdaParameter(Lambda lambda, int index)
        {
            return lambda.Parameters[index];
        }

        private ITypeSymbol GetLambdaReturnType(AnonymousFunctionExpressionSyntax lambda)
        {
            var symbol = ((INamedTypeSymbol)semantic.GetTypeInfo(lambda).ConvertedType).TypeArguments.Last();
            return symbol;
        }



        private Lambda RenameSymbol(Lambda container, int argIndex, string newname)
        {
            var oldparameter = GetLambdaParameter(container, argIndex).Identifier.ValueText;
            //var oldsymbol = semantic.GetDeclaredSymbol(oldparameter);
            var tokensToRename = container.Body.DescendantNodesAndSelf().Where(x =>
            {
                var sem = semantic.GetSymbolInfo(x).Symbol;
                if (sem != null && (sem is ILocalSymbol || sem is IParameterSymbol) && sem.Name == oldparameter) return true;
                //  if (sem.Symbol == oldsymbol) return true;
                return false;
            });
            var syntax = SyntaxFactory.ParenthesizedLambdaExpression(CreateParameters(container.Parameters.Select((x, i) => i == argIndex ? SyntaxFactory.Parameter(SyntaxFactory.Identifier(newname)).WithType(x.Type) : x)), container.Body.ReplaceNodes(tokensToRename, (a, b) =>
                 {
                     var ide = b as IdentifierNameSyntax;
                     if (ide != null) return ide.WithIdentifier(SyntaxFactory.Identifier(newname));
                     throw new NotImplementedException();
                 }));
            return new Lambda(syntax);
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



        ITypeSymbol GetSymbolType(VariableCapture x)
        {
            return GetSymbolType(x.Symbol);
        }

        private string GetMethodFullName(InvocationExpressionSyntax invocation)
        {
            
            var n = (semantic.GetSymbolInfo(invocation.Expression).Symbol as IMethodSymbol)?.OriginalDefinition.ToDisplayString();
            const string ienumerableOfTsource = "System.Collections.Generic.IEnumerable<TSource>";
            n = n
                ?.Replace("System.Collections.Generic.List<TSource>", ienumerableOfTsource)
                .Replace("TSource[]", ienumerableOfTsource);
                    
            return n;
        }

        const string ItemsName = "_linqitems";
        const string ItemName = "_linqitem";
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

        delegate StatementSyntax AggregationDelegate(LinqStep invocation, ArgumentListSyntax arguments, ParameterSyntax param);
        private AggregationDelegate currentAggregation;
        private ExpressionSyntax RewriteAsLoop(TypeSyntax returnType, IEnumerable<StatementSyntax> prologue, IEnumerable<StatementSyntax> epilogue, ExpressionSyntax collection, List<LinqStep> chain, AggregationDelegate k, bool noaggregation = false, IEnumerable<Tuple<ParameterSyntax, ExpressionSyntax>> additionalParameters = null)
        {
            var old = currentAggregation;
            currentAggregation = k;

            var collectionType = semantic.GetTypeInfo(collection).Type;
            var collectionItemType = GetItemType(collectionType);
            if (collectionItemType == null) throw new NotSupportedException();
            var collectionSemanticType = semantic.GetTypeInfo(collection).Type;

            var parameters =  new[] { CreateParameter(ItemsName, collectionSemanticType) }.Concat(currentFlow.Select(x => CreateParameter(x.Name, GetSymbolType(x.Symbol)).WithRef(x.Changes)));
            if (additionalParameters != null) parameters = parameters.Concat(additionalParameters.Select(x => x.Item1));

            

            var functionName = GetUniqueName(currentMethodName + "_ProceduralLinq");
            var arguments = CreateArguments(new[] { SyntaxFactory.Argument(SyntaxFactory.IdentifierName(ItemName)) }.Concat(currentFlow.Select(x => SyntaxFactory.Argument(SyntaxFactory.IdentifierName(x.Name)).WithRef(x.Changes))));

            var loopContent = CreateProcessingStep(chain, chain.Count - 1, SyntaxFactory.ParseTypeName(collectionItemType.ToDisplayString()), ItemName, arguments, noaggregation);

            StatementSyntax foreachStatement;
            if (collectionType.ToDisplayString().StartsWith("System.Collections.Generic.List<") || collectionType is IArrayTypeSymbol)
            {

                foreachStatement = SyntaxFactory.ForStatement(
                    SyntaxFactory.VariableDeclaration(CreatePrimitiveType(SyntaxKind.IntKeyword), CreateSeparatedList<VariableDeclaratorSyntax>(SyntaxFactory.VariableDeclarator("_index").WithInitializer(SyntaxFactory.EqualsValueClause(SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0)))))), default(SeparatedSyntaxList<ExpressionSyntax>),
                    SyntaxFactory.BinaryExpression(SyntaxKind.LessThanExpression, SyntaxFactory.IdentifierName("_index"), GetCollectionCount(collection, false)), CreateSeparatedList<ExpressionSyntax>(SyntaxFactory.PostfixUnaryExpression(SyntaxKind.PostIncrementExpression, SyntaxFactory.IdentifierName("_index"))),
                    SyntaxFactory.Block(new StatementSyntax[] { CreateLocalVariableDeclaration(ItemName, SyntaxFactory.ElementAccessExpression(SyntaxFactory.IdentifierName(ItemsName), SyntaxFactory.BracketedArgumentList(CreateSeparatedList(SyntaxFactory.Argument(SyntaxFactory.IdentifierName("_index")))))) }.Union((loopContent as BlockSyntax)?.Statements ?? (IEnumerable<StatementSyntax>)new[] { loopContent })));
            }
            else
            {
                foreachStatement = SyntaxFactory.ForEachStatement(
                SyntaxFactory.IdentifierName("var"),
                ItemName,
                SyntaxFactory.IdentifierName(ItemsName),
                loopContent is BlockSyntax ? loopContent : SyntaxFactory.Block(loopContent));

            }



            var coreFunction = SyntaxFactory.MethodDeclaration(returnType, functionName)
                        .WithParameterList(CreateParameters(parameters))
                        .WithBody(SyntaxFactory.Block((collectionSemanticType.IsValueType ? Enumerable.Empty<StatementSyntax>() : new[] {
                            SyntaxFactory.IfStatement(SyntaxFactory.BinaryExpression(SyntaxKind.EqualsExpression, SyntaxFactory.IdentifierName(ItemsName) ,SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)), CreateThrowException("System.ArgumentNullException"))
                        }).Concat(prologue).Concat(new[] {
                            foreachStatement
                        }).Concat(epilogue)))
                        .WithStatic(currentMethodIsStatic)
                        .WithTypeParameterList(currentMethodTypeParameters)
                        .WithConstraintClauses(currentMethodConstraintClauses)
                        .NormalizeWhitespace();
            methodsToAddToCurrentType.Add(Tuple.Create(currentType, coreFunction));

            IEnumerable<ArgumentSyntax> args = new[] { SyntaxFactory.Argument((ExpressionSyntax)Visit(collection)) }.Concat(arguments.Arguments.Skip(1));
            if (additionalParameters != null) args = args.Concat(additionalParameters.Select(x => SyntaxFactory.Argument(x.Item2)));
            var inv = SyntaxFactory.InvocationExpression(GetMethodNameSyntaxWithCurrentTypeParameters(functionName), CreateArguments(args));

            currentAggregation = old;
            return inv;
        }

        private string GetUniqueName(string v)
        {
            for (int i = 1; ; i++)
            {
                var name = v + i;
                if (methodsToAddToCurrentType.Any(x => x.Item2.Identifier.ValueText == name)) continue;
                return name;
            }
        }

        private ITypeSymbol GetItemType(ITypeSymbol collectionType)
        {
            return collectionType is IArrayTypeSymbol ? ((IArrayTypeSymbol)collectionType).ElementType : collectionType.AllInterfaces.Concat(new[] { collectionType }).OfType<INamedTypeSymbol>().FirstOrDefault(x => x.IsGenericType && x.ConstructUnboundGenericType().ToString() == "System.Collections.Generic.IEnumerable<>")?.TypeArguments.First();
        }

        private static PredefinedTypeSyntax CreatePrimitiveType(SyntaxKind keyword)
        {
            return SyntaxFactory.PredefinedType(SyntaxFactory.Token(keyword));
        }

        private List<Tuple<TypeDeclarationSyntax, MethodDeclarationSyntax>> methodsToAddToCurrentType = new List<Tuple<TypeDeclarationSyntax, MethodDeclarationSyntax>>();
        private int lastId;
        private bool currentMethodIsStatic;
        private string currentMethodName;
        private IEnumerable<VariableCapture> currentFlow;
        

        public override SyntaxNode VisitStructDeclaration(StructDeclarationSyntax node)
        {
            return VisitTypeDeclaration(node);
        }

        private SyntaxNode VisitTypeDeclaration(TypeDeclarationSyntax node)
        {
            if (HasNoRewriteAttribute(node.AttributeLists)) return node;

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

        public override SyntaxNode VisitForEachStatement(ForEachStatementSyntax node)
        {
            return TryVisitForEachStatement(node) ?? base.VisitForEachStatement(node);
        }

        private SyntaxNode TryVisitForEachStatement(ForEachStatementSyntax node)
        {
            var collection = node.Expression as InvocationExpressionSyntax;
            if (collection != null && IsSupportedMethod(collection))
            {
                var visitor = new CanRewrapForeachVisitor();
                visitor.Visit(node.Statement);
                if (!visitor.Fail)
                {
                    var k = TryCatchVisitInvocationExpression(collection, node);
                    if (k != null) return SyntaxFactory.ExpressionStatement(k);
                }
            }
            return base.VisitForEachStatement(node);
        }

        private ExpressionSyntax InlineOrCreateMethod(Lambda lambda, TypeSyntax returnType, ArgumentListSyntax arguments, ParameterSyntax param)
        {
            var p = GetLambdaParameter(lambda, 0).Identifier.ValueText;
            //var lambdaParameter = semantic.GetDeclaredSymbol(p);
            var currentFlow = semantic.AnalyzeDataFlow(lambda.Body);
            var currentCaptures = currentFlow
                .DataFlowsOut
                .Union(currentFlow.DataFlowsIn)
                .Where(x => x.Name != p && (x as IParameterSymbol)?.IsThis != true)
                .Select(x => CreateVariableCapture(x, currentFlow.DataFlowsOut))
                .ToList();
            lambda = RenameSymbol(lambda, 0, param.Identifier.ValueText);


            return InlineOrCreateMethod(lambda.Body, returnType, param, currentCaptures);
        }

        private ExpressionSyntax InlineOrCreateMethod(CSharpSyntaxNode body, TypeSyntax returnType, ParameterSyntax param, IEnumerable<VariableCapture> captures)
        {

            var fn = GetUniqueName(currentMethodName + "_ProceduralLinqHelper");

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
        private static SeparatedSyntaxList<T> CreateSeparatedList<T>(params T[] items) where T : SyntaxNode
        {
            return SyntaxFactory.SeparatedList<T>(items);
        }
        private static ArgumentListSyntax CreateArguments(IEnumerable<ExpressionSyntax> items)
        {
            return CreateArguments(items.Select(x => SyntaxFactory.Argument(x)));
        }
        private static ArgumentListSyntax CreateArguments(params ExpressionSyntax[] items)
        {
            return CreateArguments((IEnumerable<ExpressionSyntax>)items);
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


        public Diagnostic CreateDiagnosticForException(Exception ex, string path)
        {
            var message = "roslyn-linq-rewrite exception while processing '" + path + "', method " + currentMethodName + ": " + ex.Message + " -- " + ex.StackTrace.Replace("\n", "");

            return Diagnostic.Create("LQRW1001", "Compiler", new LiteralString(message), DiagnosticSeverity.Error, DiagnosticSeverity.Error, true, 0);
        }

        private class LiteralString : LocalizableString
        {
            private string str;
            public LiteralString(string str)
            {
                this.str = str;
            }
            protected override bool AreEqual(object other)
            {
                var o = other as LiteralString;
                if (o != null) return o.str == this.str;
                return false;
            }

            protected override int GetHash()
            {
                return str.GetHashCode();
            }

            protected override string GetText(IFormatProvider formatProvider)
            {
                return str;
            }
        }
    }


}