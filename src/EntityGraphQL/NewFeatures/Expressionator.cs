using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Reflection;
using System.Linq.Expressions;
using EntityGraphQL.NewFeatures;
using System.ComponentModel;

namespace EntityGraphQL.CodeGeneration
{
    public static class Expressionator
    {
        public static Expression ExpressionMaker<TFieldEnum>(FilterInput<TFieldEnum> filter, Comparisons? comparison, ParameterExpression mainParam)
        {
            try
            {
                // Get the property with the name <filter.field> within mainParam's Type, then get the type of that property.
                var fieldType = mainParam.Type.GetProperty(filter.Field.ToString().Capitalize()).PropertyType;

                var compareTo = FilterInput<TFieldEnum>.GetPropValue(filter, comparison.ToString()); // Get the value we want to compare to our field.

                TypeConverter typeConverter = TypeDescriptor.GetConverter(fieldType);
                object propValue = (compareTo.GetType().IsArray || compareTo.GetType() == typeof(bool)) // Don't want it trying to convert an array or bool to a string
                    ? compareTo : typeConverter.ConvertFromString(compareTo.ToString());

                ConstantExpression constCompare = Expression.Constant(propValue, propValue.GetType());

                MemberExpression mainParamField = Expression.PropertyOrField(mainParam, filter.Field.ToString().Capitalize());
                MethodCallExpression mainLambdaParamToString = Expression.Call(mainParamField, typeof(object).GetMethod("ToString", System.Type.EmptyTypes));

                Expression binaryExpression;

                var zeroConst = Expression.Constant(0, typeof(int));
                var nullConst = Expression.Constant("", typeof(string));
                switch (comparison)
                {
                    case Comparisons.Equals:
                        binaryExpression = Expression.Equal(mainParamField, constCompare);
                        break;
                    case Comparisons.NotEqualTo:
                        binaryExpression = Expression.NotEqual(mainParamField, constCompare);
                        break;
                    case Comparisons.GreaterThan:
                        binaryExpression = Expression.Call(mainParamField, mainParamField.Type.GetMethod("CompareTo", new Type[] { constCompare.Type }), constCompare);
                        binaryExpression = Expression.GreaterThan(binaryExpression, zeroConst);
                        break;
                    case Comparisons.GreaterThanOrEqualTo:
                        binaryExpression = Expression.Call(mainParamField, mainParamField.Type.GetMethod("CompareTo", new Type[] { constCompare.Type }), constCompare);
                        binaryExpression = Expression.GreaterThanOrEqual(binaryExpression, zeroConst);
                        break;
                    case Comparisons.LessThan:
                        binaryExpression = Expression.Call(mainParamField, mainParamField.Type.GetMethod("CompareTo", new Type[] { constCompare.Type }), constCompare);
                        binaryExpression = Expression.LessThan(binaryExpression, zeroConst);
                        break;
                    case Comparisons.LessThanOrEqualTo:
                        binaryExpression = Expression.Call(mainParamField, mainParamField.Type.GetMethod("CompareTo", new Type[] { constCompare.Type }), constCompare);
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
            catch (ArgumentException ex)
            {
                // error with string conversion
                throw new ArgumentException(ex.Message.Replace("\r\nParameter name: value", ""), ex.InnerException);
            }
            catch(FormatException ex)
            {
                throw ex;
            }
        }

        
    }
}
