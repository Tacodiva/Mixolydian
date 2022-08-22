
using System.Collections.Generic;
using Mixolydian.Common;
using Mono.Cecil;

namespace Mixolydian;

public class Mod {

    public readonly AssemblyDefinition Assembly;
    public readonly string FileName;

    private readonly List<TypeMixin> _TypeMixins;
    public IEnumerable<TypeMixin> TypeMixins => _TypeMixins;

    public Mod(AssemblyDefinition assembly, string file) {
        Assembly = assembly;
        FileName = file;

        _TypeMixins = new List<TypeMixin>();

        // Search for types that have the ClassMixin attribute and find that mixins target.
        foreach (ModuleDefinition module in assembly.Modules)
            foreach (TypeDefinition type in module.GetTypes()) {
                if (!type.HasCustomAttributes)
                    continue;
                foreach (CustomAttribute typeAttribute in type.CustomAttributes) {
                    if (typeAttribute.AttributeType.FullName == typeof(TypeMixinAttribute).FullName) {
                        CustomAttributeArgument[] typeAttributeArgs = typeAttribute.ConstructorArguments.ToArray();
                        if (typeAttributeArgs.Length == 0 || typeAttributeArgs[0].Value is not TypeReference typeTargetRef)
                            throw new InvalidModException($"Type {type} is using an invalid constructor for {nameof(TypeMixinAttribute)}.", this);
                        _TypeMixins.Add(new TypeMixin(this, type, typeTargetRef));
                    }
                }
            }
    }

}