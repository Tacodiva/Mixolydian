
using Mono.Cecil;

namespace Mixolydian;

public class MixinExtension {

    public readonly Mod Mod;
    
    public readonly MethodDefinition Method;
    public readonly TypeReference MixinTypeReference;
    public readonly string TargetName;

    public MixinExtension(Mod mod, MethodDefinition method, TypeReference mixin, string targetName) {
        Mod = mod;
        Method = method;
        MixinTypeReference = mixin;
        TargetName = targetName;
    }

    public void Apply() {
        
    }
}