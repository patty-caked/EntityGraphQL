using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Reflection;
using System.Linq.Expressions;
using EntityGraphQL.NewFeatures;

namespace EntityGraphQL.CodeGeneration
{
    public static class Expressionator<TEnum>
    {
        public static Expression ExpressionMaker(FilterInput<TEnum> filter, Comparisons? comparison, out ParameterExpression mainParam)
        {
            mainParam = Expression.Parameter(typeof(object), "Lambda parameter obj");

            return ExpressionMaker(filter, comparison, mainParam);
        }

        public static Expression ExpressionMaker(FilterInput<TEnum> filter, Comparisons? comparison, ParameterExpression mainParam)
        {
            object temp = FilterInput<TEnum>.GetPropValue(filter, comparison.ToString());
            string methName;
            switch (comparison)
            {
                case Comparisons.Equals:
                    methName = "EqualTo";
                    break;
                case Comparisons.NotEqualTo:
                    methName = "NotEqualTo";
                    break;
                case Comparisons.GreaterThan:
                    methName = "GreaterThan";
                    break;
                case Comparisons.GreaterThanOrEqualTo:
                    methName = "GreaterThanOrEqualTo";
                    break;
                case Comparisons.LessThan:
                    methName = "LessThan";
                    break;
                case Comparisons.LessThanOrEqualTo:
                    methName = "LessThanOrEqualTo";
                    break;
                case Comparisons.In:
                    methName = "IsIn";
                    break;
                case Comparisons.NotIn:
                    methName = "IsNotIn";
                    break;
                case Comparisons.IsNull:
                    if ((bool)filter.IsNull)
                        methName = "IsNull";
                    else
                        methName = "IsNotNull";
                    break;
                default:
                    mainParam = null;
                    return null;
            }

            return ExpressionMaker(temp, filter.Field.ToString(), methName, mainParam);
        }

        private static Expression ExpressionMaker(object compareTo, string field, string methName, ParameterExpression mainParam)
        {
            // mainParam => rest of the lambda expression
            //mainParam = Expression.Parameter(typeof(object), "Lambda parameter obj");

            ConstantExpression constEnum = Expression.Constant(field);
            ConstantExpression constCompare = Expression.Constant(compareTo, compareTo.GetType());

            //Expression propValMCE = Expression.Call(typeof(FilterInput).GetMethod("GetPropValueAsString", new Type[] { typeof(object), typeof(string) }),
                //new Expression[] { mainParam, constEnum });

            MethodInfo methInfo = typeof(Expressionator<TEnum>).GetMethod(methName);

            Expression boolValMCE = Expression.Call(methInfo,
                new Expression[] { mainParam, constEnum, constCompare });

            return boolValMCE;
        }

        public static Expression<Func<T,bool>> Lambdanator<T>(Expression body, ParameterExpression paramEx)
        {
            return Expression.Lambda<Func<T, bool>>(body, new[] { paramEx });
        }



        public static bool EqualTo(object src, string field, string compValue)
        {
            return FilterInput<TEnum>.GetPropValueAsString(src, field) == compValue;
        }

        public static bool NotEqualTo(object src, string field, string compValue)
        {
            return FilterInput<TEnum>.GetPropValueAsString(src, field) != compValue;
        }

        public static bool GreaterThan(object src, string field, string compValue)
        {
            return string.Compare(FilterInput<TEnum>.GetPropValueAsString(src, field), compValue) > 0;
        }

        public static bool GreaterThanOrEqual(object src, string field, string compValue)
        {
            return string.Compare(FilterInput<TEnum>.GetPropValueAsString(src, field), compValue) >= 0;
        }

        public static bool LessThan(object src, string field, string compValue)
        {
            return string.Compare(FilterInput<TEnum>.GetPropValueAsString(src, field), compValue) < 0;
        }

        public static bool LessThanOrEqual(object src, string field, string compValue)
        {
            return string.Compare(FilterInput<TEnum>.GetPropValueAsString(src, field), compValue) <= 0;
        }

        public static bool IsIn(object src, string field, string[] compValue)
        {
            return compValue.Contains(FilterInput<TEnum>.GetPropValueAsString(src, field));
        }

        public static bool IsNotIn(object src, string field, string[] compValue)
        {
            return !compValue.Contains(FilterInput<TEnum>.GetPropValueAsString(src, field));
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "This needs blah for expression stuff")]
        public static bool IsNull(object src, string field, bool blah = false)
        {
            return FilterInput<TEnum>.GetPropValueAsString(src, field) == "";
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "This needs blah for expression stuff")]
        public static bool IsNotNull(object src, string field, bool blah = false)
        {
            return FilterInput<TEnum>.GetPropValueAsString(src, field) != "";
        }
    }
}
