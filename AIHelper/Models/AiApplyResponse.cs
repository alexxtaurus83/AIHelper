using System.Text.Json.Serialization;

namespace AIHelper.Models
{
    /// <summary>
    /// Response from AI for applying a fix to an issue.
    /// </summary>
    public class AiApplyResponse
    {
        /// <summary>
        /// The scanner issue ID being fixed.
        /// </summary>
        [JsonPropertyName("issueId")]
        public string? IssueId { get; set; }

        /// <summary>
        /// Status of the apply operation.
        /// </summary>
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        /// <summary>
        /// The updated file content after applying the fix.
        /// </summary>
        [JsonPropertyName("updatedFile")]
        public string? UpdatedFile { get; set; }

        /// <summary>
        /// Optional patch representation of the changes.
        /// </summary>
        [JsonPropertyName("patch")]
        public string? Patch { get; set; }

        /// <summary>
        /// Reason if the request was blocked (null if not blocked).
        /// </summary>
        [JsonPropertyName("blockedReason")]
        public string? BlockedReason { get; set; }
    }
}
