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
    private readonly List<MixoMethodAccessor> _MethodAccessors;
    public Collection<MixoMethodAccessor> MethodAccessors => new(_MethodAccessors);
    private readonly List<MixoMethod> _Methods;
    public Collection<MixoMethod> Methods => new(_Methods);

    private readonly List<MixoField> _Fields;
    public Collection<MixoField> Fields => new(_Fields);
    private readonly List<MixoFieldAccessor> _FieldAccessors;
    public Collection<MixoFieldAccessor> FieldAccessors => new(_FieldAccessors);

    public MixoTypeMixin(TypeDefinition type, TypeReference target) {
        Type = type;
        Target = target;

        _MethodMixins = new List<MixoMethodMixin>();
        _MethodAccessors = new List<MixoMethodAccessor>();
        _Methods = new List<MixoMethod>();
        foreach (MethodDefinition method in type.Methods) {

            bool isSpecialMethod = false;
            // Search for the MethodMixin attribute.
            {
                if (method.HasCustomAttributes)
                    foreach (CustomAttribute methodAttribute in method.CustomAttributes) {
                        if (methodAttribute.AttributeType.FullName == typeof(MethodMixinAttribute).FullName) {
                            CustomAttributeArgument[] methodAttribArgs = methodAttribute.ConstructorArguments.ToArray();
                            if (methodAttribArgs.Length != 1 || methodAttribArgs[0].Value is not string methodTargetName)
                                throw new SystemException($"Method {method.FullName} is using an invalid constructor for {nameof(MethodMixinAttribute)}.");

                            // TODO Move this into the CIL patcher?
                            TypeReference[] methodTargetParams = new TypeReference[method.Parameters.Count];
                            for (int i = 0; i < method.Parameters.Count; i++)
                                methodTargetParams[i] = method.Parameters[i].ParameterType;

                            TypeReference? methodTargetReturn = null;
                            if (method.ReturnType is GenericInstanceType methodReturnGeneric)
                                methodTargetReturn = methodReturnGeneric.GenericArguments[0];

                            _MethodMixins.Add(new MixoMethodMixin(method, methodTargetName, methodTargetParams, methodTargetReturn));

                            isSpecialMethod = true;
                            break;
                        } else if (methodAttribute.AttributeType.FullName == typeof(MixinMethodAccessorAttribute).FullName) {
                            CustomAttributeArgument[] methodAttribArgs = methodAttribute.ConstructorArguments.ToArray();
                            if (methodAttribArgs.Length != 1 || methodAttribArgs[0].Value is not string methodTargetName)
                                throw new SystemException($"Method {method.FullName} is using an invalid constructor for {nameof(MixinMethodAccessorAttribute)}.");
                            _MethodAccessors.Add(new MixoMethodAccessor(method, methodTargetName));
                            isSpecialMethod = true;
                            break;
                        }
                    }
            }

            if (!isSpecialMethod) {
                if (method.IsConstructor)
                    continue;
                _Methods.Add(new MixoMethod(method));
            }
        }

        _FieldAccessors = new List<MixoFieldAccessor>();
        _Fields = new List<MixoField>();
        foreach (FieldDefinition field in type.Fields) {

            bool isSpecialField = false;
            if (field.HasCustomAttributes) {
                foreach (CustomAttribute fieldAttribute in field.CustomAttributes) {
                    if (fieldAttribute.AttributeType.FullName == typeof(MixinFieldAccessorAttribute).FullName) {
                        CustomAttributeArgument[] methodAttribArgs = fieldAttribute.ConstructorArguments.ToArray();
                        if (methodAttribArgs.Length != 1 || methodAttribArgs[0].Value is not string fieldTargetName)
                            throw new SystemException($"Field {field.FullName} is using an invalid constructor for {nameof(MixinFieldAccessorAttribute)}.");
                        _FieldAccessors.Add(new MixoFieldAccessor(field, fieldTargetName));
                        isSpecialField = true;
                        break;
                    } else if (fieldAttribute.AttributeType.FullName == typeof(MixinThisAttribute).FullName) {
                        if (fieldAttribute.HasConstructorArguments)
                            throw new SystemException($"Field {field.FullName} is using an invalid constructor for {nameof(MixinThisAttribute)}.");
                        _FieldAccessors.Add(new MixoFieldAccessor(field, MixoFieldAccessor.ThisTargetName));
                        isSpecialField = true;
                        break;
                    }
                }
            }

            if (!isSpecialField)
                _Fields.Add(new MixoField(field));
        }
    }
}