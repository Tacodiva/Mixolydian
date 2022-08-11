using System;
using Mono.Cecil;

namespace Mixolydian;

public class MixoMethodMixin {

    public readonly MethodDefinition Method;

    public readonly string TargetName;
    public readonly TypeReference[] TargetParameters;
    public readonly TypeReference? TargetReturn;

    public MixoMethodMixin(MethodDefinition method, string targetName, TypeReference[] targetParameters, TypeReference? targetReturn) {
        Method = method;
        TargetName = targetName;
        TargetParameters = targetParameters;
        TargetReturn = targetReturn;
        Console.WriteLine($"Method mixin {method.DeclaringType.FullName}.{method.Name} found targeting {targetName}.");
    }
}