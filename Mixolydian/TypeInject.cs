using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Mixolydian;

public class TypeInjection {

    public readonly Mod Mod;

    public readonly TypeDefinition Type;
    public readonly ModuleDefinition Target;

    public TypeInjection(Mod mod, ModuleDefinition target, TypeDefinition type) {
        Type = type;
        Mod = mod;
        Target = target;
    }

    public void Inject() {

        TypeDefinition outType = new(Type.Namespace, Type.Name, Type.Attributes);
        Target.Types.Add(outType);

        CopyGenericParameters(Type, outType);
        CopyCustomAttributes(Type, outType, outType);

        if (Type.BaseType != null) outType.BaseType = Target.ImportReference(Type.BaseType, outType);

        foreach (InterfaceImplementation inInterface in Type.Interfaces) {
            InterfaceImplementation outInterface = new(Target.ImportReference(inInterface.InterfaceType, outType));
            CopyCustomAttributes(inInterface, outInterface, outType);
            outType.Interfaces.Add(outInterface);
        }

        // TODO Nested types

        foreach (FieldDefinition field in Type.Fields)
            outType.Fields.Add(CopyFeild(field, outType));

        Dictionary<MethodReference, MethodDefinition> inOutMethodMap = new();
        foreach (MethodDefinition method in Type.Methods) {
            inOutMethodMap[method] = CopyMethod(method, outType);
        }

        foreach (PropertyDefinition prop in Type.Properties)
            outType.Properties.Add(CopyProperty(prop, inOutMethodMap, outType));

    }

    private PropertyDefinition CopyProperty(PropertyDefinition inProp, Dictionary<MethodReference, MethodDefinition> inOutMethodMap, TypeDefinition outType) {
        PropertyDefinition outProp = new(inProp.Name, inProp.Attributes, Target.ImportReference(inProp.PropertyType, outType));

        if (inProp.SetMethod != null) outProp.SetMethod = inOutMethodMap[inProp.SetMethod];
        if (inProp.GetMethod != null) outProp.GetMethod = inOutMethodMap[inProp.GetMethod];

        foreach (MethodDefinition inMethod in inProp.OtherMethods)
            outProp.OtherMethods.Add(inOutMethodMap[inMethod]);

        return outProp;
    }

    private MethodDefinition CopyMethod(MethodDefinition inMethod, TypeDefinition outType) {
        MethodDefinition outMethod = new(inMethod.Name, inMethod.Attributes, Target.TypeSystem.Void) {
            ImplAttributes = inMethod.ImplAttributes
        };
        outType.Methods.Add(outMethod);

        CopyGenericParameters(inMethod, outMethod);
        CopyCustomAttributes(inMethod, outMethod, outMethod);

        foreach (ParameterDefinition inParam in inMethod.Parameters) {
            outMethod.Parameters.Add(CopyParameter(inParam, outMethod));
        }

        foreach (MethodReference inOverride in inMethod.Overrides) {
            outMethod.Overrides.Add(Target.ImportReference(inOverride, outMethod));
        }

        outMethod.ReturnType = Target.ImportReference(inMethod.ReturnType, outMethod);
        outMethod.MethodReturnType.Attributes = inMethod.MethodReturnType.Attributes;
        if (inMethod.MethodReturnType.HasConstant) outMethod.MethodReturnType.Constant = inMethod.MethodReturnType.Constant;
        if (inMethod.MethodReturnType.HasMarshalInfo) outMethod.MethodReturnType.MarshalInfo = inMethod.MethodReturnType.MarshalInfo;
        CopyCustomAttributes(inMethod.MethodReturnType, outMethod.MethodReturnType, outMethod);

        outMethod.IsAddOn = inMethod.IsAddOn;
        outMethod.IsRemoveOn = inMethod.IsRemoveOn;
        outMethod.IsGetter = inMethod.IsGetter;
        outMethod.IsSetter = inMethod.IsSetter;
        outMethod.CallingConvention = inMethod.CallingConvention;

        MethodBody inBody = inMethod.Body;
        MethodBody outBody = new(outMethod) {
            MaxStackSize = inBody.MaxStackSize,
            InitLocals = inBody.InitLocals,
            LocalVarToken = inBody.LocalVarToken
        };

        foreach (VariableDefinition inVar in inBody.Variables) {
            outBody.Variables.Add(new VariableDefinition(Target.ImportReference(inVar.VariableType, outMethod)));
        }

        foreach (Instruction inInstr in inBody.Instructions) {
            Instruction outInstr;

            if (inInstr.OpCode == OpCodes.Calli) {
                CallSite inCallSite = (CallSite)inInstr.Operand;
                CallSite outCallSite = new(Target.ImportReference(inCallSite.ReturnType, outMethod)) {
                    HasThis = inCallSite.HasThis,
                    ExplicitThis = inCallSite.ExplicitThis,
                    CallingConvention = inCallSite.CallingConvention
                };
                foreach (ParameterDefinition inParam in inCallSite.Parameters) {
                    outCallSite.Parameters.Add(CopyParameter(inParam, outMethod));
                }
                outInstr = Instruction.Create(OpCodes.Calli, outCallSite);
            } else {

                switch (inInstr.OpCode.OperandType) {
                    case OperandType.InlineArg:
                    case OperandType.ShortInlineArg:
                        if (inInstr.Operand == inBody.ThisParameter) {
                            outInstr = Instruction.Create(inInstr.OpCode, outBody.ThisParameter);
                        } else {
                            int param = inBody.Method.Parameters.IndexOf((ParameterDefinition)inInstr.Operand);
                            outInstr = Instruction.Create(inInstr.OpCode, outMethod.Parameters[param]);
                        }
                        break;
                    case OperandType.InlineVar:
                    case OperandType.ShortInlineVar:
                        int var = inBody.Variables.IndexOf((VariableDefinition)inInstr.Operand);
                        outInstr = Instruction.Create(inInstr.OpCode, outBody.Variables[var]);
                        break;
                    case OperandType.InlineField:
                        outInstr = Instruction.Create(inInstr.OpCode, Target.ImportReference((FieldReference)inInstr.Operand, outMethod));
                        break;
                    case OperandType.InlineMethod:
                        outInstr = Instruction.Create(inInstr.OpCode, Target.ImportReference((MethodReference)inInstr.Operand, outMethod));
                        break;
                    case OperandType.InlineType:
                        outInstr = Instruction.Create(inInstr.OpCode, Target.ImportReference((TypeReference)inInstr.Operand, outMethod));
                        break;
                    case OperandType.InlineTok:
                        if (inInstr.Operand is TypeReference reference)
                            outInstr = Instruction.Create(inInstr.OpCode, Target.ImportReference(reference, outMethod));
                        else if (inInstr.Operand is FieldReference reference1)
                            outInstr = Instruction.Create(inInstr.OpCode, Target.ImportReference(reference1, outMethod));
                        else if (inInstr.Operand is MethodReference reference2)
                            outInstr = Instruction.Create(inInstr.OpCode, Target.ImportReference(reference2, outMethod));
                        else
                            throw new InvalidOperationException();
                        break;
                    case OperandType.InlineNone:
                        outInstr = Instruction.Create(inInstr.OpCode);
                        break;
                    default:
                        outInstr = Instruction.Create(inInstr.OpCode, (dynamic)inInstr.Operand);
                        break;
                }
            }
            outBody.Instructions.Add(outInstr);
        }

        Instruction ConvertInstruction(Instruction inInstr) {
            return outBody.Instructions[inBody.Instructions.IndexOf(inInstr)];
        }

        for (int i = 0; i < outBody.Instructions.Count; i++) {
            Instruction outInstr = outBody.Instructions[i];
            switch (outInstr.Operand) {
                case Instruction outInstrParam:
                    outInstr.Operand = ConvertInstruction(outInstrParam);
                    break;
                case Instruction[] outInstrArrParam:
                    for (int j = 0; j < outInstrArrParam.Length; j++)
                        outInstrArrParam[j] = ConvertInstruction(outInstrArrParam[j]);
                    break;
            }
        }

        foreach (ExceptionHandler inExceptHandler in inBody.ExceptionHandlers) {
            ExceptionHandler outExceptHandler = new(inExceptHandler.HandlerType) {
                TryStart = ConvertInstruction(inExceptHandler.TryStart),
                TryEnd = ConvertInstruction(inExceptHandler.TryEnd),
                HandlerStart = ConvertInstruction(inExceptHandler.HandlerStart),
                HandlerEnd = ConvertInstruction(inExceptHandler.HandlerEnd),
            };

            switch (inExceptHandler.HandlerType) {
                case ExceptionHandlerType.Catch:
                    outExceptHandler.CatchType = Target.ImportReference(inExceptHandler.CatchType, outMethod);
                    break;
                case ExceptionHandlerType.Filter:
                    outExceptHandler.FilterStart = ConvertInstruction(inExceptHandler.FilterStart);
                    break;
            }

            outBody.ExceptionHandlers.Add(outExceptHandler);
        }

        outMethod.Body = outBody;

        return outMethod;
    }

    private ParameterDefinition CopyParameter(ParameterDefinition inParam, IGenericParameterProvider context) {
        ParameterDefinition outParam = new(inParam.Name, inParam.Attributes, Target.ImportReference(inParam.ParameterType, context));
        if (inParam.HasConstant) outParam.Constant = inParam.Constant;
        if (inParam.HasMarshalInfo) outParam.MarshalInfo = inParam.MarshalInfo;
        CopyCustomAttributes(inParam, outParam, context);
        return outParam;
    }

    private FieldDefinition CopyFeild(FieldDefinition inField, TypeDefinition context) {
        FieldDefinition outField = new(inField.Name, inField.Attributes, Target.ImportReference(inField.FieldType, context));

        if (outField.HasConstant)
            outField.Constant = inField.Constant;

        if (inField.HasMarshalInfo)
            outField.MarshalInfo = inField.MarshalInfo;

        if (inField.InitialValue != null && inField.InitialValue.Length > 0)
            outField.InitialValue = inField.InitialValue;

        if (inField.HasLayoutInfo)
            outField.Offset = inField.Offset;

        CopyCustomAttributes(inField, outField, context);
        return outField;
    }

    private void CopyCustomAttributes(ICustomAttributeProvider source, ICustomAttributeProvider target, IGenericParameterProvider? context) {
        object CopyArgumentValue(object val) {
            return val switch {
                TypeReference valType => Target.ImportReference(valType, context),
                CustomAttributeArgument valArg => CopyArgument(valArg),
                CustomAttributeArgument[] valArgArr => Array.ConvertAll(valArgArr, inValArg => CopyArgument(inValArg)),
                _ => val,
            };
        }

        CustomAttributeArgument CopyArgument(CustomAttributeArgument inArg) {
            return new CustomAttributeArgument(
                Target.ImportReference(inArg.Type, context),
                CopyArgumentValue(inArg.Value)
            );
        }

        CustomAttributeNamedArgument CopyNamedArgument(CustomAttributeNamedArgument inArg) {
            return new CustomAttributeNamedArgument(inArg.Name, CopyArgument(inArg.Argument));
        }

        foreach (CustomAttribute inAttrib in source.CustomAttributes) {
            CustomAttribute outAtrib = new(Target.ImportReference(inAttrib.Constructor, context));

            foreach (CustomAttributeArgument inArg in inAttrib.ConstructorArguments)
                outAtrib.ConstructorArguments.Add(CopyArgument(inArg));

            foreach (CustomAttributeNamedArgument inArg in inAttrib.Fields)
                outAtrib.Fields.Add(CopyNamedArgument(inArg));

            foreach (CustomAttributeNamedArgument inArg in inAttrib.Properties)
                outAtrib.Properties.Add(CopyNamedArgument(inArg));

            target.CustomAttributes.Add(outAtrib);
        }
    }

    private void CopyGenericParameters(IGenericParameterProvider source, IGenericParameterProvider target) {
        int offset = target.GenericParameters.Count;

        foreach (GenericParameter inParam in source.GenericParameters) {
            GenericParameter outParam = new(inParam.Name, target) {
                Attributes = inParam.Attributes
            };
            target.GenericParameters.Add(outParam);
        }

        for (int i = 0; i < source.GenericParameters.Count; i++) {
            GenericParameter inParam = source.GenericParameters[i];
            GenericParameter outParam = target.GenericParameters[i + offset];

            CopyCustomAttributes(inParam, outParam, target);

            foreach (GenericParameterConstraint inConstraint in inParam.Constraints) {
                GenericParameterConstraint outConstraint = new(Target.ImportReference(inConstraint.ConstraintType, target));
                CopyCustomAttributes(inConstraint, outConstraint, target);
                outParam.Constraints.Add(outConstraint);
            }
        }
    }
}