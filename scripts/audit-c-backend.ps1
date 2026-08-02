[CmdletBinding()]
param(
    [string]$BackendPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($BackendPath)) {
    $BackendPath = Join-Path $repositoryRoot "src/Cx.Compiler/Backends/C"
}

$rules = @(
    [pscustomobject]@{
        Pattern = "\bCx\.Compiler\.(CompileTime|Parser|Modules|Completion|Testing)\b"
        Reason = "frontend or project-layer namespace"
    },
    [pscustomobject]@{
        Pattern = "\bCx\.Compiler\.Semantic\.(Analyzers|Resolvers)\b"
        Reason = "semantic discovery namespace"
    },
    [pscustomobject]@{
        Pattern = "\bCx\.Compiler\.Lowering\b"
        Reason = "CX lowering namespace"
    },
    [pscustomobject]@{
        Pattern = "\b(FunctionCatalog|SemanticModel|ScopeResolver|TypeResolutionPass|TypeInferencePass|SemanticAnalyzer|RequirementMatcher|GenericConstraintMatcher|CallResolver|ExpressionTypeResolver|TypeSystem|TypeRefParser)\b"
        Reason = "semantic discovery service"
    },
    [pscustomobject]@{
        Pattern = "\b(ResolvedCallInfo|ResolvedExternCallInfo)\b"
        Reason = "pre-Core call model"
    },
    [pscustomobject]@{
        Pattern = "\b(GenericSpecializationPass|GenericFunctionSpecializer|GenericStructSpecializer|GenericUseCollector|GenericCallRetargeter|GenericCallNormalizationPass)\b"
        Reason = "generic specialization machinery"
    }
)

function Find-BoundaryViolation {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Text
    )

    foreach ($rule in $rules) {
        if ($Text -match $rule.Pattern) {
            return $rule
        }
    }

    return $null
}

# Keep the audit rules themselves covered without requiring another test runner.
if ($null -eq (Find-BoundaryViolation "using Cx.Compiler.Semantic.Resolvers;")) {
    throw "C backend audit self-test failed to reject a semantic resolver dependency."
}
if ($null -eq (Find-BoundaryViolation "FunctionCatalog catalog")) {
    throw "C backend audit self-test failed to reject a semantic discovery service."
}
if ($null -eq (Find-BoundaryViolation "ResolvedCallInfo call")) {
    throw "C backend audit self-test failed to reject a pre-Core call model."
}
if ($null -ne (Find-BoundaryViolation "using Cx.Compiler.Semantic;")) {
    throw "C backend audit self-test rejected the Core semantic facts namespace."
}
if ($null -ne (Find-BoundaryViolation "CoreDirectCallInfo call")) {
    throw "C backend audit self-test rejected an allowed Core call fact."
}

$violations = @()
foreach ($file in Get-ChildItem -LiteralPath $BackendPath -Recurse -Filter "*.cs") {
    $lineNumber = 0
    foreach ($line in Get-Content -LiteralPath $file.FullName) {
        $lineNumber++
        $violation = Find-BoundaryViolation $line
        if ($null -eq $violation) {
            continue
        }

        $relativePath = $file.FullName.Substring(
            $repositoryRoot.Length).TrimStart([char[]]@('\', '/'))
        $violations += [pscustomobject]@{
            Path = $relativePath
            Line = $lineNumber
            Text = $line.Trim()
            Reason = $violation.Reason
        }
    }
}

if ($violations.Count -gt 0) {
    Write-Host (
        "The C backend depends on services outside the validated Core CX boundary.") `
        -ForegroundColor Red
    foreach ($violation in $violations) {
        Write-Host (
            "{0}:{1}: {2}: {3}" -f
                $violation.Path,
                $violation.Line,
                $violation.Reason,
                $violation.Text)
    }
    exit 1
}

Write-Host "C backend dependency boundary is clean."
