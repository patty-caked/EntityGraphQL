using EntityGraphQL.Compiler;
using EntityGraphQL.NewFeatures;
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

                schema.AddEnum(entry.Key, finished, $"A list of the fields in {entry.Key.Replace("FilterEnum", "")} that can be filtered by.");
                //schema.AddInputType<FilterInput>("FilterInput", "Input Type description").AddAllFields();
                var varTest = Activator.CreateInstance(finished);
                dynamic dynTest = Activator.CreateInstance(finished);
                //schema.FilterAdder(Activator.CreateInstance(finished));
            }

            return types;
        }

        public static void FilterAdder<TContextType, TFilterEnum>(this MappedSchemaProvider<TContextType> schema, TFilterEnum type)
        {
            var blah = typeof(TFilterEnum);
            var typeTest = type.GetType();
            var test = schema.AddInputType<FilterInput<TFilterEnum>>(type.GetType().Name.Replace("FilterEnum", "") + "FilterInput", "Input Type description");
            //Figure out how to replace the "Field" field with the same field of a different type.
            //var parameter = Expression.Parameter(FilterInput);
            //Expression.Lambda(Expression.Property(parameter, f.Name), parameter), description, null));

            //test.ReplaceField("Field",)

            test.AddAllFields();

        }
    }
}