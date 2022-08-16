using Mono.Cecil;

namespace Mixolydian;

/// <summary>
/// A method within a mixin class that should be redirected to a method in the target.
/// All call to this method need to be replaced with the target method 
/// </summary>
public class MixoMethodAccessor {

    public readonly MethodDefinition Method;

    public readonly string TargetMethodName;

    public MixoMethodAccessor(MethodDefinition method, string targetName) {
        Method = method;
        TargetMethodName = targetName;
    }
}