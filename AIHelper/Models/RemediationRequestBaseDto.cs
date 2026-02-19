using System.Text.Json.Serialization;

/// <summary>
/// Base data transfer object for remediation requests containing common fields.
/// </summary>
public abstract class RemediationRequestBaseDto
{
    /// <summary>
    /// Gets or sets the project key or release ID.
    /// </summary>
    public string? ProjectKeyOrReleaseId { get; set; }

    /// <summary>
    /// Gets or sets the repository ID in GitLab.
    /// </summary>
    public string? RepoId { get; set; }

    /// <summary>
    /// Gets or sets the source branch name.
    /// </summary>
    public string? SourceBranch { get; set; }

    /// <summary>
    /// Gets or sets the target branch name for the merge request.
    /// </summary>
    public string? TargetBranch { get; set; }

    /// <summary>
    /// Gets or sets the GitLab access token.
    /// </summary>
    public string? GitlabToken { get; set; }

    /// <summary>
    /// Gets or sets the scanner access token.
    /// </summary>
    public string? ScannerToken { get; set; }

    /// <summary>
    /// Gets or sets the system prompt for LOCALIZE mode.
    /// </summary>
    public string? SystemPromptLocalize { get; set; }

    /// <summary>
    /// Gets or sets the system prompt for APPLY mode.
    /// </summary>
    public string? SystemPromptApply { get; set; }
}
