using FakerLib.PluginSupport;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace FakerLib
{
    public class Faker
    {
        public int MaxCircularDependencyDepth { get; set; } = 0;

        private int currentCircularDependencyDepth = 0;

        private Dictionary<Type, IGenerator> generators;

        private Stack<Type> constructionStack = new Stack<Type>();

        public T Create<T>()
        {
            if (IsPrimitive(typeof(T)))
            {
                try
                {
                    return (T) generators[typeof(T)].GetType().InvokeMember(
                        "Generate",
                        BindingFlags.InvokeMethod | BindingFlags.Instance | BindingFlags.Public,
                        null,
                        generators[typeof(T)],
                        null
                    );
                }
                catch
                {
                    return default;
                }
            }

            ConstructorInfo[] constructors = typeof(T).GetConstructors(BindingFlags.Instance | BindingFlags.Public);

            if ((constructors.Length == 0 && !typeof(T).IsValueType)
                || (currentCircularDependencyDepth = constructionStack.Count(t => t == typeof(T))) >
                MaxCircularDependencyDepth)
            {
                return default;
            }

            constructionStack.Push(typeof(T));

            object constructed = default;
            if (typeof(T).IsValueType && constructors.Length == 0)
            {
                constructed = Activator.CreateInstance(typeof(T));
            }

            object[] ctorParams = null;
            ConstructorInfo ctor = null;

            foreach (ConstructorInfo cInfo in constructors.OrderByDescending(c => c.GetParameters().Length))
            {
                ctorParams = GenerateCtorParams(cInfo);
                constructed = cInfo.Invoke(ctorParams);
                ctor = cInfo;
            }

            GenerateFieldsAndProperties(constructed, ctorParams, ctor);

            constructionStack.Pop();
            return (T) constructed;
        }

        private void GenerateFieldsAndProperties(object constructed, object[] ctorParams, ConstructorInfo cInfo)
        {
            ParameterInfo[] pInfo = cInfo?.GetParameters();
            var fields = constructed.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Cast<MemberInfo>();
            var properties = constructed.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Cast<MemberInfo>();
            var fieldsAndProperties = fields.Concat(properties);

            foreach (MemberInfo m in fieldsAndProperties)
            {
                bool wasInitialized = false;

                Type memberType = (m as FieldInfo)?.FieldType ?? (m as PropertyInfo)?.PropertyType;
                object memberValue = (m as FieldInfo)?.GetValue(constructed) ??
                                     (m as PropertyInfo)?.GetValue(constructed);

                for (int i = 0; i < ctorParams?.Length; i++)
                {
                    object defaultValue = this.GetType()
                        .GetMethod("GetDefaultValue", BindingFlags.NonPublic | BindingFlags.Instance)
                        .MakeGenericMethod(memberType).Invoke(this, null);
                    if (pInfo != null && ctorParams[i] == memberValue && memberType == pInfo[i].ParameterType &&
                        m.Name == pInfo[i].Name || defaultValue?.Equals(memberValue) == false)
                    {
                        wasInitialized = true;
                        break;
                    }
                }

                if (!wasInitialized)
                {
                    object newValue = default;
                    try
                    {
                        if (!memberType.IsGenericType)
                        {
                            newValue = generators[memberType].GetType().InvokeMember("Generate",
                                BindingFlags.InvokeMethod | BindingFlags.Instance
                                                          | BindingFlags.Public, null, generators[memberType],
                                null);
                        }
                        else
                        {
                            Type[] tmp = memberType.GetGenericArguments();
                            newValue = generators[tmp[0]].GetType().InvokeMember("GenerateList",
                                BindingFlags.InvokeMethod | BindingFlags.Instance
                                                          | BindingFlags.Public, null, generators[tmp[0]], null);
                        }
                    }
                    catch (KeyNotFoundException e)
                    {
                        if (!IsPrimitive(memberType))
                        {
                            newValue = this.GetType().GetMethod("Create").MakeGenericMethod(memberType)
                                .Invoke(this, null);
                        }
                    }

                    (m as FieldInfo)?.SetValue(constructed, newValue);
                    if ((m as PropertyInfo)?.CanWrite == true)
                    {
                        (m as PropertyInfo).SetValue(constructed, newValue);
                    }
                }
            }
        }


        private object[] GenerateCtorParams(ConstructorInfo cInfo)
        {
            ParameterInfo[] pInfo = cInfo.GetParameters();
            object[] ctorParams = new object[pInfo.Length];

            for (int i = 0; i < ctorParams.Length; i++)
            {
                Type fieldType = pInfo[i].ParameterType;
                object newValue = default;
                try
                {
                    if (!fieldType.IsGenericType)
                    {
                        newValue = generators[fieldType].GetType().InvokeMember("Generate",
                            BindingFlags.InvokeMethod |
                            BindingFlags.Instance | BindingFlags.Public, null, generators[fieldType], null);
                    }
                    else
                    {
                        Type[] tmp = fieldType.GetGenericArguments();
                        newValue = generators[tmp[0]].GetType().InvokeMember("GenerateList",
                            BindingFlags.InvokeMethod |
                            BindingFlags.Instance | BindingFlags.Public, null, generators[tmp[0]], null);
                    }
                }
                catch (KeyNotFoundException e)
                {
                    if (!IsPrimitive(fieldType))
                    {
                        newValue = this.GetType().GetMethod("Create").MakeGenericMethod(fieldType)
                            .Invoke(this, null);
                    }
                }

                ctorParams[i] = newValue;
            }

            return ctorParams;
        }

        public Faker()
        {
            this.generators = LoadAllAvailableGenerators();
        }

        private object GetDefaultValue<T>()
        {
            return default(T);
        }

        private Dictionary<Type, IGenerator> LoadAllAvailableGenerators()
        {
            Dictionary<Type, IGenerator> result = new Dictionary<Type, IGenerator>();
            string pluginsPath = Directory.GetCurrentDirectory() + "\\FakerLib Plugins\\Generators\\";
            if (!Directory.Exists(pluginsPath))
            {
                Directory.CreateDirectory(pluginsPath);
            }

            foreach (string str in Directory.GetFiles(pluginsPath, "*.dll"))
            {
                Assembly asm = Assembly.LoadFrom(str);
                foreach (Type t in asm.GetTypes())
                {
                    if (IsRequiredType(t, typeof(GeneratorPlugin<>)))
                    {
                        var tmp = Activator.CreateInstance(t);
                        result.Add(t.BaseType.GetGenericArguments()[0], (IGenerator) tmp);
                    }
                }
            }

            foreach (Type t in Assembly.GetExecutingAssembly().GetTypes())
            {
                if (IsRequiredType(t, typeof(Generator<>)))
                {
                    result.Add(t.BaseType.GetGenericArguments()[0], (IGenerator) Activator.CreateInstance(t));
                }
            }

            return result;
        }

        private bool IsPrimitive(Type t)
        {
            return t.IsPrimitive || (t == typeof(string)) || (t == typeof(decimal)) || (t == typeof(DateTime));
        }

        private bool IsRequiredType(Type plugin, Type required)
        {
            while (plugin != null && plugin != typeof(object))
            {
                Type tmp = plugin.IsGenericType ? plugin.GetGenericTypeDefinition() : plugin;
                if (required == tmp)
                {
                    return true;
                }

                plugin = plugin.BaseType;
            }

            return false;
        }
    }
}