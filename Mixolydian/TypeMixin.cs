using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Mixolydian.Common;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Mixolydian;

public class TypeMixin {

    public readonly Mod Mod;

    public readonly TypeDefinition Source;
    public readonly TypeDefinition Target;

    public readonly GenericMap GenericMap;
    public readonly IDictionary<string, FieldDefinition?> FieldMap;
    public readonly IDictionary<string, MethodDefinition> MethodMap;

    public readonly MethodDefinition? InstanceConstructor, StaticConstructor;

    public readonly List<FunctionMixin> FunctionMixins;
    public readonly List<MethodAccessor> MethodAccessors;
    public readonly List<MethodInject> MethodInjectors;
    public readonly List<FieldAccessor> FieldAccessors;
    public readonly List<FieldInject> FieldInjectors;

    public TypeMixin(Mod mod, TypeDefinition source, TypeReference target) {
        Mod = mod;
        Source = source;
        Target = target.Resolve();

        if ((Target.Module.Attributes & ModuleAttributes.ILOnly) == 0)
            throw new InvalidModException($"Cannot target {Target} as it's assembly {Target.Module.Assembly.Name.Name} contains native code.", this);

        if (Source.BaseType.FullName != typeof(object).FullName)
            throw new InvalidModException($"Mixin must not extend another class!", this);

        if (source.HasNestedTypes)
            throw new InvalidModException($"Mixins cannot have nested type!", this, source.NestedTypes[0]);

        int genericCount = Source.GenericParameters.Count;
        if (Target.GenericParameters.Count != genericCount)
            throw new InvalidModException($"Mixin's {genericCount} generic parameters do not match target's {Target.GenericParameters.Count} generic parameters.", this);

        if (genericCount != 0) {
            GenericMap = new Dictionary<string, GenericParameter>(genericCount);
            for (int i = 0; i < genericCount; i++)
                GenericMap[Source.GenericParameters[i].FullName] = Target.GenericParameters[i];
            foreach (GenericParameter generic in Source.GenericParameters)
                if (!CILUtils.CompareGenericParameterConstraints(generic, GenericMap[generic.FullName], this, null, null))
                    throw new InvalidModException($"Mixin's generic parameter constraints don't match the target.", this, null);
        } else {
            GenericMap = ImmutableDictionary<string, GenericParameter>.Empty;
        }

        FieldMap = new Dictionary<string, FieldDefinition?>();
        MethodMap = new Dictionary<string, MethodDefinition>();

        FunctionMixins = new List<FunctionMixin>();
        MethodAccessors = new List<MethodAccessor>();
        MethodInjectors = new List<MethodInject>();
        foreach (MethodDefinition method in Source.Methods) {
            bool isSpecialMethod = false;
            // Search for special attributes
            if (method.HasCustomAttributes) {
                foreach (CustomAttribute methodAttribute in method.CustomAttributes) {
                    string attributeName = methodAttribute.AttributeType.FullName;
                    if (attributeName == typeof(MethodTailMixinAttribute).FullName) {
                        CustomAttributeArgument[] methodAttribArgs = methodAttribute.ConstructorArguments.ToArray();
                        if (methodAttribArgs.Length != 2 || methodAttribArgs[0].Value is not string methodTargetName
                                                         || methodAttribArgs[1].Value is not int priority)
                            throw new InvalidModException($"Method is using an invalid constructor for {nameof(MethodTailMixinAttribute)}.", this, method);
                        FunctionMixins.Add(MethodTailMixin.Resolve(method, methodTargetName, (MixinPriority)priority, this));
                        isSpecialMethod = true;
                        break;
                    } else if (attributeName == typeof(MethodHeadMixinAttribute).FullName) {
                        CustomAttributeArgument[] methodAttribArgs = methodAttribute.ConstructorArguments.ToArray();
                        if (methodAttribArgs.Length != 2 || methodAttribArgs[0].Value is not string methodTargetName
                                                         || methodAttribArgs[1].Value is not int priority)
                            throw new InvalidModException($"Method is using an invalid constructor for {nameof(MethodHeadMixinAttribute)}.", this, method);
                        FunctionMixins.Add(MethodHeadMixin.Resolve(method, methodTargetName, (MixinPriority)priority, this));
                        isSpecialMethod = true;
                        break;
                    } else if (attributeName == typeof(MixinMethodAccessorAttribute).FullName) {
                        CustomAttributeArgument[] methodAttribArgs = methodAttribute.ConstructorArguments.ToArray();
                        if (methodAttribArgs.Length != 1 || methodAttribArgs[0].Value is not string methodTargetName)
                            throw new InvalidModException($"Method is using an invalid constructor for {nameof(MixinMethodAccessorAttribute)}.", this, method);
                        MethodAccessors.Add(MethodAccessor.Resolve(method, methodTargetName, this));
                        isSpecialMethod = true;
                        break;
                    } else if (attributeName == typeof(ConstructorMixinAttribute).FullName) {
                        CustomAttributeArgument[] methodAttribArgs = methodAttribute.ConstructorArguments.ToArray();
                        if (methodAttribArgs.Length != 2 || methodAttribArgs[0].Value is not int position
                                                         || methodAttribArgs[1].Value is not int priority)
                            throw new InvalidModException($"Method is using an invalid constructor for {nameof(ConstructorMixinAttribute)}.", this, method);
                        if (method.IsStatic) FunctionMixins.Add(StaticConstructorMixin.Resolve(method, (MixinPriority)priority, (MixinPosition)position, this));
                        else FunctionMixins.Add(InstanceConstructorMixin.Resolve(method, (MixinPriority)priority, (MixinPosition)position, this));
                        isSpecialMethod = true;
                        break;
                    }
                }
            }

            // If we don't have any attributes, we still need to copy the method into the target.
            if (!isSpecialMethod) {
                if (method.IsConstructor) {
                    if (method.IsStatic) {
                        if (method.HasParameters || StaticConstructor != null)
                            throw new InvalidModException($"Cannot declare a static constructor in a mixin!", this, method);
                        StaticConstructor = method;
                    } else {
                        if (method.HasParameters || InstanceConstructor != null)
                            throw new InvalidModException($"Cannot declare a constructor in a mixin!", this, method);
                        InstanceConstructor = method;
                    }
                } else {
                    MethodInjectors.Add(MethodInject.Resolve(this, method));
                }
            }
        }

        FieldAccessors = new List<FieldAccessor>();
        FieldInjectors = new List<FieldInject>();
        foreach (FieldDefinition field in Source.Fields) {
            bool isSpecialField = false;
            if (field.HasCustomAttributes) {
                foreach (CustomAttribute fieldAttribute in field.CustomAttributes) {
                    if (fieldAttribute.AttributeType.FullName == typeof(MixinFieldAccessorAttribute).FullName) {
                        CustomAttributeArgument[] methodAttribArgs = fieldAttribute.ConstructorArguments.ToArray();
                        if (methodAttribArgs.Length != 1 || methodAttribArgs[0].Value is not string fieldTargetName)
                            throw new InvalidModException($"Field is using an invalid constructor for {nameof(MixinFieldAccessorAttribute)}.", this, field);
                        FieldAccessors.Add(FieldAccessor.Resolve(field, fieldTargetName, this));
                        isSpecialField = true;
                        break;
                    } else if (fieldAttribute.AttributeType.FullName == typeof(MixinThisAttribute).FullName) {
                        if (fieldAttribute.HasConstructorArguments)
                            throw new InvalidModException($"Field {field.FullName} is using an invalid constructor for {nameof(MixinThisAttribute)}.", this, field);
                        FieldAccessors.Add(FieldAccessor.ResolveThis(field, this));
                        isSpecialField = true;
                        break;
                    }
                }
            }

            if (!isSpecialField)
                FieldInjectors.Add(FieldInject.Resolve(field, this));
        }

        foreach (PropertyDefinition property in Source.Properties) {
            if (property.HasCustomAttributes) {
                foreach (CustomAttribute propAttribute in property.CustomAttributes) {
                    if (propAttribute.AttributeType.FullName == typeof(MixinFieldAccessorAttribute).FullName) {
                        CustomAttributeArgument[] methodAttribArgs = propAttribute.ConstructorArguments.ToArray();
                        if (methodAttribArgs.Length != 1 || methodAttribArgs[0].Value is not string propTargetName)
                            throw new InvalidModException($"Property is using an invalid constructor for {nameof(MixinFieldAccessorAttribute)}.", this, property);

                        if (property.GetMethod != null) {
                            MethodAccessors.Add(MethodAccessor.Resolve(property.GetMethod, "get_" + propTargetName, this));
                        }
                        if (property.SetMethod != null)
                            MethodAccessors.Add(MethodAccessor.Resolve(property.SetMethod, "set_" + propTargetName, this));

                        MethodInjectors.RemoveAll(methodInjector => 
                                methodInjector.Source == property.GetMethod || methodInjector.Source == property.SetMethod);
                        break;
                    }
                }
            }
        }
    }

    public void InjectConstructor() {
        if (InstanceConstructor != null) {
            // Mixin classes could have things like `class A { string s = "Hello"; }`
            // In this case, `s = "Hello"` is inserted into the generated blank constructor
            // so, we need to copy all the instructions before the call to the object base
            // constructor in the mixin's constructor into the target constructor


            // Firstly, find the location of the call to the base constructor
            int baseConstructorLoc = -1;
            for (int i = 0; i < InstanceConstructor.Body.Instructions.Count; i++) {
                Instruction inst = InstanceConstructor.Body.Instructions[i];
                if (inst.OpCode == OpCodes.Call) {
                    if (inst.Operand is MethodReference methodRef && methodRef.Name == ".ctor" && methodRef.DeclaringType.FullName == typeof(object).FullName) {
                        if (InstanceConstructor.Body.Instructions[i - 1].OpCode == OpCodes.Ldarg_0) {
                            baseConstructorLoc = i;
                            break;
                        }
                        throw new InvalidModException("Unexpected context around call to base constructor in mixin constructor. Expected `ldarg.0`.", this, null);
                    }
                }
            }
            if (baseConstructorLoc == -1)
                throw new InvalidModException("Could not find call to base constructor in mixin constructor.", this, null);

            // If the base constructor is not the last thing in the constructor, than the mod has
            //  declaired a constructor. Wait, that's illegal.
            if (baseConstructorLoc != (InstanceConstructor.Body.Instructions.Count - 2))
                throw new InvalidModException($"Type cannot declare a constructor!", this, InstanceConstructor);

            if (baseConstructorLoc != 1) { // If there is actually something to copy

                // We don't need to copy the instruction to all the constructors, only the ones that don't call some other
                //  constructor.
                //  A type like `class A { A() {} A(string arg) : this() {}}` only the blank constructor needs to initalize
                //  the instance fields.
                List<MethodDefinition> targetConstructors = new();
                foreach (MethodDefinition method in Target.Methods) {
                    if (method.IsConstructor) {
                        if (method.Body.Instructions.Any(inst => {
                            if (inst.OpCode == OpCodes.Call && inst.Operand is MethodReference methodRef && methodRef.Name == ".ctor")
                                // If we call a constructor that isn't our own, we need to have initalized the variables
                                return methodRef.DeclaringType.FullName != Target.FullName;
                            return false;
                        })) {
                            // So copy the instructions!
                            targetConstructors.Add(method);
                        }
                    }
                }

                VariableDefinition[][] localVariables = new VariableDefinition[targetConstructors.Count][];
                for (int i = 0; i < targetConstructors.Count; i++) {
                    localVariables[i] = CILUtils.CopyLocalVariables(InstanceConstructor, targetConstructors[i], this, ImmutableDictionary<string, GenericParameter>.Empty);
                }

                // Copy the instructions before the call to the base constructor into the target constructor
                for (int i = 0; i < baseConstructorLoc - 1; i++) { // -1 as to not include the Ldarg_0 instruction
                    Instruction inst = InstanceConstructor.Body.Instructions[i];
                    for (int j = 0; j < targetConstructors.Count; j++) {
                        CILUtils.ConvertInstruction(inst, this, ImmutableDictionary<string, GenericParameter>.Empty, localVariables[j], InstanceConstructor);
                        targetConstructors[j].Body.Instructions.Insert(i, inst);
                    }
                }
            }
        }

        if (StaticConstructor != null) {
            // Same as above, but for static constructors.
            MethodDefinition? targetStaticConstructor = null;

            foreach (MethodDefinition method in Target.Methods) {
                if (method.IsConstructor && method.IsStatic) {
                    targetStaticConstructor = method;
                    break;
                }
            }

            // Static consturctors have the added complexity of there may not actually be one in
            //  the target, so we might have to create one.
            if (targetStaticConstructor == null) {
                targetStaticConstructor = new MethodDefinition(".cctor",
                    MethodAttributes.Private | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName |
                    MethodAttributes.Static, Target.Module.ImportReference(typeof(void)));
                targetStaticConstructor.Body.GetILProcessor().Emit(OpCodes.Ret);
                Target.Methods.Add(targetStaticConstructor);
            }

            Instruction firstIntruction = targetStaticConstructor.Body.Instructions[0];
            ILProcessor body = targetStaticConstructor.Body.GetILProcessor();
            VariableDefinition[] localVariables = CILUtils.CopyLocalVariables(StaticConstructor, targetStaticConstructor, this, ImmutableDictionary<string, GenericParameter>.Empty);

            foreach (Instruction inst in StaticConstructor.Body.Instructions) {
                CILUtils.ConvertInstruction(inst, this, ImmutableDictionary<string, GenericParameter>.Empty, localVariables, StaticConstructor);
                if (inst.OpCode == OpCodes.Ret) {
                    inst.OpCode = OpCodes.Br;
                    inst.Operand = firstIntruction;
                }
                body.InsertBefore(firstIntruction, inst);
            }
        }
    }
}