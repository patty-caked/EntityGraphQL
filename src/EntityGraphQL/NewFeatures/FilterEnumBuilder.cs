using EntityGraphQL.Compiler;
using EntityGraphQL.Compiler.Util;
using EntityGraphQL.Extensions;
using EntityGraphQL.NewFeatures;
using Humanizer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace EntityGraphQL.Schema
{
    public static class FilterEnumBuilder
    {
        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TContextType">The name of your database context class</typeparam>
        /// <typeparam name="TAppDomain">MUST BE TYPE AppDomain</typeparam>
        /// <param name="schema"></param>
        /// <param name="appDomain">Pass AppDomain.CurrentDomain here.</param>
        public static void AddFilterFieldEnums<TContextType>(this MappedSchemaProvider<TContextType> schema)
        {
            //if (typeof(TAppDomain).Name != "AppDomain")
            //    throw new EntityGraphQLCompilerException($"Wrong type for {appDomain}. Must be of type 'AppDomain'.");

            Type dbContextType = typeof(TContextType);
            PropertyInfo[] fields = dbContextType.GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public);
            Dictionary<string, string[]> dbSets = new Dictionary<string, string[]>();

            // We need a list of Types to ignore when making our list of properties
            Type[] unacceptableTypes = fields.Where(pi => pi.PropertyType.GenericTypeArguments.Any()).Select(pi => pi.PropertyType.GenericTypeArguments.First()).ToArray();

            foreach (PropertyInfo propInfo in fields)
            {
                var propName = propInfo.Name;

                dbSets.Add(SchemaGenerator.ToCamelCaseStartsLower(propName + "FilterEnum"), GetPropNameList(propInfo, unacceptableTypes));
            }


            /// Todo: Make this add all these "enums" to this schema.

            List<Type> enumList = schema.DictionaryToEnumList(dbSets);


        }

        /// <summary>
        /// Takes a <paramref name="fieldName"/> and makes an enum out of it and it's fields
        /// </summary>
        /// <typeparam name="TContextType"></typeparam>
        /// <param name="schema"></param>
        /// <param name="fieldName"></param>
        /// <returns></returns>
        public static Type SingleEnumGenerator<TContextType>(this MappedSchemaProvider<TContextType> schema, string fieldName)
        {
            Type dbContextType = typeof(TContextType);
            PropertyInfo field = dbContextType.GetProperty(fieldName, BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public);
            if (field == null)
                return typeof(string);

            // Get a list of types from our dbContext to ignore when making our enum values
            PropertyInfo[] dbFields = dbContextType.GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public);
            Type[] unacceptableTypes = dbFields.Where(pi => pi.PropertyType.GenericTypeArguments.Any()).Select(pi => pi.PropertyType.GenericTypeArguments.First()).ToArray();

            // In practice, this should only hold one item.
            Dictionary<string, string[]> enumAsDictionary = new Dictionary<string, string[]>();

            string[] enumValueNames = GetPropNameList(field, unacceptableTypes);
            enumAsDictionary.Add($"{fieldName}FilterEnum", enumValueNames);

            List<Type> enumList = schema.DictionaryToEnumList(enumAsDictionary);

            // Make a solid generic FilterInput<> type and pass it to the "AddInputType" method with an expression
            var genericFilterInputType = typeof(FilterInput<>).MakeGenericType(enumList.First());
            //Expression methExp = Expression.Call(typeof(FilterInput), "FilterThisQueryable", new Type[] { genericFilterInputType }, fieldProp.Resolve, argFilter);
            var result = schema.AddFilterInputType(genericFilterInputType, $"{fieldName}FilterInput", $"Dynamically generated input type for the {fieldName} query.");
            

            return enumList.First();
        }

        public static ISchemaType AddFilterInputType<TContextType>(this MappedSchemaProvider<TContextType> schema, Type filterType, string name, string description)
        {
            if (filterType is null)
            {
                throw new ArgumentNullException(nameof(filterType));
            }

            // Make a generic SchemaType<>
            Type genericSchemaType = typeof(SchemaType<>).MakeGenericType(filterType);

            // Get the MethodInfo for the AddAllFields method of our genericSchemaType
            MethodInfo addAllFieldsMethodInfo = genericSchemaType.GetMethod("AddAllFields");


            // Get method info representing the generic method "AddInputType."
            MethodInfo mi = schema.GetType().GetMethod("AddInputType");

            // Assign the filterType to the type parameter of the schema method.
            MethodInfo miConstructed = mi.MakeGenericMethod(filterType);

            // Invoke the method.
            string[] args = { name, description };
            dynamic result = miConstructed.Invoke(schema, args);

            // Assign our TContextType to the type parameter of our addAllFields method.
            MethodInfo schemaMiConstructed = addAllFieldsMethodInfo.MakeGenericMethod(typeof(TContextType));
            //Invoke our addAllFields method.
            object[] args2 = { schema, true, true };
            dynamic addFieldsResult = schemaMiConstructed.Invoke(result, args2);

            //schema.AddInputType<FilterInput<TBaseType>>(name, description);
            return addFieldsResult;
        }

        /// <summary>
        /// Returns a list of property names to be used in filtering
        /// </summary>
        /// <param name="table"></param>
        /// <param name="unacceptableTypes">A list of Types to filter by.</param>
        /// <returns></returns>
        public static string[] GetPropNameList(PropertyInfo table, Type[] unacceptableTypes)
        {
            Type propType = table.PropertyType.GenericTypeArguments.First();
            PropertyInfo[] tableProps = propType.GetProperties();
            List<string> nameList = new List<string>();

            // where tp is a property in a DbSet within the database context 
            foreach (PropertyInfo tp in tableProps)
            {
                // if tp is not the same type as any unacceptable Types
                if (!unacceptableTypes.Contains(tp.PropertyType))
                {
                    nameList.Add(SchemaGenerator.ToCamelCaseStartsLower(tp.Name));
                }
            }

            return nameList.ToArray();
        }


        // Create a dynamic assembly in the current application domain, 
        // and allow it to be executed and saved to disk.
        public static AssemblyName aName = new AssemblyName() { Name = "EnumAssembly" };
        //AssemblyBuilder ab = currentDomain.DefineDynamicAssembly(aName, AssemblyBuilderAccess.RunAndSave);
        //public static AssemblyBuilder ab = AssemblyBuilder.DefineDynamicAssembly(aName, AssemblyBuilderAccess.Run);

        // Define a dynamic module in "TempAssembly" assembly. For a single-
        // module assembly, the module has the same name as the assembly.
        public static ModuleBuilder mb = AssemblyBuilder.DefineDynamicAssembly(aName, AssemblyBuilderAccess.Run).DefineDynamicModule(aName.Name);

        public static List<Type> DictionaryToEnumList<TContextType>(this MappedSchemaProvider<TContextType> schema, Dictionary<string, string[]> enumStringDict)
        {
            /// Helpful info: 
            /// https://stackoverflow.com/questions/41784393/how-to-emit-a-type-in-net-core
            /// https://stackoverflow.com/questions/36937276/is-there-any-replace-of-assemblybuilder-definedynamicassembly-in-net-core

            List<Type> types = new List<Type>();

            
            // Get the current application domain for the current thread.
            //AppDomain currentDomain = AppDomain.CurrentDomain;

            
            
            #region EnumBuilding Reference
            /*// Define a public enumeration with the name "Elevation" and an 
            // underlying type of Integer.
            EnumBuilder ebTest = mb.DefineEnum("Elevation", TypeAttributes.Public, typeof(int));

            // Define two members, "High" and "Low".
            ebTest.DefineLiteral("Low", 0);
            ebTest.DefineLiteral("High", 1);

            // Create the type and save the assembly.
            Type finishedTest = ebTest.CreateTypeInfo().AsType();
            //ab.Save(aName.Name + ".dll");
            foreach (object o in Enum.GetValues(finishedTest))
            {
                Console.WriteLine("{0}.{1} = {2}", finishedTest, o, ((int)o));
            }*/
            #endregion


            foreach (KeyValuePair<string, string[]> entry in enumStringDict)
            {
                // Define a public enumeration with the name held at entry.Key and an 
                // underlying type of string.
                EnumBuilder eb = mb.DefineEnum(entry.Key, TypeAttributes.Public, typeof(int));
                int index = 0;
                foreach (string member in entry.Value)
                {
                    // Define the member of the enum.
                    eb.DefineLiteral(member, index);
                    index++;
                }
                // Create the type and save the assembly.
                Type finished = eb.CreateTypeInfo().AsType();
                types.Add(finished);

                string baseName = entry.Key.Replace("FilterEnum", "");

                schema.AddEnum(entry.Key, finished, $"A list of the fields in {baseName} that can be filtered by.");

                //schema.FilterAdder(Activator.CreateInstance(finished), baseName);
            }

            return types;
        }

        /// <summary>
        /// Adds a FilterInput input type to the schema and dynamically changes the "Field" field's type to mach a given enum type.
        /// </summary>
        /// <typeparam name="TContextType"></typeparam>
        /// <typeparam name="TFilterEnum"></typeparam>
        /// <param name="schema"></param>
        /// <param name="type">An instance of the given enum type to change to.</param>
        /// <param name="filterNamePrefix">Probably don't need this. Update later.</param>
        public static void FilterAdder<TContextType, TFilterEnum>(this MappedSchemaProvider<TContextType> schema, TFilterEnum type, string filterNamePrefix)
        {
            //var blah = typeof(TFilterEnum);
            //Should hold the type of enum we've generated
            var typeTest = type.GetType(); 



            var inType = schema.AddInputType<FilterInput>($"{filterNamePrefix}FilterInput", "Dynamically generated input type.").AddAllFields(schema, true, true);
            Field origField = inType.GetField("field");

            //var memInfo = origField.ReturnTypeClr.pro
            //PropertyInfo propInfo = typeof(FilterInput).GetProperty("Field");
            //ParameterExpression pe = ParameterExpression.Parameter(typeTest);
            LambdaExpression le = Expression.Lambda(Expression.Property(origField.FieldParam, "Field"), origField.FieldParam);

            //var newResolve = origField.Resolve;
            Field newField = new Field("field", le, "InputField field made on the fly.", $"{filterNamePrefix}FilterEnum", typeTest, 0);

            //inType.ReplaceField()
            inType.RemoveField("field");
            inType.AddField(newField);
        }

        public static void AddFieldWithFilterArgument<TContextType>(MappedSchemaProvider<TContextType> schema, Type contextType, Field fieldProp)
        {
            if (!fieldProp.Resolve.Type.IsEnumerableOrArray())
                return;


            // We need to build a lambda resembling (ctx, filters) => FilterThisQueryable(ctx.Item, filters)

            // Make our FilterInput<enum> generic type that will be used for our lambda argument
            //var dynamicEnum = schema.SingleEnumGenerator(fieldProp.Name.Capitalize());
            //var genericFilterInputType = typeof(FilterInput<>).MakeGenericType(dynamicEnum);

            //var newFilters = Array.CreateInstance(genericFilterInputType, 0);
            //dynamic newFilters = Activator.CreateInstance(genericFilterInputType.MakeArrayType(), 0);
            //var arrayType = newFilters.GetType();

            // Make a generic version of the "filters" array below.
            //var filterArrayInfo = genericFilterInputType.MakeArrayType().GetTypeInfo();
            //var filterConstruct = filterArrayInfo.GetConstructors()[0];
            //var genericFilters = filterConstruct.Invoke(new object[] { 0 });

            //var filters = new FilterInput[0];

            var anonFilterArray = new { filters = new FilterInput[0], sortBy = new SortInput(), page = new int?(), pagesize = new int?() };
            //var anonFilterArray = new TestArgs();
            var anonFilterArrayType = anonFilterArray.GetType();

            Type arrayContextType = schema.Type(fieldProp.ReturnTypeClrSingle).ContextType;
            //var notRequiredArgType = typeof(FilterInput<string>).MakeArrayType();
            //var filterNameAndType = new Dictionary<string, Type> { { "filters", genericFilterInputType.MakeArrayType() } };
            //var filterArgTypes = LinqRuntimeTypeBuilder.GetDynamicType(filterNameAndType);
            var filterArgTypesValue = anonFilterArrayType.GetTypeInfo().GetConstructors()[0].Invoke(new object[0]);
            var filterArgTypeParam = Expression.Parameter(anonFilterArrayType);

            // making a PropertyOrField expression that represents TContextType.PropertyName
            var argFilter = Expression.PropertyOrField(filterArgTypeParam, "filters");
            var argSort = Expression.PropertyOrField(filterArgTypeParam, "sortBy");
            var argPage = Expression.PropertyOrField(filterArgTypeParam, "page");
            var argPageSize = Expression.PropertyOrField(filterArgTypeParam, "pagesize");

            var dbContextParam = Expression.Parameter(contextType);// A parameter for the database context type. This will be the first lambda parameter
            //var ctxField = Expression.PropertyOrField(dbContextParam, "Item");
            //var ctxTable = Expression.Property(ctxField, "Value");
            //var queryableMeths = genericFilterInputType.GetMethod("FilterThisQueryable");
            //var queryableMethInfo = queryableMeths.FirstOrDefault(method => method.Name == "FilterThisQueryable" && method.IsGenericMethod == true);
            //Expression methExp = Expression.Call(queryableMethInfo, fieldProp.Resolve, argFilter);
            //Expression methExp = Expression.Call(queryableMeths.DeclaringType, queryableMeths.Name, new Type[] { arrayContextType }, fieldProp.Resolve, argFilter);
            Expression methExp = Expression.Call(typeof(FilterInput), "FilterThisQueryable", new Type[] { arrayContextType }, fieldProp.Resolve, argFilter, argSort, argPage, argPageSize);
            //var asQueryableExp = ExpressionUtil.MakeExpressionCall(new[] { typeof(Queryable), typeof(Enumerable) }, "AsQueryable", new Type[] { arrayContextType }, fieldProp.Resolve, ctxField);
            methExp = new ParameterReplacer().ReplaceByType(methExp, contextType, dbContextParam);
            
            var filterLambdaParams = new[] { dbContextParam, filterArgTypeParam };
            var filterSelectionExpression = Expression.Lambda(methExp, filterLambdaParams);
            // End new stuff

            var name = fieldProp.Name.Singularize();
            if (name == null)
            {
                // If we can't singularize it just use the name plus something as GraphQL doesn't support field overloads
                name = $"{fieldProp.Name}";
            }
            var field = new Field(name, filterSelectionExpression, $"Return a {fieldProp.ReturnTypeClrSingle} after filtering it", fieldProp.ReturnTypeClrSingle, filterArgTypesValue);
            schema.AddField(field);


            // Not needed right now, but might be later
            /*MethodInfo mi = schema.GetType().GetMethod("AddTypeMapping");

            // Assign the filterType to the type parameter of the schema method.
            MethodInfo miConstructed = mi.MakeGenericMethod(genericFilterInputType);
            //string inputName = $"{fieldProp.Name.Capitalize()}FilterInput";
            // Invoke the method.
            string[] args = { $"{fieldProp.Name.Capitalize()}FilterInput" };
            miConstructed.Invoke(schema, args);*/

            
        }

        public static void AddField<TContextType>(this MappedSchemaProvider<TContextType> schema, Field field, bool? isNullable = null)
        {
            schema.Type<TContextType>().AddField(field, isNullable);
        }

        public static void AddField<TBaseType>(this SchemaType<TBaseType> schemaType, Field field, bool? isNullable = null)
        {
            if (isNullable.HasValue)
                field.ReturnTypeNotNullable = !isNullable.Value;
            var bleh = field.RequiredArgumentNames;
            schemaType.AddField(field);
        }

        public class TestArgs
        {
            public FilterInput[] filters { get; set; }
            public SortInput sortBy { get; set; }
            public int? page { get; set; }
            public int? pagesize { get; set; }
        }
    }
}