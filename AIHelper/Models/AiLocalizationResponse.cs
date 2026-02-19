using System.Text.Json.Serialization;

namespace AIHelper.Models
{
    /// <summary>
    /// Response from AI for issue localization request.
    /// </summary>
    public class AiLocalizationResponse
    {
        /// <summary>
        /// The scanner issue ID being localized.
        /// </summary>
        [JsonPropertyName("issueId")]
        public string? IssueId { get; set; }

        /// <summary>
        /// Status of the localization operation.
        /// </summary>
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        /// <summary>
        /// Confidence score (0.0 to 1.0) of the localization.
        /// </summary>
        [JsonPropertyName("confidence")]
        public double Confidence { get; set; }

        /// <summary>
        /// Anchors identifying the issue location in the code.
        /// </summary>
        [JsonPropertyName("anchors")]
        public IssueAnchors? Anchors { get; set; }

        /// <summary>
        /// Description of the planned edit to fix the issue.
        /// </summary>
        [JsonPropertyName("editPlan")]
        public string? EditPlan { get; set; }

        /// <summary>
        /// Reason if the request was blocked (null if not blocked).
        /// </summary>
        [JsonPropertyName("blockedReason")]
        public string? BlockedReason { get; set; }
    }
}
