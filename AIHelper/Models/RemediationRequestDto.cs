using System.Text.Json.Serialization;

/// <summary>
/// Represents the type of scan to perform.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ScanType
{
    /// <summary>
    /// SonarQube scan type.
    /// </summary>
    SONAR,
    /// <summary>
    /// Fortify scan type.
    /// </summary>
    FORTIFY
}

/// <summary>
/// Data transfer object for the remediation request.
/// </summary>
public class RemediationRequestDto
{
    /// <summary>
    /// Gets or sets the type of scan to perform (SONAR or FORTIFY).
    /// </summary>
    public ScanType ScanType { get; set; } // "SONAR" or "FORTIFY"
    /// <summary>
    /// Gets or sets the project key or release ID.
    /// </summary>
    public string? ProjectKeyOrReleaseId { get; set; }
    /// <summary>
    /// Gets or sets a comma-separated list of issue severities to filter by. Possible values: INFO, MINOR, MAJOR, CRITICAL, BLOCKER.
    /// </summary>
    public string? Severities { get; set; }
    /// <summary>
    /// Gets or sets a comma-separated list of software quality severities to filter by. Possible values: INFO, LOW, MEDIUM, HIGH, BLOCKER.
    /// </summary>
    public string? ImpactSeverities { get; set; }
    /// <summary>
    /// Gets or sets a comma-separated list of software qualities to filter by. Possible values: MAINTAINABILITY, RELIABILITY, SECURITY.
    /// </summary>
    public string? ImpactSoftwareQualities { get; set; }

    
    // Git Details
    /// <summary>
    /// Gets or sets the repository ID in GitLab.
    /// </summary>
    public string? RepoId { get; set; } // Project ID in GitLab
    /// <summary>
    /// Gets or sets the source branch name.
    /// </summary>
    public string? SourceBranch { get; set; }
    /// <summary>
    /// Gets or sets the target branch name for the merge request.
    /// </summary>
    public string? TargetBranch { get; set; } // Branch to create MR into
    // Credentials (Passed per request)
    /// <summary>
    /// Gets or sets the GitLab access token.
    /// </summary>
    public string? GitlabToken { get; set; }
    /// <summary>
    /// Gets or sets the scanner access token.
    /// </summary>
    public string? ScannerToken { get; set; }
    
    /// <summary>
    /// Gets or sets the SonarQube task ID.
    /// </summary>
    public string? TaskId { get; set; }

    /// <summary>
    /// Gets or sets the system prompt for LOCALIZE mode.
    /// </summary>
    public string? SystemPromptLocalize { get; set; }

    /// <summary>
    /// Gets or sets the system prompt for APPLY mode.
    /// </summary>
    public string? SystemPromptApply { get; set; }
   
    
}