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
    /// Gets or sets the minimum severity level for issues to be processed. Possible values: INFO, MINOR, MAJOR, CRITICAL, BLOCKER.
    /// </summary>
    public string? MinSeverity { get; set; } // e.g., "High"
    
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
    /// <summary>
    /// Gets or sets the name of the new branch to create for fixes.
    /// </summary>
    public string? NewBranchName { get; set; } // Branch to create for fixes
    
    // Credentials (Passed per request)
    /// <summary>
    /// Gets or sets the GitLab access token.
    /// </summary>
    public string? GitlabToken { get; set; }
    /// <summary>
    /// Gets or sets the scanner access token.
    /// </summary>
    public string? ScannerToken { get; set; }
}