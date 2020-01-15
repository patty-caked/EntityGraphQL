using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using EntityGraphQL.Compiler.Util;
using EntityGraphQL.Extensions;

namespace EntityGraphQL.Compiler
{
    public class GraphQLMutationNode : IGraphQLNode
    {
        private readonly CompiledQueryResult result;
        private readonly IGraphQLNode graphQLNode;

        public IEnumerable<IGraphQLNode> Fields { get; private set; }

        public string Name => graphQLNode.Name;
        public OperationType Type => OperationType.Mutation;

        public IReadOnlyDictionary<ParameterExpression, object> ConstantParameters => new Dictionary<ParameterExpression, object>();

        public List<ParameterExpression> Parameters => throw new NotImplementedException();

        public GraphQLMutationNode(CompiledQueryResult result, IGraphQLNode graphQLNode)
        {
            this.result = result;
            this.graphQLNode = graphQLNode;
            Fields = new List<IGraphQLNode>();
        }

        public ExpressionResult GetNodeExpression()
        {
            throw new NotImplementedException();
        }
        public void SetNodeExpression(ExpressionResult expr)
        {
            throw new NotImplementedException();
        }

        public object Execute(params object[] args)
        {
            // run the mutation to get the context for the query select
            var mutation = (MutationResult)this.result.ExpressionResult;
            var result = mutation.Execute(args);
            if (typeof(LambdaExpression).IsAssignableFrom(result.GetType()))
            {
                var mutationLambda = (LambdaExpression)result;
                var mutationContextParam = mutationLambda.Parameters.First();
                var mutationExpression = mutationLambda.Body;

                // this willtypically be similar to
                // db => db.Entity.Where(filter) or db => db.Entity.First(filter)
                // i.e. they'll be returning a list of items or a specific item
                // We want to take the field selection from the GraphQL query and add a LINQ Select() onto the expression
                // In the case of a First() we need to insert that select before the first
                // This is all to have 1 nice expression that can work with ORMs (like EF)
                // E.g  we want db => db.Entity.Select(e => new {name = e.Name, ...}).First(filter)
                // we dot not want db => new {name = db.Entity.First(filter).Name, ...})

                var selectParam = graphQLNode.Parameters.First();

                if (!mutationLambda.ReturnType.IsEnumerableOrArray())
                {
                    if (mutationExpression.NodeType == ExpressionType.Call)
                    {
                        var call = (MethodCallExpression)mutationExpression;
                        if (call.Method.Name == "First" || call.Method.Name == "FirstOrDefault" || call.Method.Name == "Last" || call.Method.Name == "LastOrDefault")
                        {
                            var baseExp = call.Arguments.First();
                            if (call.Arguments.Count == 2)
                            {
                                // move the fitler to a Where call
                                var filter = call.Arguments.ElementAt(1);
                                baseExp = ExpressionUtil.MakeExpressionCall(new [] {typeof(Queryable), typeof(Enumerable)}, "Where", new Type[] { selectParam.Type }, baseExp, filter);
                            }

                            // build select
                            var selectExp = ExpressionUtil.MakeExpressionCall(new [] {typeof(Queryable), typeof(Enumerable)}, "Select", new Type[] { selectParam.Type, graphQLNode.GetNodeExpression().Type}, baseExp, Expression.Lambda(graphQLNode.GetNodeExpression(), selectParam));

                            // add First/Last back
                            var firstExp = ExpressionUtil.MakeExpressionCall(new [] {typeof(Queryable), typeof(Enumerable)}, call.Method.Name, new Type[] { selectExp.Type.GetGenericArguments()[0] }, selectExp);

                            // we're done
                            graphQLNode.SetNodeExpression(firstExp);
                        }
                    }
                    else
                    {
                        // if they just return a constant I.e the entity they just updated. It comes as a memebr access constant
                        if (mutationLambda.Body.NodeType == ExpressionType.MemberAccess)
                        {
                            var me = (MemberExpression)mutationLambda.Body;
                            if (me.Expression.NodeType == ExpressionType.Constant)
                            {
                                graphQLNode.AddConstantParameter(Expression.Parameter(me.Type), Expression.Lambda(me).Compile().DynamicInvoke());
                            }
                        }
                        else if (mutationLambda.Body.NodeType == ExpressionType.Constant)
                        {
                            var ce = (ConstantExpression)mutationLambda.Body;
                            graphQLNode.AddConstantParameter(Expression.Parameter(ce.Type), ce.Value);
                        }
                    }
                }
                else
                {
                    var exp = ExpressionUtil.MakeExpressionCall(new [] {typeof(Queryable), typeof(Enumerable)}, "Select", new Type[] { selectParam.Type, graphQLNode.GetNodeExpression().Type}, mutationExpression, Expression.Lambda(graphQLNode.GetNodeExpression(), selectParam));
                    graphQLNode.SetNodeExpression(exp);
                }

                // make sure we use the right parameter
                graphQLNode.Parameters[0] = mutationContextParam;
                var executionArg = args[0];
                result = graphQLNode.Execute(executionArg);
                return result;
            }
            // run the query select
            result = graphQLNode.Execute(result);
            return result;
        }

        public void AddConstantParameter(ParameterExpression param, object val)
        {
            throw new NotImplementedException();
        }
    }
}
