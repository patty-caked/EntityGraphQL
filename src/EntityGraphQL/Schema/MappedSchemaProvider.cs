using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using EntityGraphQL.Compiler;
using EntityGraphQL.Compiler.Util;
using EntityGraphQL.Extensions;

namespace EntityGraphQL.Schema
{
    /// <summary>
    /// Builder interface to build a schema definition. The built schema definition maps an external view of your data model to you internal model.
    /// This allows your internal model to change over time while not break your external API. You can create new versions when needed.
    /// </summary>
    /// <typeparam name="TContextType">Base object graph. Ex. DbContext</typeparam>
    public class MappedSchemaProvider<TContextType> : ISchemaProvider
    {
        protected Dictionary<string, ISchemaType> types = new Dictionary<string, ISchemaType>();
        protected Dictionary<string, IMethodType> mutations = new Dictionary<string, IMethodType>();
        protected Dictionary<Type, string> customTypeMappings = new Dictionary<Type, string>();

        private readonly string queryContextName;
        private readonly Dictionary<Type, string> customScalarTypes = new Dictionary<Type, string> {
            {typeof(int), "Int"},
            {typeof(float), "Float"},
            {typeof(string), "String"},
            {typeof(bool), "Boolean"},
            {typeof(Guid), "ID"},
        };
        public IEnumerable<string> CustomScalarTypes { get { return customScalarTypes.Values; } }

        public MappedSchemaProvider()
        {
            var queryContext = new SchemaType<TContextType>(typeof(TContextType).Name, "Query schema");
            queryContextName = queryContext.Name;
            types.Add(queryContext.Name, queryContext);

            AddType<Models.TypeElement>("__Type", "Information about types").AddAllFields(this);
            AddType<Models.EnumValue>("__EnumValue", "Information about enums").AddAllFields(this);
            AddType<Models.InputValue>("__InputValue", "Arguments provided to Fields or Directives and the input fields of an InputObject are represented as Input Values which describe their type and optionally a default value.").AddAllFields(this);
            AddType<Models.Directives>("__Directive", "Information about directives").AddAllFields(this);
            AddType<Models.Field>("__Field", "Information about fields").AddAllFields(this);
            AddType<Models.SubscriptionType>("Information about subscriptions").AddAllFields(this);
            AddType<Models.Schema>("__Schema", "A GraphQL Schema defines the capabilities of a GraphQL server. It exposes all available types and directives on the server, as well as the entry points for query, mutation, and subscription operations.").AddAllFields(this);

            Type<Models.TypeElement>("__Type").ReplaceField("enumValues", new { includeDeprecated = false },
                (t, p) => t.EnumValues.Where(f => p.includeDeprecated ? f.IsDeprecated || !f.IsDeprecated : !f.IsDeprecated).ToList(), "Enum values available on type");

            SetupIntrospectionTypesAndField();
        }

        private void SetupIntrospectionTypesAndField()
        {
            var allTypeMappings = SchemaGenerator.DefaultTypeMappings.ToDictionary(k => k.Key, v => v.Value.Trim('!'));
            // add the top level __schema field which is made _at runtime_ currently e.g. introspection could be faster
            foreach (var item in customTypeMappings)
            {
                allTypeMappings[item.Key] = item.Value;
            }
            foreach (var item in customScalarTypes)
            {
                allTypeMappings[item.Key] = item.Value;
            }

            // evaluate Fields lazily so we don't end up in endless loop
            Type<Models.TypeElement>("__Type").ReplaceField("fields", new { includeDeprecated = false },
                (t, p) => SchemaIntrospection.BuildFieldsForType(this, allTypeMappings, t.Name).Where(f => p.includeDeprecated ? f.IsDeprecated || !f.IsDeprecated : !f.IsDeprecated).ToList(), "Fields available on type");


            ReplaceField("__schema", db => SchemaIntrospection.Make(this, allTypeMappings), "Introspection of the schema", "__Schema");
            ReplaceField("__type", new { name = ArgumentHelper.Required<string>() }, (db, p) => SchemaIntrospection.Make(this, allTypeMappings).Types.Where(s => s.Name == p.name).First(), "Query a type by name", "__Type");
        }

        /// <summary>
        /// Add a new type into the schema with TBaseType as it's context
        /// </summary>
        /// <param name="name">Name of the type</param>
        /// <param name="description">description of the type</param>
        /// <typeparam name="TBaseType"></typeparam>
        /// <returns></returns>
        public SchemaType<TBaseType> AddType<TBaseType>(string name, string description)
        {
			var tt = new SchemaType<TBaseType>(name, description);
            types.Add(name, tt);
			return tt;
        }

        public ISchemaType AddType(Type contextType, string name, string description)
        {
			var tt = new SchemaType<object>(contextType, name, description);
            types.Add(name, tt);
			return tt;
        }

        public SchemaType<TBaseType> AddInputType<TBaseType>(string name, string description)
        {
            var tt = new SchemaType<TBaseType>(name, description, true);
            types.Add(name, tt);
			return tt;
        }

        /// <summary>
        /// Add any methods marked with GraphQLMutationAttribute in the given type to the schema. Names are added as lowerCaseCamel`
        /// </summary>
        /// <param name="mutationClassInstance"></param>
        /// <typeparam name="TType"></typeparam>
        public void AddMutationFrom<TType>(TType mutationClassInstance)
        {
            foreach (var method in mutationClassInstance.GetType().GetMethods())
            {
                var attribute = method.GetCustomAttribute(typeof(GraphQLMutationAttribute)) as GraphQLMutationAttribute;
                if (attribute != null)
                {
                    string name = SchemaGenerator.ToCamelCaseStartsLower(method.Name);
                    var mutationType = new MutationType(name, types[GetSchemaTypeNameForRealType(method.ReturnType)], mutationClassInstance, method, attribute.Description);
                    mutations[name] = mutationType;
                }
            }
        }

        public bool HasMutation(string method)
        {
            return mutations.ContainsKey(method);
        }

        public void AddTypeMapping<TFrom>(string gqlType)
        {
            // add mapping
            customTypeMappings.Add(typeof(TFrom), gqlType);
            // add scalar if needed
            if (!HasType(gqlType) && !gqlType.StartsWith("["))
            {
                AddCustomScalarType(typeof(TFrom), gqlType);
            }
            SetupIntrospectionTypesAndField();
        }

        /// <summary>
        /// Adds a new type into the schema. The name defaults to the TBaseType name
        /// </summary>
        /// <param name="description"></param>
        /// <typeparam name="TBaseType"></typeparam>
        /// <returns></returns>
        public SchemaType<TBaseType> AddType<TBaseType>(string description)
        {
            var name = typeof(TBaseType).Name;
            return AddType<TBaseType>(name, description);
        }

        /// <summary>
        /// Add a field to the root type. This is where you define top level objects/names that you can query.
        /// The name defaults to the MemberExpression from selection modified to lowerCamelCase
        /// </summary>
        /// <param name="selection"></param>
        /// <param name="description"></param>
        /// <param name="returnSchemaType"></param>
        public void AddField(Expression<Func<TContextType, object>> selection, string description, string returnSchemaType = null, bool? isNullable = null)
        {
            var exp = ExpressionUtil.CheckAndGetMemberExpression(selection);
            AddField(SchemaGenerator.ToCamelCaseStartsLower(exp.Member.Name), selection, description, returnSchemaType, isNullable);
        }

        /// <summary>
        /// Add a field to the root type. This is where you define top level objects/names that you can query.
        /// Note the name you use is case sensistive. We recommend following GraphQL and useCamelCase as this library will for methods that use Expressions.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="selection"></param>
        /// <param name="description"></param>
        /// <param name="returnSchemaType"></param>
        public void AddField(string name, Expression<Func<TContextType, object>> selection, string description, string returnSchemaType = null, bool? isNullable = null)
        {
            Type<TContextType>().AddField(name, selection, description, returnSchemaType, isNullable);
        }

        public void ReplaceField<TReturn>(string name, Expression<Func<TContextType, TReturn>> selectionExpression, string description, string returnSchemaType = null, bool? isNullable = null)
        {
            Type<TContextType>().RemoveField(name);
            Type<TContextType>().AddField(name, selectionExpression, description, returnSchemaType, isNullable);
        }

        public void ReplaceField<TParams, TReturn>(string name, TParams argTypes, Expression<Func<TContextType, TParams, TReturn>> selectionExpression, string description, string returnSchemaType = null, bool? isNullable = null)
        {
            Type<TContextType>().RemoveField(name);
            Type<TContextType>().AddField(name, argTypes, selectionExpression, description, returnSchemaType, isNullable);
        }

        /// <summary>
        /// Add a field with arguments.
        /// {
        ///     field(arg: val) {}
        /// }
        /// Note the name you use is case sensistive. We recommend following GraphQL and useCamelCase as this library will for methods that use Expressions.
        /// </summary>
        /// <param name="name">Field name</param>
        /// <param name="argTypes">Anonymous object defines the names and types of each argument</param>
        /// <param name="selectionExpression">The expression that selects the data from TContextType using the arguments</param>
        /// <param name="returnSchemaType">The schema type to return, it defines the fields available on the return object. If null, defaults to TReturn type mapped in the schema.</param>
        /// <typeparam name="TParams">Type describing the arguments</typeparam>
        /// <typeparam name="TReturn">The return entity type that is mapped to a type in the schema</typeparam>
        /// <returns></returns>
        public void AddField<TParams, TReturn>(string name, TParams argTypes, Expression<Func<TContextType, TParams, TReturn>> selectionExpression, string description, string returnSchemaType = null, bool? isNullable = null)
        {
            Type<TContextType>().AddField(name, argTypes, selectionExpression, description, returnSchemaType, isNullable);
        }

        /// <summary>
        /// Add a field to the root query.
        /// Note the name you use is case sensistive. We recommend following GraphQL and useCamelCase as this library will for methods that use Expressions.
        /// </summary>
        /// <param name="field"></param>
        public void AddField(Field field)
        {
            types[queryContextName].AddField(field);
        }

        /// <summary>
        /// Get registered type by TType name
        /// </summary>
        /// <typeparam name="TType"></typeparam>
        /// <returns></returns>
        public SchemaType<TType> Type<TType>()
        {
            // look up by the actual type not the name
            return (SchemaType<TType>) types.Values.Where(t => t.ContextType == typeof(TType)).First();
        }
        public SchemaType<TType> Type<TType>(string typeName)
        {
            return (SchemaType<TType>)types[typeName];
        }
        public ISchemaType Type(string typeName)
        {
            return types[typeName];
        }
        // ISchemaProvider interface
        public Type ContextType { get { return types[queryContextName].ContextType; } }
        public bool TypeHasField(string typeName, string identifier, IEnumerable<string> fieldArgs)
        {
            if (!types.ContainsKey(typeName))
                return false;
            var t = types[typeName];
            if (!t.HasField(identifier))
            {
                if ((fieldArgs == null || !fieldArgs.Any()) && t.HasField(identifier))
                {
                    var field = t.GetField(identifier);
                    if (field != null)
                    {
                        // if there are defaults for all, continue
                        if (field.RequiredArgumentNames.Any())
                        {
                            throw new EntityGraphQLCompilerException($"Field '{identifier}' missing required argument(s) '{string.Join(", ", field.RequiredArgumentNames)}'");
                        }
                        return true;
                    }
                    else
                    {
                        throw new EntityGraphQLCompilerException($"Field '{identifier}' not found on current context '{typeName}'");
                    }
                }
                return false;
            }
            return true;
        }

        public bool TypeHasField(Type type, string identifier, IEnumerable<string> fieldArgs)
        {
            return TypeHasField(type.Name, identifier, fieldArgs);
        }
        public string GetActualFieldName(string typeName, string identifier)
        {
            if (types.ContainsKey(typeName) && types[typeName].HasField(identifier))
                return types[typeName].GetField(identifier).Name;
            if (typeName == queryContextName && types[queryContextName].HasField(identifier))
                return types[queryContextName].GetField(identifier).Name;
            throw new EntityGraphQLCompilerException($"Field {identifier} not found on any type");
        }

        public IMethodType GetFieldType(Expression context, string fieldName)
        {
            if (mutations.ContainsKey(fieldName))
            {
                var mutation = mutations[fieldName];
                return mutation;
            }
            if (types.ContainsKey(GetSchemaTypeNameForRealType(context.Type)))
            {
                var field = types[GetSchemaTypeNameForRealType(context.Type)].GetField(fieldName);
                return field;
            }
            throw new EntityGraphQLCompilerException($"No field or mutation '{fieldName}' found in schema.");
        }

        public ExpressionResult GetExpressionForField(Expression context, string typeName, string fieldName, Dictionary<string, ExpressionResult> args)
        {
            if (!types.ContainsKey(typeName))
                throw new EntityQuerySchemaException($"{typeName} not found in schema.");

            var field = types[typeName].GetField(fieldName);
            var result = new ExpressionResult(field.Resolve ?? Expression.Property(context, fieldName));

            if (field.ArgumentTypesObject != null)
            {
                var argType = field.ArgumentTypesObject.GetType();
                // get the values for the argument anonymous type object constructor
                var propVals = new Dictionary<PropertyInfo, object>();
                var fieldVals = new Dictionary<FieldInfo, object>();
                // if they used AddField("field", new { id = Required<int>() }) the compiler makes properties and a constructor with the values passed in
                foreach (var argField in argType.GetProperties())
                {
                    var val = BuildArgumentFromMember(args, field, argField.Name, argField.PropertyType, argField.GetValue(field.ArgumentTypesObject));
                    // if this was a EntityQueryType we actually get a Func from BuildArgumentFromMember but the anonymous type requires EntityQueryType<>. We marry them here, this allows users to EntityQueryType<> as a Func in LINQ methods while not having it defined until runtime
                    if (argField.PropertyType.IsConstructedGenericType && argField.PropertyType.GetGenericTypeDefinition() == typeof(EntityQueryType<>))
                    {
                        var queryVal = argField.GetValue(field.ArgumentTypesObject);
                        // set HasValue
                        var hasValue = val != null;
                        var genericProp = queryVal.GetType().GetProperty("HasValue");
                        genericProp.SetValue(queryVal, hasValue);
                        if (hasValue)
                        {
                            // set Query
                            genericProp = queryVal.GetType().GetProperty("Query");
                            genericProp.SetValue(queryVal, ((dynamic)val).Expression);
                        }

                        propVals.Add(argField, queryVal);
                    }
                    else
                    {
                        if (val != null && val.GetType() != argField.PropertyType)
                            val = ExpressionUtil.ChangeType(val, argField.PropertyType);
                        propVals.Add(argField, val);
                    }
                }
                // The auto argument is built at runtime from LinqRuntimeTypeBuilder which just makes public fields
                // they could also use a custom class, so we need to look for both fields and properties
                foreach (var argField in argType.GetFields())
                {
                    var val = BuildArgumentFromMember(args, field, argField.Name, argField.FieldType, argField.GetValue(field.ArgumentTypesObject));
                    fieldVals.Add(argField, val);
                }

                // create a copy of the anonymous object. It will have the default values set
                // there is only 1 constructor for the anonymous type that takes all the property values
                var con = argType.GetConstructor(propVals.Keys.Select(v => v.PropertyType).ToArray());
                object parameters;
                if (con != null)
                {
                    parameters = con.Invoke(propVals.Values.ToArray());
                    foreach (var item in fieldVals)
                    {
                        item.Key.SetValue(parameters, item.Value);
                    }
                }
                else
                {
                    // expect an empty constructor
                    con = argType.GetConstructor(new Type[0]);
                    parameters = con.Invoke(new object[0]);
                    foreach (var item in fieldVals)
                    {
                        item.Key.SetValue(parameters, item.Value);
                    }
                    foreach (var item in propVals)
                    {
                        item.Key.SetValue(parameters, item.Value);
                    }
                }
                // tell them this expression has another parameter
                var argParam = Expression.Parameter(argType);
                result.Expression = new ParameterReplacer().ReplaceByType(result.Expression, argType, argParam);
                result.AddConstantParameter(argParam, parameters);
            }

            // the expressions we collect have a different starting parameter. We need to change that
            var paramExp = field.FieldParam;
            result.Expression = new ParameterReplacer().Replace(result.Expression, paramExp, context);

            return result;
        }

        private static object BuildArgumentFromMember(Dictionary<string, ExpressionResult> args, Field field, string memberName, Type memberType, object defaultValue)
        {
            string argName = memberName;
            // check we have required arguments
            if (memberType.GetGenericArguments().Any() && memberType.GetGenericTypeDefinition() == typeof(RequiredField<>))
            {
                if (args == null || !args.ContainsKey(argName))
                {
                    throw new EntityGraphQLCompilerException($"Field '{field.Name}' missing required argument '{argName}'");
                }
                var item = Expression.Lambda(args[argName]).Compile().DynamicInvoke();
                var constructor = memberType.GetConstructor(new [] {item.GetType()});
                if (constructor == null)
                {
                    // we might need to change the type
                    foreach (var c in memberType.GetConstructors())
                    {
                        var parameters = c.GetParameters();
                        if (parameters.Count() == 1)
                        {
                            item = ExpressionUtil.ChangeType(item, parameters[0].ParameterType);
                            constructor = memberType.GetConstructor(new [] {item.GetType()});
                            break;
                        }
                    }
                }

                if (constructor == null)
                {
                    throw new EntityGraphQLCompilerException($"Could not find a constructor for type {memberType.Name} that takes value '{item}'");
                }

                var typedVal = constructor.Invoke(new [] {item});
                return typedVal;
            }
            else if (defaultValue != null && defaultValue.GetType().IsConstructedGenericType && defaultValue.GetType().GetGenericTypeDefinition() == typeof(EntityQueryType<>))
            {
                return args != null && args.ContainsKey(argName) ? args[argName] : null;
            }
            else if (args != null && args.ContainsKey(argName))
            {
                return Expression.Lambda(args[argName]).Compile().DynamicInvoke();
            }
            else
            {
                // set the default value
                return defaultValue;
            }
        }

        public string GetSchemaTypeNameForRealType(Type type)
        {
            if (type.GetTypeInfo().BaseType == typeof(LambdaExpression))
            {
                // This should be Expression<Func<Context, ReturnType>>
                type = type.GetGenericArguments()[0].GetGenericArguments()[1];
                if (type.IsEnumerableOrArray())
                {
                    type = type.GetGenericArguments()[0];
                }
            }
            if (type == types[queryContextName].ContextType)
                return type.Name;

            foreach (var eType in types.Values)
            {
                if (eType.ContextType == type)
                    return eType.Name;
            }
            throw new EntityGraphQLCompilerException($"No mapped entity found for type '{type}'");
        }

        public bool HasType(string typeName)
        {
            return types.ContainsKey(typeName);
        }

        public bool HasType(Type type)
        {
            if (type == types[queryContextName].ContextType)
                return true;

            foreach (var eType in types.Values)
            {
                if (eType.ContextType == type)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Builds a GraphQL schema file
        /// </summary>
        /// <returns></returns>
        public string GetGraphQLSchema()
        {
            var extraMappings = customTypeMappings.ToDictionary(k => k.Key, v => v.Value);
            return SchemaGenerator.Make(this, extraMappings, this.customScalarTypes);
        }

        public void AddCustomScalarType(Type clrType, string gqlTypeName, bool required = false)
        {
            this.customScalarTypes[clrType] = gqlTypeName;
            this.customTypeMappings[clrType] = required ? gqlTypeName + "!" : gqlTypeName;
            // _customScalarMappings has change, need to make the introspectino again. Do this like this so we don't need to build the mappings inline
            SetupIntrospectionTypesAndField();
        }

        public IEnumerable<Field> GetQueryFields()
        {
            return types[queryContextName].GetFields();
        }

        public IEnumerable<ISchemaType> GetNonContextTypes()
        {
            return types.Values.Where(s => s.Name != queryContextName).ToList();
        }

        public IEnumerable<IMethodType> GetMutations()
        {
            return mutations.Values.ToList();
        }

        /// <summary>
        /// Remove type and any field that returns that type
        /// </summary>
        /// <typeparam name="TSchemaType"></typeparam>
        public void RemoveTypeAndAllFields<TSchemaType>()
        {
            this.RemoveTypeAndAllFields(typeof(TSchemaType).Name);
        }
        /// <summary>
        /// Remove type and any field that returns that type
        /// </summary>
        /// <param name="typeName"></param>
        public void RemoveTypeAndAllFields(string typeName)
        {
            foreach (var context in types.Values)
            {
                RemoveFieldsOfType(typeName, context);
            }
            types.Remove(typeName);
        }

        private void RemoveFieldsOfType(string typeName, ISchemaType contextType)
        {
            foreach (var field in contextType.GetFields().ToList())
            {
                if (field.ReturnTypeClrSingle == typeName)
                {
                    contextType.RemoveField(field.Name);
                }
            }
        }

        public ISchemaType AddEnum(string name, Type type, string description)
        {
            var schemaType = new SchemaType<object>(type, name, description, false, true);
            types.Add(name, schemaType);
            return schemaType.AddAllFields(this);
        }
    }
}