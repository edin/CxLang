internal sealed record ResolvedBuildPlanResult(bool Success, ResolvedBuildPlan Value, string Error)
{
    public static ResolvedBuildPlanResult Succeeded(ResolvedBuildPlan value) =>
        new(true, value, string.Empty);

    public static ResolvedBuildPlanResult Failed(string error) =>
        new(false, null!, error);
}
