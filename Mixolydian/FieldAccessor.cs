using System.Collections.Immutable;
using System.Linq;
using Mono.Cecil;

namespace Mixolydian;

public class FieldAccessor {

    public static FieldAccessor Resolve(FieldDefinition source, string targetName, TypeMixin type) {
        FieldDefinition? target = type.Target.Fields.FirstOrDefault(field => field.Name == targetName);
        if (target == null)
            throw new InvalidModException($"Could not find target field.", type, source);

        if (!target.IsStatic && source.IsStatic)
            throw new InvalidModException($"Accessor is static, but target field is not.", type, source);
        if (target.IsStatic && !source.IsStatic)
            throw new InvalidModException($"Accessor is not static, but target field is.", type, source);

        TypeReference mappedFieldType = CILUtils.ConvertTypeReference(source.FieldType, type, ImmutableDictionary<string, GenericParameter>.Empty, source);
        if (mappedFieldType.FullName != target.FieldType.FullName)
            throw new InvalidModException($"Field has an invalid type {source.FieldType}, expected {target.FieldType}", type, source);

        return new FieldAccessor(source, target, type);
    }

    public static FieldAccessor ResolveThis(FieldDefinition source, TypeMixin type) {
        if (source.IsStatic)
            throw new InvalidModException($"'MixinThis' fields cannot be static!", type, source);

        if (!source.IsInitOnly)
            throw new InvalidModException($"'MixinThis' fields must be readonly!", type, source);

        if (type.Target.HasGenericParameters) {
            if (source.FieldType is not IGenericInstance fieldType)
                throw new InvalidModException($"'MixinThis' fields must have the same generic parameters as their target type!", type, source);

            int genericCount = fieldType.GenericArguments.Count;
            if (type.Target.GenericParameters.Count != genericCount)
                throw new InvalidModException($"'MixinThis' fields must have the same generic parameters as their target type!", type, source);

            for (int i = 0; i < genericCount; i++) {
                if (!type.GenericMap.TryGetValue(fieldType.GenericArguments[i].FullName, out GenericParameter? mappedGeneric))
                    throw new InvalidModException($"'MixinThis' fields must have the same generic parameters as their target type!", type, source);
                if (mappedGeneric.FullName != type.Target.GenericParameters[i].FullName)
                    throw new InvalidModException($"'MixinThis' fields must have the same generic parameters as their target type!", type, source);
            }
        } else {
            if (source.FieldType.FullName != type.Target.FullName)
                throw new InvalidModException($"Field has an invalid type {source.FieldType}, expected {type.Target}", type, source);
        }
        return new FieldAccessor(source, null, type);
    }

    public readonly TypeMixin Type;

    public readonly FieldDefinition Source;
    public readonly FieldDefinition? Target;
    public bool IsThis => Target == null;

    private FieldAccessor(FieldDefinition source, FieldDefinition? target, TypeMixin type) {
        Type = type;
        Source = source;
        Target = target;
    }

    public void CreateDefinition() {
        Type.FieldMap[Source.Name] = Target;
    }

}