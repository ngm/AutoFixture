﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace Ploeh.SemanticComparison
{
    internal static class ProxyGenerator
    {
        private const string assemblyName = "SemanticComparisonGeneratedAssembly";

        internal static TDestination CreateLikenessProxy<TSource, TDestination>(TSource source, IEqualityComparer comparer, IEnumerable<MemberInfo> members)
        {
            ProxyType proxyType = ProxyGenerator.FindCompatibleConstructor<TDestination>(source, members.ToArray());
            TypeBuilder builder = ProxyGenerator.BuildType<TDestination>(BuildModule(BuildAssembly(assemblyName)));
            FieldBuilder equals = ProxyGenerator.BuildFieldComparer(builder, comparer.GetType());

            ProxyGenerator.BuildConstructors<TDestination>(proxyType.Constructor, builder, equals);
            ProxyGenerator.BuildMethodEquals(builder, BuildFieldEqualsHasBeenCalled(builder), equals);
            ProxyGenerator.BuildMethodGetHashCode<TDestination>(builder);

            Type proxy = builder.CreateType();

            var destination = (TDestination)Activator.CreateInstance(
                proxy, 
                proxyType.Parameters.Concat(new[] { comparer }).ToArray());

            ProxyGenerator.Map(source, destination);

            return destination;
        }

        internal static T CreateLikenessResemblance<T>(Likeness<T> likeness)
        {
            var members = typeof(T)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Concat(typeof(T)
                    .GetFields(BindingFlags.Public | BindingFlags.Instance)
                    .Cast<MemberInfo>())
                .ToArray();

            ProxyType proxyType = 
                ProxyGenerator.FindCompatibleConstructor<T>(
                    likeness.Value,
                    members);

            TypeBuilder builder = 
                ProxyGenerator.BuildType<T>(
                    ProxyGenerator.BuildModule(
                        ProxyGenerator.BuildAssembly(assemblyName)));

            FieldBuilder equals =
                ProxyGenerator.BuildFieldComparer(builder, likeness.GetType());

            ProxyGenerator.BuildConstructors<T>(
                proxyType.Constructor, 
                builder,
                equals);

            ProxyGenerator.BuildMethodEquals(builder, equals);
            ProxyGenerator.BuildMethodGetHashCode<T>(builder);

            return (T)Activator.CreateInstance(
                builder.CreateType(),
                proxyType.Parameters.Concat(
                    new[] { likeness }).ToArray());
        }

        private static AssemblyBuilder BuildAssembly(string name)
        {
            var an = new AssemblyName(name) { Version = Assembly.GetExecutingAssembly().GetName().Version };
            return AppDomain.CurrentDomain.DefineDynamicAssembly(an, AssemblyBuilderAccess.RunAndSave);
        }

        private static ModuleBuilder BuildModule(AssemblyBuilder ab)
        {
            return ab.DefineDynamicModule(assemblyName, assemblyName + ".dll");
        }

        private static TypeBuilder BuildType<TDestination>(ModuleBuilder mb)
        {
            TypeBuilder type = mb.DefineType(
                typeof(TDestination).Name + "Proxy" + Guid.NewGuid().ToString().Replace("-", ""),
                TypeAttributes.NotPublic,
                typeof(TDestination));
            return type;
        }

        private static FieldBuilder BuildFieldEqualsHasBeenCalled(TypeBuilder type)
        {
            FieldBuilder field = type.DefineField(
                "equalsHasBeenCalled",
                typeof(bool),
                  FieldAttributes.Private
                );
            return field;
        }

        private static FieldBuilder BuildFieldComparer(
            TypeBuilder type, 
            Type comparerType)
        {
            FieldBuilder field = type.DefineField(
                "comparer",
                comparerType,
                FieldAttributes.Private | FieldAttributes.InitOnly);
            return field;
        }

        private static void BuildConstructors<TDestination>(ConstructorInfo ci, TypeBuilder type, FieldInfo comparer)
        {
            Type[] constructorParameterTypes = BuildConstructorParameterTypes(ci, comparer.FieldType).ToArray();

            ConstructorBuilder constructor = type.DefineConstructor(
                MethodAttributes.Public | MethodAttributes.HideBySig,
                CallingConventions.Standard,
                constructorParameterTypes);

            for (int position = 1; position <= constructorParameterTypes.Length; position++)
            {
                constructor.DefineParameter(position, ParameterAttributes.None, "arg" + position);
            }

            ILGenerator gen = constructor.GetILGenerator();

            for (int position = 0; position < constructorParameterTypes.Length; position++)
            {
                if (position == 0)
                {
                    gen.Emit(OpCodes.Ldarg_0);
                }
                else if (position == 1)
                {
                    gen.Emit(OpCodes.Ldarg_1);
                }
                else if (position == 2)
                {
                    gen.Emit(OpCodes.Ldarg_2);
                }
                else if (position == 3)
                {
                    gen.Emit(OpCodes.Ldarg_3);
                }
                else
                {
                    gen.Emit(OpCodes.Ldarg_S, position);
                }
            }

            gen.Emit(OpCodes.Call, ci);
            gen.Emit(OpCodes.Nop);
            gen.Emit(OpCodes.Nop);
            gen.Emit(OpCodes.Ldarg_0);
            gen.Emit(OpCodes.Ldarg_S, constructorParameterTypes.Length);
            gen.Emit(OpCodes.Stfld, comparer);
            gen.Emit(OpCodes.Nop);
            gen.Emit(OpCodes.Ret);
        }

        private static IEnumerable<Type> BuildConstructorParameterTypes(ConstructorInfo baseConstructor, Type comparerType)
        {
            return (from pi in baseConstructor.GetParameters()
                    select pi.ParameterType)
                    .Concat(new[] { comparerType });
        }

        private static void BuildMethodGetHashCode<TDestination>(TypeBuilder type)
        {
            var methodAttributes = MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig;
            MethodBuilder method = type.DefineMethod("GetHashCode", methodAttributes);
            method.SetReturnType(typeof(int));

            int derivedGetHashCode = 135;
            MethodInfo getHashCode = typeof(TDestination).GetMethod(
                "GetHashCode",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new Type[] { },
                null
                );

            ILGenerator gen = method.GetILGenerator();
            gen.DeclareLocal(typeof(int));
            
            Label label = gen.DefineLabel();
            
            gen.Emit(OpCodes.Nop);
            gen.Emit(OpCodes.Ldarg_0);
            gen.Emit(OpCodes.Call, getHashCode);
            gen.Emit(OpCodes.Ldc_I4, derivedGetHashCode);
            gen.Emit(OpCodes.Add);
            gen.Emit(OpCodes.Stloc_0);
            gen.Emit(OpCodes.Br_S, label);
            gen.MarkLabel(label);
            gen.Emit(OpCodes.Ldloc_0);
            gen.Emit(OpCodes.Ret);
        }

        private static void BuildMethodEquals(TypeBuilder type, FieldInfo equalsHasBeenCalled, FieldInfo comparer)
        {
            var methodAttributes = MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig;
            MethodBuilder method = type.DefineMethod("Equals", methodAttributes);

            MethodInfo objectEquals = typeof(object).GetMethod(
                "Equals",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(object) },
                null
                );

            MethodInfo equalityComparerEquals = typeof(IEqualityComparer).GetMethod(
                "Equals",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(object), typeof(object) },
                null
                );
            
            method.SetReturnType(typeof(bool));
            method.SetParameters(typeof(object));
            method.DefineParameter(1, ParameterAttributes.None, "obj");
            
            ILGenerator gen = method.GetILGenerator();
            gen.DeclareLocal(typeof(bool));
            gen.DeclareLocal(typeof(bool));
            
            Label label1 = gen.DefineLabel();
            Label label2 = gen.DefineLabel();

            gen.Emit(OpCodes.Nop);
            gen.Emit(OpCodes.Ldarg_0);
            gen.Emit(OpCodes.Ldfld, equalsHasBeenCalled);
            gen.Emit(OpCodes.Ldc_I4_0);
            gen.Emit(OpCodes.Ceq);
            gen.Emit(OpCodes.Stloc_1);
            gen.Emit(OpCodes.Ldloc_1);
            gen.Emit(OpCodes.Brtrue_S, label1);
            gen.Emit(OpCodes.Nop);
            gen.Emit(OpCodes.Ldarg_0);
            gen.Emit(OpCodes.Ldc_I4_0);
            gen.Emit(OpCodes.Stfld, equalsHasBeenCalled);
            gen.Emit(OpCodes.Ldarg_0);
            gen.Emit(OpCodes.Ldarg_1);
            gen.Emit(OpCodes.Call, objectEquals);
            gen.Emit(OpCodes.Stloc_0);
            gen.Emit(OpCodes.Br_S, label2);
            gen.MarkLabel(label1);
            gen.Emit(OpCodes.Ldarg_0);
            gen.Emit(OpCodes.Ldc_I4_1);
            gen.Emit(OpCodes.Stfld, equalsHasBeenCalled);
            gen.Emit(OpCodes.Ldarg_0);
            gen.Emit(OpCodes.Ldfld, comparer);
            gen.Emit(OpCodes.Ldarg_0);
            gen.Emit(OpCodes.Ldarg_1);
            gen.Emit(OpCodes.Callvirt, equalityComparerEquals);
            gen.Emit(OpCodes.Stloc_0);
            gen.Emit(OpCodes.Br_S, label2);
            gen.MarkLabel(label2);
            gen.Emit(OpCodes.Ldloc_0);
            gen.Emit(OpCodes.Ret);
        }

        private static void BuildMethodEquals(TypeBuilder type, FieldInfo comparer)
        {
            var methodAttributes = MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig;
            MethodBuilder method = type.DefineMethod("Equals", methodAttributes);

            MethodInfo equals = typeof(object).GetMethod(
                "Equals",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(object) },
                null
                );

            method.SetReturnType(typeof(bool));
            method.SetParameters(typeof(object));

            ILGenerator gen = method.GetILGenerator();
            gen.DeclareLocal(typeof(bool));

            var label = gen.DefineLabel();

            gen.Emit(OpCodes.Nop);
            gen.Emit(OpCodes.Ldarg_0);
            gen.Emit(OpCodes.Ldfld, comparer);
            gen.Emit(OpCodes.Ldarg_1);
            gen.Emit(OpCodes.Callvirt, equals);
            gen.Emit(OpCodes.Stloc_0);
            gen.Emit(OpCodes.Br_S, label);
            gen.MarkLabel(label);
            gen.Emit(OpCodes.Ldloc_0);
            gen.Emit(OpCodes.Ret);
        }

        private static ProxyType FindCompatibleConstructor<TDestination>(
            object source,
            MemberInfo[] members)
        {
            IEnumerable<ConstructorInfo> constructors = typeof(TDestination)
                .GetPublicAndProtectedConstructors();
           
            foreach (ConstructorInfo constructor in constructors)
            {
                List<Type> parameterTypes =
                    constructor.GetParameterTypes().ToList();

                var parameters = members.GetParameters(source, parameterTypes).ToArray();

                var parameterValues = parameters.Select(a => a.Value).ToArray();

                if (!parameters.Any())
                    return new ProxyType(constructor);

                foreach (var parameter in parameters)                
                    if (parameterTypes.Any(x => x == parameter.Type))
                        return new ProxyType(constructor, parameterValues);              
            }

            throw new InvalidOperationException();
        }

        private static IEnumerable<ConstructorInfo> GetPublicAndProtectedConstructors(
            this Type type)
        {
            return type.GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(x => x.IsPublic || x.IsFamily);
        }

        private static IEnumerable<Type> GetParameterTypes(
            this ConstructorInfo ci)
        {
            return ci.GetParameters().Select(pi => pi.ParameterType);
        }

      
       private static IEnumerable<SourceTypeValuePair> GetParameters(
          this IEnumerable<MemberInfo> members,
          object source,
          List<Type> parameterTypes)
       {
           List<object> parameters = members
               .Where(mi => parameterTypes.Matches(mi.ToUnderlyingType()))
               .Select(x => source.GetType()
                   .MatchProperty(x.Name).GetValue(source, null))
               .Take(parameterTypes.Count())
               .ToList();

           var sourceMap = source.GetSourceTypeValuePairs();

           return (!sourceMap.AreOrderedBy(parameterTypes)) ? 
                sourceMap.OrderByType(parameterTypes)
                   :  sourceMap;
                      
       }     

        private static bool Matches(this List<Type> types, Type type)
        {
            return types.Contains(type)
                || types.Any(t => t.IsAssignableFrom(type)
                    || type.IsAssignableFrom(t));
        }

        private static PropertyInfo MatchProperty(this Type type, string name)
        {
            return type.GetProperty(name) ?? type.FindCompatibleProperty(name);
        }

        private static PropertyInfo FindCompatibleProperty(this Type type, 
            string name)
        {
            return type.GetProperties(
                    BindingFlags.Public | BindingFlags.Instance)
                       .First(x => x.Name.StartsWith(
                            name, StringComparison.OrdinalIgnoreCase));
        }

       
        private static IEnumerable<SourceTypeValuePair> GetSourceTypeValuePairs(this object source)
        {
            return source.GetType()
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Select(a => new SourceTypeValuePair(a.PropertyType, a.GetValue(source, null)));            
        }       
      
        private static bool AreOrderedBy(this IEnumerable<SourceTypeValuePair> sourceMap, IEnumerable<Type> types)
        {
            return sourceMap.Select(a => a.Type.Name).SequenceEqual(types.Select(b => b.Name));
        }

        private static IEnumerable<SourceTypeValuePair> OrderByType(
         this IEnumerable<SourceTypeValuePair> sequence,
         IEnumerable<Type> types)
        {
            return from t in types
                   select sequence.First(x => x.Type.IsAssignableFrom(t));
        }


        private static void Map(object source, object target)
        {
            ISet<FieldInfo> sourceFields = source.GetType().FindAllFields();
            ISet<FieldInfo> targetFields = target.GetType().BaseType.FindAllFields();

            var matchedTargetFields =
                (from s in sourceFields
                 from t in targetFields
                 where s.Match(t)
                 select t)
                 .ToArray();

            foreach (FieldInfo fi in matchedTargetFields)
            {
                var sourceField = sourceFields
                    .Where(s => s.Name.Equals(fi.Name))
                        .Concat(sourceFields
                            .Where(s => s.Match(fi)))
                    .FirstOrDefault();
                if (sourceField != null)
                    fi.SetValue(
                        target,
                        sourceField.GetValue(source));
            }
        }

        private static ISet<FieldInfo> FindAllFields(this Type t)
        {
            var allFields = new HashSet<FieldInfo>();
            
            Type type = t;
            while (type != null)
            {
                var fields =
                    type.GetFields(
                        BindingFlags.Public |
                        BindingFlags.Instance |
                        BindingFlags.NonPublic);

                foreach (FieldInfo field in fields)
                    if (!allFields.Contains(field))
                        allFields.Add(field);

                type = type.BaseType;
            }

            return allFields;
        }

        private static bool Match(this FieldInfo source, FieldInfo target)
        {
            var sourceName = ProxyGenerator
                .TrimCompilerGeneratedText(source.Name)
                .ToUpperInvariant();
            
            var targetName = ProxyGenerator
                .TrimCompilerGeneratedText(target.Name)
                .ToUpperInvariant();

            return (sourceName.Contains(targetName)
                 || targetName.Contains(sourceName))
                 && source.FieldType == target.FieldType;
        }

        private static string TrimCompilerGeneratedText(string s)
        {
            return s
                .Replace("i__Field", null)
                .Replace("<", null)
                .Replace(">", null)
                .Replace("k__BackingField", null);
        }

        private class SourceTypeValuePair
        {
            private readonly Type type;
            private readonly object value; 

            public SourceTypeValuePair(Type type, object value)
            {
                if (type == null)
                    throw new ArgumentNullException("type");

                this.type = type;
                this.value = value;
            }

            public Type Type { get { return this.type; } }
            public object Value { get { return this.value; } }
        }
    }
}
