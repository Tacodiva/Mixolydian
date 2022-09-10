using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Mixolydian;

public class MethodInject {

    public static MethodInject Resolve(TypeMixin type, MethodDefinition source) {
        return new MethodInject(source, type);
    }

    public readonly TypeMixin Type;

    public readonly MethodDefinition Source;

    public MethodDefinition? Target { get; private set; }
    public GenericMap? GenericMap { get; private set; }

    public MethodInject(MethodDefinition source, TypeMixin type) {
        Source = source;
        Type = type;
    }

    public void CreateDefinition() {
        if (Target != null || GenericMap != null)
            throw new InvalidOperationException("Definition has already been created.");

        string newMethodName;
        if (Type.Target.Methods.Any(targetMethod => targetMethod.Name == Source.Name)) {
            int nameIdx = 0;
            while (Type.Target.Methods.Any(targetMethod => targetMethod.Name == Source.Name + "_" + nameIdx))
                ++nameIdx;
            newMethodName = Source.Name + "_" + nameIdx;
        } else newMethodName = Source.Name;

        // The void return type is only temporary, we will repalce it after we have the generic parameters
        Target = new(newMethodName, Source.Attributes, Type.Target.Module.ImportReference(typeof(void)));

        // The generic map must be created before the method return type as the return type of the method 
        //  may contain generics we need to map. IE `public A Get<A>()`
        GenericMap = new Dictionary<string, GenericParameter>();
        foreach (GenericParameter generic in Source.GenericParameters) { // Copy generics
            GenericParameter newGeneric = new(generic.Name, Target);
            Target.GenericParameters.Add(newGeneric);
            GenericMap[generic.FullName] = newGeneric;
        }

        Target.ReturnType = CILUtils.ConvertTypeReference(Source.ReturnType, Type, GenericMap, Source);

        // TODO Copy attributes

        foreach (ParameterDefinition parameter in Source.Parameters) {// Copy parameters
            Target.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes, CILUtils.ConvertTypeReference(parameter.ParameterType, Type, GenericMap, Source)));
        }

        Type.Target.Methods.Add(Target);
        Type.MethodMap[CILUtils.MethodHash(Source)] = Target;
    }

    public void Inject() {
        if (Target == null || GenericMap == null)
            throw new InvalidOperationException("Definition has not been created.");

        foreach (VariableDefinition localVar in Source.Body.Variables) { // Copy local variables
            Target.Body.Variables.Add(new VariableDefinition(CILUtils.ConvertTypeReference(localVar.VariableType, Type, GenericMap, Source)));
        }
        foreach (Instruction inst in Source.Body.Instructions) { // Copy instructions
            CILUtils.ConvertInstruction(inst, Type, GenericMap, null, Source);
            Target.Body.Instructions.Add(inst);
        }
    }
}