namespace Mixolydian;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class CILUtils {
    /// <summary>
    /// Creates a generic map from source to target, if they have the same number of generic parameters and
    ///   the generic constraints match.
    /// </summary>
    /// <param name="source"></param>
    /// <param name="target"></param>
    /// <returns>The map, null if the methods do not have the same number of generic parameters.</returns>
    public static GenericMap? TryCreateGenericMap(MethodDefinition source, MethodDefinition target, TypeMixin type) {
        int genericParamCount = source.GenericParameters.Count;
        if (target.GenericParameters.Count != genericParamCount)
            return null;
        if (genericParamCount == 0)
            return ImmutableDictionary<string, GenericParameter>.Empty;
        GenericMap methodGenericMap = new Dictionary<string, GenericParameter>();
        for (int i = 0; i < genericParamCount; i++)
            methodGenericMap[source.GenericParameters[i].FullName] = target.GenericParameters[i];
        foreach (GenericParameter sourceParam in source.GenericParameters)
            if (!CompareGenericParameterConstraints(sourceParam, methodGenericMap[sourceParam.FullName], type, methodGenericMap, source))
                return null;
        return methodGenericMap;
    }

    /// <summary>
    /// Converts a type reference from a mixin into a type reference that can be used in the target.
    ///  Generics have to be considered. If a mixin `void Test<A>()` targets `void Target<B>()` the
    ///  generic has changed name. Any references to a type like List<A> in the mixin need to be 
    ///  converted into List<B>, and the reference to 'List' needs to be imported into the new module.
    /// </summary>
    /// <param name="type">The type reference to be converted</param>
    /// <param name="target"></param>
    /// <param name="methodGenericMap"></param>
    /// <param name="source">The method this reference is from, for errors. Null if n/a </param>
    /// <returns></returns>
    public static TypeReference ConvertTypeReference(TypeReference type, TypeMixin target, GenericMap methodGenericMap, MemberReference? source = null) {
        // Is this a generic parameter, like 'T'?
        if (type.IsGenericParameter && type is GenericParameter genericType) {
            GenericMap genericMap = genericType.Type switch {
                GenericParameterType.Type => target.GenericMap,
                GenericParameterType.Method => methodGenericMap,
                _ => throw new InvalidModException($"Invalid generic parameter type {genericType.Type}", target),
            };
            if (!genericMap.TryGetValue(type.FullName, out GenericParameter? mappedParam))
                throw new InvalidModException($"Couldn't find generic parameter {type}.", target, source);
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
        TypeReference importedReference = target.Target.Module.ImportReference(type);

        // If we had generic parameters, re-add them!
        if (genericParameters != null) {
            if (importedReference is not IGenericInstance genericInstType2)
                throw new InvalidModException($"Method was generic, but imported reference isn't?", target, source);
            foreach (TypeReference reference in genericParameters)
                genericInstType2.GenericArguments.Add(
                    ConvertTypeReference(reference, target, methodGenericMap, source)
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
    public static MethodReference ConvertMethodReference(MethodReference method, TypeMixin target, GenericMap methodGenericMap, MemberReference? source = null) {

        if (target.MethodMap.TryGetValue(MethodHash(method), out MethodDefinition? mappedDefinition)) {
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
                    type.GenericArguments.Add(ConvertTypeReference(generic, target, methodGenericMap, source));
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
                        ConvertTypeReference(reference, target, methodGenericMap, source)
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

        MethodReference importedReference = target.Target.Module.ImportReference(method);

        {
            // Convert and re-add any generics stripped before

            if (typeGenericArguments != null) {
                if (importedReference.DeclaringType is not IGenericInstance genericInstType2)
                    throw new InvalidModException($"Method declaring type was generic, but imported reference's isn't?", target, source);
                foreach (TypeReference reference in typeGenericArguments)
                    genericInstType2.GenericArguments.Add(
                        ConvertTypeReference(reference, target, methodGenericMap, source)
                    );
            }
            if (methodGenericArguments != null) {
                if (importedReference is not IGenericInstance genericInstType2)
                    throw new InvalidModException($"Method was generic, but imported reference isn't?", target, source);
                foreach (TypeReference reference in methodGenericArguments)
                    genericInstType2.GenericArguments.Add(
                        ConvertTypeReference(reference, target, methodGenericMap, source)
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
    public static FieldReference? ConvertFieldReference(FieldReference field, TypeMixin target, GenericMap methodGenericMap, MethodDefinition source) {

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

        if (CompareTypesNoGenerics(field.DeclaringType, source.DeclaringType) && target.FieldMap.TryGetValue(field.Name, out FieldDefinition? outField)) {
            return outField;
        }

        FieldReference importedReference = target.Target.Module.ImportReference(field);
        {
            if (typeGenericArguments != null) {
                if (importedReference.FieldType is not IGenericInstance fieldTypeInst2)
                    throw new InvalidModException($"Field type was generic, but imported reference's isn't?", target, source);
                foreach (TypeReference reference in typeGenericArguments)
                    fieldTypeInst2.GenericArguments.Add(
                        ConvertTypeReference(reference, target, methodGenericMap, source)
                    );
            }
            if (declairingTypeGenericArguments != null) {
                if (importedReference.DeclaringType is not IGenericInstance declaringTypeInst2)
                    throw new InvalidModException($"Field declaring type was generic, but imported reference's isn't?", target, source);
                foreach (TypeReference reference in declairingTypeGenericArguments)
                    declaringTypeInst2.GenericArguments.Add(
                        ConvertTypeReference(reference, target, methodGenericMap, source)
                    );
            }
        }
        return importedReference;
    }

    public static void ConvertInstruction(Instruction inst, TypeMixin target, GenericMap methodGenericMap, VariableDefinition[]? localVariables, MethodDefinition source) {
        if (inst.Operand is MethodReference operandMethod) {
            inst.Operand = ConvertMethodReference(operandMethod, target, methodGenericMap, source);
        } else if (inst.Operand is TypeReference operandType) {
            inst.Operand = ConvertTypeReference(operandType, target, methodGenericMap, source);
        } else if (inst.Operand is FieldReference operandField) {
            FieldReference? fieldReference = ConvertFieldReference(operandField, target, methodGenericMap, source);
            if (fieldReference == null) { // If the field reference is null, we should actually refer to 'this'
                // We can only load 'this', not set it
                if (inst.OpCode != OpCodes.Ldfld && inst.OpCode != OpCodes.Ldflda)
                    throw new InvalidModException("Cannot set a field with the MixinThis attribute!", target, source);

                // If we are loading an instance field, 'this' must already be the top of the stack,
                //  so to load 'this' instead of the field, we just do nothing.
                inst.OpCode = OpCodes.Nop;
                inst.Operand = null;
            } else
                inst.Operand = fieldReference;
        } else if (localVariables != null && localVariables.Length != 0) {
            (int localVariable, StackInstruction localVariableInstruction) = GetLocalVariableInstructionInfo(inst);
            if (localVariableInstruction != StackInstruction.INVALID) { // If the operand is a local variable index
                VariableDefinition newVariableDef = localVariables[localVariable];
                Instruction replace = CreateLocalVariableInstruction(newVariableDef, localVariableInstruction);
                // We can't just `inst = reaplce` because it may break branching instructions that point to inst
                inst.OpCode = replace.OpCode;
                inst.Operand = replace.Operand;
            }
        }
    }

    public static VariableDefinition[] CopyLocalVariables(MethodDefinition source, MethodDefinition target, TypeMixin type, GenericMap methodGenericMap) {
        VariableDefinition[] localVariables = new VariableDefinition[source.Body.Variables.Count];
        for (int i = 0; i < localVariables.Length; i++) {
            VariableDefinition oldVariable = source.Body.Variables[i];
            VariableDefinition newVariable = new(ConvertTypeReference(oldVariable.VariableType, type, methodGenericMap, source));
            target.Body.Variables.Add(newVariable);
            localVariables[i] = newVariable;
        }
        return localVariables;
    }

    public static string MethodHash(MethodReference method) {
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

    public static bool CompareTypesNoGenerics(TypeReference a, TypeReference b) {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        if (a.Name != b.Name) return false;
        if (a.Namespace != b.Namespace) return false;
        if (!CompareTypesNoGenerics(a.DeclaringType, b.DeclaringType)) return false;
        return true;
    }

    // Used for the ldloc, stloc, ldarg and starg opcodes
    public enum StackInstruction {
        INVALID,
        LOAD_VAL_TO_STACK,
        LOAD_PTR_TO_STACK,
        SET_VAL_FROM_STACK,
    }

    /// <returns>
    /// 'var' is the local variable index, -1 if not a local variable instruction
    /// 'type' is if the instruction type. INVALID if not a local variable instruction.
    /// </returns>
    public static (int var, StackInstruction type) GetLocalVariableInstructionInfo(Instruction inst) {
        OpCode opcode = inst.OpCode;
        if (opcode == OpCodes.Ldloc || opcode == OpCodes.Ldloc_S) return (((VariableDefinition)inst.Operand).Index, StackInstruction.LOAD_VAL_TO_STACK);
        if (opcode == OpCodes.Ldloc_0) return (0, StackInstruction.LOAD_VAL_TO_STACK);
        if (opcode == OpCodes.Ldloc_1) return (1, StackInstruction.LOAD_VAL_TO_STACK);
        if (opcode == OpCodes.Ldloc_2) return (2, StackInstruction.LOAD_VAL_TO_STACK);
        if (opcode == OpCodes.Ldloc_3) return (3, StackInstruction.LOAD_VAL_TO_STACK);
        if (opcode == OpCodes.Ldloca || opcode == OpCodes.Ldloca_S) return (((VariableDefinition)inst.Operand).Index, StackInstruction.LOAD_PTR_TO_STACK);
        if (opcode == OpCodes.Stloc || opcode == OpCodes.Stloc_S) return (((VariableDefinition)inst.Operand).Index, StackInstruction.SET_VAL_FROM_STACK);
        if (opcode == OpCodes.Stloc_0) return (0, StackInstruction.SET_VAL_FROM_STACK);
        if (opcode == OpCodes.Stloc_1) return (1, StackInstruction.SET_VAL_FROM_STACK);
        if (opcode == OpCodes.Stloc_2) return (2, StackInstruction.SET_VAL_FROM_STACK);
        if (opcode == OpCodes.Stloc_3) return (3, StackInstruction.SET_VAL_FROM_STACK);
        return (-1, StackInstruction.INVALID);
    }

    public static Instruction CreateLocalVariableInstruction(VariableDefinition var, StackInstruction type) {
        switch (type) {
            case StackInstruction.LOAD_VAL_TO_STACK:
                if (var.Index == 0) return Instruction.Create(OpCodes.Ldloc_0);
                if (var.Index == 1) return Instruction.Create(OpCodes.Ldloc_1);
                if (var.Index == 2) return Instruction.Create(OpCodes.Ldloc_2);
                if (var.Index == 3) return Instruction.Create(OpCodes.Ldloc_3);
                if (var.Index <= byte.MaxValue) return Instruction.Create(OpCodes.Ldloc_S, var);
                return Instruction.Create(OpCodes.Ldloc, var);
            case StackInstruction.LOAD_PTR_TO_STACK:
                if (var.Index <= byte.MaxValue) return Instruction.Create(OpCodes.Ldloca_S, var);
                return Instruction.Create(OpCodes.Ldloca, var);
            case StackInstruction.SET_VAL_FROM_STACK:
                if (var.Index == 0) return Instruction.Create(OpCodes.Stloc_0);
                if (var.Index == 1) return Instruction.Create(OpCodes.Stloc_1);
                if (var.Index == 2) return Instruction.Create(OpCodes.Stloc_2);
                if (var.Index == 3) return Instruction.Create(OpCodes.Stloc_3);
                if (var.Index <= byte.MaxValue) return Instruction.Create(OpCodes.Stloc_S, var);
                return Instruction.Create(OpCodes.Stloc, var);
        }
        throw new SystemException($"Invalid local variable instruction type {type}.");
    }

    public static (int arg, StackInstruction type) GetArgumentInstructionInfo(Instruction inst) {
        OpCode opcode = inst.OpCode;
        if (opcode == OpCodes.Ldarg || opcode == OpCodes.Ldarg_S) return (((ParameterDefinition)inst.Operand).Index, StackInstruction.LOAD_VAL_TO_STACK);
        if (opcode == OpCodes.Ldarg_0) return (0, StackInstruction.LOAD_VAL_TO_STACK);
        if (opcode == OpCodes.Ldarg_1) return (1, StackInstruction.LOAD_VAL_TO_STACK);
        if (opcode == OpCodes.Ldarg_2) return (2, StackInstruction.LOAD_VAL_TO_STACK);
        if (opcode == OpCodes.Ldarg_3) return (3, StackInstruction.LOAD_VAL_TO_STACK);
        if (opcode == OpCodes.Ldarga || opcode == OpCodes.Ldarga_S) return (((ParameterDefinition)inst.Operand).Index, StackInstruction.LOAD_PTR_TO_STACK);
        if (opcode == OpCodes.Starg || opcode == OpCodes.Starg_S) return (((ParameterDefinition)inst.Operand).Index, StackInstruction.SET_VAL_FROM_STACK);
        return (-1, StackInstruction.INVALID);
    }


    /// <summary>
    /// Compares two methods generics and arguments. 
    /// </summary>
    /// <returns>True if the methods generics and arguments match, false otherwise.<returns>
    public static bool CompareMethodArguments(MethodDefinition a, MethodDefinition b, TypeMixin type, GenericMap? methodGenericMap = null, MethodDefinition? source = null) {
        int paramCount = a.Parameters.Count;
        if (b.Parameters.Count != paramCount)
            return false;
        for (int i = 0; i < paramCount; i++) {
            if (!CompareTypes(a.Parameters[i].ParameterType, b.Parameters[i].ParameterType, type, methodGenericMap, source))
                return false;
        }
        return true;
    }

    public static bool CompareTypes(TypeReference a, TypeReference b, TypeMixin type, GenericMap? methodGenericMap = null, MemberReference? source = null) {
        if (a.IsGenericParameter && a is GenericParameter aGenericParam) {
            if (!b.IsGenericParameter)
                return false;
            GenericMap? genericMap = aGenericParam.Type switch {
                GenericParameterType.Type => type.GenericMap,
                GenericParameterType.Method => methodGenericMap,
                _ => throw new InvalidModException($"Unknown generic parameter type {aGenericParam.Type}", type, source),
            };
            if (!(genericMap?.TryGetValue(a.FullName, out GenericParameter? bExpected) ?? false))
                return false;
            return b.FullName == bExpected.FullName;
        }

        if (a is IGenericInstance aGenericInst) {
            if (b is not IGenericInstance bGenericInst)
                return false;

            if (a.Name != b.Name || a.DeclaringType?.Name != b.DeclaringType?.Name)
                return false;

            int genericArgumentCount = aGenericInst.GenericArguments.Count;
            if (bGenericInst.GenericArguments.Count != genericArgumentCount)
                return false;

            for (int i = 0; i < genericArgumentCount; i++) {
                if (!CompareTypes(aGenericInst.GenericArguments[i], bGenericInst.GenericArguments[i], type, methodGenericMap, source))
                    return false;
            }
            return true;
        }
        return a.FullName == b.FullName;
    }

    public static bool CompareGenericParameterConstraints(GenericParameter source, GenericParameter target, TypeMixin type, GenericMap? methodGenerics, MemberReference? from) {
        if (source.Constraints.Count != target.Constraints.Count) return false;

        if (source.HasDefaultConstructorConstraint != target.HasDefaultConstructorConstraint) return false;
        if (source.HasNotNullableValueTypeConstraint != target.HasNotNullableValueTypeConstraint) return false;
        if (source.HasReferenceTypeConstraint != target.HasReferenceTypeConstraint) return false;
        if (source.Constraints.Count == 0) return true;

        // TODO Investigate comparing attributes for the T : unmanaged constraint.
        List<GenericParameterConstraint> targetConstaints = target.Constraints.ToList();
        foreach (GenericParameterConstraint sourceConstaint in source.Constraints) {
            GenericParameterConstraint? match = null;
            foreach (GenericParameterConstraint targetConstraint in targetConstaints) {
                if (CompareTypes(sourceConstaint.ConstraintType, targetConstraint.ConstraintType, type, methodGenerics, from)) {
                    match = targetConstraint;
                    break;
                }
            }
            if (match == null) return false;
            targetConstaints.Remove(match);
        }
        return true;
    }

    public static readonly ImmutableDictionary<string, string[]> OperatorFunctionNames = new KeyValuePair<string, string[]>[] {
        new("==", new string[] {"op_Equality"}),
        new("!=", new string[] {"op_Inequality"}),
        new(">",  new string[] {"op_GreaterThan"}),
        new("<",  new string[] {"op_LessThan"}),
        new(">=", new string[] {"op_GreaterThanOrEqual"}),
        new("<=", new string[] {"op_LessThanOrEqual"}),
        new("&",  new string[] {"op_BitwiseAnd"}),
        new("|",  new string[] {"op_BitwiseOr"}),
        new("+",  new string[] {"op_Addition", "op_UnaryPlus"}),
        new("-",  new string[] {"op_Subtraction", "op_UnaryNegation"}),
        new("/",  new string[] {"op_Division"}),
        new("%",  new string[] {"op_Modulus"}),
        new("*",  new string[] {"op_Multiply"}),
        new("<<", new string[] {"op_LeftShift"}),
        new(">>", new string[] {"op_RightShift"}),
        new("^",  new string[] {"op_ExclusiveOr"}),
        new("!",  new string[] {"op_LogicalNot"}),
        new("~",  new string[] {"op_OnesComplement"}),
        new("false", new string[] {"op_False"}),
        new("true", new string[] {"op_True"}),
        new("++", new string[] {"op_Increment"}),
        new("--", new string[] {"op_Decrement"}),
    }.ToImmutableDictionary();
}