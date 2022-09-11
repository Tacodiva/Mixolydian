using System.Collections.Generic;
using System.Linq;
using Mixolydian.Common;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Mixolydian;

internal class MethodHeadMixin : FunctionMixin {

    public static MethodHeadMixin Resolve(MethodDefinition source, string targetName, MixinPriority priority, TypeMixin type) {

        string[] targetNames;
        if (CILUtils.OperatorFunctionNames.TryGetValue(targetName, out string[]? operatorFuncName))
            targetNames = operatorFuncName;
        else targetNames = new string[] { targetName };

        // Find the expected return type by extracting it from MixinReturn
        TypeReference? expectedReturn; // If null, expected return is `void`
        if (source.ReturnType.IsGenericInstance && source.ReturnType is IGenericInstance methodReturnGeneric) {
            if (source.ReturnType.Name != typeof(MixinReturn<object>).Name || methodReturnGeneric.GenericArguments.Count != 1)
                throw new InvalidModException($"Mixins must return `MixinReturn`!", type, source);
            expectedReturn = methodReturnGeneric.GenericArguments[0];
        } else {
            if (source.ReturnType.FullName != typeof(MixinReturn).FullName)
                throw new InvalidModException($"Mixins must return `MixinReturn`!", type, source);
            expectedReturn = null;
        }

        // Find a matching target method definition
        MethodDefinition? target = null;
        GenericMap? methodGenericMap = null;
        foreach (MethodDefinition possibleTarget in type.Target.Methods) {
            if (!targetNames.Contains(possibleTarget.Name)) continue;
            if (possibleTarget.IsStatic != source.IsStatic) continue;
            GenericMap? possibleMethodGenericMap = CILUtils.TryCreateGenericMap(source, possibleTarget, type);
            if (possibleMethodGenericMap == null) continue;
            if (CILUtils.CompareMethodArguments(source, possibleTarget, type, possibleMethodGenericMap, source)) {
                if (expectedReturn != null) {
                    if (!CILUtils.CompareTypes(expectedReturn, possibleTarget.ReturnType, type, possibleMethodGenericMap, source))
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
            throw new InvalidModException($"Could not find mixin target '{targetName}' with the same parameters.", type, source);

        return new MethodHeadMixin(methodGenericMap, type, source, target, priority);
    }

    private Instruction? _injectionPoint;

    private MethodHeadMixin(GenericMap genericMap, TypeMixin type, MethodDefinition source, MethodDefinition target, MixinPriority priority)
        : base(type, source, target, genericMap, priority) {
    }

    protected override IEnumerable<Instruction?> EnumerateInjectionPoints() {
        yield return _injectionPoint = Target.Body.Instructions[0];
    }

    protected override IEnumerable<Instruction> ConvertInstructions(VariableDefinition[] localVariables) {
        IEnumerator<Instruction> convertedInstructions = base.ConvertInstructions(localVariables).GetEnumerator();

        while (convertedInstructions.MoveNext()) {
            Instruction inst = convertedInstructions.Current;
            CILUtils.GetArgumentInstructionInfo(inst);

            if (inst.OpCode == OpCodes.Call && inst.Operand is MethodReference callOperand
                && callOperand.ReturnType.FullName.StartsWith(typeof(MixinReturn).FullName!)
                && callOperand.DeclaringType.Namespace == "Mixolydian.Common") {
                // Found a call to MixinReturn!

                inst.OpCode = OpCodes.Nop;
                inst.Operand = null;
                yield return inst;

                // The next instruction should be a return
                if (!convertedInstructions.MoveNext() || (inst = convertedInstructions.Current).OpCode != OpCodes.Ret)
                    throw new InvalidModException($"Calls to {callOperand.Name} must be instantly returned.", Type, Source);

                switch (callOperand.Name) {
                    case nameof(MixinReturn.Continue):
                        // Jump to the end of the mixin
                        if (_injectionPoint != null) {
                            inst.OpCode = OpCodes.Br;
                            inst.Operand = _injectionPoint;
                        }
                        break;
                    case nameof(MixinReturn.Return):
                        break;
                    default:
                        throw new InvalidModException($"Unknown {nameof(MixinReturn)} method {callOperand.Name}.", Type, Source);
                }

                yield return inst;
                continue;
            }

            if (inst.OpCode == OpCodes.Ret)
                throw new InvalidModException($"Only calls to {nameof(MixinReturn)} can be returned!", Type, Source);

            yield return inst;
        }
    }
}