using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Reflection;
using System.Linq.Expressions;
using EntityGraphQL.NewFeatures;

namespace EntityGraphQL.CodeGeneration
{
    public static class Expressionator
    {
        public static Expression ExpressionMaker(FilterInput filter, Comparisons? comparison, ParameterExpression mainParam)
        {
            var compareTo = FilterInput.GetPropValue(filter, comparison.ToString());
            ConstantExpression constCompare = Expression.Constant(compareTo, compareTo.GetType());

            MemberExpression mainParamField = Expression.PropertyOrField(mainParam, filter.Field.ToString().Capitalize());
            MethodCallExpression mainLambdaParamToString = Expression.Call(mainParamField, typeof(object).GetMethod("ToString", System.Type.EmptyTypes));

            Expression binaryExpression;


            var zeroConst = Expression.Constant(0, typeof(int));
            var nullConst = Expression.Constant("", typeof(string));
            switch (comparison)
            {
                case Comparisons.Equals:
                    binaryExpression = Expression.Equal(mainLambdaParamToString, constCompare);
                    break;
                case Comparisons.NotEqualTo:
                    binaryExpression = Expression.NotEqual(mainLambdaParamToString, constCompare);
                    break;
                case Comparisons.GreaterThan:
                    binaryExpression = Expression.Call(typeof(string).GetMethod("Compare", new Type[] { typeof(string), typeof(string) }), mainLambdaParamToString, constCompare);
                    binaryExpression = Expression.GreaterThan(binaryExpression, zeroConst);
                    break;
                case Comparisons.GreaterThanOrEqualTo:
                    binaryExpression = Expression.Call(typeof(string).GetMethod("Compare", new Type[] { typeof(string), typeof(string) }), mainLambdaParamToString, constCompare);
                    binaryExpression = Expression.GreaterThanOrEqual(binaryExpression, zeroConst);
                    break;
                case Comparisons.LessThan:
                    binaryExpression = Expression.Call(typeof(string).GetMethod("Compare", new Type[] { typeof(string), typeof(string) }), mainLambdaParamToString, constCompare);
                    binaryExpression = Expression.LessThan(binaryExpression, zeroConst);
                    break;
                case Comparisons.LessThanOrEqualTo:
                    binaryExpression = Expression.Call(typeof(string).GetMethod("Compare", new Type[] { typeof(string), typeof(string) }), mainLambdaParamToString, constCompare);
                    binaryExpression = Expression.LessThanOrEqual(binaryExpression, zeroConst);
                    break;
                case Comparisons.In:
                    binaryExpression = Expression.Call(typeof(Enumerable), "Contains", new Type[] { typeof(string) }, constCompare, mainLambdaParamToString);
                    break;
                case Comparisons.NotIn:
                    binaryExpression = Expression.Call(typeof(Enumerable), "Contains", new Type[] { typeof(string) }, constCompare, mainLambdaParamToString);
                    binaryExpression = Expression.Not(binaryExpression);
                    break;
                case Comparisons.IsNull:
                    if ((bool)filter.IsNull)
                    {
                        binaryExpression = Expression.Equal(mainLambdaParamToString, nullConst);
                    }
                    else
                    {
                        binaryExpression = Expression.NotEqual(mainLambdaParamToString, nullConst);
                    }
                    break;
                default:
                    mainParam = null;
                    binaryExpression = null;
                    return null;
            }

            //return ExpressionMaker(compareTo, filter.Field.ToString(), methName, mainParam);
            return binaryExpression;
        }

        /*public static BinaryExpression EqualTo(Expression source, ConstantExpression compareTo)
        {
            return Expression.Equal(source, compareTo);
        }

        public static bool NotEqualTo(object src, string field, string compValue)
        {
            return FilterInput.GetPropValueAsString(src, field) != compValue;
        }

        public static bool GreaterThan(object src, string field, string compValue)
        {
            return string.Compare(FilterInput.GetPropValueAsString(src, field), compValue) > 0;
        }

        public static bool GreaterThanOrEqualTo(object src, string field, string compValue)
        {
            return string.Compare(FilterInput.GetPropValueAsString(src, field), compValue) >= 0;
        }

        public static bool LessThan(object src, string field, string compValue)
        {
            return string.Compare(FilterInput.GetPropValueAsString(src, field), compValue) < 0;
        }

        public static bool LessThanOrEqualTo(object src, string field, string compValue)
        {
            return string.Compare(FilterInput.GetPropValueAsString(src, field), compValue) <= 0;
        }

        public static bool IsIn(object src, string field, string[] compValue)
        {
            return compValue.Contains(FilterInput.GetPropValueAsString(src, field));
        }

        public static bool IsNotIn(object src, string field, string[] compValue)
        {
            return !compValue.Contains(FilterInput.GetPropValueAsString(src, field));
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "This needs blah for expression stuff")]
        public static bool IsNull(object src, string field, bool blah = false)
        {
            return FilterInput.GetPropValueAsString(src, field) == "";
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "This needs blah for expression stuff")]
        public static bool IsNotNull(object src, string field, bool blah = false)
        {
            return FilterInput.GetPropValueAsString(src, field) != "";
        }*/
    }
}
