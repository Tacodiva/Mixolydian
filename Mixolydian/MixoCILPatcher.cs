using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Mixolydian.Common;
using Mono.Cecil;
using Mono.Cecil.Cil;
using FieldMap = System.Collections.Generic.Dictionary<string, Mono.Cecil.FieldDefinition?>;
using GenericMap = System.Collections.Generic.IDictionary<string, Mono.Cecil.GenericParameter>;
using MethodMap = System.Collections.Generic.Dictionary<string, Mono.Cecil.MethodDefinition>;

namespace Mixolydian;

public static class MixoCILPatcher {

    private static readonly GenericMap EmptyGenericMap = ImmutableDictionary<string, GenericParameter>.Empty;

    public static void Apply(MixoMod mod, List<AssemblyDefinition> modifiedAssemblies) {
        foreach (MixoTypeMixin typeMixin in mod.TypeMixins) {
            TypeDefinition targetType = typeMixin.Target.Resolve();

            if ((targetType.Module.Attributes & ModuleAttributes.ILOnly) == 0)
                throw new SystemException($"Mixin cannot target {targetType} as it's assembly {targetType.Module.Assembly.Name.Name} contains native code.");

            if (!modifiedAssemblies.Contains(targetType.Module.Assembly))
                modifiedAssemblies.Add(targetType.Module.Assembly);

            GenericMap typeGenericMap = EmptyGenericMap;
            if (typeMixin.Type.HasGenericParameters) {
                if (!targetType.HasGenericParameters)
                    throw new SystemException($"Unexpected generic parameters on mixin " + typeMixin.Type);

                int genericCount = typeMixin.Type.GenericParameters.Count;
                if (targetType.GenericParameters.Count != genericCount)
                    throw new SystemException($"Wrong number of generic arguments on {typeMixin}. Found {genericCount}, expected {targetType.GenericParameters.Count}");

                typeGenericMap = new Dictionary<string, GenericParameter>();
                for (int i = 0; i < genericCount; i++)
                    typeGenericMap[typeMixin.Type.GenericParameters[i].FullName] = targetType.GenericParameters[i];
            }
            if (targetType.HasGenericParameters && !typeMixin.Type.HasGenericParameters)
                throw new SystemException($"Expected generic parameters on mixin!");

            MethodMap methodMap = new();
            FieldMap fieldMap = new();

            { // Add accessor fields to the field map
                foreach (MixoFieldAccessor accessor in typeMixin.FieldAccessors) {
                    if (accessor.IsThis()) {
                        if (accessor.Field.IsStatic)
                            throw new SystemException($"'MixinThis' fields cannot be static!");

                        if (!accessor.Field.IsInitOnly)
                            throw new SystemException($"'MixinThis' fields must be readonly!");

                        if (targetType.HasGenericParameters) {
                            if (accessor.Field.FieldType is not IGenericInstance fieldType)
                                throw new SystemException($"'MixinThis' fields must have the same generic parameters as their target type!");

                            int genericCount = fieldType.GenericArguments.Count;
                            if (targetType.GenericParameters.Count != genericCount || typeGenericMap == null)
                                throw new SystemException($"'MixinThis' fields must have the same generic parameters as their target type!");

                            for (int i = 0; i < genericCount; i++) {
                                if (!typeGenericMap.TryGetValue(fieldType.GenericArguments[i].FullName, out GenericParameter? mappedGeneric))
                                    throw new SystemException($"'MixinThis' fields must have the same generic parameters as their target type!");
                                if (mappedGeneric.FullName != targetType.GenericParameters[i].FullName)
                                    throw new SystemException($"'MixinThis' fields must have the same generic parameters as their target type!");
                            }
                        } else {
                            if (accessor.Field.FieldType.FullName != targetType.FullName)
                                throw new SystemException($"Field {accessor.Field.FullName} has an invalid type {accessor.Field.FieldType}, expected {targetType}");
                        }

                        // Null entries in this map mean push 'this' onto the stack instead of some actual field. 
                        //   See 'ConvertInstruction' for how *this* is implimented (pun intended).
                        fieldMap[accessor.Field.Name] = null;
                    } else {
                        FieldDefinition? targetField = targetType.Fields.FirstOrDefault(field => field.Name == accessor.TargetFieldName);
                        if (targetField is null)
                            throw new SystemException($"Could not find target field {accessor.TargetFieldName} on {targetType}");

                        if (!targetField.IsStatic && accessor.Field.IsStatic)
                            throw new SystemException($"Accessor {accessor.Field} is static, but target field {targetField} is not static");
                        if (targetField.IsStatic && !accessor.Field.IsStatic)
                            throw new SystemException($"Accessor {accessor.Field} is not static, but target field {targetField} is static");

                        TypeReference mappedFieldType = ConvertTypeReference(accessor.Field.FieldType, targetType, EmptyGenericMap, typeGenericMap);
                        if (mappedFieldType.FullName != targetField.FieldType.FullName)
                            throw new SystemException($"Field {accessor.Field.FullName} has an invalid type {accessor.Field.FieldType}, expected {targetField.FieldType}");
                        fieldMap[accessor.Field.Name] = targetField;
                    }
                }
            }

            { // Copy the non-accessor fields over
                foreach (MixoField field in typeMixin.Fields) {
                    // Find a field name that's avaliable
                    string fieldName = field.Field.Name;
                    if (targetType.Fields.Any(f => f.Name == fieldName)) {
                        int nameIdx = 0;
                        while (targetType.Fields.Any(f => f.Name == fieldName + "_" + nameIdx))
                            nameIdx++;
                        fieldName = fieldName + "_" + nameIdx;
                    }

                    FieldDefinition newField = new(fieldName, field.Field.Attributes, ConvertTypeReference(field.Field.FieldType, targetType, EmptyGenericMap, typeGenericMap));
                    targetType.Fields.Add(newField);
                    fieldMap[field.Field.Name] = newField;
                }
            }

            { // Resolve the method accessors
                foreach (MixoMethodAccessor methodAccessor in typeMixin.MethodAccessors) {
                    // TODO Check if method accessor is extern and has no body

                    MethodDefinition[] targetPotentialMethods = targetType.Methods.Where(method => method.Name == methodAccessor.TargetMethodName).ToArray();
                    if (targetPotentialMethods.Length == 0)
                        throw new SystemException($"No methods named {methodAccessor.TargetMethodName} in {methodAccessor.Method}.");

                    // Find the method we are trying to access. It must have the same parameters and return type.
                    MethodDefinition? target = null;
                    foreach (MethodDefinition possibleTarget in targetPotentialMethods) {
                        GenericMap? methodGenericMap = TryCreateGenericMap(methodAccessor.Method, possibleTarget);
                        if (methodGenericMap == null) continue;
                        if (CompareMethodArguments(methodAccessor.Method, possibleTarget, methodGenericMap, typeGenericMap)) {
                            if (!CompareTypes(methodAccessor.Method.ReturnType, possibleTarget.ReturnType, methodGenericMap, typeGenericMap))
                                continue;
                            target = possibleTarget;
                            break;
                        }
                    }

                    if (target == null)
                        throw new SystemException($"Could not find matching method named {methodAccessor.TargetMethodName} in {targetType}.");

                    methodMap[MethodHash(methodAccessor.Method)] = target;
                }
            }

            { // Copy the non-mixin methods over!
                // The method definitions must be all created before the method instructions are copied
                //  as we may need to find other methods new definitions

                // As to not have to do the work finding generics again, the generic map is stored alongside the method definitions
                List<(MethodDefinition newMethod, GenericMap methodGenericMap)> methods = new();

                foreach (MixoMethod method in typeMixin.Methods) {
                    // Firstly, find a method name that's avaliable
                    string newMethodName;
                    if (targetType.Methods.Any(targetMethod => targetMethod.Name == method.Method.Name)) {
                        int nameIdx = 0;
                        while (targetType.Methods.Any(targetMethod => targetMethod.Name == method.Method.Name + "_" + nameIdx))
                            ++nameIdx;
                        newMethodName = method.Method.Name + "_" + nameIdx;
                    } else newMethodName = method.Method.Name;

                    // The void return type is only temporary, we will repalce it after we have the generic parameters
                    MethodDefinition newMethod = new(newMethodName, method.Method.Attributes, targetType.Module.ImportReference(typeof(void)));

                    // The generic map must be created before the method return type as the return type of the method 
                    //  may contain generics we need to map. IE `public A Get<A>()`
                    GenericMap methodGenericMap = new Dictionary<string, GenericParameter>();
                    foreach (GenericParameter generic in method.Method.GenericParameters) { // Copy generics
                        GenericParameter newGeneric = new(generic.Name, newMethod);
                        newMethod.GenericParameters.Add(newGeneric);
                        methodGenericMap[generic.FullName] = newGeneric;
                    }

                    newMethod.ReturnType = ConvertTypeReference(method.Method.ReturnType, targetType, methodGenericMap, typeGenericMap);

                    // TODO Copy attributes

                    foreach (ParameterDefinition parameter in method.Method.Parameters) {// Copy parameters
                        newMethod.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes, ConvertTypeReference(parameter.ParameterType, targetType, methodGenericMap, typeGenericMap)));
                    }

                    targetType.Methods.Add(newMethod);
                    methodMap.Add(MethodHash(method.Method), newMethod);
                    methods.Add((newMethod, methodGenericMap));
                }

                // Now we have created all the method definitions, copy the instructions!
                for (int i = 0; i < typeMixin.Methods.Count; i++) {
                    (MethodDefinition newMethod, GenericMap methodGenericMap) = methods[i];
                    MixoMethod method = typeMixin.Methods[i];

                    foreach (VariableDefinition localVar in method.Method.Body.Variables) { // Copy local variables
                        newMethod.Body.Variables.Add(new VariableDefinition(ConvertTypeReference(localVar.VariableType, targetType, methodGenericMap, typeGenericMap)));
                    }
                    foreach (Instruction inst in method.Method.Body.Instructions) { // Copy instructions
                        newMethod.Body.Instructions.Add(ConvertInstruction(inst, newMethod, method.Method, methodMap, fieldMap, methodGenericMap, typeGenericMap));
                    }
                }
            }

            if (typeMixin.Constructor != null) { // Insert instance field initalizers to other constructors

                // Mixin classes could have things like `class A { string s = "Hello"; }`
                // In this case, `s = "Hello"` is inserted into the generated blank constructor
                // so, we need to copy all the instructions before the call to the object base
                // constructor in the mixin's constructor into the target constructor

                Instruction[] constructorInstructions = typeMixin.Constructor.Body.Instructions.ToArray();

                // Firstly, find the location of the call to the base constructor
                int baseConstructorLoc = -1;
                for (int i = 0; i < constructorInstructions.Length; i++) {
                    Instruction inst = constructorInstructions[i];
                    if (inst.OpCode == OpCodes.Call) {
                        if (inst.Operand is MethodReference methodRef && methodRef.Name == ".ctor" && methodRef.DeclaringType.FullName == typeof(object).FullName) {
                            if (constructorInstructions[i - 1].OpCode == OpCodes.Ldarg_0) {
                                baseConstructorLoc = i;
                                break;
                            }
                            throw new SystemException("Unexpected context around call to base constructor in mixin constructor. Expected `ldarg.0`.");
                        }
                    }
                }
                if (baseConstructorLoc == -1)
                    throw new SystemException("Could not find call to base constructor in mixin constructor.");

                // If the base constructor is not the last thing in the constructor, than the mod has
                //  declaired a constructor and that's illegal.
                if (baseConstructorLoc != (constructorInstructions.Length - 2))
                    throw new SystemException($"Type {typeMixin.Methods} cannot declare a constructor! Found {typeMixin.Constructor}");

                if (baseConstructorLoc != 1) { // If there is actually something to copy

                    // We don't need to copy the instruction to all the constructors, only the ones that don't call some other
                    //  constructor.
                    //  A type like `class A { A() {} A(string arg) : this() {}}` only the blank constructor needs to initalize
                    //  the instance fields.
                    List<MethodDefinition> targetConstructors = new();
                    foreach (MethodDefinition method in targetType.Methods) {
                        if (method.IsConstructor) {
                            if (method.Body.Instructions.Any(inst => {
                                if (inst.OpCode == OpCodes.Call && inst.Operand is MethodReference methodRef && methodRef.Name == ".ctor")
                                    // If we call a constructor that isn't our own, we need to have initalized the variables
                                    return methodRef.DeclaringType.FullName != targetType.FullName;
                                return false;
                            })) {
                                // So copy the instructions!
                                targetConstructors.Add(method);
                            }
                        }
                    }

                    // Copy the instructions before the call to the base constructor into the target constructor
                    for (int i = 0; i < baseConstructorLoc - 1; i++) { // -1 as to not include the Ldarg_0 instruction
                        Instruction inst = constructorInstructions[i];
                        foreach (MethodDefinition method in targetConstructors) {
                            method.Body.Instructions.Insert(i, ConvertInstruction(inst, method, typeMixin.Constructor, methodMap, fieldMap, EmptyGenericMap, typeGenericMap));
                        }
                    }
                }
            }

            foreach (MixoConstructorMixin constructorMixin in typeMixin.ConstructorMixins) { // Inject the constructor mixins!
                MethodDefinition method = constructorMixin.Method;
                MethodDefinition? target = null;

                if (method.ReturnType.FullName != typeof(void).FullName)
                    throw new SystemException("Constructor mixins must return void!");

                { // Find the target
                    foreach (MethodDefinition potentialTarget in targetType.Methods) {
                        if (!potentialTarget.IsConstructor) continue;
                        if (!CompareMethodArguments(method, potentialTarget, EmptyGenericMap, typeGenericMap))
                            continue;
                        target = potentialTarget;
                        break;
                    }
                    if (target == null)
                        throw new SystemException($"Couldn't find matching constructor in target for constructor mixin {method}.");
                }

                { // Apply the mixin
                    // TODO Find a way to reduce code duplication.
                    //  I'm thinking some kind of OO-ness might save us here.

                    ILProcessor targetMethodProcessor = target.Body.GetILProcessor();

                    // Firstly, copy over all the local variables
                    VariableDefinition[] newMethodVariables = new VariableDefinition[method.Body.Variables.Count];
                    for (int i = 0; i < newMethodVariables.Length; i++) {
                        VariableDefinition oldVariable = method.Body.Variables[i];
                        VariableDefinition newVariable = new(ConvertTypeReference(oldVariable.VariableType, targetType, EmptyGenericMap, typeGenericMap));
                        target.Body.Variables.Add(newVariable);
                        newMethodVariables[i] = newVariable;
                    }

                    // Next, find the first constructor instruction.
                    //  Field initalizers and the call to the base's constructor come before the start of the
                    //   actual constructor's code, so find the call to the base and inject our code after that.
                    Instruction[] targetInstructions = target.Body.Instructions.ToArray();

                    // Firstly, find the location of the call to the base constructor
                    Instruction? firstInstruction = null;
                    for (int i = 0; i < targetInstructions.Length; i++) {
                        Instruction inst = targetInstructions[i];
                        if (inst.OpCode == OpCodes.Call) {
                            if (inst.Operand is MethodReference methodRef && methodRef.Name == ".ctor") {
                                if (targetInstructions[i - 1].OpCode == OpCodes.Ldarg_0) {
                                    firstInstruction = targetInstructions[i + 1];
                                    break;
                                }
                                throw new SystemException("Unexpected context around call to base constructor in mixin constructor. Expected `ldarg.0`.");
                            }
                        }
                    }
                    if (firstInstruction == null)
                        throw new SystemException("Could not find call to base constructor in target constructor.");

                    Instruction[] methodInstructions = method.Body.Instructions.ToArray();
                    for (int i = 0; i < methodInstructions.Length; i++) {
                        Instruction inst = ConvertInstruction(methodInstructions[i], target, method, methodMap, fieldMap, EmptyGenericMap, typeGenericMap);

                        if (inst.OpCode == OpCodes.Ret) {
                            inst.OpCode = OpCodes.Br;
                            inst.Operand = firstInstruction;
                        }

                        if (newMethodVariables.Length != 0) {
                            (int localVariable, LocalVariableInstruction localVariableInstruction) = GetLocalVariableInstruction(inst);
                            if (localVariableInstruction != LocalVariableInstruction.INVALID) {
                                VariableDefinition newVariableDef = newMethodVariables[localVariable];
                                Instruction replace = CreateLocalVariableInstruction(newVariableDef, localVariableInstruction);
                                // We can't just `inst = reaplce` because it may break branching instructions that point to inst
                                inst.OpCode = replace.OpCode;
                                inst.Operand = replace.Operand;
                            }
                        }

                        targetMethodProcessor.InsertBefore(firstInstruction, inst);
                    }
                    Console.WriteLine($"Injected {newMethodVariables.Length} variables, {method.Body.Instructions.Count} instructions into {target}");
                }
            }

            foreach (MixoMethodMixin methodMixin in typeMixin.MethodMixins) { // Inject the method mixins!

                MethodDefinition method = methodMixin.Method;
                MethodDefinition? target = null;
                GenericMap? methodGenericMap = null;

                TypeReference? expectedReturn; // If null, expected return is `void`
                { // Find the expected return type by extracting it from MixinReturn
                    if (method.ReturnType.IsGenericInstance && method.ReturnType is IGenericInstance methodReturnGeneric) {
                        if (method.ReturnType.Name != typeof(MixinReturn<object>).Name || methodReturnGeneric.GenericArguments.Count != 1)
                            throw new SystemException($"Mixins must return `MixinReturn`! Found {method}");
                        expectedReturn = methodReturnGeneric.GenericArguments[0];
                    } else {
                        if (method.ReturnType.FullName != typeof(MixinReturn).FullName)
                            throw new SystemException($"Mixins must return `MixinReturn`! Found {method}");
                        expectedReturn = null;
                    }
                }

                { // Find the target method definition in the resolved type
                    MethodDefinition[] targetPotentialMethods = targetType.Methods.Where(method => method.Name == methodMixin.TargetName).ToArray();
                    if (targetPotentialMethods.Length == 0)
                        throw new SystemException($"No method named {methodMixin.TargetName} in {targetType}.");

                    // Look through all the methods to try and find a matching method definition
                    foreach (MethodDefinition possibleTarget in targetPotentialMethods) {
                        if (possibleTarget.IsStatic != method.IsStatic) continue;
                        GenericMap? possibleMethodGenericMap = TryCreateGenericMap(method, possibleTarget);
                        if (possibleMethodGenericMap == null) continue;
                        if (CompareMethodArguments(method, possibleTarget, possibleMethodGenericMap, typeGenericMap)) {

                            if (expectedReturn != null) {
                                if (!CompareTypes(expectedReturn, possibleTarget.ReturnType, possibleMethodGenericMap, typeGenericMap))
                                    continue;
                            } else {
                                if (possibleTarget.ReturnType.FullName != typeof(void).FullName)
                                    continue;
                            }

                            methodGenericMap = possibleMethodGenericMap;
                            target = possibleTarget;
                            break;
                        }
                    }

                    if (target == null || methodGenericMap == null)
                        throw new SystemException($"Could not find matching method named {methodMixin.TargetName} in {targetType}.");

                    // We only need to create the map if we actually have generic parameters
                    if (method.HasGenericParameters) {
                        methodGenericMap = new Dictionary<string, GenericParameter>();
                        for (int i = 0; i < method.GenericParameters.Count; i++)
                            methodGenericMap[method.GenericParameters[i].FullName] = target.GenericParameters[i];
                    }
                }

                { // Apply the mixin!
                    ILProcessor targetMethodProcessor = target.Body.GetILProcessor();

                    // Firstly, copy over all the local variables
                    VariableDefinition[] newMethodVariables = new VariableDefinition[method.Body.Variables.Count];
                    for (int i = 0; i < newMethodVariables.Length; i++) {
                        VariableDefinition oldVariable = method.Body.Variables[i];
                        VariableDefinition newVariable = new(ConvertTypeReference(oldVariable.VariableType, targetType, methodGenericMap, typeGenericMap));
                        target.Body.Variables.Add(newVariable);
                        newMethodVariables[i] = newVariable;
                    }

                    // Next copy the instructions
                    Instruction firstInstruction = target.Body.Instructions[0];
                    Instruction[] methodInstructions = method.Body.Instructions.ToArray();
                    for (int i = 0; i < methodInstructions.Length; i++) {
                        Instruction inst = ConvertInstruction(methodInstructions[i], target, method, methodMap, fieldMap, methodGenericMap, typeGenericMap);

                        if (inst.OpCode == OpCodes.Call && inst.Operand is MethodReference callOperand
                            && callOperand.ReturnType.FullName.StartsWith(typeof(MixinReturn).FullName!) && callOperand.DeclaringType.Namespace == "Mixolydian.Common") {
                            // Found a call to MixinReturn!

                            // Skip this instruction, the next instruction should be a return
                            ++i;
                            Instruction currentInst;
                            if (i >= methodInstructions.Length || (currentInst = methodInstructions[i]).OpCode != OpCodes.Ret)
                                throw new SystemException($"Calls to {callOperand.Name} must be instantly returned. Mixin {method}");

                            inst.OpCode = OpCodes.Nop;
                            inst.Operand = null;
                            targetMethodProcessor.InsertBefore(firstInstruction, inst);

                            inst = currentInst;
                            switch (callOperand.Name) {
                                case nameof(MixinReturn.Continue):
                                    // Jump to the end of the mixin
                                    inst.OpCode = OpCodes.Br;
                                    inst.Operand = firstInstruction;
                                    break;
                                case nameof(MixinReturn.Return):
                                    inst.OpCode = OpCodes.Ret;
                                    inst.Operand = null;
                                    break;
                                default:
                                    throw new SystemException($"Unknown {nameof(MixinReturn)} method {callOperand.Name}.");
                            }
                            targetMethodProcessor.InsertBefore(firstInstruction, inst);
                            continue;
                        }

                        if (inst.OpCode == OpCodes.Ret)
                            throw new SystemException($"Only calls to {nameof(MixinReturn)} can be returned! Mixin {method}");

                        if (newMethodVariables.Length != 0) {
                            (int localVariable, LocalVariableInstruction localVariableInstruction) = GetLocalVariableInstruction(inst);
                            if (localVariableInstruction != LocalVariableInstruction.INVALID) {
                                VariableDefinition newVariableDef = newMethodVariables[localVariable];
                                Instruction replace = CreateLocalVariableInstruction(newVariableDef, localVariableInstruction);
                                // We can't just `inst = reaplce` because it may break branching instructions that point to inst
                                inst.OpCode = replace.OpCode;
                                inst.Operand = replace.Operand;
                            }
                        }

                        targetMethodProcessor.InsertBefore(firstInstruction, inst);
                    }
                    Console.WriteLine($"Injected {newMethodVariables.Length} variables, {method.Body.Instructions.Count} instructions into {target}");
                }
            }

            // Remove the mixin from the mod's assembly
            typeMixin.Type.Module.Types.Remove(typeMixin.Type);
        }
    }

    /// <summary>
    /// Creates a generic map from source to target, if they have the same number of generic parameters.
    /// </summary>
    /// <param name="source"></param>
    /// <param name="target"></param>
    /// <returns>The map, null if the methods do not have the same number of generic parameters.</returns>
    private static GenericMap? TryCreateGenericMap(MethodDefinition source, MethodDefinition target) {
        int genericParamCount = source.GenericParameters.Count;
        if (target.GenericParameters.Count != genericParamCount)
            return null;
        if (genericParamCount == 0)
            return EmptyGenericMap;
        GenericMap methodGenericMap = new Dictionary<string, GenericParameter>();
        for (int i = 0; i < genericParamCount; i++)
            methodGenericMap[source.GenericParameters[i].FullName] = target.GenericParameters[i];
        return methodGenericMap;
    }

    /// <summary>
    /// Compares two methods generics and arguments. 
    /// </summary>
    /// <returns>True if the methods generics and arguments match, false otherwise.<returns>
    /// TODO Compare generic parameter constraints?
    private static bool CompareMethodArguments(MethodDefinition mixin, MethodDefinition target, GenericMap methodGenericMap, GenericMap typeGenericMap) {
        int paramCount = mixin.Parameters.Count;
        if (target.Parameters.Count != paramCount)
            return false;
        for (int i = 0; i < paramCount; i++) {
            if (!CompareTypes(mixin.Parameters[i].ParameterType, target.Parameters[i].ParameterType, methodGenericMap, typeGenericMap))
                return false;
        }
        return true;
    }


    private static bool CompareTypes(TypeReference a, TypeReference b, GenericMap methodGenericMap, GenericMap typeGenericMap) {
        if (a.IsGenericParameter && a is GenericParameter aGenericParam) {
            if (!b.IsGenericParameter)
                return false;
            GenericMap genericMap = aGenericParam.Type switch {
                GenericParameterType.Type => typeGenericMap,
                GenericParameterType.Method => methodGenericMap,
                _ => throw new SystemException($"Unknown generic parameter type {aGenericParam.Type}"),
            };
            if (!genericMap.TryGetValue(a.FullName, out GenericParameter? bExpected))
                return false;
            return b.FullName == bExpected.FullName;
        }

        if (a is IGenericInstance aGenericInst) {
            if (b is not IGenericInstance bGenericInst)
                return false;

            if (a.Name != b.Name || a.DeclaringType != b.DeclaringType)
                return false;

            int genericArgumentCount = aGenericInst.GenericArguments.Count;
            if (bGenericInst.GenericArguments.Count != genericArgumentCount)
                return false;

            for (int i = 0; i < genericArgumentCount; i++) {
                if (!CompareTypes(aGenericInst.GenericArguments[i], bGenericInst.GenericArguments[i], methodGenericMap, typeGenericMap))
                    return false;
            }
            return true;
        }
        return a.FullName == b.FullName;
    }

    // TODO Document
    private static Instruction ConvertInstruction(Instruction inst, MethodDefinition target, MethodDefinition source, MethodMap methodMap, FieldMap fieldMap, GenericMap methodGenericMap, GenericMap typeGenericMap) {
        if (inst.Operand is MethodReference operandMethod) {
            inst.Operand = ConvertMethodReference(operandMethod, target.DeclaringType, methodMap, methodGenericMap, typeGenericMap);
        } else if (inst.Operand is TypeReference operandType) {
            inst.Operand = ConvertTypeReference(operandType, target.DeclaringType, methodGenericMap, typeGenericMap);
        } else if (inst.Operand is FieldReference operandField) {
            FieldReference? fieldReference = ConvertFieldReference(operandField, target.DeclaringType, source.DeclaringType, fieldMap, methodGenericMap, typeGenericMap);
            if (fieldReference == null) { // If the field reference is null, we should actually refer to 'this'
                // We can only load 'this', not set it
                if (inst.OpCode != OpCodes.Ldfld && inst.OpCode != OpCodes.Ldflda)
                    throw new SystemException("Cannot set a field with the MixinThis attribute!");

                // If we are loading an instance field, 'this' must already be the top of the stack,
                //  so to load 'this' instead of the field, we just do nothing.
                inst.OpCode = OpCodes.Nop;
                inst.Operand = null;
            } else
                inst.Operand = fieldReference;
        }
        return inst;
    }

    /// <summary>
    /// Converts a type reference from a mixin into a type reference that can be used in the target.
    ///  Generics have to be considered. If a mixin `void Test<A>()` targets `void Target<B>()` the
    ///  generic has changed name. Any references to a type like List<A> in the mixin need to be 
    ///  converted into List<B>, and the reference to 'List' needs to be imported into the new module.
    /// </summary>
    /// <param name="type">The type reference to be converted</param>
    /// <param name="target">The type the rference is going to be put in</param>
    /// <param name="methodGenericMap">A map that converts from the mixins method's generics to the target method's generics</param>
    /// <param name="typeGenericMap">A map the converts from the mixins declairing type's generic to the targets type's generics.</param>
    /// <returns>The converted reference</returns>
    private static TypeReference ConvertTypeReference(TypeReference type, TypeDefinition target, GenericMap methodGenericMap, GenericMap typeGenericMap) {
        // Is this a generic parameter, like 'T'?
        if (type.IsGenericParameter && type is GenericParameter genericType) {
            GenericMap genericMap = genericType.Type switch {
                GenericParameterType.Type => typeGenericMap,
                GenericParameterType.Method => methodGenericMap,
                _ => throw new SystemException($"Invalid generic parameter type {genericType.Type}"),
            };
            if (!genericMap.TryGetValue(type.FullName, out GenericParameter? mappedParam))
                throw new SystemException($"Couldn't find generic parameter {type}.");
            return mappedParam;
        }

        // Importing a reference like List<A> will crash if 'A' is a generic parameter of our method.
        //  Solution: Remove all generic parameters, import the reference, then convert the generics
        //  and re-add them.
        List<TypeReference>? genericParameters = null;
        // Is this a generic instance, like List<object>?
        if (type.IsGenericInstance && type is IGenericInstance genericInstType) {
            genericParameters = new List<TypeReference>();
            foreach (TypeReference reference in genericInstType.GenericArguments) {
                genericParameters.Add(reference);
            }
            genericInstType.GenericArguments.Clear();
        }

        // Importing the reference essentially just changes what module it's in.
        TypeReference importedReference = target.Module.ImportReference(type);

        // If we had generic parameters, re-add them!
        if (genericParameters != null) {
            if (importedReference is not IGenericInstance genericInstType2)
                throw new SystemException($"Method was generic, but imported reference isn't?");
            foreach (TypeReference reference in genericParameters)
                genericInstType2.GenericArguments.Add(
                    ConvertTypeReference(reference, target, methodGenericMap, typeGenericMap)
                );
        }

        return importedReference;
    }

    /// <summary>
    /// Converts a method reference from a mixin into a method reference that can be used in the target.
    ///  Generics have to be considered. For exmaple, if a mixin `void Test<A>()` targets `void Target<B>()`
    ///  the generic has changed name from 'A' to 'B'. Any references to a method like List<A>.Clear() or
    ///  Array.Empty<A>() in the mixin need to be converted into List<B>.Clear() or Array.Empty<B>(), and
    ///  the reference to 'List' or 'Array' needs to be imported into the new module.
    /// </summary>
    /// <param name="method">The method reference to be converted</param>
    /// <param name="target">The type the rference is going to be put in</param>
    /// <param name="methodGenericMap">A map that converts from the mixins method's generics to the target method's generics</param>
    /// <param name="typeGenericMap">A map the converts from the mixins declairing type's generic to the targets type's generics.</param>
    /// <returns></returns>
    private static MethodReference ConvertMethodReference(MethodReference method, TypeDefinition target, MethodMap methodMap, GenericMap methodGenericMap, GenericMap typeGenericMap) {

        if (methodMap.TryGetValue(MethodHash(method), out MethodDefinition? mappedDefinition)) {
            // This is for methods that need to be redirected.
            //  For them, we need to keep most of the method definition properties the same, but map all
            //   of the generic parameters. 
            MethodReference outRef = new(mappedDefinition.Name, mappedDefinition.ReturnType) {
                HasThis = mappedDefinition.HasThis,
                ExplicitThis = mappedDefinition.ExplicitThis,
                CallingConvention = mappedDefinition.CallingConvention,
            };

            TypeReference declairingType = mappedDefinition.DeclaringType;
            if (method.DeclaringType.IsGenericInstance && method.DeclaringType is IGenericInstance declaringTypeGeneric) {
                GenericInstanceType type = new(mappedDefinition.DeclaringType);
                foreach (TypeReference generic in declaringTypeGeneric.GenericArguments) {
                    type.GenericArguments.Add(ConvertTypeReference(generic, target, methodGenericMap, typeGenericMap));
                }
                declairingType = type;
            }
            outRef.DeclaringType = declairingType;

            foreach (ParameterDefinition parameter in mappedDefinition.Parameters)
                outRef.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes, parameter.ParameterType));

            foreach (GenericParameter parameter in mappedDefinition.GenericParameters)
                outRef.GenericParameters.Add(new GenericParameter(parameter.Name, parameter.Owner));

            if (method.IsGenericInstance && method is GenericInstanceMethod methodIst) {
                GenericInstanceMethod outRefGeneric = new(outRef);
                foreach (TypeReference reference in methodIst.GenericArguments) {
                    outRefGeneric.GenericArguments.Add(
                        ConvertTypeReference(reference, target, methodGenericMap, typeGenericMap)
                    );
                }
                outRef = outRefGeneric;
            }

            return outRef;
        }

        List<TypeReference>? typeGenericArguments = null;
        List<TypeReference>? methodGenericArguments = null;

        {
            // For calls like List<A>.Clear() where the declaring type has generics.
            //  Remove the generics so the method can be resolved, then re-add the
            //  converted generics later.
            if (method.DeclaringType.IsGenericInstance && method.DeclaringType is IGenericInstance typeGenInst) {
                typeGenericArguments = new List<TypeReference>();
                foreach (TypeReference reference in typeGenInst.GenericArguments)
                    typeGenericArguments.Add(reference);
                typeGenInst.GenericArguments.Clear();
            }

            // For calls like Array.Empty<A>() where the method itself has generics.
            //  Same strategy as above.
            if (method.IsGenericInstance && method is IGenericInstance methodGenInst) {
                methodGenericArguments = new List<TypeReference>();
                foreach (TypeReference reference in methodGenInst.GenericArguments)
                    methodGenericArguments.Add(reference);
                methodGenInst.GenericArguments.Clear();
            }
        }

        MethodReference importedReference = target.Module.ImportReference(method);

        {
            // Convert and re-add any generics stripped before

            if (typeGenericArguments != null) {
                if (importedReference.DeclaringType is not IGenericInstance genericInstType2)
                    throw new SystemException($"Method declaring type was generic, but imported reference's isn't?");
                foreach (TypeReference reference in typeGenericArguments)
                    genericInstType2.GenericArguments.Add(
                        ConvertTypeReference(reference, target, methodGenericMap, typeGenericMap)
                    );
            }
            if (methodGenericArguments != null) {
                if (importedReference is not IGenericInstance genericInstType2)
                    throw new SystemException($"Method was generic, but imported reference isn't?");
                foreach (TypeReference reference in methodGenericArguments)
                    genericInstType2.GenericArguments.Add(
                        ConvertTypeReference(reference, target, methodGenericMap, typeGenericMap)
                    );
            }
        }

        return importedReference;
    }

    /// <summary>
    /// Converts a field reference from a mixin into a field reference that can be used in the target.
    /// Generics have to be considered. For exmaple, if a mixin `void Test<A>()` targets `void Target<B>()`
    /// the generic has changed name from 'A' to 'B'. Any references to a field like int `List<A>.Count` or
    /// `A Value` the generics need to be converted.
    /// </summary>
    /// <param name="field">The field to be converted</param>
    /// <param name="target">The type the rference is going to be put in</param>
    /// <param name="source">The type the rference is coming from</param>
    /// <param name="fieldMap">A map of field names to field definitions in the target. Used for injected fields and redirected fields</param>
    /// <param name="methodGenericMap">A map that converts from the mixins method's generics to the target method's generics</param>
    /// <param name="typeGenericMap">A map the converts from the mixins declairing type's generic to the targets type's generics.</param>
    /// <returns>The field reference. Null if the field should refer to `this`</returns>
    private static FieldReference? ConvertFieldReference(FieldReference field, TypeDefinition target, TypeDefinition source, FieldMap fieldMap, GenericMap methodGenericMap, GenericMap typeGenericMap) {

        List<TypeReference>? typeGenericArguments = null;
        List<TypeReference>? declairingTypeGenericArguments = null;


        {
            if (field.FieldType.IsGenericInstance && field.FieldType is IGenericInstance fieldTypeInst) {
                typeGenericArguments = new List<TypeReference>();
                foreach (TypeReference reference in fieldTypeInst.GenericArguments)
                    typeGenericArguments.Add(reference);
                fieldTypeInst.GenericArguments.Clear();
            }

            if (field.DeclaringType.IsGenericInstance && field.DeclaringType is IGenericInstance declaringTypeInst) {
                declairingTypeGenericArguments = new List<TypeReference>();
                foreach (TypeReference reference in declaringTypeInst.GenericArguments)
                    declairingTypeGenericArguments.Add(reference);
                declaringTypeInst.GenericArguments.Clear();
            }
        }

        if (CompareTypesNoGenerics(field.DeclaringType, source) && fieldMap.TryGetValue(field.Name, out FieldDefinition? outField)) {
            return outField;
        }

        FieldReference importedReference = target.Module.ImportReference(field);
        {
            if (typeGenericArguments != null) {
                if (importedReference.FieldType is not IGenericInstance fieldTypeInst2)
                    throw new SystemException($"Field type was generic, but imported reference's isn't?");
                foreach (TypeReference reference in typeGenericArguments)
                    fieldTypeInst2.GenericArguments.Add(
                        ConvertTypeReference(reference, target, methodGenericMap, typeGenericMap)
                    );
            }
            if (declairingTypeGenericArguments != null) {
                if (importedReference.DeclaringType is not IGenericInstance declaringTypeInst2)
                    throw new SystemException($"Field declaring type was generic, but imported reference's isn't?");
                foreach (TypeReference reference in declairingTypeGenericArguments)
                    declaringTypeInst2.GenericArguments.Add(
                        ConvertTypeReference(reference, target, methodGenericMap, typeGenericMap)
                    );
            }
        }
        return importedReference;
    }


    #region Local Variable Instruction Conversion
    private enum LocalVariableInstruction {
        INVALID,
        LOAD_VAL_TO_STACK,
        LOAD_PTR_TO_STACK,
        SET_VAL_FROM_STACK,
    }

    /// <returns>
    /// 'var' is the local variable index, -1 if not a local variable instruction
    /// 'type' is if the instruction type. INVALID if not a local variable instruction.
    /// </returns>
    private static (int var, LocalVariableInstruction type) GetLocalVariableInstruction(Instruction inst) {
        OpCode opcode = inst.OpCode;
        if (opcode == OpCodes.Ldloc || opcode == OpCodes.Ldloc_S) return (((VariableDefinition)inst.Operand).Index, LocalVariableInstruction.LOAD_VAL_TO_STACK);
        if (opcode == OpCodes.Ldloc_0) return (0, LocalVariableInstruction.LOAD_VAL_TO_STACK);
        if (opcode == OpCodes.Ldloc_1) return (1, LocalVariableInstruction.LOAD_VAL_TO_STACK);
        if (opcode == OpCodes.Ldloc_2) return (2, LocalVariableInstruction.LOAD_VAL_TO_STACK);
        if (opcode == OpCodes.Ldloc_3) return (3, LocalVariableInstruction.LOAD_VAL_TO_STACK);
        if (opcode == OpCodes.Ldloca || opcode == OpCodes.Ldloca_S) return (((VariableDefinition)inst.Operand).Index, LocalVariableInstruction.LOAD_PTR_TO_STACK);
        if (opcode == OpCodes.Stloc || opcode == OpCodes.Stloc_S) return (((VariableDefinition)inst.Operand).Index, LocalVariableInstruction.SET_VAL_FROM_STACK);
        if (opcode == OpCodes.Stloc_0) return (0, LocalVariableInstruction.SET_VAL_FROM_STACK);
        if (opcode == OpCodes.Stloc_1) return (1, LocalVariableInstruction.SET_VAL_FROM_STACK);
        if (opcode == OpCodes.Stloc_2) return (2, LocalVariableInstruction.SET_VAL_FROM_STACK);
        if (opcode == OpCodes.Stloc_3) return (3, LocalVariableInstruction.SET_VAL_FROM_STACK);
        return (-1, LocalVariableInstruction.INVALID);
    }

    private static Instruction CreateLocalVariableInstruction(VariableDefinition var, LocalVariableInstruction type) {
        switch (type) {
            case LocalVariableInstruction.LOAD_VAL_TO_STACK:
                if (var.Index == 0) return Instruction.Create(OpCodes.Ldloc_0);
                if (var.Index == 1) return Instruction.Create(OpCodes.Ldloc_1);
                if (var.Index == 2) return Instruction.Create(OpCodes.Ldloc_2);
                if (var.Index == 3) return Instruction.Create(OpCodes.Ldloc_3);
                if (var.Index <= byte.MaxValue) return Instruction.Create(OpCodes.Ldloc_S, var);
                return Instruction.Create(OpCodes.Ldloc, var);
            case LocalVariableInstruction.LOAD_PTR_TO_STACK:
                if (var.Index <= byte.MaxValue) return Instruction.Create(OpCodes.Ldloca_S, var);
                return Instruction.Create(OpCodes.Ldloca, var);
            case LocalVariableInstruction.SET_VAL_FROM_STACK:
                if (var.Index == 0) return Instruction.Create(OpCodes.Stloc_0);
                if (var.Index == 1) return Instruction.Create(OpCodes.Stloc_1);
                if (var.Index == 2) return Instruction.Create(OpCodes.Stloc_2);
                if (var.Index == 3) return Instruction.Create(OpCodes.Stloc_3);
                if (var.Index <= byte.MaxValue) return Instruction.Create(OpCodes.Stloc_S, var);
                return Instruction.Create(OpCodes.Stloc, var);
        }
        throw new SystemException($"Invalid local variable instruction type {type}.");
    }
    #endregion

    private static string MethodHash(MethodReference method) {
        int genericCount;
        if (method.IsGenericInstance && method is IGenericInstance inst)
            genericCount = inst.GenericArguments.Count;
        else genericCount = method.GenericParameters.Count;
        int declaringTypeGenericCount;
        if (method.DeclaringType.IsGenericInstance && method.DeclaringType is IGenericInstance genericInst)
            declaringTypeGenericCount = genericInst.GenericArguments.Count;
        else declaringTypeGenericCount = method.DeclaringType.GenericParameters.Count;
        return method.DeclaringType.Namespace + "$$" + method.DeclaringType.Name + "$$" + declaringTypeGenericCount
            + "$$" + method.Name + "$$" + string.Concat(method.Parameters.Select(param => param.ParameterType))
            + "$$" + genericCount;
    }

    private static bool CompareTypesNoGenerics(TypeReference a, TypeReference b) {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        if (a.Name != b.Name) return false;
        if (a.Namespace != b.Namespace) return false;
        if (!CompareTypesNoGenerics(a.DeclaringType, b.DeclaringType)) return false;
        return true;
    }

}