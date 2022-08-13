using Mono.Cecil;

namespace Mixolydian;

/// <summary>
/// A field within a mixin class that represents a field in the target class.
/// All references to this field will be replaced with the target field.
/// </summary>
public class MixoFieldAccessor {

    public readonly FieldDefinition Field;

    public readonly string TargetFieldName;

    public MixoFieldAccessor(FieldDefinition field, string targetName) {
        Field = field;
        TargetFieldName = targetName;
    }
}