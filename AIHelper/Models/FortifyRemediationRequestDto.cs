/// <summary>
/// Data transfer object for Fortify remediation requests.
/// </summary>
public class FortifyRemediationRequestDto : RemediationRequestBaseDto
{
    /// <summary>
    /// Gets or sets a comma-separated list of issue severities to filter by. Possible values: Critical, High, Medium, Low.
    /// </summary>
    public string? Severities { get; set; }
}
