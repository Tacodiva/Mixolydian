using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Mixolydian.Common;
using Mono.Cecil;

namespace Mixolydian;

public class MixoTypeMixin {

    public readonly TypeDefinition Type;
    public readonly TypeReference Target;

    private readonly List<MixoMethodMixin> _MethodMixins;
    public Collection<MixoMethodMixin> MethodMixins => new(_MethodMixins);

    public MixoTypeMixin(TypeDefinition type, TypeReference target) {
        Type = type;
        Target = target;

        // Search for methods that have the MethodMixin attribute and find that mixins target method.
        _MethodMixins = new List<MixoMethodMixin>();
        foreach (MethodDefinition method in type.Methods) {
            if (!method.HasCustomAttributes)
                continue;
            foreach (CustomAttribute methodAttribute in method.CustomAttributes) {
                if (methodAttribute.AttributeType.FullName == typeof(MethodMixinAttribute).FullName) {
                    CustomAttributeArgument[] methodAttribArgs = methodAttribute.ConstructorArguments.ToArray();
                    if (methodAttribArgs.Length != 1 || methodAttribArgs[0].Value is not string methodTargetName)
                        throw new SystemException($"Method {method.FullName} is using an invalid constructor for {nameof(MethodMixinAttribute)}.");

                    TypeReference[] methodTargetParams = new TypeReference[method.Parameters.Count];
                    for (int i = 0; i < method.Parameters.Count; i++)
                        methodTargetParams[i] = method.Parameters[i].ParameterType;

                    TypeReference? methodTargetReturn = null;
                    if (method.ReturnType is GenericInstanceType methodReturnGeneric)
                        methodTargetReturn = methodReturnGeneric.GenericArguments[0];

                    _MethodMixins.Add(new MixoMethodMixin(method, methodTargetName, methodTargetParams, methodTargetReturn));
                }
            }
        }
    }
}