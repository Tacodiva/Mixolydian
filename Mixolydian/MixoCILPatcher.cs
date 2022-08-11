using System;
using System.Linq;
using Mixolydian.Common;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Mixolydian;

public static class MixoCILPatcher {

    public static void Apply(MixoMod mod) {
        foreach (MixoTypeMixin typeMixin in mod.TypeMixins) {
            TypeDefinition targetType = typeMixin.Target.Resolve();
            foreach (MixoMethodMixin methodMixin in typeMixin.MethodMixins) {

                // Now that we've resolved the type, we can find the target method definition
                MethodDefinition[] targetPotentialMethods = targetType.Methods.Where(method => method.Name == methodMixin.TargetName).ToArray();
                if (targetPotentialMethods.Length == 0)
                    throw new SystemException($"Could not find target method {methodMixin.TargetName} in {targetType}.");

                MethodDefinition? targetMethod = null;
                foreach (MethodDefinition targetPotentialMethod in targetPotentialMethods) {
                    ParameterDefinition[] targetPotentialMethodParams = targetPotentialMethod.Parameters.ToArray();
                    if (targetPotentialMethodParams.Length != methodMixin.TargetParameters.Length)
                        continue;
                    bool match = true;
                    for (int i = 0; i < targetPotentialMethodParams.Length; i++) {
                        if (targetPotentialMethodParams[i].ParameterType.FullName != methodMixin.TargetParameters[i].FullName) {
                            match = false;
                            break;
                        }
                    }
                    if (match) {
                        targetMethod = targetPotentialMethod;
                        break;
                    }
                }

                if (targetMethod == null)
                    throw new SystemException($"Could not find target method {methodMixin.TargetName} with {methodMixin.TargetParameters?.Length ?? 0} params in {targetType}.");

                string methodTargetReturnName = methodMixin.TargetReturn?.FullName ?? typeof(void).FullName!;
                if (targetMethod.ReturnType.FullName != methodTargetReturnName)
                    throw new SystemException($"Target method {methodMixin.TargetName} has return type {targetMethod.ReturnType} (expected {methodTargetReturnName}).");

                Console.WriteLine($"Resolved {methodMixin.Method.DeclaringType}::{methodMixin.Method.Name}");
                Apply(methodMixin.Method, targetMethod);
            }
        }
    }

    private static void Apply(MethodDefinition method, MethodDefinition target) {
        ILProcessor targetMethodProcessor = target.Body.GetILProcessor();

        // Firstly, copy over all the local variables
        VariableDefinition[] newMethodVariables = new VariableDefinition[method.Body.Variables.Count];
        for (int i = 0; i < newMethodVariables.Length; i++) {
            VariableDefinition oldVariable = method.Body.Variables[i];
            VariableDefinition newVariable = new(target.Module.ImportReference(oldVariable.VariableType));
            target.Body.Variables.Add(newVariable);
            newMethodVariables[i] = newVariable;
        }

        // Next copy the instructions
        Instruction firstInstruction = target.Body.Instructions[0];
        Instruction[] methodInstructions = method.Body.Instructions.ToArray();
        for (int i = 0; i < methodInstructions.Length; i++) {
            Instruction inst = methodInstructions[i];

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
            } else if (inst.OpCode == OpCodes.Ret) {
                throw new SystemException($"Only calls to {nameof(MixinReturn)} can be returned! Mixin {method}");
            } else if (inst.Operand is MethodReference operandMethod) {
                inst.Operand = target.Module.ImportReference(operandMethod);
            } else if (inst.Operand is TypeReference operandType) {
                inst.Operand = target.Module.ImportReference(operandType);
            } else if (inst.Operand is FieldReference operandField) {
                inst.Operand = target.Module.ImportReference(operandField);
            }

            if (newMethodVariables.Length != 0) {
                (int localVariable, LocalVariableInstruction localVariableInstruction) = GetLocalVariableInstruction(inst);
                if (localVariableInstruction != LocalVariableInstruction.INVALID) {
                    VariableDefinition newVariableDef = newMethodVariables[localVariable];
                    inst = CreateLocalVariableInstruction(newVariableDef, localVariableInstruction);
                }
            }

            targetMethodProcessor.InsertBefore(firstInstruction, inst);
        }
        Console.WriteLine($"Injected {newMethodVariables.Length} variables, {method.Body.Instructions.Count} instructions into {target}");
    }

    private enum LocalVariableInstruction {
        INVALID,
        LOAD_VAL_TO_STACK,
        LOAD_PTR_TO_STACK,
        SET_VAL_FROM_STACK,
    }

    /// <returns>
    /// 'var' is the local variable index, uint.MaxValue if not a local variable instruction
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
}