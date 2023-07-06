using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Mixolydian.Common;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Mixolydian;

internal class InstanceConstructorMixin : FunctionMixin {

    public static InstanceConstructorMixin Resolve(MethodDefinition source, MixinPriority priority, MixinPosition position, TypeMixin type) {
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

        return new InstanceConstructorMixin(type, source, target, priority, position);
    }

    public readonly MixinPosition Position;
    private Instruction? _injectionPoint;

    private InstanceConstructorMixin(TypeMixin type, MethodDefinition source, MethodDefinition target, MixinPriority priority, MixinPosition position)
        : base(type, source, target, ImmutableDictionary<string, GenericParameter>.Empty, priority) {
        Position = position;
    }

    protected override IEnumerable<Instruction?> EnumerateInjectionPoints() {
        switch (Position) {
            case MixinPosition.HEAD:
                for (int i = 0; i < Target.Body.Instructions.Count; i++) {
                    Instruction inst = Target.Body.Instructions[i];
                    if (inst.OpCode == OpCodes.Call) {
                        if (inst.Operand is MethodReference methodRef && methodRef.Name == ".ctor") {
                            if (Target.Body.Instructions[i - 1].OpCode == OpCodes.Ldarg_0) {
                                _injectionPoint = Target.Body.Instructions[i + 1];
                                break;
                            }
                            throw new InvalidModException("Unexpected context around call to base constructor within mixin constructor. Expected `ldarg.0`.", Type, Source);
                        }
                    }
                }
                if (_injectionPoint == null)
                    throw new InvalidModException("Could not find call to base constructor in target constructor.", Type, Source);
                yield return _injectionPoint;
                yield break;
            case MixinPosition.TAIL:
                _injectionPoint = Target.Body.Instructions.Last();
                if (_injectionPoint.OpCode != OpCodes.Ret)
                    throw new InvalidModException("Last instruction of target is not ret.", Type, Source);
                _injectionPoint.OpCode = OpCodes.Nop;
                yield return null;
                yield break;
            default:
                throw new InvalidModException($"Unknown mixin position {Position}", Type, Source);
        }
    }

    protected override IEnumerable<Instruction> ConvertInstructions(VariableDefinition[] localVariables) {
        foreach (Instruction inst in base.ConvertInstructions(localVariables)) {
            if (inst.OpCode == OpCodes.Ret && Position == MixinPosition.HEAD) {
                inst.OpCode = OpCodes.Br;
                inst.Operand = _injectionPoint;
            }
            yield return inst;
        }
    }

    public override void Inject() {
        base.Inject();
        // The consturctor might return early. If it does, we still want to execute the code
        //  we injected at the end, so replace the ret with a br to the new code.
        if (Position == MixinPosition.TAIL)
            foreach (Instruction inst in Target.Body.Instructions) {
                if (inst == _injectionPoint) break;
                if (inst.OpCode == OpCodes.Ret) {
                    inst.OpCode = OpCodes.Br;
                    inst.Operand = _injectionPoint;
                }
            }
    }
}