using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Mixolydian.Common;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Mixolydian;

public class MixoMod {

    public readonly AssemblyDefinition Assembly;

    private readonly List<MixoTypeMixin> _TypeMixins;
    public Collection<MixoTypeMixin> TypeMixins => new(_TypeMixins);

    public MixoMod(string file) : this(AssemblyDefinition.ReadAssembly(file)) {
    }

    public MixoMod(AssemblyDefinition assembly) {
        Assembly = assembly;

        _TypeMixins = new List<MixoTypeMixin>();

        // Search for types that have the ClassMixin attribute and find that mixins target.
        foreach (ModuleDefinition module in assembly.Modules)
            foreach (TypeDefinition type in module.GetTypes()) {
                if (!type.HasCustomAttributes)
                    continue;
                foreach (CustomAttribute typeAttribute in type.CustomAttributes) {
                    if (typeAttribute.AttributeType.FullName == typeof(ClassMixinAttribute).FullName) {
                        CustomAttributeArgument[] typeAttributeArgs = typeAttribute.ConstructorArguments.ToArray();
                        if (typeAttributeArgs.Length == 0 || typeAttributeArgs[0].Value is not TypeReference typeTargetRef)
                            throw new SystemException($"Type {type} is using an invalid constructor for {nameof(ClassMixinAttribute)}.");
                        Console.WriteLine($"Found mixin {type} targeting {typeTargetRef}");
                        _TypeMixins.Add(new MixoTypeMixin(type, typeTargetRef));
                    }
                }
            }
    }
}