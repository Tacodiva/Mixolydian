
using System.Collections.Generic;
using System.Linq;
using Mixolydian.Common;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Mixolydian;

public abstract class FunctionMixin {

    public readonly TypeMixin Type;

    public readonly MethodDefinition Source;
    public readonly MethodDefinition Target;

    public readonly MixinPriority Priority;

    public readonly GenericMap GenericMap;

    protected FunctionMixin(TypeMixin type, MethodDefinition source, MethodDefinition target, GenericMap genericMap, MixinPriority priority) {
        Type = type;
        Source = source;
        Target = target;
        GenericMap = genericMap;
        Priority = priority;
    }

    public virtual void Inject() {
        VariableDefinition[] localVariables = CILUtils.CopyLocalVariables(Source, Target, Type, GenericMap);
        List<Instruction?> injectionPoints = EnumerateInjectionPoints().ToList();
        List<Instruction> convertedInstructions = ConvertInstructions(localVariables).ToList();
        ILProcessor targetInstructions = Target.Body.GetILProcessor();
        foreach (Instruction? injectionPoint in injectionPoints) {
            foreach (Instruction inst in convertedInstructions)
                AppendInstruction(targetInstructions, inst, injectionPoint);
        }
    }

    protected abstract IEnumerable<Instruction?> EnumerateInjectionPoints();

    protected virtual void AppendInstruction(ILProcessor target, Instruction inst, Instruction? injectionPoint) {
        if (injectionPoint == null) target.Append(inst);
        else target.InsertBefore(injectionPoint, inst);
    }

    protected virtual IEnumerable<Instruction> ConvertInstructions(VariableDefinition[] localVariables) {
        foreach (Instruction inst in Source.Body.Instructions) {
            CILUtils.ConvertInstruction(inst, Type, GenericMap, localVariables, Source);
            yield return inst;
        }
    }
}
