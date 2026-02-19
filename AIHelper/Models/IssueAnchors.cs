using System.Text.Json.Serialization;

namespace AIHelper.Models
{
    /// <summary>
    /// Represents line number anchors for locating code issues.
    /// Used for mapping issues to specific code regions without AST.
    /// </summary>
    public class IssueAnchors
    {
        /// <summary>
        /// Lines before the target issue location (context).
        /// </summary>
        [JsonPropertyName("beforeLines")]
        public string[] BeforeLines { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Target lines where the issue is located.
        /// </summary>
        [JsonPropertyName("targetLines")]
        public string[] TargetLines { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Lines after the target issue location (context).
        /// </summary>
        [JsonPropertyName("afterLines")]
        public string[] AfterLines { get; set; } = Array.Empty<string>();
    }
}
