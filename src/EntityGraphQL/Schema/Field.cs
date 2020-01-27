using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using EntityGraphQL.Extensions;

namespace EntityGraphQL.Schema
{
    /// <summary>
    /// Describes an entity field. It's expression based on the base type (your data model) and it's mapped return type
    /// </summary>
    public class Field : IMethodType
    {
        private readonly Dictionary<string, ArgType> allArguments = new Dictionary<string, ArgType>();

        public string Name { get; internal set; }
        public ParameterExpression FieldParam { get; private set; }
        public bool ReturnTypeNotNullable { get; set; }
        public bool ReturnElementTypeNullable { get; set; }

        public Field(string name, LambdaExpression resolve, string description, string returnSchemaType, Type returnClrType, int eh) : this(name, resolve, description, returnSchemaType, returnClrType)
        {
            ReturnTypeClrSingle = returnSchemaType;
            ReturnTypeClr = returnClrType;
        }


        internal Field(string name, LambdaExpression resolve, string description, string returnSchemaType = null, Type returnClrType = null)
        {
            Name = name;
            Description = description;
            ReturnTypeClrSingle = returnSchemaType;
            ReturnTypeClr = returnClrType;

            if (resolve != null)
            {
                Resolve = resolve.Body;
                FieldParam = resolve.Parameters.First();
                ReturnTypeClr = Resolve.Type;

                if (resolve.Body.NodeType == ExpressionType.MemberAccess)
                {
                    ReturnTypeNotNullable = GraphQLNotNullAttribute.IsMemberMarkedNotNull(((MemberExpression)resolve.Body).Member);
                    ReturnElementTypeNullable = GraphQLElementTypeNullable.IsMemberElementMarkedNullable(((MemberExpression)resolve.Body).Member);
                }
                if (ReturnTypeClrSingle == null)
                {
                    if (resolve.Body.Type.IsEnumerableOrArray())
                    {
                        if (!resolve.Body.Type.IsArray && !resolve.Body.Type.GetGenericArguments().Any())
                        {
                            throw new ArgumentException($"We think {resolve.Body.Type} is IEnumerable<> or an array but didn't find it's enumerable type");
                        }
                        ReturnTypeClrSingle = resolve.Body.Type.GetEnumerableOrArrayType().Name;
                    }
                    else
                    {
                        ReturnTypeClrSingle = resolve.Body.Type.Name;
                    }
                }
            }
        }

        public Field(string name, LambdaExpression resolve, string description, string returnSchemaType, object argTypes) : this(name, resolve, description, returnSchemaType)
        {
            this.ArgumentTypesObject = argTypes;
            this.allArguments = argTypes.GetType().GetProperties().ToDictionary(p => p.Name, p => new ArgType
            {
                Type = p.PropertyType,
                TypeNotNullable = GraphQLNotNullAttribute.IsMemberMarkedNotNull(p),
            });
            argTypes.GetType().GetFields().ToDictionary(p => p.Name, p => new ArgType
            {
                Type = p.FieldType,
                TypeNotNullable = GraphQLNotNullAttribute.IsMemberMarkedNotNull(p),
            }).ToList().ForEach(kvp => allArguments.Add(kvp.Key, kvp.Value));
        }

        public Expression Resolve { get; private set; }
        public string Description { get; private set; }
        public string ReturnTypeClrSingle { get; private set; }

        public object ArgumentTypesObject { get; private set; }
        public IDictionary<string, ArgType> Arguments { get { return allArguments; } }

        public IEnumerable<string> RequiredArgumentNames
        {
            get
            {
                if (ArgumentTypesObject == null)
                    return new List<string>();

                var required = ArgumentTypesObject.GetType().GetTypeInfo().GetFields().Where(f => f.FieldType.IsConstructedGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(RequiredField<>)).Select(f => f.Name);
                var requiredProps = ArgumentTypesObject.GetType().GetTypeInfo().GetProperties().Where(f => f.PropertyType.IsConstructedGenericType && f.PropertyType.GetGenericTypeDefinition() == typeof(RequiredField<>)).Select(f => f.Name);
                return required.Concat(requiredProps).ToList();
            }
        }

        public Type ReturnTypeClr { get; private set; }

        public bool HasArgumentByName(string argName)
        {
            return allArguments.ContainsKey(argName);
        }

        public ArgType GetArgumentType(string argName)
        {
            return allArguments[argName];
        }
    }
}