using System;
using System.Collections.Immutable;
using System.Linq;
using Mono.Cecil;

namespace Mixolydian;

public class FieldInject {
    public static FieldInject Resolve(FieldDefinition definition, TypeMixin type) {
        return new FieldInject(definition, type);
    }

    public readonly TypeMixin Type;

    public readonly FieldDefinition Source;
    public FieldDefinition? Target { get; private set; }

    private FieldInject(FieldDefinition source, TypeMixin type) {
        Type = type;
        Source = source;
    }

    public void CreateDefinition() {
        if (Target != null)
            throw new InvalidOperationException("Field definition has already been created.");

        // Find a field name that's avaliable
        string fieldName = Source.Name;
        if (Type.Target.Fields.Any(f => f.Name == fieldName)) {
            int nameIdx = 0;
            while (Type.Target.Fields.Any(f => f.Name == fieldName + "_" + nameIdx))
                nameIdx++;
            fieldName = fieldName + "_" + nameIdx;
        }
                
        Target = new(fieldName, Source.Attributes,
            CILUtils.ConvertTypeReference(Source.FieldType, Type, ImmutableDictionary<string, GenericParameter>.Empty, Source)
        );

        Type.Target.Fields.Add(Target);
        Type.FieldMap[Source.Name] = Target;
    }
}