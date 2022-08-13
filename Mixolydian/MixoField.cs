using Mono.Cecil;

namespace Mixolydian;

/// <summary>
/// A field within a mixin class that isn't being redirected to another field.
/// These fields need to be injected into the target class.
/// </summary>
public class MixoField {

    public readonly FieldDefinition Field;

    public MixoField(FieldDefinition field) {
        Field = field;
    }
}