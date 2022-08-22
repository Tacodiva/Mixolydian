using System.Collections.Generic;
using System.Collections.Immutable;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Mixolydian;

internal class ConstructorMixin : FunctionMixin {

    public static ConstructorMixin Resolve(MethodDefinition source, TypeMixin type) {
        if (source.HasGenericParameters)
            throw new InvalidModException("Constructor mixins cannot have generic parameters.", type, source);

        if (source.ReturnType.FullName != typeof(void).FullName)
            throw new InvalidModException("Constructor mixins must return void.", type, source);

        MethodDefinition? target = null;

        foreach (MethodDefinition potentialTarget in type.Target.Methods) {
            if (!potentialTarget.IsConstructor) continue;
            if (!CILUtils.CompareMethodArguments(source, potentialTarget, type, null, source))
                continue;
            target = potentialTarget;
            break;
        }

        if (target == null)
            throw new InvalidModException($"Couldn't find matching constructor in target for constructor mixin.", type, source);

        return new ConstructorMixin(type, source, target);
    }

    private ConstructorMixin(TypeMixin type, MethodDefinition source, MethodDefinition target)
        : base(type, source, target, ImmutableDictionary<string, GenericParameter>.Empty) { }

    protected override Instruction? FindInjectionPoint() {
        Instruction? injectionPoint = null;
        for (int i = 0; i < Target.Body.Instructions.Count; i++) {
            Instruction inst = Target.Body.Instructions[i];
            if (inst.OpCode == OpCodes.Call) {
                if (inst.Operand is MethodReference methodRef && methodRef.Name == ".ctor") {
                    if (Target.Body.Instructions[i - 1].OpCode == OpCodes.Ldarg_0) {
                        injectionPoint = Target.Body.Instructions[i + 1];
                        break;
                    }
                    throw new InvalidModException("Unexpected context around call to base constructor within mixin constructor. Expected `ldarg.0`.", Type, Source);
                }
            }
        }
        if (injectionPoint == null)
            throw new InvalidModException("Could not find call to base constructor in target constructor.", Type, Source);
        return injectionPoint;
    }

    protected override IEnumerable<Instruction> ConvertInstructions(VariableDefinition[] localVariables, Instruction? injectionPoint) {
        foreach (Instruction inst in base.ConvertInstructions(localVariables, injectionPoint)) {
            if (inst.OpCode == OpCodes.Ret && injectionPoint != null) {
                inst.OpCode = OpCodes.Br;
                inst.Operand = injectionPoint;
            }
            yield return inst;
        }
    }
}