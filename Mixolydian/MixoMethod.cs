
using Mono.Cecil;

namespace Mixolydian;

/// <summary>
/// A method within a mixin class that isn't targeting another method.
/// These methods need to be injected into the target class.
/// </summary>
public class MixoMethod {

    public readonly MethodDefinition Method;

    public MixoMethod(MethodDefinition method) {
        Method = method;
    }

}