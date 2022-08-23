
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using GenericMap = System.Collections.Generic.IDictionary<string, Mono.Cecil.GenericParameter>;

namespace Mixolydian;

public abstract class FunctionMixin {

    public readonly TypeMixin Type;

    public readonly MethodDefinition Source;
    public readonly MethodDefinition Target;

    public readonly int Priority;

    public readonly GenericMap GenericMap;

    protected FunctionMixin(TypeMixin type, MethodDefinition source, MethodDefinition target, GenericMap genericMap, int priority) {
        Type = type;
        Source = source;
        Target = target;
        GenericMap = genericMap;
        Priority = priority;
    }

    public virtual void Inject() {
        Instruction? injectionPoint = FindInjectionPoint();
        ILProcessor targetInstructions = Target.Body.GetILProcessor();

        VariableDefinition[] localVariables = CILUtils.CopyLocalVariables(Source, Target, Type, GenericMap);
        
        foreach (Instruction inst in ConvertInstructions(localVariables, injectionPoint))
            AppendInstruction(targetInstructions, inst, injectionPoint);
    }

    protected virtual Instruction? FindInjectionPoint() {
        if (!Target.HasBody || Target.Body.Instructions.Count == 0)
            return null;
        return Target.Body.Instructions[0];
    }

    protected virtual void AppendInstruction(ILProcessor target, Instruction inst, Instruction? injectionPoint) {
        if (injectionPoint == null) target.Append(inst);
        else target.InsertBefore(injectionPoint, inst);
    }

    protected virtual IEnumerable<Instruction> ConvertInstructions(VariableDefinition[] localVariables, Instruction? injectionPoint) {
        foreach (Instruction inst in Source.Body.Instructions) {
            CILUtils.ConvertInstruction(inst, Type, GenericMap, localVariables, Source);
            yield return inst;
        }
    }
}
