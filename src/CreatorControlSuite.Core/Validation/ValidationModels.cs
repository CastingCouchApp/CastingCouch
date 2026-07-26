namespace CreatorControlSuite.Core.Validation;

public enum ValidationSeverity
{
    Information,
    Warning,
    Error
}

public sealed record ValidationIssue(
    string Code,
    ValidationSeverity Severity,
    string Section,
    string Message,
    string SuggestedFix);

public sealed record ValidationReport(
    bool IsValid,
    IReadOnlyList<ValidationIssue> Issues);
