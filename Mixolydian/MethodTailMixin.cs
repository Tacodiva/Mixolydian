using System.Collections.Generic;
using System.Linq;
using Mixolydian.Common;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Mixolydian;

public class MethodTailMixin : FunctionMixin {

    public static MethodTailMixin Resolve(MethodDefinition source, string targetName, MixinPriority priority, TypeMixin type) {
        TypeReference? expectedReturn = source.ReturnType;
        int returnArgIdx = -1;

        if (expectedReturn.FullName != typeof(void).FullName) {
            if (source.Parameters.Count == 0 || source.Parameters.Last().ParameterType.FullName != expectedReturn.FullName)
                throw new InvalidModException("The last parameter of method tail mixins must be the same as the methods return type.", type, source);
            // Remove the last parameter so we can compare method arguments later
            //  Any referenes to the last parameter will be replaced
            // returnArgIdx = source.Parameters.Count - (source.HasThis ? 0 : 1);
            returnArgIdx = source.Parameters.Count - (source.HasThis ? 0 : 1);
            System.Console.WriteLine($"{source} -> {returnArgIdx}");
            source.Parameters.RemoveAt(source.Parameters.Count - 1);
        }

        // Find a matching target method definition
        MethodDefinition? target = null;
        GenericMap? methodGenericMap = null;
        foreach (MethodDefinition possibleTarget in type.Target.Methods) {
            if (possibleTarget.Name != targetName) continue;
            if (possibleTarget.IsStatic != source.IsStatic) continue;
            GenericMap? possibleMethodGenericMap = CILUtils.TryCreateGenericMap(source, possibleTarget);
            if (possibleMethodGenericMap == null) continue;
            if (CILUtils.CompareMethodArguments(source, possibleTarget, type, possibleMethodGenericMap, source)) {
                if (!CILUtils.CompareTypes(expectedReturn, possibleTarget.ReturnType, type, possibleMethodGenericMap, source))
                    continue;
                methodGenericMap = possibleMethodGenericMap;
                target = possibleTarget;
                break;
            }
        }

        if (target == null || methodGenericMap == null)
            throw new InvalidModException($"Could not find mixin target '{targetName}' with the same parameters.", type, source);

        return new MethodTailMixin(returnArgIdx, type, source, target, methodGenericMap, priority);
    }

    public readonly int ReturnArgIndex;
    public VariableDefinition? ReturnVar { get; private set; }

    public bool HasReturnArg => ReturnArgIndex != -1;

    private MethodTailMixin(int returnArgIdx, TypeMixin type, MethodDefinition source, MethodDefinition target, GenericMap genericMap, MixinPriority priority)
        : base(type, source, target, genericMap, priority) {
        ReturnArgIndex = returnArgIdx;
    }

    protected override IEnumerable<Instruction?> EnumerateInjectionPoints() {
        if (HasReturnArg) {
            ReturnVar = new(Target.ReturnType);
            Target.Body.Variables.Add(ReturnVar);
        }

        for (int i = 0; i < Target.Body.Instructions.Count; i++) {
            Instruction inst = Target.Body.Instructions[i];

            if (inst.OpCode != OpCodes.Ret)
                continue;

            if (HasReturnArg) {
                if (ReturnVar == null)
                    throw new System.SystemException("Haven't created a local variable to hold the return value!");
                Instruction newInst = CILUtils.CreateLocalVariableInstruction(ReturnVar, CILUtils.StackInstruction.SET_VAL_FROM_STACK);
                inst.OpCode = newInst.OpCode;
                inst.Operand = newInst.Operand;
                System.Console.WriteLine(inst);
                System.Console.WriteLine(ReturnArgIndex);
            } else {
                inst.OpCode = OpCodes.Nop;
            }

            if (i == Target.Body.Instructions.Count - 1)
                yield return null;
            else
                yield return Target.Body.Instructions[i + 1];
        }
    }

    protected override IEnumerable<Instruction> ConvertInstructions(VariableDefinition[] localVariables) {
        if (HasReturnArg && ReturnVar != null) {
            VariableDefinition[] newLocalVariables = new VariableDefinition[localVariables.Length + 1];
            for (int i = 0; i < localVariables.Length; i++) newLocalVariables[i] = localVariables[i];
            newLocalVariables[localVariables.Length] = ReturnVar;
            localVariables = newLocalVariables;
        }

        IEnumerator<Instruction> convertedInstructions = base.ConvertInstructions(localVariables).GetEnumerator();

        while (convertedInstructions.MoveNext()) {
            Instruction inst = convertedInstructions.Current;
            (int argIdx, CILUtils.StackInstruction argInst) = CILUtils.GetArgumentInstructionInfo(inst);

            if (argInst != CILUtils.StackInstruction.INVALID && argIdx == ReturnArgIndex) {
                if (ReturnVar == null)
                    throw new System.SystemException("Haven't created a local variable to hold the return value!");
                Instruction newInst = CILUtils.CreateLocalVariableInstruction(ReturnVar, argInst);
                inst.OpCode = newInst.OpCode;
                inst.Operand = newInst.Operand;
            }

            yield return inst;
        }
    }
}