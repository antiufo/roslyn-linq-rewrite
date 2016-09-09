using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shaman.Roslyn.LinqRewrite
{
    public partial class LinqRewriter : CSharpSyntaxRewriter
    {

        private ExpressionSyntax TryRewrite(string aggregationMethod, ExpressionSyntax collection, ITypeSymbol semanticReturnType, List<LinqStep> chain, InvocationExpressionSyntax node)
        {
            var returnType = SyntaxFactory.ParseTypeName(semanticReturnType.ToDisplayString());

            if (RootMethodsThatRequireYieldReturn.Contains(aggregationMethod))
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

            if (aggregationMethod.Contains(".Sum"))
            {
                var elementType = ((returnType as NullableTypeSyntax)?.ElementType ?? returnType);
                return RewriteAsLoop(
                    returnType,
                    new[] { CreateLocalVariableDeclaration("sum_", SyntaxFactory.CastExpression(elementType, SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0)))) },
                    new[] { SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName("sum_")) },
                    collection,
                    MaybeAddSelect(chain, node.ArgumentList.Arguments.Count != 0),
                    (inv, arguments, param) =>
                    {
                        var currentValue = SyntaxFactory.IdentifierName(param.Identifier.ValueText);
                        return IfNullableIsNotNull(elementType != returnType, currentValue, x =>
                        {
                            return SyntaxFactory.CheckedStatement(SyntaxKind.CheckedStatement, SyntaxFactory.Block(SyntaxFactory.ExpressionStatement(SyntaxFactory.AssignmentExpression(SyntaxKind.AddAssignmentExpression, SyntaxFactory.IdentifierName("sum_"), x))));
                        });
                    }
                );
            }

            if (aggregationMethod.Contains(".Max") || aggregationMethod.Contains(".Min"))
            {
                var minmax = aggregationMethod.Contains(".Max") ? "max_" : "min_";
                var elementType = ((returnType as NullableTypeSyntax)?.ElementType ?? returnType);
                return RewriteAsLoop(
                    returnType,
                    new[] {
                        CreateLocalVariableDeclaration("found_", SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression)),
                        CreateLocalVariableDeclaration(minmax, SyntaxFactory.CastExpression(elementType, SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0))))
                    },
                    new[] {
                        SyntaxFactory.Block(
                        SyntaxFactory.IfStatement(SyntaxFactory.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, SyntaxFactory.IdentifierName("found_")),
                            returnType == elementType ? (StatementSyntax)CreateThrowException("System.InvalidOperationException", "The sequence did not contain any elements.") :
                            SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression))
                        ),
                         SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName(minmax))
                        )
                    },
                    collection,
                    MaybeAddSelect(chain, node.ArgumentList.Arguments.Count != 0),
                    (inv, arguments, param) =>
                    {
                        var identifierNameSyntax = SyntaxFactory.IdentifierName(param.Identifier.ValueText);
                        return IfNullableIsNotNull(elementType != returnType, identifierNameSyntax, x =>
                        {
                            var assignmentExpressionSyntax = SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, SyntaxFactory.IdentifierName(minmax), x);
                            var condition = SyntaxFactory.BinaryExpression(aggregationMethod.Contains(".Max") ? SyntaxKind.GreaterThanExpression : SyntaxKind.LessThanExpression, x, SyntaxFactory.IdentifierName(minmax));
                            var kind = (elementType as PredefinedTypeSyntax).Keyword.Kind();
                            if (kind == SyntaxKind.DoubleKeyword || kind == SyntaxKind.FloatKeyword)
                            {
                                condition = SyntaxFactory.BinaryExpression(SyntaxKind.LogicalOrExpression, condition, SyntaxFactory.InvocationExpression(SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, elementType, SyntaxFactory.IdentifierName("IsNaN")), CreateArguments(x)));
                            }
                            return SyntaxFactory.IfStatement(SyntaxFactory.IdentifierName("found_"),
                               SyntaxFactory.Block(SyntaxFactory.IfStatement(condition, SyntaxFactory.ExpressionStatement(assignmentExpressionSyntax))),
                               SyntaxFactory.ElseClause(SyntaxFactory.Block(SyntaxFactory.ExpressionStatement(SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, SyntaxFactory.IdentifierName("found_"), SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression))), SyntaxFactory.ExpressionStatement(assignmentExpressionSyntax))));
                        });
                    });
            }


            if (aggregationMethod.Contains(".Average"))
            {
                var elementType = ((returnType as NullableTypeSyntax)?.ElementType ?? returnType);
                var primitive = ((PredefinedTypeSyntax)elementType).Keyword.Kind();

                ExpressionSyntax sumIdentifier = SyntaxFactory.IdentifierName("sum_");
                ExpressionSyntax countIdentifier = SyntaxFactory.IdentifierName("count_");

                if (primitive != SyntaxKind.DecimalKeyword)
                {
                    sumIdentifier = SyntaxFactory.CastExpression(CreatePrimitiveType(SyntaxKind.DoubleKeyword), sumIdentifier);
                    countIdentifier = SyntaxFactory.CastExpression(CreatePrimitiveType(SyntaxKind.DoubleKeyword), countIdentifier);
                }
                ExpressionSyntax division = SyntaxFactory.BinaryExpression(SyntaxKind.DivideExpression, sumIdentifier, countIdentifier);
                if (primitive != SyntaxKind.DoubleKeyword && primitive != SyntaxKind.DecimalKeyword)
                {
                    division = SyntaxFactory.CastExpression(elementType, SyntaxFactory.ParenthesizedExpression(division));
                }

                return RewriteAsLoop(
                    returnType,
                    new[] {
                        CreateLocalVariableDeclaration("sum_", SyntaxFactory.CastExpression(primitive == SyntaxKind.IntKeyword || primitive==SyntaxKind.LongKeyword ? CreatePrimitiveType(SyntaxKind.LongKeyword) : primitive == SyntaxKind.DecimalKeyword ? CreatePrimitiveType(SyntaxKind.DecimalKeyword) : CreatePrimitiveType(SyntaxKind.DoubleKeyword), SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0)))),
                        CreateLocalVariableDeclaration("count_", SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.ParseToken("0L")))
                    },
                    new[] {
                        SyntaxFactory.Block(
                        SyntaxFactory.IfStatement(SyntaxFactory.BinaryExpression(SyntaxKind.EqualsExpression,
                            SyntaxFactory.IdentifierName("count_"),
                            SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.ParseToken("0"))),
                            returnType == elementType ? (StatementSyntax)CreateThrowException("System.InvalidOperationException", "The sequence did not contain any elements.") :
                            SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression))
                        ),
                         SyntaxFactory.ReturnStatement(division)
                        )
                    },
                    collection,
                    MaybeAddSelect(chain, node.ArgumentList.Arguments.Count != 0),
                    (inv, arguments, param) =>
                    {
                        var currentValue = SyntaxFactory.IdentifierName(param.Identifier.ValueText);
                        return IfNullableIsNotNull(elementType != returnType, currentValue, x =>
                        {
                            return SyntaxFactory.CheckedStatement(SyntaxKind.CheckedStatement, SyntaxFactory.Block(
                                SyntaxFactory.ExpressionStatement(SyntaxFactory.PostfixUnaryExpression(SyntaxKind.PostIncrementExpression, SyntaxFactory.IdentifierName("count_"))),
                                SyntaxFactory.ExpressionStatement(SyntaxFactory.AssignmentExpression(SyntaxKind.AddAssignmentExpression, SyntaxFactory.IdentifierName("sum_"), x))
                            ));
                        });
                    }
                );
            }




            if (aggregationMethod == AnyMethod || aggregationMethod == AnyWithConditionMethod)
            {

                return RewriteAsLoop(
                    CreatePrimitiveType(SyntaxKind.BoolKeyword),
                    Enumerable.Empty<StatementSyntax>(),
                    new[] { SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression)) },
                    collection,
                    MaybeAddFilter(chain, aggregationMethod == AnyWithConditionMethod),
                    (inv, arguments, param) =>
                    {
                        return SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression));
                    }
                );
            }

            if (aggregationMethod == ListForEachMethod || aggregationMethod == IEnumerableForEachMethod)
            {
                return RewriteAsLoop(
                    CreatePrimitiveType(SyntaxKind.VoidKeyword),
                    Enumerable.Empty<StatementSyntax>(),
                    Enumerable.Empty<StatementSyntax>(),
                    collection,
                    chain,
                    (inv, arguments, param) =>
                    {
                        var lambda = inv.Lambda ?? new Lambda((AnonymousFunctionExpressionSyntax)inv.Arguments.First());
                        return SyntaxFactory.ExpressionStatement(InlineOrCreateMethod(lambda, CreatePrimitiveType(SyntaxKind.VoidKeyword), arguments, param));
                    }
                    );
            }

            if (aggregationMethod == ContainsMethod)
            {
                var elementType = SyntaxFactory.ParseTypeName(semantic.GetTypeInfo(node.ArgumentList.Arguments.First().Expression).ConvertedType.ToDisplayString());
                var comparerIdentifier = ((elementType as NullableTypeSyntax)?.ElementType ?? elementType) is PredefinedTypeSyntax ? null : SyntaxFactory.IdentifierName("comparer_");
                return RewriteAsLoop(
                    CreatePrimitiveType(SyntaxKind.BoolKeyword),
                    comparerIdentifier != null ? new StatementSyntax[] { CreateLocalVariableDeclaration("comparer_", SyntaxFactory.ParseExpression("System.Collections.Generic.EqualityComparer<" + elementType.ToString() + ">.Default")) } : Enumerable.Empty<StatementSyntax>(),
                    new[] { SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression)) },
                    collection,
                    chain,
                    (inv, arguments, param) =>
                    {
                        var target = SyntaxFactory.IdentifierName("_target");
                        var current = SyntaxFactory.IdentifierName(param.Identifier.ValueText);
                        var condition = comparerIdentifier != null ? SyntaxFactory.InvocationExpression(SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, comparerIdentifier, SyntaxFactory.IdentifierName("Equals")), CreateArguments(current, target)) : (ExpressionSyntax)SyntaxFactory.BinaryExpression(SyntaxKind.EqualsExpression, current, target);
                        return SyntaxFactory.IfStatement(condition, SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)));
                    },
                    additionalParameters: new[] { Tuple.Create(CreateParameter("_target", elementType), node.ArgumentList.Arguments.First().Expression) }
                );
            }

            if (aggregationMethod == AllWithConditionMethod) // All alone does not exist
            {

                return RewriteAsLoop(
                    CreatePrimitiveType(SyntaxKind.BoolKeyword),
                    Enumerable.Empty<StatementSyntax>(),
                    new[] { SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)) },
                    collection,
                    chain,
                    (inv, arguments, param) =>
                    {
                        var lambda = (LambdaExpressionSyntax)inv.Arguments.First();
                        return SyntaxFactory.IfStatement(SyntaxFactory.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, SyntaxFactory.ParenthesizedExpression(InlineOrCreateMethod(new Lambda(lambda), CreatePrimitiveType(SyntaxKind.BoolKeyword), arguments, param))),
                         SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression)
                         ));
                    }
                );
            }



            if (aggregationMethod == CountMethod || aggregationMethod == CountWithConditionMethod || aggregationMethod == LongCountMethod || aggregationMethod == LongCountWithConditionMethod)
            {

                return RewriteAsLoop(
                    returnType,
                    new[] { CreateLocalVariableDeclaration("_count", SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.ParseToken(aggregationMethod == LongCountMethod || aggregationMethod == LongCountWithConditionMethod ? "0L" : "0"))) },
                    new[] { SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName("_count")) },
                    collection,
                    MaybeAddFilter(chain, aggregationMethod == CountWithConditionMethod || aggregationMethod == LongCountWithConditionMethod),
                    (inv, arguments, param) =>
                    {
                        return SyntaxFactory.ExpressionStatement(SyntaxFactory.PostfixUnaryExpression(SyntaxKind.PostIncrementExpression, SyntaxFactory.IdentifierName("_count")));
                    }
                );
            }

            if (aggregationMethod == ElementAtMethod || aggregationMethod == ElementAtOrDefaultMethod)
            {

                return RewriteAsLoop(
                    returnType,
                    new[] { CreateLocalVariableDeclaration("_count", SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.ParseToken(aggregationMethod == LongCountMethod || aggregationMethod == LongCountWithConditionMethod ? "0L" : "0"))) },
                    new[] { aggregationMethod == ElementAtMethod ? (StatementSyntax)CreateThrowException("System.InvalidOperationException", "The specified index is not included in the sequence.") : SyntaxFactory.ReturnStatement(SyntaxFactory.DefaultExpression(returnType)) },
                    collection,
                    MaybeAddFilter(chain, aggregationMethod == CountWithConditionMethod || aggregationMethod == LongCountWithConditionMethod),
                    (inv, arguments, param) =>
                    {
                        return SyntaxFactory.IfStatement(SyntaxFactory.BinaryExpression(SyntaxKind.EqualsExpression, SyntaxFactory.IdentifierName("_requestedPosition"), SyntaxFactory.PostfixUnaryExpression(SyntaxKind.PostIncrementExpression, SyntaxFactory.IdentifierName("_count"))), SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName(param.Identifier.ValueText)));
                    },
                    additionalParameters: new[] { Tuple.Create(CreateParameter("_requestedPosition", CreatePrimitiveType(SyntaxKind.IntKeyword)), node.ArgumentList.Arguments.First().Expression) }
                );
            }

            if (aggregationMethod == FirstMethod || aggregationMethod == FirstWithConditionMethod)
            {
                return RewriteAsLoop(
                    returnType,
                    Enumerable.Empty<StatementSyntax>(),
                    new[] { CreateThrowException("System.InvalidOperationException", "The sequence did not contain any elements.") },
                    collection,
                    MaybeAddFilter(chain, aggregationMethod == FirstWithConditionMethod),
                    (inv, arguments, param) =>
                    {
                        return SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName(param.Identifier.ValueText));
                    }
                );
            }



            if (aggregationMethod == FirstOrDefaultMethod || aggregationMethod == FirstOrDefaultWithConditionMethod)
            {
                return RewriteAsLoop(
                    returnType,
                    Enumerable.Empty<StatementSyntax>(),
                    new[] { SyntaxFactory.ReturnStatement(SyntaxFactory.DefaultExpression(returnType)) },
                    collection,
                    MaybeAddFilter(chain, aggregationMethod == FirstOrDefaultWithConditionMethod),
                    (inv, arguments, param) =>
                    {
                        return SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName(param.Identifier.ValueText));
                    }
                );
            }

            if (aggregationMethod == LastOrDefaultMethod || aggregationMethod == LastOrDefaultWithConditionMethod)
            {
                return RewriteAsLoop(
                    returnType,
                    new[] { CreateLocalVariableDeclaration("_last", SyntaxFactory.DefaultExpression(returnType)) },
                    new[] { SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName("_last")) },
                    collection,
                    MaybeAddFilter(chain, aggregationMethod == LastOrDefaultWithConditionMethod),
                    (inv, arguments, param) =>
                    {
                        return SyntaxFactory.ExpressionStatement(SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, SyntaxFactory.IdentifierName("_last"), SyntaxFactory.IdentifierName(param.Identifier.ValueText)));
                    }
                );
            }
            if (aggregationMethod == LastMethod || aggregationMethod == LastWithConditionMethod)
            {
                return RewriteAsLoop(
                    returnType,
                    new[] { CreateLocalVariableDeclaration("_last", SyntaxFactory.DefaultExpression(returnType)), CreateLocalVariableDeclaration("_found", SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression)) },
                    new StatementSyntax[] { SyntaxFactory.IfStatement(SyntaxFactory.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, SyntaxFactory.IdentifierName("_found")), CreateThrowException("System.InvalidOperationException", "The sequence did not contain any elements.")), SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName("_last")) },
                    collection,
                    MaybeAddFilter(chain, aggregationMethod == LastWithConditionMethod),
                    (inv, arguments, param) =>
                    {
                        return SyntaxFactory.Block(
                            SyntaxFactory.ExpressionStatement(SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, SyntaxFactory.IdentifierName("_found"), SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression))),
                            SyntaxFactory.ExpressionStatement(SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, SyntaxFactory.IdentifierName("_last"), SyntaxFactory.IdentifierName(param.Identifier.ValueText))));
                    }
                );
            }
            if (aggregationMethod == SingleMethod || aggregationMethod == SingleWithConditionMethod)
            {
                return RewriteAsLoop(
                    returnType,
                    new[] { CreateLocalVariableDeclaration("_last", SyntaxFactory.DefaultExpression(returnType)), CreateLocalVariableDeclaration("_found", SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression)) },
                    new StatementSyntax[] { SyntaxFactory.IfStatement(SyntaxFactory.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, SyntaxFactory.IdentifierName("_found")), CreateThrowException("System.InvalidOperationException", "The sequence did not contain any elements.")), SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName("_last")) },
                    collection,
                    MaybeAddFilter(chain, aggregationMethod == SingleWithConditionMethod),
                    (inv, arguments, param) =>
                    {
                        return SyntaxFactory.Block(
                            SyntaxFactory.IfStatement(SyntaxFactory.IdentifierName("_found"), CreateThrowException("System.InvalidOperationException", "The sequence contains more than one element.")),
                            SyntaxFactory.ExpressionStatement(SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, SyntaxFactory.IdentifierName("_found"), SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression))),
                            SyntaxFactory.ExpressionStatement(SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, SyntaxFactory.IdentifierName("_last"), SyntaxFactory.IdentifierName(param.Identifier.ValueText))));
                    }
                );
            }
            if (aggregationMethod == SingleOrDefaultMethod || aggregationMethod == SingleOrDefaultWithConditionMethod)
            {
                return RewriteAsLoop(
                    returnType,
                    new[] { CreateLocalVariableDeclaration("_last", SyntaxFactory.DefaultExpression(returnType)), CreateLocalVariableDeclaration("_found", SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression)) },
                    new StatementSyntax[] { SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName("_last")) },
                    collection,
                    MaybeAddFilter(chain, aggregationMethod == SingleOrDefaultWithConditionMethod),
                    (inv, arguments, param) =>
                    {
                        return SyntaxFactory.Block(
                            SyntaxFactory.IfStatement(SyntaxFactory.IdentifierName("_found"), CreateThrowException("System.InvalidOperationException", "The sequence contains more than one element.")),
                            SyntaxFactory.ExpressionStatement(SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, SyntaxFactory.IdentifierName("_found"), SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression))),
                            SyntaxFactory.ExpressionStatement(SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, SyntaxFactory.IdentifierName("_last"), SyntaxFactory.IdentifierName(param.Identifier.ValueText))));
                    }
                );
            }


            if (aggregationMethod == ToListMethod || aggregationMethod == ReverseMethod)
            {
                var count = chain.All(x => MethodsThatPreserveCount.Contains(x.MethodName)) ? GetCollectionCount(collection, true) : null;

                var listIdentifier = SyntaxFactory.IdentifierName("_list");
                return RewriteAsLoop(
                    returnType,
                    new[] { CreateLocalVariableDeclaration("_list", SyntaxFactory.ObjectCreationExpression(SyntaxFactory.ParseTypeName("System.Collections.Generic.List<" + GetItemType(semanticReturnType).ToDisplayString() + ">"), CreateArguments(count != null ? new[] { count } : Enumerable.Empty<ExpressionSyntax>()), null)) },
                    aggregationMethod == ReverseMethod ? new StatementSyntax[] { SyntaxFactory.ExpressionStatement(SyntaxFactory.InvocationExpression(SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.IdentifierName("_list"), SyntaxFactory.IdentifierName("Reverse")))), SyntaxFactory.ReturnStatement(listIdentifier) } : new[] { SyntaxFactory.ReturnStatement(listIdentifier) },
                    collection,
                    chain,
                    (inv, arguments, param) =>
                    {
                        return CreateStatement(SyntaxFactory.InvocationExpression(SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, listIdentifier, SyntaxFactory.IdentifierName("Add")), CreateArguments(new[] { SyntaxFactory.IdentifierName(param.Identifier.ValueText) })));
                    }
                );
            }



            if (/*aggregationMethod == ToDictionaryWithKeyMethod || */aggregationMethod == ToDictionaryWithKeyValueMethod)
            {
                var dictIdentifier = SyntaxFactory.IdentifierName("_dict");
                return RewriteAsLoop(
                    returnType,
                    new[] { CreateLocalVariableDeclaration("_dict", SyntaxFactory.ObjectCreationExpression(returnType, CreateArguments(Enumerable.Empty<ArgumentSyntax>()), null)) },
                    new[] { SyntaxFactory.ReturnStatement(dictIdentifier) },
                    collection,
                    chain,
                    (inv, arguments, param) =>
                    {
                        var keyLambda = (AnonymousFunctionExpressionSyntax)node.ArgumentList.Arguments.First().Expression;
                        var valueLambda = (AnonymousFunctionExpressionSyntax)node.ArgumentList.Arguments.ElementAtOrDefault(1)?.Expression;
                        return CreateStatement(SyntaxFactory.InvocationExpression(SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, dictIdentifier, SyntaxFactory.IdentifierName("Add")), CreateArguments(new[] {
                            InlineOrCreateMethod(new Lambda(keyLambda), SyntaxFactory.ParseTypeName( GetLambdaReturnType(keyLambda).ToDisplayString()), arguments, param),
                            aggregationMethod == ToDictionaryWithKeyValueMethod ?
                            InlineOrCreateMethod( new Lambda(valueLambda), SyntaxFactory.ParseTypeName( GetLambdaReturnType(valueLambda).ToDisplayString()), arguments, param):
                             SyntaxFactory.IdentifierName(param.Identifier.ValueText),
                        })));
                    }
                );
            }

            if (aggregationMethod == ToArrayMethod)
            {
                var count = chain.All(x => MethodsThatPreserveCount.Contains(x.MethodName)) ? GetCollectionCount(collection, false) : null;

                if (count != null)
                {
                    var arrayIdentifier = SyntaxFactory.IdentifierName("_array");
                    return RewriteAsLoop(
                        returnType,
                        new[] { CreateLocalVariableDeclaration("_array", SyntaxFactory.ArrayCreationExpression(SyntaxFactory.ArrayType(((ArrayTypeSyntax)returnType).ElementType, SyntaxFactory.List(new[] { SyntaxFactory.ArrayRankSpecifier(CreateSeparatedList(new[] { count })) })))) },
                        new[] { SyntaxFactory.ReturnStatement(arrayIdentifier) },
                        collection,
                        chain,
                        (inv, arguments, param) =>
                        {
                            return CreateStatement(SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, SyntaxFactory.ElementAccessExpression(arrayIdentifier, SyntaxFactory.BracketedArgumentList(CreateSeparatedList(new[] { SyntaxFactory.Argument(SyntaxFactory.IdentifierName("_index")) }))), SyntaxFactory.IdentifierName(param.Identifier.ValueText)));
                        }
                    );

                }
                else
                {
                    var listIdentifier = SyntaxFactory.IdentifierName("_list");
                    var listType = SyntaxFactory.ParseTypeName("System.Collections.Generic.List<" + ((ArrayTypeSyntax)returnType).ElementType + ">");
                    return RewriteAsLoop(
                        returnType,
                        new[] { CreateLocalVariableDeclaration("_list", SyntaxFactory.ObjectCreationExpression(listType, CreateArguments(Enumerable.Empty<ArgumentSyntax>()), null)) },
                        new[] { SyntaxFactory.ReturnStatement(SyntaxFactory.InvocationExpression(SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, listIdentifier, SyntaxFactory.IdentifierName("ToArray")))) },
                        collection,
                        chain,
                        (inv, arguments, param) =>
                        {
                            return CreateStatement(SyntaxFactory.InvocationExpression(SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, listIdentifier, SyntaxFactory.IdentifierName("Add")), CreateArguments(new[] { SyntaxFactory.IdentifierName(param.Identifier.ValueText) })));
                        }
                    );
                }
            }

#if false

            


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
            return null;
        }

        private StatementSyntax IfNullableIsNotNull(bool nullable, IdentifierNameSyntax currentValue, Func<ExpressionSyntax, StatementSyntax> p)
        {
            var k = nullable ? (ExpressionSyntax)SyntaxFactory.InvocationExpression(SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, currentValue, SyntaxFactory.IdentifierName("GetValueOrDefault"))) : currentValue;
            return nullable ? (StatementSyntax)SyntaxFactory.IfStatement(SyntaxFactory.BinaryExpression(SyntaxKind.NotEqualsExpression, currentValue, SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)), p(k)) : p(k);
        }

        private ExpressionSyntax GetCollectionCount(ExpressionSyntax collection, bool allowUnknown)
        {
            var collectionType = semantic.GetTypeInfo(collection).Type;
            if (collectionType is IArrayTypeSymbol)
            {
                return SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.IdentifierName(ItemsName), SyntaxFactory.IdentifierName("Length"));
            }
            if (collectionType.ToDisplayString().StartsWith("System.Collections.Generic.IReadOnlyCollection<") || collectionType.AllInterfaces.Any(x => x.ToDisplayString().StartsWith("System.Collections.Generic.IReadOnlyCollection<")))
            {
                return SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.IdentifierName(ItemsName), SyntaxFactory.IdentifierName("Count"));
            }
            if (collectionType.ToDisplayString().StartsWith("System.Collections.Generic.ICollection<") || collectionType.AllInterfaces.Any(x => x.ToDisplayString().StartsWith("System.Collections.Generic.ICollection<")))
            {
                return SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.IdentifierName(ItemsName), SyntaxFactory.IdentifierName("Count"));
            }
            if (allowUnknown)
            {
                var items = new int[] { };
                if (collectionType.IsValueType) return null;
                var itemType = GetItemType(collectionType);
                if (itemType == null) return null;
                return
                    SyntaxFactory.InvocationExpression(
                        SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.ParenthesizedExpression(
                                SyntaxFactory.ConditionalAccessExpression(
                                    SyntaxFactory.ParenthesizedExpression(
                                        SyntaxFactory.BinaryExpression(
                                            SyntaxKind.AsExpression,
                                            SyntaxFactory.IdentifierName(ItemsName),
                                            SyntaxFactory.ParseTypeName("System.Collections.Generic.ICollection<" + itemType.ToDisplayString() + ">")
                                        )
                                    ),
                                    SyntaxFactory.MemberBindingExpression(
                                        SyntaxFactory.IdentifierName("Count")
                                    )
                                )
                            ),
                            SyntaxFactory.IdentifierName("GetValueOrDefault")
                        )
                    );
            }
            return null;
        }

        private List<LinqStep> MaybeAddFilter(List<LinqStep> chain, bool condition)
        {
            if (!condition) return chain;
            var lambda = (LambdaExpressionSyntax)chain.First().Arguments.FirstOrDefault();
            return InsertExpandedShortcutMethod(chain, WhereMethod, lambda);
        }


        private List<LinqStep> MaybeAddSelect(List<LinqStep> chain, bool condition)
        {
            if (!condition) return chain;
            var lambda = (LambdaExpressionSyntax)chain.First().Arguments.FirstOrDefault();
            return InsertExpandedShortcutMethod(chain, SelectMethod, lambda);
        }

        private List<LinqStep> InsertExpandedShortcutMethod(List<LinqStep> chain, string methodFullName, LambdaExpressionSyntax lambda)
        {
            var ch = chain.ToList();
            //    var baseExpression = ((MemberAccessExpressionSyntax)chain.First().Expression).Expression;
            ch.Insert(1, new LinqStep(methodFullName, new[] { lambda }));
            return ch;
        }

        private StatementSyntax CreateProcessingStep(List<LinqStep> chain, int chainIndex, TypeSyntax itemType, string itemName, ArgumentListSyntax arguments, bool noAggregation)
        {

            if (chainIndex == 0 && !noAggregation || chainIndex == -1)
            {
                return currentAggregation(chain[0], arguments, CreateParameter(itemName, itemType));
            }

            var step = chain[chainIndex];


            var method = step.MethodName;



            if (method == WhereMethod)
            {
                var lambda = (AnonymousFunctionExpressionSyntax)step.Arguments[0];

                var check = InlineOrCreateMethod(new Lambda(lambda), CreatePrimitiveType(SyntaxKind.BoolKeyword), arguments, CreateParameter(itemName, itemType));
                var next = CreateProcessingStep(chain, chainIndex - 1, itemType, itemName, arguments, noAggregation);
                return SyntaxFactory.IfStatement(check, next is BlockSyntax ? next : SyntaxFactory.Block(next));
            }



            if (method == OfTypeMethod || method == CastMethod)
            {
                var newtype = ((GenericNameSyntax)((MemberAccessExpressionSyntax)step.Invocation.Expression).Name).TypeArgumentList.Arguments.First();

                var newname = "_linqitem" + ++lastId;

                var next = CreateProcessingStep(chain, chainIndex - 1, newtype, newname, arguments, noAggregation);


                if (method == CastMethod)
                {
                    var local = CreateLocalVariableDeclaration(newname, SyntaxFactory.CastExpression(newtype, SyntaxFactory.IdentifierName(itemName)));
                    var nexts = next is BlockSyntax ? ((BlockSyntax)next).Statements : (IEnumerable<StatementSyntax>)new[] { next };
                    return SyntaxFactory.Block(new[] { local }.Concat(nexts));
                }
                else
                {
                    var type = semantic.GetTypeInfo(newtype).Type;
                    if (type.IsValueType)
                    {
                        return SyntaxFactory.IfStatement(SyntaxFactory.BinaryExpression(SyntaxKind.IsExpression, SyntaxFactory.IdentifierName(itemName), newtype), SyntaxFactory.Block(
                                CreateLocalVariableDeclaration(newname, SyntaxFactory.CastExpression(newtype, SyntaxFactory.IdentifierName(itemName))),
                                next

                            ));
                    }
                    else
                    {
                        var local = CreateLocalVariableDeclaration(newname, SyntaxFactory.BinaryExpression(SyntaxKind.AsExpression, SyntaxFactory.IdentifierName(itemName), newtype));
                        return SyntaxFactory.Block(local, SyntaxFactory.IfStatement(SyntaxFactory.BinaryExpression(SyntaxKind.NotEqualsExpression, SyntaxFactory.IdentifierName(newname), SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)),
                            next));
                    }
                }
            }


            if (method == SelectMethod)
            {
                var lambda = (LambdaExpressionSyntax)step.Arguments[0];

                var newname = "_linqitem" + ++lastId;
                var lambdaType = (INamedTypeSymbol)semantic.GetTypeInfo(lambda).ConvertedType;
                var lambdaBodyType = lambdaType.TypeArguments.Last();
                var newtype = IsAnonymousType(lambdaBodyType) ? null : SyntaxFactory.ParseTypeName(lambdaBodyType.ToDisplayString());


                var local = CreateLocalVariableDeclaration(newname, InlineOrCreateMethod(new Lambda(lambda), newtype, arguments, CreateParameter(itemName, itemType)));


                var next = CreateProcessingStep(chain, chainIndex - 1, newtype, newname, arguments, noAggregation);
                var nexts = next is BlockSyntax ? ((BlockSyntax)next).Statements : (IEnumerable<StatementSyntax>)new[] { next };
                return SyntaxFactory.Block(new[] { local }.Concat(nexts));
            }



            throw new NotSupportedException();
        }


        //readonly static string ToDictionaryWithKeyMethod = "System.Collections.Generic.IEnumerable<TSource>.ToDictionary<TSource, TKey>(System.Func<TSource, TKey>)";
        readonly static string ToDictionaryWithKeyValueMethod = "System.Collections.Generic.IEnumerable<TSource>.ToDictionary<TSource, TKey, TElement>(System.Func<TSource, TKey>, System.Func<TSource, TElement>)";
        readonly static string ToArrayMethod = "System.Collections.Generic.IEnumerable<TSource>.ToArray<TSource>()";
        readonly static string ToListMethod = "System.Collections.Generic.IEnumerable<TSource>.ToList<TSource>()";
        readonly static string ReverseMethod = "System.Collections.Generic.IEnumerable<TSource>.Reverse<TSource>()";
        readonly static string FirstMethod = "System.Collections.Generic.IEnumerable<TSource>.First<TSource>()";
        readonly static string SingleMethod = "System.Collections.Generic.IEnumerable<TSource>.Single<TSource>()";
        readonly static string LastMethod = "System.Collections.Generic.IEnumerable<TSource>.Last<TSource>()";
        readonly static string FirstOrDefaultMethod = "System.Collections.Generic.IEnumerable<TSource>.FirstOrDefault<TSource>()";
        readonly static string SingleOrDefaultMethod = "System.Collections.Generic.IEnumerable<TSource>.SingleOrDefault<TSource>()";
        readonly static string LastOrDefaultMethod = "System.Collections.Generic.IEnumerable<TSource>.LastOrDefault<TSource>(System.Func<TSource, bool>)";
        readonly static string FirstWithConditionMethod = "System.Collections.Generic.IEnumerable<TSource>.First<TSource>(System.Func<TSource, bool>)";
        readonly static string SingleWithConditionMethod = "System.Collections.Generic.IEnumerable<TSource>.Single<TSource>(System.Func<TSource, bool>)";
        readonly static string LastWithConditionMethod = "System.Collections.Generic.IEnumerable<TSource>.Last<TSource>(System.Func<TSource, bool>)";
        readonly static string FirstOrDefaultWithConditionMethod = "System.Collections.Generic.IEnumerable<TSource>.FirstOrDefault<TSource>(System.Func<TSource, bool>)";
        readonly static string SingleOrDefaultWithConditionMethod = "System.Collections.Generic.IEnumerable<TSource>.SingleOrDefault<TSource>(System.Func<TSource, bool>)";
        readonly static string LastOrDefaultWithConditionMethod = "System.Collections.Generic.IEnumerable<TSource>.LastOrDefault<TSource>(System.Func<TSource, bool>)";

        readonly static string CountMethod = "System.Collections.Generic.IEnumerable<TSource>.Count<TSource>()";
        readonly static string CountWithConditionMethod = "System.Collections.Generic.IEnumerable<TSource>.Count<TSource>(System.Func<TSource, bool>)";
        readonly static string LongCountMethod = "System.Collections.Generic.IEnumerable<TSource>.LongCount<TSource>()";
        readonly static string LongCountWithConditionMethod = "System.Collections.Generic.IEnumerable<TSource>.LongCount<TSource>(System.Func<TSource, bool>)";

        readonly static string ElementAtMethod = "System.Collections.Generic.IEnumerable<TSource>.ElementAt<TSource>(int)";
        readonly static string ElementAtOrDefaultMethod = "System.Collections.Generic.IEnumerable<TSource>.ElementAtOrDefault<TSource>(int)";

        readonly static string AnyMethod = "System.Collections.Generic.IEnumerable<TSource>.Any<TSource>()";
        readonly static string AnyWithConditionMethod = "System.Collections.Generic.IEnumerable<TSource>.Any<TSource>(System.Func<TSource, bool>)";

        readonly static string AllWithConditionMethod = "System.Collections.Generic.IEnumerable<TSource>.All<TSource>(System.Func<TSource, bool>)";



        readonly static string ContainsMethod = "System.Collections.Generic.IEnumerable<TSource>.Contains<TSource>(TSource)";

        readonly static string ListForEachMethod = "System.Collections.Generic.List<T>.ForEach(System.Action<T>)";
        readonly static string IEnumerableForEachMethod = "System.Collections.Generic.IEnumerable<T>.ForEach<T>(System.Action<T>)";

        //readonly static string RecursiveEnumerationMethod = "T.RecursiveEnumeration<T>(System.Func<T, T>)";

        readonly static string WhereMethod = "System.Collections.Generic.IEnumerable<TSource>.Where<TSource>(System.Func<TSource, bool>)";
        readonly static string SelectMethod = "System.Collections.Generic.IEnumerable<TSource>.Select<TSource, TResult>(System.Func<TSource, TResult>)";
        readonly static string CastMethod = "System.Collections.IEnumerable.Cast<TResult>()";
        readonly static string OfTypeMethod = "System.Collections.IEnumerable.OfType<TResult>()";
        readonly static string[] RootMethodsThatRequireYieldReturn = new[] {
            WhereMethod, SelectMethod, CastMethod, OfTypeMethod
        };
        readonly static string[] MethodsThatPreserveCount = new[] {
            SelectMethod, CastMethod, ReverseMethod, ToListMethod, ToArrayMethod /*OrderBy*/
        };
    }
}
