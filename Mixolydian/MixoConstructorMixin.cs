using System;
using Mono.Cecil;

namespace Mixolydian;

public class MixoConstructorMixin {

    public readonly MethodDefinition Method;

    public MixoConstructorMixin(MethodDefinition method) {
        Method = method;
    }
}