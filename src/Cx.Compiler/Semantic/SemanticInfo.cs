using Cx.Compiler.Syntax;

namespace Cx.Compiler.Semantic;

internal sealed class SemanticInfo
{
    public TypeRef? Type { get; set; }

    public Symbol? Symbol { get; set; }

    public SyntaxNode? Origin { get; set; }

    public string? ModuleName { get; set; }

    public ResolvedCallInfo? ResolvedCall { get; set; }

    public ResolvedExternCallInfo? ResolvedExternCall { get; set; }

    public CoreExternCallInfo? CoreExternCall { get; set; }

    public CoreDirectCallInfo? CoreDirectCall { get; set; }

    public CoreIndirectCallInfo? CoreIndirectCall { get; set; }

    public CoreConstructorCallInfo? ConstructorCall { get; set; }

    public CoreMemberReferenceInfo? MemberReference { get; set; }

    public CoreMemberAccessInfo? CoreMemberAccess { get; set; }

    public CoreSymbolInfo? CoreSymbol { get; set; }

    public CoreTypeIdInfo? CoreTypeId { get; set; }

    public CoreFunctionReferenceInfo? CoreFunctionReference { get; set; }

    public CoreValueConversionInfo? ValueConversion { get; set; }

    public IReadOnlyList<CoreInterfaceImplementationInfo>
        CoreInterfaceImplementations { get; set; } = [];

    public CoreInterfaceCallInfo? CoreInterfaceCall { get; set; }

    public GenericFunctionSpecializationInfo? GenericFunctionSpecialization { get; set; }

    public GenericStructSpecializationInfo? GenericStructSpecialization { get; set; }

    public CoreFunctionInfo? CoreFunction { get; set; }

    public OperatorDerivationKind? OperatorDerivation { get; set; }

    public bool IsScopeCleanup { get; set; }

    public bool IsCoreCxValidated { get; set; }

    public SemanticInfo Clone() =>
        new()
        {
            Type = Type,
            Symbol = Symbol,
            Origin = Origin,
            ModuleName = ModuleName,
            ResolvedCall = ResolvedCall,
            ResolvedExternCall = ResolvedExternCall,
            CoreExternCall = CoreExternCall,
            CoreDirectCall = CoreDirectCall,
            CoreIndirectCall = CoreIndirectCall,
            ConstructorCall = ConstructorCall,
            MemberReference = MemberReference,
            CoreMemberAccess = CoreMemberAccess,
            CoreSymbol = CoreSymbol,
            CoreTypeId = CoreTypeId,
            CoreFunctionReference = CoreFunctionReference,
            ValueConversion = ValueConversion,
            CoreInterfaceImplementations = CoreInterfaceImplementations,
            CoreInterfaceCall = CoreInterfaceCall,
            GenericFunctionSpecialization = GenericFunctionSpecialization,
            GenericStructSpecialization = GenericStructSpecialization,
            CoreFunction = CoreFunction,
            OperatorDerivation = OperatorDerivation,
            IsScopeCleanup = IsScopeCleanup,
            IsCoreCxValidated = IsCoreCxValidated,
        };
}
