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

        public static List<Type> DictionaryToEnumList<TContextType>(this MappedSchemaProvider<TContextType> schema, Dictionary<string, string[]> enumStringDict)
        {
            /// Helpful info: 
            /// https://stackoverflow.com/questions/41784393/how-to-emit-a-type-in-net-core
            /// https://stackoverflow.com/questions/36937276/is-there-any-replace-of-assemblybuilder-definedynamicassembly-in-net-core

            List<Type> types = new List<Type>();

            
            // Get the current application domain for the current thread.
            //AppDomain currentDomain = AppDomain.CurrentDomain;

            // Create a dynamic assembly in the current application domain, 
            // and allow it to be executed and saved to disk.
            AssemblyName aName = new AssemblyName("EnumAssembly");
            //AssemblyBuilder ab = currentDomain.DefineDynamicAssembly(aName, AssemblyBuilderAccess.RunAndSave);
            AssemblyBuilder ab = AssemblyBuilder.DefineDynamicAssembly(aName, AssemblyBuilderAccess.Run);

            // Define a dynamic module in "TempAssembly" assembly. For a single-
            // module assembly, the module has the same name as the assembly.
            ModuleBuilder mb = ab.DefineDynamicModule(aName.Name);
            
            #region EnumBuilding Reference
            // Define a public enumeration with the name "Elevation" and an 
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
            }
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

                schema.FilterAdder(Activator.CreateInstance(finished), baseName);
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

            /*
            var schemaType = schema.Type(fieldProp.ReturnTypeClrSingle);
            // Find the first field named "id" or "<fieldProp.Name>Id" to turn into a field with arguments
            var idFieldDef = schemaType.GetFields().FirstOrDefault(f => f.Name.ToLower() == "id" || f.Name.ToLower() == $"{fieldProp.Name.ToLower()}id");
            if (idFieldDef == null)
                return;

            // Save a little bit of typing and clean things up.
            var idFieldName = idFieldDef.Name;

            // {ctx => FilterThis(ctx.Item.AsQueryable(), value(EFTest.EntityGraphQL.Mutations.TestMutations+<>c__DisplayClass1_0).args.Filters)}

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
            */

            // making a PropertyOrField expression that represents TContextType.PropertyName
            var asdf = new { filters = new FilterInput[0]};
            var asdfType = asdf.GetType();

            Type arrayContextType = schema.Type(fieldProp.ReturnTypeClrSingle).ContextType;
            var notRequiredArgType = typeof(FilterInput);
            var filterNameAndType = new Dictionary<string, Type> { { "filters", notRequiredArgType } };
            var filterArgTypes = LinqRuntimeTypeBuilder.GetDynamicType(filterNameAndType);
            var filterArgTypesValue = asdfType.GetTypeInfo().GetConstructors()[0].Invoke(new object[] { asdf.filters });
            var filterArgTypeParam = Expression.Parameter(asdfType);

            Expression argFilter = Expression.PropertyOrField(filterArgTypeParam, "filters");

            var dbContextParam = Expression.Parameter(contextType);// A parameter for the database context type. This will be the first lambda parameter
            var ctxField = Expression.PropertyOrField(dbContextParam, "Item");
            //var ctxTable = Expression.Property(ctxField, "Value");
            var queryableMeths = typeof(FilterInput).GetMethods();
            var queryableMethInfo = queryableMeths.FirstOrDefault(method => method.Name == "FilterThisEnumerable" && method.IsGenericMethod == true);
            //Expression methExp = Expression.Call(queryableMethInfo, fieldProp.Resolve, argFilter);
            Expression methExp = Expression.Call(typeof(FilterInput), "FilterThisEnumerable", new Type[] { arrayContextType }, fieldProp.Resolve, argFilter);
            //var asQueryableExp = ExpressionUtil.MakeExpressionCall(new[] { typeof(Queryable), typeof(Enumerable) }, "AsQueryable", new Type[] { arrayContextType }, fieldProp.Resolve, ctxField);
            methExp = new ParameterReplacer().ReplaceByType(methExp, contextType, dbContextParam);
            
            var filterLambdaParams = new[] { dbContextParam, filterArgTypeParam };
            var filterSelectionExpression = Expression.Lambda(methExp, filterLambdaParams);
            // End new stuff

            /*
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
            */
            var name = $"{fieldProp.Name}FilterQuery";
            var field = new Field(name, filterSelectionExpression, $"Return a {fieldProp.ReturnTypeClrSingle} after filtering it", fieldProp.ReturnTypeClrSingle, filterArgTypesValue);
            schema.AddField(field);
        }
    }
}