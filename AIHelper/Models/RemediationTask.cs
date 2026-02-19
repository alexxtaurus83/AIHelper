public class RemediationTask
{
    public string? ScannerIssueId { get; set; }
    public string? TypeCategory { get; set; } // Issue type (sonar) \ category (fortify)
    public string? FilePath { get; set; } // path + name
    public string? Name { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string? Severity { get; set; }
    public string? Description { get; set; }
    public string? RemediationAdvice { get; set; }
    public int EffortEstimation { get; set; } // only for sonar

    public override string? ToString() {
        return $"ScannerIssueId: '{ScannerIssueId}' | Name: '{Name}' | StartLine: '{StartLine}' | EndLine: {EndLine}";
    }
}