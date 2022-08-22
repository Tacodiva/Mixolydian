using Mono.Cecil;

using GenericMap = System.Collections.Generic.IDictionary<string, Mono.Cecil.GenericParameter>;

namespace Mixolydian;

public class MethodAccessor {
    public static MethodAccessor Resolve(MethodDefinition source, string targetName, TypeMixin type) {
        if (source.Body.Instructions.Count != 0) throw new InvalidModException("Method accessor must be marked `extern`.", type, source);

        MethodDefinition? target = null;
        foreach (MethodDefinition potentialTarget in type.Target.Methods) {
            if (potentialTarget.Name != targetName) continue;
            if (potentialTarget.IsStatic != source.IsStatic) continue;
            GenericMap? targetGenericMap = CILUtils.TryCreateGenericMap(source, potentialTarget);
            if (targetGenericMap == null) continue;
            if (!CILUtils.CompareMethodArguments(source, potentialTarget, type, targetGenericMap, source))
                continue;
            if (!CILUtils.CompareTypes(source.ReturnType, potentialTarget.ReturnType, type, targetGenericMap, source))
                continue;
            target = potentialTarget;
            break;
        }
        if (target == null)
            throw new InvalidModException($"Cannot find method accessor target '{targetName}'", type, source);
        return new MethodAccessor(source, target, type);
    }

    public readonly TypeMixin Type;

    public readonly MethodDefinition Source;
    public readonly MethodDefinition Target;

    public MethodAccessor(MethodDefinition source, MethodDefinition target, TypeMixin type) {
        Source = source;
        Target = target;
        Type = type;
    }

    public void CreateDefinition() {
        Type.MethodMap[CILUtils.MethodHash(Source)] = Target;
    }
}