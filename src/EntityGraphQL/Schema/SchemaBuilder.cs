using System;
using System.Linq;
using System.Linq.Expressions;
using System.Collections.Generic;
using System.Reflection;
using EntityGraphQL.Extensions;
using Humanizer;
using EntityGraphQL.Compiler.Util;
using System.ComponentModel;

namespace EntityGraphQL.Schema
{
    /// <summary>
    /// A simple schema provider to automattically create a query schema based on an object.
    /// Commonly used with a DbContext.
    /// </summary>
    public static class SchemaBuilder
    {
        private static readonly HashSet<string> ignoreProps = new HashSet<string> {
            "Database",
            "Model",
            "ChangeTracker"
        };

        private static readonly HashSet<string> ignoreTypes = new HashSet<string> {
            "String",
            "Byte[]"
        };

        /// <summary>
        /// Given the type TContextType recursively create a query schema based on the public properties of the object.
        /// </summary>
        /// <param name="autoCreateIdArguments">If True, automatically create a field for any root array thats context object contains an Id property. I.e. If Actor has an Id property and the root TContextType contains IEnumerable<Actor> Actors. A root field Actor(id) will be created.</param>
        /// <typeparam name="TContextType"></typeparam>
        /// <returns></returns>
        public static MappedSchemaProvider<TContextType> FromObject<TContextType>(bool autoCreateIdArguments = true, bool autoCreateEnumTypes = true)
        {
            var schema = new MappedSchemaProvider<TContextType>();
            var contextType = typeof(TContextType);
            var rootFields = GetFieldsFromObject(contextType, schema, autoCreateEnumTypes);
            foreach (var f in rootFields)
            {
                if (autoCreateIdArguments)
                {
                    // add non-pural field with argument of ID
                    AddFieldWithIdArgumentIfExists(schema, contextType, f);
                }
                /// New Addition
                FilterEnumBuilder.AddFieldWithFilterArgument(schema, contextType, f);

                f.Name = f.Name.Pluralize();
                schema.AddField(f);
            }
            return schema;
        }

        private static void AddFieldWithIdArgumentIfExists<TContextType>(MappedSchemaProvider<TContextType> schema, Type contextType, Field fieldProp)
        {
            if (!fieldProp.Resolve.Type.IsEnumerableOrArray())
                return;
            var schemaType = schema.Type(fieldProp.ReturnTypeClrSingle);
            // Find the first field named "id" or "<fieldProp.Name>Id" to turn into a field with arguments
            var idFieldDef = schemaType.GetFields().FirstOrDefault(f => f.Name.ToLower() == "id" || f.Name.ToLower() == $"{fieldProp.Name.ToLower()}id");
            if (idFieldDef == null)
                return;

            // Save a little bit of typing and clean things up.
            var idFieldName = idFieldDef.Name; 

            // We need to build an anonymous type with id = RequiredField<idFieldDef.Resolve.Type>()
            // Resulting lambda is (a, p) => a.Where(b => b.Id == p.Id).First()
            // This allows us to "insert" .Select() (and .Include()) before the .First()
            var requiredFieldType = typeof(RequiredField<>).MakeGenericType(idFieldDef.Resolve.Type);
            var fieldNameAndType = new Dictionary<string, Type> { { idFieldName, requiredFieldType } };
            var argTypes = LinqRuntimeTypeBuilder.GetDynamicType(fieldNameAndType);
            var argTypesValue = argTypes.GetTypeInfo().GetConstructors()[0].Invoke(new Type[0]);
            var argTypeParam = Expression.Parameter(argTypes);
            Type arrayContextType = schema.Type(fieldProp.ReturnTypeClrSingle).ContextType;
            var arrayContextParam = Expression.Parameter(arrayContextType);

            var ctxId = Expression.PropertyOrField(arrayContextParam, $"{char.ToUpper(idFieldName[0])}{idFieldName.Substring(1)}");
            Expression argId = Expression.PropertyOrField(argTypeParam, idFieldName);

            argId = Expression.Property(argId, "Value"); // call RequiredField<>.Value to get the real type without a convert
            var idBody = Expression.MakeBinary(ExpressionType.Equal, ctxId, argId);
            var idLambda = Expression.Lambda(idBody, new[] { arrayContextParam });
            Expression body = ExpressionUtil.MakeExpressionCall(new[] { typeof(Queryable), typeof(Enumerable) }, "Where", new Type[] { arrayContextType }, fieldProp.Resolve, idLambda);

            body = ExpressionUtil.MakeExpressionCall(new[] { typeof(Queryable), typeof(Enumerable) }, "FirstOrDefault", new Type[] { arrayContextType }, body);
            var contextParam = Expression.Parameter(contextType);
            var lambdaParams = new[] { contextParam, argTypeParam };
            body = new ParameterReplacer().ReplaceByType(body, contextType, contextParam);
            var selectionExpression = Expression.Lambda(body, lambdaParams);
            var name = fieldProp.Name.Singularize();
            if (name == null)
            {
                // If we can't singularize it just use the name plus something as GraphQL doesn't support field overloads
                name = $"{fieldProp.Name}";
            }
            var field = new Field(name, selectionExpression, $"Return a {fieldProp.ReturnTypeClrSingle} by its Id", fieldProp.ReturnTypeClrSingle, argTypesValue);
            schema.AddField(field);
        }

        public static List<Field> GetFieldsFromObject<TContextType>(Type type, MappedSchemaProvider<TContextType> schema, bool createEnumTypes, bool createNewComplexTypes = true)
        {
            var fields = new List<Field>();
            // cache fields/properties
            var param = Expression.Parameter(type);
            if (type.IsArray || type.IsEnumerableOrArray())
                return fields;

            foreach (var prop in type.GetProperties())
            {
                var f = ProcessFieldOrProperty(prop, prop.PropertyType, param, schema, createEnumTypes, createNewComplexTypes);
                if (f != null)
                    fields.Add(f);
            }
            foreach (var prop in type.GetFields())
            {
                var f = ProcessFieldOrProperty(prop, prop.FieldType, param, schema, createEnumTypes, createNewComplexTypes);
                if (f != null)
                    fields.Add(f);
            }
            return fields;
        }

        private static Field ProcessFieldOrProperty<TContextType>(MemberInfo prop, Type fieldOrPropType, ParameterExpression param, MappedSchemaProvider<TContextType> schema, bool createEnumTypes, bool createNewComplexTypes)
        {
            if (ignoreProps.Contains(prop.Name) || GraphQLIgnoreAttribute.ShouldIgnoreMemberFromQuery(prop))
            {
                return null;
            }

            // Get Description from ComponentModel.DescriptionAttribute
            string description = "";
            var d = (DescriptionAttribute)prop.GetCustomAttribute(typeof(DescriptionAttribute), false);
            if (d != null)
            {
                description = d.Description;
            }

            LambdaExpression le = Expression.Lambda(prop.MemberType == MemberTypes.Property ? Expression.Property(param, prop.Name) : Expression.Field(param, prop.Name), param);
            var f = new Field(SchemaGenerator.ToCamelCaseStartsLower(prop.Name), le, description);
            var t = CacheType(fieldOrPropType, schema, createEnumTypes, createNewComplexTypes);
            if (t != null && t.IsEnum && !f.ReturnTypeClr.IsNullableType())
            {
                f.ReturnTypeNotNullable = true;
            }
            return f;
        }

        private static ISchemaType CacheType<TContextType>(Type propType, MappedSchemaProvider<TContextType> schema, bool createEnumTypes, bool createNewComplexTypes)
        {
            if (propType.IsEnumerableOrArray())
            {
                propType = propType.GetEnumerableOrArrayType();
            }

            if (!schema.HasType(propType) && !ignoreTypes.Contains(propType.Name))
            {
                var typeInfo = propType.GetTypeInfo();
                string description = "";
                var d = (DescriptionAttribute)typeInfo.GetCustomAttribute(typeof(DescriptionAttribute), false);
                if (d != null)
                {
                    description = d.Description;
                }

                if (createNewComplexTypes && (typeInfo.IsClass || typeInfo.IsInterface))
                {
                    // add type before we recurse more that may also add the type
                    // dynamcially call generic method
                    // hate this, but want to build the types with the right Genenics so you can extend them later.
                    // this is not the fastest, but only done on schema creation
                    var method = schema.GetType().GetMethod("AddType", new[] { typeof(string), typeof(string) });
                    method = method.MakeGenericMethod(propType);
                    var t = (ISchemaType)method.Invoke(schema, new object[] { propType.Name, description });

                    var fields = GetFieldsFromObject(propType, schema, createEnumTypes);
                    t.AddFields(fields);
                    return t;
                }
                else if (createEnumTypes && typeInfo.IsEnum && !schema.HasType(propType.Name))
                {
                    var t = schema.AddEnum(propType.Name, propType, description);
                    return t;
                }
                else if (createEnumTypes && propType.IsNullableType() && Nullable.GetUnderlyingType(propType).GetTypeInfo().IsEnum && !schema.HasType(Nullable.GetUnderlyingType(propType).Name))
                {
                    Type type = Nullable.GetUnderlyingType(propType);
                    var t = schema.AddEnum(type.Name, type, description);
                    return t;
                }
            }
            else if (schema.HasType(propType.Name))
            {
                return schema.Type(propType.Name);
            }
            return null;
        }
    }
}
