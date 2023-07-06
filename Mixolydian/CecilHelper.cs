using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Mixolydian;

public interface ITypeConverter {
    public TypeReference Convert(TypeReference inRef);
}

public interface IMethodConverter : ITypeConverter {
    public MethodReference Convert(MethodReference inRef);
}

public abstract class MethodConverter : IMethodConverter {

    public readonly ModuleDefinition Module;

    public MethodConverter(ModuleDefinition module) {
        Module = module;
    }

    public MethodReference Convert(MethodReference inRef) {
    }

    public TypeReference Convert(TypeReference inRef) {

    }

    public abstract GenericParameter Convert(GenericParameter inGeneric);
}

public class CopyMethodConverter : MethodConverter {

    public readonly MethodDefinition Source, Target;

    public CopyMethodConverter(MethodDefinition source, MethodDefinition target) {
        Source = source;
        Target = target;
    }

    public MethodReference Convert(MethodReference inRef) {

    }

    public TypeReference Convert(TypeReference inRef) {
        // if (inRef.IsGen)
    }
}

public static class CecilHelper {

    public static void AppendInstructions(MethodBody source, MethodBody target, IMethodConverter converter) {

    }

    public static TypeReference ConvertTypeReference(TypeReference type, TypeMixin target, GenericMap methodGenericMap, MemberReference? source = null) {
        // Is this a generic parameter, like 'T'?
        if (type.IsGenericParameter && type is GenericParameter genericType) {
            GenericMap genericMap = genericType.Type switch {
                GenericParameterType.Type => target.GenericMap,
                GenericParameterType.Method => methodGenericMap,
                _ => throw new InvalidModException($"Invalid generic parameter type {genericType.Type}", target),
            };
            if (!genericMap.TryGetValue(type.FullName, out GenericParameter? mappedParam))
                throw new InvalidModException($"Couldn't find generic parameter {type}.", target, source);
            return mappedParam;
        }

        // Importing a reference like List<A> will crash if 'A' is a generic parameter of our method.
        //  Solution: Remove all generic parameters, import the reference, then convert the generics
        //  and re-add them.
        List<TypeReference>? genericParameters = null;
        // Is this a generic instance, like List<object>?
        if (type.IsGenericInstance && type is IGenericInstance genericInstType) {
            genericParameters = new List<TypeReference>();
            foreach (TypeReference reference in genericInstType.GenericArguments) {
                genericParameters.Add(reference);
            }
            genericInstType.GenericArguments.Clear();
        }

        // Importing the reference essentially just changes what module it's in.
        TypeReference importedReference = target.Target.Module.ImportReference(type);

        // If we had generic parameters, re-add them!
        if (genericParameters != null) {
            if (importedReference is not IGenericInstance genericInstType2)
                throw new InvalidModException($"Method was generic, but imported reference isn't?", target, source);
            foreach (TypeReference reference in genericParameters)
                genericInstType2.GenericArguments.Add(
                    ConvertTypeReference(reference, target, methodGenericMap, source)
                );
        }

        return importedReference;
    }

}

public class TypeDescriptor {

    public readonly string Namespace;
    public readonly string Name;
    public readonly int GenericCount;
    public readonly TypeDescriptor? DeclaringType;

    public TypeDescriptor(TypeReference type) {
        Namespace = type.Namespace;
        Name = type.Name;
        if (type is IGenericInstance genericInst) GenericCount = genericInst.GenericArguments.Count;
        else GenericCount = type.GenericParameters.Count;
        if (type.DeclaringType != null) DeclaringType = new TypeDescriptor(type.DeclaringType);
    }

    public static bool operator ==(TypeDescriptor? a, TypeDescriptor? b) => a?.Equals(b) ?? b == null;
    public static bool operator !=(TypeDescriptor? a, TypeDescriptor? b) => (!a?.Equals(b)) ?? b != null;

    public override bool Equals(object? obj) {
        if (object.ReferenceEquals(obj, this)) return true;
        if (obj is not TypeDescriptor descriptor) return false;
        return Equals(descriptor);
    }

    public bool Equals(TypeDescriptor? b) {
        if (b is null) return false;
        if (Namespace != b.Namespace) return false;
        if (Name != b.Name) return false;
        if (GenericCount != b.GenericCount) return false;
        if (DeclaringType != b.DeclaringType) return false;
        return true;
    }

    public override int GetHashCode() {
        return HashCode.Combine(Namespace, Name, GenericCount, DeclaringType);
    }
}

public class MethodDescriptor {

    public readonly string Name;
    public readonly int GenericCount;
    public readonly TypeDescriptor DeclaringType;
    public readonly TypeDescriptor[] Arguments;

    public MethodDescriptor(string name, int genericCount, TypeDescriptor declaringType, params TypeDescriptor[] args) {
        Name = name;
        GenericCount = genericCount;
        DeclaringType = declaringType;
        Arguments = args;
    }

    public MethodDescriptor(MethodReference method) {
        Name = method.Name;
        if (method is IGenericInstance genericInst) GenericCount = genericInst.GenericArguments.Count;
        else GenericCount = method.GenericParameters.Count;
        DeclaringType = new TypeDescriptor(method.DeclaringType);
        Arguments = method.Parameters.Select(type => new TypeDescriptor(type.ParameterType)).ToArray();
    }

    public static bool operator ==(MethodDescriptor a, MethodDescriptor b) => a.Equals(b);
    public static bool operator !=(MethodDescriptor a, MethodDescriptor b) => !a.Equals(b);

    public override bool Equals(object? obj) {
        if (object.ReferenceEquals(obj, this)) return true;
        if (obj is not MethodDescriptor descriptor) return false;
        return Equals(descriptor);
    }

    public bool Equals(MethodDescriptor? b) {
        if (b is null) return false;
        if (Name != b.Name) return false;
        if (GenericCount != b.GenericCount) return false;
        if (DeclaringType != b.DeclaringType) return false;
        if (Arguments.Length != b.Arguments.Length) return false;
        for (int i = 0; i < Arguments.Length; i++)
            if (Arguments[i] != b.Arguments[i])
                return false;
        return true;
    }

    public override int GetHashCode() {
        int hash = 0;
        foreach (TypeDescriptor argument in Arguments)
            hash = HashCode.Combine(hash, argument);
        return HashCode.Combine(Name, GenericCount, DeclaringType, hash);
    }
}
