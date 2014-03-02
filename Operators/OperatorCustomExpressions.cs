﻿using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.CSharp.RuntimeBinder;
using Binder = Microsoft.CSharp.RuntimeBinder.Binder;

namespace ExpressionEvaluator.Operators
{

    internal class OperatorCustomExpressions
    {
        private static Dictionary<Type, List<Type>> NumConv = new Dictionary<Type, List<Type>> {
			{typeof(sbyte), new List<Type> { typeof(short), typeof(int), typeof(long), typeof(float), typeof(double), typeof(decimal) }},
			{typeof(byte), new List<Type> { typeof(short) ,typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double) ,typeof(decimal)}},
			{typeof(short), new List<Type> { typeof(int) ,typeof(long), typeof(float), typeof(double), typeof(decimal)}},
			{typeof(ushort), new List<Type> {typeof(int), typeof(uint), typeof(long), typeof(ulong),typeof(float), typeof(double), typeof(decimal)}},
			{typeof(int), new List<Type> { typeof(long), typeof(float), typeof(double), typeof(decimal) }},
			{typeof(uint), new List<Type> { typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal) }},
			{typeof(long), new List<Type> { typeof(float), typeof(double), typeof(decimal)}},
			{typeof(ulong), new List<Type> { typeof(float), typeof(double), typeof(decimal)}},
			{typeof(char), new List<Type> { typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal)}},
			{typeof(float), new List<Type> { typeof(double)}}
		};

        private static Type[] GetTypesInNamespace(Assembly assembly, string nameSpace)
        {
            return assembly.GetTypes().Where(t => String.Equals(t.Namespace, nameSpace, StringComparison.Ordinal)).ToArray();
        }

        private static Expression GetExactMatch(Type type, Expression instance, string membername, List<Expression> args)
        {
            var argTypes = args.Select(x => x.Type);

            // Look for an exact match
            var methodInfo = type.GetMethod(membername, argTypes.ToArray());

            if (methodInfo != null)
            {
                var parameterInfos = methodInfo.GetParameters();

                for (int i = 0; i < parameterInfos.Length; i++)
                {
                    args[i] = TypeConversion.Convert(args[i], parameterInfos[i].ParameterType);
                }

                return Expression.Call(instance, methodInfo, args);
            }
            return null;
        }

        private static Expression GetParamsMatch(Type type, Expression instance, string membername, List<Expression> args)
        {
            // assume params

            var methodInfos = type.GetMethods().Where(x => x.Name == membername);
            var matchScore = new List<Tuple<MethodInfo, int>>();

            foreach (var info in methodInfos.OrderByDescending(m => m.GetParameters().Count()))
            {
                var parameterInfos = info.GetParameters();
                var lastParam = parameterInfos.Last();
                var newArgs = args.Take(parameterInfos.Length - 1).ToList();
                var paramArgs = args.Skip(parameterInfos.Length - 1).ToList();

                int i = 0;
                int k = 0;

                foreach (var expression in newArgs)
                {
                    k += TypeConversion.CanConvert(expression.Type, parameterInfos[i].ParameterType);
                    i++;
                }

                if (k > 0)
                {
                    if (Attribute.IsDefined(lastParam, typeof(ParamArrayAttribute)))
                    {
                        k += paramArgs.Sum(arg => TypeConversion.CanConvert(arg.Type, lastParam.ParameterType.GetElementType()));
                    }
                }

                matchScore.Add(new Tuple<MethodInfo, int>(info, k));
            }

            var info2 = matchScore.OrderBy(x => x.Item2).FirstOrDefault(x => x.Item2 >= 0);

            if (info2 != null)
            {
                var parameterInfos2 = info2.Item1.GetParameters();
                var lastParam2 = parameterInfos2.Last();
                var newArgs2 = args.Take(parameterInfos2.Length - 1).ToList();
                var paramArgs2 = args.Skip(parameterInfos2.Length - 1).ToList();


                for (int i = 0; i < parameterInfos2.Length - 1; i++)
                {
                    newArgs2[i] = TypeConversion.Convert(newArgs2[i], parameterInfos2[i].ParameterType);
                }

                var targetType = lastParam2.ParameterType.GetElementType();

                newArgs2.Add(Expression.NewArrayInit(targetType, paramArgs2.Select(x => TypeConversion.Convert(x, targetType))));
                return Expression.Call(instance, info2.Item1, newArgs2);
            }
            return null;
        }

        public static bool CanConvertType(object value, bool isLiteral, Type from, Type to)
        {
            // null literal conversion 6.1.5
            //if (value == null)
            //{
            //    return IsNullableType(to);
            //}

            // identity conversion 6.1.1
            if (from.GetHashCode().Equals(to.GetHashCode()))
                return true;

            // implicit constant expressions 6.1.9
            if (isLiteral)
            {
                bool canConv = false;

                dynamic num = value;
                if (from == typeof(int))
                {
                    switch (Type.GetTypeCode(to))
                    {
                        case TypeCode.SByte:
                            if (num >= sbyte.MinValue && num <= sbyte.MaxValue)
                                canConv = true;
                            break;
                        case TypeCode.Byte:
                            if (num >= byte.MinValue && num <= byte.MaxValue)
                                canConv = true;
                            break;
                        case TypeCode.Int16:
                            if (num >= short.MinValue && num <= short.MaxValue)
                                canConv = true;
                            break;
                        case TypeCode.UInt16:
                            if (num >= ushort.MinValue && num <= ushort.MaxValue)
                                canConv = true;
                            break;
                        case TypeCode.UInt32:
                            if (num >= uint.MinValue && num <= uint.MaxValue)
                                canConv = true;
                            break;
                        case TypeCode.UInt64:
                            if (num >= 0)
                                canConv = true;
                            break;
                    }
                }
                else if (from == typeof(long))
                {
                    if (to == typeof(ulong))
                    {
                        if (num >= 0)
                            canConv = true;
                    }
                }

                if (canConv)
                    return true;
            }

            // string conversion
            // TODO: check if this is necessary
            if (from == typeof(string))
            {
                if (to == typeof(object))
                    return true;
                else
                    return false;
            }


            // implicit nullable conversion 6.1.4
            if (IsNullableType(to))
            {

                if (IsNullableType(from))
                {

                    // If the source value is null, then just return successfully (because the target value is a nullable type)
                    if (value == null)
                    {
                        return true;
                    }

                }

                return CanConvertType(value, isLiteral, Nullable.GetUnderlyingType(from), Nullable.GetUnderlyingType(to));

            }

            // implicit enumeration conversion 6.1.3
            long longTest = -1;

            if (isLiteral && to.IsEnum && long.TryParse(value.ToString(), out longTest))
            {
                if (longTest == 0)
                    return true;
            }

            // implicit reference conversion 6.1.5
            if (!from.IsValueType && !to.IsValueType)
            {
                bool? irc = ImpRefConv(value, from, to);
                if (irc.HasValue)
                    return irc.Value;
            }

            // implicit numeric conversion 6.1.2
            try
            {
                object fromObj = null;
                double dblTemp;
                decimal decTemp;
                char chrTemp;
                fromObj = Activator.CreateInstance(from);

                if (char.TryParse(fromObj.ToString(), out chrTemp) || double.TryParse(fromObj.ToString(), out dblTemp) || decimal.TryParse(fromObj.ToString(), out decTemp))
                {
                    if (NumConv.ContainsKey(from) && NumConv[from].Contains(to))
                        return true;
                    else
                        return CrawlThatShit(to.GetHashCode(), from, new List<int>());
                }
                //else {
                //   return CrawlThatShit(to.GetHashCode(), from, new List<int>());
                //}
            }
            catch
            {
                //return CrawlThatShit(to.GetHashCode(), from, new List<int>());
            }

            return false;
        }


        public static List<MemberInfo> GetApplicableMembers(Type type, string membername, List<Expression> args)
        {
            var results = GetCandidateMembers(type, membername);

            // paramater matching && ref C# lang spec section 7.5.1.1
            var appMembers = new List<MemberInfo>();

            // match each param with an arg. 
            //List<CallArgMod> paramMods;
            foreach (var methodInfo in results)
            {
                bool isMatch = true;
                //paramMods = new List<CallArgMod>();
                int argCount = 0;
                foreach (ParameterInfo pInfo in methodInfo.GetParameters())
                {
                    bool haveArg = argCount < args.Count;

                    if (pInfo.IsOut || pInfo.ParameterType.IsByRef)
                    {
                        if (!haveArg)
                        {
                            isMatch = false;
                        }
                        //else if (pInfo.IsOut)
                        //{
                        //    if (mrSettings.Args[argCount].CallMod != CallArgMod.OUT)
                        //    {
                        //        isMatch = false;
                        //    }
                        //}
                        //else if (pInfo.ParameterType.IsByRef)
                        //{
                        //    if (mrSettings.Args[argCount].CallMod != CallArgMod.REF)
                        //    {
                        //        isMatch = false;
                        //    }
                        //}

                        // Step 4 (technically)
                        // Check types if either are a ref type. Must match exactly
                        String argTypeStr = args[argCount].Type.FullName;
                        Type paramType = methodInfo.GetParameters()[argCount].ParameterType;
                        String paramTypeStr = paramType.ToString().Substring(0, paramType.ToString().Length - 1);

                        if (argTypeStr != paramTypeStr)
                        {
                            isMatch = false;
                        }

                    }
                    else
                    {
                        if (pInfo.IsOptional)
                        {
                            // If an argument for this parameter position was specified, check its type
                            if (haveArg && !CanConvertType(((ConstantExpression)args[argCount]).Value, false, args[argCount].Type, pInfo.ParameterType))
                            {
                                isMatch = false;
                            }
                        }
                        else if (pInfo.GetCustomAttributes(typeof(ParamArrayAttribute), false).Length > 0)
                        { // Check ParamArray arguments
                            // TODO: set OnlyAppInExpForm here
                            for (int j = pInfo.Position; j < args.Count; j++)
                            {
                                if (!CanConvertType(null, false, args[j].Type, pInfo.ParameterType.GetElementType()))
                                {
                                    isMatch = false;
                                }
                                argCount++;
                            }
                            break;
                        }
                        else
                        { // Checking non-optional, non-ParamArray arguments
                            if (!haveArg || !CanConvertType(null, false, args[argCount].Type, pInfo.ParameterType))
                            {
                                isMatch = false;
                            }
                        }
                    }

                    if (!isMatch)
                    {
                        break;
                    }

                    argCount++;
                }

                if (isMatch && argCount < args.Count)
                    isMatch = false;

                if (isMatch)
                    appMembers.Add(methodInfo);
            }

            return appMembers;
            //}
        }

        public static bool? ImpRefConv(object value, Type from, Type to)
        {
            bool? success = null;

            if (from == to)
                // identity
                success = true;

            else if (to == typeof(object))
                // ref -> object
                success = true;

            else if (value == null)
                // null literal -> Ref-type
                success = !to.IsValueType;

            else if (false)
                // ref -> dynamic (6.1.8)
                // figure out how to do this
                ;

            else if (from.IsArray && to.IsArray)
            {
                // Array-type -> Array-type
                bool sameRank = (from.GetArrayRank() == to.GetArrayRank());
                bool bothRef = (!from.GetElementType().IsValueType && !to.GetElementType().IsValueType);
                bool? impConv = ImpRefConv(value, from.GetElementType(), to.GetElementType());
                success = (sameRank && bothRef && impConv.GetValueOrDefault(false));
            }

            // Conversion involving type parameters (6.1.10)
            else if (to.IsGenericParameter)
            {

                //if ( fromArg.GetType().Name.Equals(to.Name)) {
                if (to.GenericParameterAttributes != GenericParameterAttributes.None)
                {

                    if ((int)(to.GenericParameterAttributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0)
                    {
                        ;
                    }
                }
                else
                {
                }


                /*genArg.GetGenericParameterConstraints();
                genArg.GenericParameterAttributes;*/
                //if( mi.GetGenericArguments()[?]
                //var t = a.GetType().GetMethod("Foo", BindingFlags.Public | BindingFlags.Instance).GetGenericArguments()[0].GetGenericParameterConstraints();//.GenericParameterAttributes;
            }

            // Boxing Conversions (6.1.7)
            else if (from.IsValueType && !to.IsValueType)
            {
                return IsBoxingConversion(from, to);
            }

            else if ((from.IsClass && to.IsClass) || (from.IsClass && to.IsInterface) || (from.IsInterface && to.IsInterface))
                // class -> class  OR  class -> interface  OR  interface -> interface
                success = CrawlThatShit(to.GetHashCode(), from, new List<int>());

            else if (from.IsArray && CrawlThatShit(to.GetHashCode(), typeof(Array), new List<int>()))
            {
                // Array-type -> System.array
                return true;
            }

            else if (from.IsArray && from.GetArrayRank() == 1 && to.IsGenericType && CrawlThatShit(to.GetHashCode(), typeof(IList<>), new List<int>()))
                // Single dim array -> IList<>
                success = ImpRefConv(value, from.GetElementType(), to.GetGenericTypeDefinition());



            return success;
        }

        // TODO: Rename this method
        ///
        /// <summary>
        ///		Recursive method to traverse through the class hierarchy in an attempt to determine if the current object may be converted
        ///		to the target type, based on it's hash code.
        /// </summary>
        /// 
        /// <param name="target">The hashCode value of the target object</param>
        /// <param name="current">The object to be converted.</param>
        /// <param name="visitedTypes">The list of visited types. This is an optimization parameter.</param>
        /// 
        /// <returns>True if the object can be converted to an object matching the hashCode property of target, false otherwise</returns>
        /// 
        public static bool CrawlThatShit(int target, Type current, List<int> visitedTypes)
        {
            int curHashCode = current.GetHashCode();

            // Optimization
            if (visitedTypes.Contains(curHashCode))
            {
                return false;
            }

            bool found = (curHashCode == target);
            visitedTypes.Add(curHashCode);

            if (!found && current.BaseType != null)
            {
                found = CrawlThatShit(target, current.BaseType, visitedTypes);
            }

            if (!found)
            {
                if (current.GetInterfaces() != null)
                {
                    foreach (Type iface in current.GetInterfaces())
                    {
                        if (CrawlThatShit(target, iface, visitedTypes))
                        {
                            found = true;
                            break;
                        }

                    }
                }
            }

            return found;
        }

        ///
        /// <summary>
        ///		Determines if the passed type is a nullable type
        /// </summary>
        /// 
        /// <param name="t">The type to check</param>
        /// 
        /// <returns>True if the type is a nullable type, false otherwise</returns>
        ///
        public static bool IsNullableType(Type t)
        {
            return t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>);
        }

        ///
        /// <summary>
        ///		Determines if a boxing conversion exists between the passed object and the type
        /// </summary>
        /// 
        /// <param name="from">The type to convert</param>
        /// <param name="to">The type to attempt to convert the object to.</param>
        /// 
        /// <returns>True if a boxing conversion exists between the object and the type, false otherwise</returns>
        /// 
        public static bool IsBoxingConversion(Type from, Type to)
        {
            if (IsNullableType(from))
            {
                from = Nullable.GetUnderlyingType(from);
            }

            if (to == typeof(ValueType) || to == typeof(object))
            {
                return true;
            }

            if (CrawlThatShit(to.GetHashCode(), @from, new List<int>()))
            {
                return true;
            }

            if (@from.IsEnum && to == typeof(Enum))
            {
                return true;
            }
            return false;
        }

        public static List<MethodInfo> GetCandidateMembers(Type type, string membername)
        {
            // Find members that match on name
            var results = GetMethodInfos(type, membername);

            // Traverse through class hierarchy
            while (results.Count == 0 && type != typeof(object))
            {
                type = type.BaseType;
                results = GetMethodInfos(type, membername);
            }

            return results;
        }

        static Func<MethodInfo, bool> IsVirtual = (mi) => (mi.Attributes & MethodAttributes.Virtual) != 0;
        static Func<MethodInfo, bool> HasVTable = (mi) => (mi.Attributes & MethodAttributes.VtableLayoutMask) != 0;

        static BindingFlags findFlags = BindingFlags.NonPublic |
                                        BindingFlags.Public |
                                        BindingFlags.Static |
                                        BindingFlags.Instance |
                                        BindingFlags.InvokeMethod |
                                        BindingFlags.OptionalParamBinding |
                                        BindingFlags.DeclaredOnly;


        public static List<MethodInfo> GetMethodInfos(Type env, string memberName)
        {
            return env.GetMethods(findFlags).Where(mi => mi.Name == memberName && (!IsVirtual(mi) || HasVTable(mi))).ToList();
        }


        /// <summary>
        /// Returns an Expression that accesses a member on an Expression
        /// </summary>
        /// <param name="isFunction">Determines whether the member being accessed is a function or a property</param>
        /// <param name="isCall">Determines whether the member returns void</param>
        /// <param name="le">The expression that contains the member to be accessed</param>
        /// <param name="membername">The name of the member to access</param>
        /// <param name="args">Optional list of arguments to be passed if the member is a method</param>
        /// <returns></returns>
        public static Expression MemberAccess(bool isFunction, bool isCall, Expression le, string membername, List<Expression> args)
        {
            var argTypes = args.Select(x => x.Type);

            Expression instance = null;
            Type type = null;

            var isDynamic = false;
            var isRuntimeType = false;

            if (le.Type.Name == "RuntimeType")
            {
                isRuntimeType = true;
                type = ((Type)((ConstantExpression)le).Value);
            }
            else
            {
                type = le.Type;
                instance = le;
                isDynamic = type.IsDynamic();
            }

            if (isFunction)
            {
                if (isDynamic)
                {
                    var expArgs = new List<Expression> { instance };

                    expArgs.AddRange(args);

                    if (isCall)
                    {
                        var binderMC = Binder.InvokeMember(
                            CSharpBinderFlags.ResultDiscarded,
                            membername,
                            null,
                            type,
                            expArgs.Select(x => CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null))
                        );

                        return Expression.Dynamic(binderMC, typeof(void), expArgs);
                    }


                    
                    var binderM = Binder.InvokeMember(
                            CSharpBinderFlags.None,
                            membername,
                            null,
                            type,
                            expArgs.Select(x => CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null))
                        );

                    return Expression.Dynamic(binderM, typeof(object), expArgs);
                }
                else
                {
                    var mis = GetApplicableMembers(type, membername, args);
                    var methodInfo = (MethodInfo)mis[0];

                    var typeArgs = methodInfo.GetGenericArguments();

                    Type[] genericArgTypes = null;

                    if (methodInfo.IsGenericMethod)
                    {
                        genericArgTypes = new Type[typeArgs.Count()];
                    }

                    // if the method is generic, try to get type args from method, if none, try to get type args from parameters

                    if (methodInfo != null)
                    {
                        var parameterInfos = methodInfo.GetParameters();

                        foreach (var parameterInfo in parameterInfos)
                        {
                            var index = parameterInfo.Position;

                            if (parameterInfo.ParameterType.IsGenericType)
                            {
                                if (methodInfo.IsGenericMethod && parameterInfo.ParameterType.IsGenericParameter && genericArgTypes != null)
                                {
                                    genericArgTypes[parameterInfo.ParameterType.GenericParameterPosition] = args[index].Type;
                                    args[index] = Expression.Convert(args[index], parameterInfos[index].ParameterType.GetGenericTypeDefinition().MakeGenericType(args[index].Type));
                                }
                                if (methodInfo.IsGenericMethod && parameterInfo.ParameterType.IsGenericType && genericArgTypes != null)
                                {
                                    foreach (var pInfoGenericArgType in parameterInfo.ParameterType.GetGenericArguments())
                                    {
                                        genericArgTypes[pInfoGenericArgType.GenericParameterPosition] = args[index].Type.GetElementType() ?? typeof(string);
                                    }
                                    args[index] = Expression.Convert(args[index], parameterInfos[index].ParameterType.GetGenericTypeDefinition().MakeGenericType(typeof(string)));
                                }
                            }
                            else
                            {
                                if (methodInfo.IsGenericMethod && parameterInfo.ParameterType.IsGenericParameter  && genericArgTypes != null)
                                {
                                    genericArgTypes[parameterInfo.ParameterType.GenericParameterPosition] = args[index].Type;
                                }
                                args[index] = TypeConversion.Convert(args[index], parameterInfo.ParameterType);
                            }
                        }

                        if (isRuntimeType)
                        {
                            if (methodInfo.IsGenericMethod)
                            {
                                return Expression.Call(type, membername, genericArgTypes, args.ToArray());
                            }
                            else
                            {
                                return Expression.Call(type, membername, null, args.ToArray());
                            }
                        }
                        else
                        {
                            if (methodInfo.IsGenericMethod)
                            {
                                return Expression.Call(instance, membername, genericArgTypes, args.ToArray());
                            }
                            else
                            {
                                return Expression.Call(instance, methodInfo, args.ToArray());
                            }
                        }

                    }


                    var match = GetExactMatch(type, instance, membername, args) ??
                                GetParamsMatch(type, instance, membername, args);
                    if (match != null)
                    {
                        return match;
                    }

                }

            }
            else
            {
                if (isDynamic)
                {
                    var binder = Binder.GetMember(
                        CSharpBinderFlags.None,
                        membername,
                        type,
                        new[] { CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null) }
                        );

                    var result = Expression.Dynamic(binder, typeof(object), instance);


                    if (args.Count > 0)
                    {
                        var expArgs = new List<Expression>() { result };

                        expArgs.AddRange(args);

                        var indexedBinder = Binder.GetIndex(
                            CSharpBinderFlags.None,
                            type,
                            expArgs.Select(x => CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null))
                            );

                        result =
                            Expression.Dynamic(indexedBinder, typeof(object), expArgs);

                    }

                    return result;
                }
                else
                {
                    Expression exp = null;

                    var propertyInfo = type.GetProperty(membername);
                    if (propertyInfo != null)
                    {
                        exp = Expression.Property(instance, propertyInfo);
                    }
                    else
                    {
                        var fieldInfo = type.GetField(membername);
                        if (fieldInfo != null)
                        {
                            exp = Expression.Field(instance, fieldInfo);
                        }
                    }

                    if (exp != null)
                    {
                        if (args.Count > 0)
                        {
                            return Expression.ArrayAccess(exp, args);
                        }
                        else
                        {
                            return exp;
                        }
                    }
                }


            }

            throw new Exception(string.Format("Member not found: {0}.{1}", le.Type.Name, membername));
        }


        private static readonly Type StringType = typeof(string);
        private static readonly MethodInfo ToStringMethodInfo = typeof(object).GetMethod("ToString");


        private static Expression CallToString(Expression instance)
        {
            return Expression.Call(instance, ToStringMethodInfo);
        }


        /// <summary>
        /// Extends the Add Expression handler to handle string concatenation
        /// </summary>
        /// <param name="le">The left-hand expression</param>
        /// <param name="re">The right-hand expression</param>
        /// <returns></returns>
        public static Expression Add(Expression le, Expression re)
        {
            if (le.Type == StringType || re.Type == StringType)
            {

                if (le.Type != typeof(string)) le = CallToString(le);
                if (re.Type != typeof(string)) re = CallToString(re);
                return Expression.Add(le, re, typeof(string).GetMethod("Concat", new Type[] { le.Type, re.Type }));
            }
            else
            {
                return Expression.Add(le, re);
            }
        }

        private static Type _stringType = typeof(string);

        /// <summary>
        /// Returns an Expression that access a 1-dimensional index on an Array expression 
        /// </summary>
        /// <param name="le">The left-hand expression</param>
        /// <param name="re">The right-hand expression</param>
        /// <returns></returns>
        public static Expression ArrayAccess(Expression le, Expression re)
        {
            if (le.Type == _stringType)
            {
                var mi = _stringType.GetMethod("ToCharArray", new Type[] { });
                le = Expression.Call(le, mi);
            }

            return Expression.ArrayAccess(le, re);
        }

        /// <summary>
        /// Placeholderthat simple returns the left expression
        /// </summary>
        /// <param name="le"></param>
        /// <param name="re"></param>
        /// <returns></returns>
        public static Expression TernarySeparator(Expression le)
        {
            return le;
        }

    }
}