﻿using System.Reflection;

namespace TypeShape.ReflectionProvider.MemberAccessors;

internal interface IReflectionMemberAccessor
{
    Getter<TDeclaringType, TPropertyType> CreateGetter<TDeclaringType, TPropertyType>(MemberInfo memberInfo, MemberInfo[]? parentMembers);
    Setter<TDeclaringType, TPropertyType> CreateSetter<TDeclaringType, TPropertyType>(MemberInfo memberInfo, MemberInfo[]? parentMembers);

    Setter<TEnumerable, TElement> CreateEnumerableAddDelegate<TEnumerable, TElement>(MethodInfo methodInfo);
    Setter<TDictionary, KeyValuePair<TKey, TValue>> CreateDictionaryAddDelegate<TDictionary, TKey, TValue>(MethodInfo methodInfo);

    Func<TDeclaringType> CreateDefaultConstructor<TDeclaringType>(IConstructorShapeInfo ctorInfo);

    Type CreateConstructorArgumentStateType(IConstructorShapeInfo ctorInfo);
    Func<TArgumentState> CreateConstructorArgumentStateCtor<TArgumentState>(IConstructorShapeInfo ctorInfo);
    Setter<TArgumentState, TParameter> CreateConstructorArgumentStateSetter<TArgumentState, TParameter>(IConstructorShapeInfo ctorInfo, int parameterIndex);
    Func<TArgumentState, TDeclaringType> CreateParameterizedConstructor<TArgumentState, TDeclaringType>(IConstructorShapeInfo ctorInfo);
}
