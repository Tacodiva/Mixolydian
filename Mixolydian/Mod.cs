
using System.Collections.Generic;
using Mixolydian.Common;
using Mono.Cecil;

namespace Mixolydian;

public class Mod {

    public readonly AssemblyDefinition Assembly;
    public readonly string FileName;

    private readonly List<TypeMixin> _TypeMixins;
    public IEnumerable<TypeMixin> TypeMixins => _TypeMixins;

    private readonly List<MixinExtension> _MixinExts;
    public IEnumerable<MixinExtension> MixinExtensions => _MixinExts;

    private readonly List<TypeInjection> _TypeInjections;
    public IEnumerable<TypeInjection> TypeInjections => _TypeInjections;


    public Mod(AssemblyDefinition assembly, string file, ModuleDefinition target) {
        Assembly = assembly;
        FileName = file;

        _TypeMixins = new List<TypeMixin>();
        _TypeInjections = new List<TypeInjection>();
        _MixinExts = new List<MixinExtension>();

        // Search for types that have the ClassMixin attribute and find that mixins target.
        foreach (ModuleDefinition module in assembly.Modules) {
            foreach (TypeDefinition type in module.GetTypes()) {
                bool isTypeMixin = false;

                foreach (CustomAttribute typeAttribute in type.CustomAttributes) {
                    if (typeAttribute.AttributeType.FullName == typeof(TypeMixinAttribute).FullName) {
                        CustomAttributeArgument[] typeAttributeArgs = typeAttribute.ConstructorArguments.ToArray();
                        if (typeAttributeArgs.Length == 0 || typeAttributeArgs[0].Value is not TypeReference typeTargetRef)
                            throw new InvalidModException($"Type {type} is using an invalid constructor for {nameof(TypeMixinAttribute)}.", this);
                        _TypeMixins.Add(new TypeMixin(this, type, typeTargetRef));
                        isTypeMixin = true;
                        break;
                    }
                }

                if (isTypeMixin) continue;
                
                foreach (MethodDefinition method in type.Methods) {
                    foreach (CustomAttribute methodAttribute in method.CustomAttributes) {
                        string attributeName = methodAttribute.AttributeType.FullName;
                        if (attributeName == typeof(MixinExtensionAttribute).FullName) {
                            CustomAttributeArgument[] methodAttribArgs = methodAttribute.ConstructorArguments.ToArray();
                            if (methodAttribArgs.Length != 2 || methodAttribArgs[0].Value is not TypeReference mixinTypeRef
                                                             || methodAttribArgs[1].Value is not string targetName)
                                throw new InvalidModException($"Method is using an invalid constructor for {nameof(MixinExtensionAttribute)}.", this);
                            _MixinExts.Add(new MixinExtension(this, method, mixinTypeRef, targetName));
                            break;
                        }
                    }
                }

                _TypeInjections.Add(new TypeInjection(this, target, type));
            }
        }
    }
}