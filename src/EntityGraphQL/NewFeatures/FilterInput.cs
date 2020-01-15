using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Reflection;
using System.Linq.Expressions;
using EntityGraphQL.CodeGeneration;

namespace EntityGraphQL.NewFeatures
{
    public enum Comparisons
    {
        Equals,
        NotEqualTo,
        GreaterThan,
        GreaterThanOrEqualTo,
        LessThan,
        LessThanOrEqualTo,
        In,
        NotIn,
        IsNull,
    }

    /// <summary>
    /// Things with an AND between them will effectively be surrounded by parenthesis when executed in a query.
    /// This means that BOTH conditions joined by an AND must be true.
    /// RIGHT Example:  A OR (B AND C) OR D
    /// WRONG Example: (A OR B) AND (C OR D)
    /// </summary>
    public enum FilterConjunctions
    {
        AND,
        OR,
    }

    public class FilterInput<TEnum>
    {
        public TEnum Field { get; set; }
        public FilterConjunctions Conjunction { get; set; }
        public new string Equals { get; set; }
        public string NotEqualTo { get; set; }
        public string GreaterThan { get; set; }
        public string GreaterThanOrEqualTo { get; set; }
        public string LessThan { get; set; }
        public string LessThanOrEqualTo { get; set; }
        public string[] In { get; set; }
        public string[] NotIn { get; set; }
        public bool? IsNull { get; set; }

        public static IQueryable<T> FilterThis<T>(IQueryable<T> query, FilterInput<TEnum>[] filters)
        {
            //var blah = Enumerizer.GetDatabaseEnums<SuryaProducts>();


            if (filters == null || filters.Length == 0)
                return query;
            List<Expression> expressions = new List<Expression>();

            ParameterExpression obj = Expression.Parameter(typeof(object), "lambdObj");

            foreach (FilterInput<TEnum> f in filters)
            {
                expressions.Add(f.ConstructFilterExpression(query, obj));
            }

            Expression andChain = null; // For chaining together binary expressions
            Expression predicateBody = null;
            // Go backwards through the list of filters and handle any AND conjunctions
            for (int i = 0; i < expressions.Count; i++)
            {
                if (filters[i].Conjunction == FilterConjunctions.AND && i > 0)
                {
                    if (andChain == null)
                    {
                        // If the AND chain is empty, make a new one with the current and previous expressions
                        andChain = Expression.AndAlso(expressions[i - 1], expressions[i]);
                    }
                    else
                    {
                        // If we have a chain of ANDs going, add the current expression to it
                        andChain = Expression.AndAlso(andChain, expressions[i]);
                    }
                }
                else if (i > 0) // If we hit an OR conjunction
                {
                    // If we have a chain of ANDs going, add it to the expChain with an OR
                    if (andChain != null)
                    {
                        if (predicateBody == null)
                            predicateBody = andChain;
                        else
                            predicateBody = Expression.OrElse(predicateBody, andChain);

                        andChain = null;
                    }
                    else
                    {
                        // If we don't have an AND chain going, then add the previous expression to expChain with an OR
                        if (predicateBody == null)
                            predicateBody = expressions[i - 1];
                        else
                            predicateBody = Expression.OrElse(predicateBody, expressions[i - 1]);


                    }

                    if (i == expressions.Count - 1)
                        predicateBody = Expression.OrElse(predicateBody, expressions[i]);
                }
            }

            // I hate the way this is and the above for loop are structured, but they work right now.
            if (predicateBody == null && andChain != null)
                predicateBody = andChain;
            else if (predicateBody == null && andChain == null)
                predicateBody = expressions[0];
            else if (predicateBody != null && andChain != null)
                predicateBody = Expression.OrElse(predicateBody, andChain);

            var boolExpressionTree = Expressionator<TEnum>.Lambdanator<T>(predicateBody, obj);

            var result = query.Where(boolExpressionTree);

            return result;
        }

        private Expression ConstructFilterExpression<T>(IQueryable<T> query, out ParameterExpression obj)
        {
            obj = Expression.Parameter(typeof(object), "Lambda parameter obj");
            return ConstructFilterExpression(query, obj);
        }
        private Expression ConstructFilterExpression<T>(IQueryable<T> query, ParameterExpression obj)
        {
            //obj = null;
            if (Field == null || HasTooManyComparisons(out Comparisons? comparison, this) || !comparison.HasValue)
                return null;

            //ParameterExpression obj;

            Expression body = Expressionator<TEnum>.ExpressionMaker(this, comparison, obj);

            // returns a lambda that looks similar to: obj => (GetPropValueString(obj, Field) == Equals)
            var boolExpressionTree = Expressionator<TEnum>.Lambdanator<T>(body, obj);

            var result = query.Where(boolExpressionTree);
            return body;
        }

        /// <summary>
        /// Returns true if the filter has more than 1 comparison applied to a field.
        /// </summary>
        /// <param name="filters"></param>
        /// <returns></returns>
        private static bool HasTooManyComparisons(out Comparisons? comparisons, FilterInput<TEnum> filters)
        {
            comparisons = null;
            var count = 0;
            var props = filters.GetType().GetProperties();
            foreach (PropertyInfo p in props)
            {
                if (p.GetValue(filters) != null && p.Name != "Field" && p.Name != "Conjunction")
                {
                    count++;
                    comparisons = (Comparisons)Enum.Parse(typeof(Comparisons), p.Name.Capitalize());
                }
            }

            if (count > 1)
            {
                comparisons = null;
                return true;
            }
            return false;
        }

        public static object GetPropValue(object src, string propName)
        {
            var propType = src.GetType();
            string name = propName.Capitalize();
            var propInfo = propType.GetProperty(name);
            var result = propInfo.GetValue(src);
            return result;
        }

        public static string GetPropValueAsString(object src, string propName)
        {
            return GetPropValue(src, propName)?.ToString() ?? "";
        }

        public static string GetPropValueAsString(object src, Enum propName)
        {
            var result = GetPropValueAsString(src, propName.ToString());
            return result;
        }
    }
}


namespace System
{
    public static class StringExtensions
    {
        /// <summary>
        /// Returns this string, but the first character is uppercase.
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        public static string Capitalize(this string str)
        {
            return char.ToUpper(str[0]) + str.Substring(1);
        }

        /// <summary>
        /// Returns this string, but the first character is lowercase.
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        public static string DeCapitalize(this string str)
        {
            return char.ToLower(str[0]) + str.Substring(1);
        }
    }
}