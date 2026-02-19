/// <summary>
/// Data transfer object for Sonar remediation requests.
/// </summary>
public class SonarRemediationRequestDto : RemediationRequestBaseDto
{
    /// <summary>
    /// Gets or sets the SonarQube task ID.
    /// </summary>
    public string? TaskId { get; set; }

    /// <summary>
    /// Gets or sets a comma-separated list of software qualities to filter by. Possible values: MAINTAINABILITY, RELIABILITY, SECURITY.
    /// </summary>
    public string? ImpactSoftwareQualities { get; set; }

    /// <summary>
    /// Gets or sets a comma-separated list of software quality severities to filter by. Possible values: INFO, LOW, MEDIUM, HIGH, BLOCKER.
    /// </summary>
    public string? ImpactSeverities { get; set; }
}
