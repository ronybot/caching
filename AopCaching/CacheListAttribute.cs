﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Reflection;
using PostSharp.Aspects;
using PubComp.Caching.Core;
using System.Collections;

namespace PubComp.Caching.AopCaching
{
    [Serializable]
    public class CacheListAttribute : MethodInterceptionAspect
    {
        private string cacheName;
        private ICache cache;
        private long initialized = 0L;
        private string className;
        private string methodName;
        private string[] parameterTypeNames;
        private int[] indexesNotToCache;
        private Type dataKeyConverterType;
        private int keyParameterNumber;
        private Type keyType;
        private Type dataType;
        private ConstructorInfo createKeyList;
        private ConstructorInfo createDataList;
        private ConstructorInfo createDataKeyConverter;
        private MethodInfo addKey;
        private MethodInfo addData;
        private MethodInfo addDataRange;
        private MethodInfo convertDataToKey;

        public CacheListAttribute(Type dataKeyConverterType, int keyParameterNumber = 0)
        {
            this.dataKeyConverterType = dataKeyConverterType;
            this.keyParameterNumber = keyParameterNumber;
        }

        public CacheListAttribute(string cacheName, Type dataKeyConverterType, int keyParameterNumber = 0)
        {
            this.cacheName = cacheName;
            this.dataKeyConverterType = dataKeyConverterType;
            this.keyParameterNumber = keyParameterNumber;
        }

        private static bool TryGetKeyDataTypes(Type dataKeyConverterType, out Type keyType, out Type dataType)
        {
            keyType = null;
            dataType = null;

            var interfaces = dataKeyConverterType.GetInterfaces();

            foreach (var @interface in interfaces)
            {
                if (!@interface.IsGenericType)
                    continue;

                var args = @interface.GetGenericArguments();

                if (args.Length != 2)
                    continue;

                if (@interface.GetGenericTypeDefinition() != typeof(IDataKeyConverter<,>))
                    return false;

                keyType = args[0];
                dataType = args[1];
                return true;
            }

            return false;
        }

        public override bool CompileTimeValidate(System.Reflection.MethodBase method)
        {
            Type keyType, dataType;
            if (!TryGetKeyDataTypes(this.dataKeyConverterType, out keyType, out dataType))
            {
                PostSharp.Extensibility.Message.Write(
                    PostSharp.MessageLocation.Of(method),
                    PostSharp.Extensibility.SeverityType.Error,
                    "Custom01",
                    "The type provided does not implement IDataKeyConverter<TKey, TData>.");
                
                return false;
            }

            if (this.dataKeyConverterType.IsAbstract || this.dataKeyConverterType.GetConstructor(new Type[0]) == null)
            {
                PostSharp.Extensibility.Message.Write(
                    PostSharp.MessageLocation.Of(method),
                    PostSharp.Extensibility.SeverityType.Error,
                    "Custom01",
                    string.Format("{0} is abstract or does not have a public parameter-less constructor", this.dataKeyConverterType.FullName));

                return false;
            }

            var keyIListType = typeof(IList<>).MakeGenericType(keyType);
            var dataIListType = typeof(IList<>).MakeGenericType(dataType);

            if (method.GetParameters().Length <= this.keyParameterNumber)
            {
                PostSharp.Extensibility.Message.Write(
                    PostSharp.MessageLocation.Of(method),
                    PostSharp.Extensibility.SeverityType.Error,
                    "Custom01",
                    string.Format("The method {0} does not have a parameter {1}.", method.Name, this.keyParameterNumber));
                
                return false;
            }

            var keysParameterType = method.GetParameters()[this.keyParameterNumber].ParameterType;
            if (keysParameterType != keyIListType)
            {
                PostSharp.Extensibility.Message.Write(
                    PostSharp.MessageLocation.Of(method),
                    PostSharp.Extensibility.SeverityType.Error,
                    "Custom01",
                    string.Format("The parameter {1} of method {0} != IList<TKey>.", method.Name, this.keyParameterNumber));
                
                return false;
            }

            var methodInfo = method as System.Reflection.MethodInfo;
            if (methodInfo == null || methodInfo.ReturnType == null)
            {
                PostSharp.Extensibility.Message.Write(
                    PostSharp.MessageLocation.Of(method),
                    PostSharp.Extensibility.SeverityType.Error,
                    "Custom01",
                    string.Format("The method {0} has no return type.", method.Name));
                
                return false;
            }

            var returnType = methodInfo.ReturnType;
            if (returnType != dataIListType)
            {
                PostSharp.Extensibility.Message.Write(
                    PostSharp.MessageLocation.Of(method),
                    PostSharp.Extensibility.SeverityType.Error,
                    "Custom01",
                    string.Format("The return type of method {0} != IList<TData>.", method.Name));
                
                return false;
            }

            return true;
        }

        public override void CompileTimeInitialize(System.Reflection.MethodBase method, AspectInfo aspectInfo)
        {
            if (this.cacheName == null)
                this.cacheName = method.DeclaringType.FullName;

            this.className = method.DeclaringType.FullName;
            this.methodName = method.Name;
            var parameters = method.GetParameters();
            this.parameterTypeNames = parameters.Select(p => p.ParameterType.FullName).ToArray();

            var indexes = new List<int>();

            for (int cnt = 0; cnt < parameters.Length; cnt++)
            {
                var doNotIncludeInCacheKey =
                    parameters[cnt].CustomAttributes
                        .Any(a => a.GetType() == typeof(DoNotIncludeInCacheKeyAttribute));

                if (doNotIncludeInCacheKey)
                    indexes.Add(cnt);
            }

            this.indexesNotToCache = indexes.ToArray();

            TryGetKeyDataTypes(this.dataKeyConverterType, out this.keyType, out this.dataType);

            this.createDataKeyConverter = this.dataKeyConverterType.GetConstructor(new Type[0]);
            this.convertDataToKey = this.dataKeyConverterType.GetMethod("GetKey", new Type[] { this.dataType });

            var keyListType = typeof(List<>).MakeGenericType(this.keyType);
            var dataListType = typeof(List<>).MakeGenericType(this.dataType);
            var dataEnumerableType = typeof(IEnumerable<>).MakeGenericType(this.dataType);

            this.createKeyList = keyListType.GetConstructor(new Type[0]);
            this.createDataList = dataListType.GetConstructor(new Type[0]);

            this.addKey = keyListType.GetMethod("Add", new[] { this.keyType });
            this.addData = dataListType.GetMethod("Add", new[] { this.dataType });
            this.addDataRange = dataListType.GetMethod("AddRange", new[] { dataEnumerableType });
        }

        public override void OnInvoke(MethodInterceptionArgs args)
        {
            if (Interlocked.Read(ref initialized) == 0L)
            {
                this.cache = CacheManager.GetCache(this.cacheName);
                Interlocked.Exchange(ref initialized, 1L);
            }

            var cacheToUse = this.cache;

            if (cacheToUse == null)
            {
                base.OnInvoke(args);
                return;
            }

            var parameterValues = args.Arguments.Where((arg, index) => !this.indexesNotToCache.Contains(index)).ToArray();

            var allKeys = parameterValues[this.keyParameterNumber];
            var allKeysCollection = allKeys as IEnumerable;

            var resultList = this.createDataList.Invoke(new object[0]);
            var missingKeys = this.createKeyList.Invoke(new object[0]);

            foreach (var k in allKeysCollection)
            {
                var keyList = this.createKeyList.Invoke(new object[0]);
                this.addKey.Invoke(keyList, new object[] { k });
                parameterValues[this.keyParameterNumber] = keyList;

                var key = new CacheKey(this.className, this.methodName, this.parameterTypeNames, parameterValues).ToString();
                object value;
                if (cacheToUse.TryGet<object>(key, out value))
                {
                    addData.Invoke(resultList, new object[] { value });
                }
                else
                {
                    addKey.Invoke(missingKeys, new object[] { k });
                }
            }

            if (missingKeys == null || !(missingKeys as List<string>).Any())
            {
                args.ReturnValue = resultList;
                return;
            }

            args.Arguments[this.keyParameterNumber] = missingKeys;
            base.OnInvoke(args);
            var resultsFromerInner = args.ReturnValue;
            addDataRange.Invoke(resultList, new object [] { resultsFromerInner });

            var converter = createDataKeyConverter.Invoke(new object[0]);

            foreach (var result in resultsFromerInner as IEnumerable)
            {
                var k = convertDataToKey.Invoke(converter, new object[] { result });
                
                var keyList = this.createKeyList.Invoke(new object[0]);
                this.addKey.Invoke(keyList, new object[] { k });
                parameterValues[this.keyParameterNumber] = keyList;

                var key = new CacheKey(this.className, this.methodName, this.parameterTypeNames, parameterValues).ToString();
                cacheToUse.Set<object>(key, result);
            }

            args.ReturnValue = resultList;
        }
    }
}
