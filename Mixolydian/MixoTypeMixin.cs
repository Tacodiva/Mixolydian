using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Mixolydian.Common;
using Mono.Cecil;

namespace Mixolydian;

public class MixoTypeMixin {

    public readonly TypeDefinition Type;
    public readonly TypeReference Target;

    private readonly List<MixoMethodMixin> _MethodMixins;
    public Collection<MixoMethodMixin> MethodMixins => new(_MethodMixins);

    private readonly List<MixoMethod> _Methods;
    public Collection<MixoMethod> Methods => new(_Methods);

    private readonly List<MixoField> _Fields;
    public Collection<MixoField> Fields => new(_Fields);

    public MixoTypeMixin(TypeDefinition type, TypeReference target) {
        Type = type;
        Target = target;

        _MethodMixins = new List<MixoMethodMixin>();
        _Methods = new List<MixoMethod>();
        foreach (MethodDefinition method in type.Methods) {

            bool isMixinMethod = false;
            // Search for the MethodMixin attribute.
            {
                if (method.HasCustomAttributes)
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

                            isMixinMethod = true;
                            break;
                        }
                    }
            }

            if (!isMixinMethod) {
                if (method.IsConstructor)
                    continue;
                _Methods.Add(new MixoMethod(method));
            }
        }

        _Fields = new List<MixoField>();
        foreach (FieldDefinition field in type.Fields) {
            _Fields.Add(new MixoField(field));
        }
    }
}