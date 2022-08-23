using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Mixolydian.Common;
using Mono.Cecil;
using Mono.Cecil.Cil;
using GenericMap = System.Collections.Generic.IDictionary<string, Mono.Cecil.GenericParameter>;

namespace Mixolydian;

public class TypeMixin {

    public readonly Mod Mod;

    public readonly TypeDefinition Source;
    public readonly TypeDefinition Target;
    public readonly GenericMap GenericMap;

    public readonly IDictionary<string, FieldDefinition?> FieldMap;
    public readonly IDictionary<string, MethodDefinition> MethodMap;

    public readonly MethodDefinition? Constructor;

    public readonly IList<FunctionMixin> FunctionMixins;
    public readonly IList<MethodAccessor> MethodAccessors;
    public readonly IList<MethodInject> MethodInjectors;
    public readonly IList<FieldAccessor> FieldAccessors;
    public readonly IList<FieldInject> FieldInjectors;

    public TypeMixin(Mod mod, TypeDefinition source, TypeReference target) {
        Mod = mod;
        Source = source;
        Target = target.Resolve();

        if ((Target.Module.Attributes & ModuleAttributes.ILOnly) == 0)
            throw new InvalidModException($"Cannot target {Target} as it's assembly {Target.Module.Assembly.Name.Name} contains native code.", this);

        if (Source.BaseType.FullName != typeof(object).FullName)
            throw new InvalidModException($"Mixin must not extend another class!", this);


        int genericCount = Source.GenericParameters.Count;
        if (Target.GenericParameters.Count != genericCount)
            throw new InvalidModException($"Mixin's {genericCount} generic parameters do not match target's {Target.GenericParameters.Count} generic parameters.", this);

        if (genericCount != 0) {
            GenericMap = new Dictionary<string, GenericParameter>(genericCount);
            for (int i = 0; i < genericCount; i++)
                GenericMap[Source.GenericParameters[i].FullName] = Target.GenericParameters[i];
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
                    if (attributeName == typeof(MethodMixinAttribute).FullName) {
                        CustomAttributeArgument[] methodAttribArgs = methodAttribute.ConstructorArguments.ToArray();
                        if (methodAttribArgs.Length != 2 || methodAttribArgs[0].Value is not string methodTargetName || methodAttribArgs[1].Value is not int priority)
                            throw new InvalidModException($"Method is using an invalid constructor for {nameof(MethodMixinAttribute)}.", this, method);
                        FunctionMixins.Add(MethodMixin.Resolve(method, methodTargetName, priority, this));
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
                        if (methodAttribArgs.Length != 1 || methodAttribArgs[0].Value is not int priority)
                            throw new InvalidModException($"Method is using an invalid constructor for {nameof(ConstructorMixinAttribute)}.", this, method);
                        FunctionMixins.Add(ConstructorMixin.Resolve(method, priority, this));
                        isSpecialMethod = true;
                        break;
                    }
                }
            }

            // If we don't have any attributes, we still need to copy the method into the target.
            if (!isSpecialMethod) {
                if (method.IsConstructor) {
                    if (method.HasParameters)
                        throw new InvalidModException($"Cannot declare a constructor in a mixin!", this, method);
                    Constructor = method;
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
    }

    public void InjectConstructor() {
        if (Constructor == null) return;
        // Mixin classes could have things like `class A { string s = "Hello"; }`
        // In this case, `s = "Hello"` is inserted into the generated blank constructor
        // so, we need to copy all the instructions before the call to the object base
        // constructor in the mixin's constructor into the target constructor


        // Firstly, find the location of the call to the base constructor
        int baseConstructorLoc = -1;
        for (int i = 0; i < Constructor.Body.Instructions.Count; i++) {
            Instruction inst = Constructor.Body.Instructions[i];
            if (inst.OpCode == OpCodes.Call) {
                if (inst.Operand is MethodReference methodRef && methodRef.Name == ".ctor" && methodRef.DeclaringType.FullName == typeof(object).FullName) {
                    if (Constructor.Body.Instructions[i - 1].OpCode == OpCodes.Ldarg_0) {
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
        if (baseConstructorLoc != (Constructor.Body.Instructions.Count - 2))
            throw new InvalidModException($"Type cannot declare a constructor!", this, Constructor);

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
                localVariables[i] = CILUtils.CopyLocalVariables(Constructor, targetConstructors[i], this, ImmutableDictionary<string, GenericParameter>.Empty);
            }

            // Copy the instructions before the call to the base constructor into the target constructor
            for (int i = 0; i < baseConstructorLoc - 1; i++) { // -1 as to not include the Ldarg_0 instruction
                Instruction inst = Constructor.Body.Instructions[i];
                for (int j = 0; j < targetConstructors.Count; j++) {
                    CILUtils.ConvertInstruction(inst, this, ImmutableDictionary<string, GenericParameter>.Empty, localVariables[j], Constructor);
                    targetConstructors[j].Body.Instructions.Insert(i, inst);
                }
            }
        }
    }
}