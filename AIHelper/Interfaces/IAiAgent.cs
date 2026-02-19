using AIHelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AIHelper.Interfaces
{
    public interface IAiAgent
    {
        Task<AiResponse> FixCodeAsync(string code, List<RemediationTask> issues, string systemPrompt);

        /// <summary>
        /// Localizes an issue in the code and returns anchors identifying the location.
        /// </summary>
        /// <param name="filePath">Path to the file being analyzed.</param>
        /// <param name="code">The source code content.</param>
        /// <param name="issue">The remediation task describing the issue.</param>
        /// <param name="contextLines">Number of context lines to include around the issue.</param>
        /// <param name="systemPrompt">Required system prompt for LOCALIZE mode. Must be provided; cannot be null or empty.</param>
        /// <returns>Localization response with anchors and edit plan.</returns>
        Task<AiLocalizationResponse> LocalizeIssueAsync(string filePath, string code, RemediationTask issue, int contextLines, string systemPrompt);

        /// <summary>
        /// Applies a fix to a localized issue using provided anchors.
        /// </summary>
        /// <param name="filePath">Path to the file being modified.</param>
        /// <param name="code">The source code content.</param>
        /// <param name="issue">The remediation task describing the issue.</param>
        /// <param name="anchors">Anchors identifying the issue location.</param>
        /// <param name="systemPrompt">Required system prompt for APPLY mode. Must be provided; cannot be null or empty.</param>
        /// <returns>Apply response with updated file content.</returns>
        Task<AiApplyResponse> ApplyIssueFixAsync(string filePath, string code, RemediationTask issue, IssueAnchors anchors, string systemPrompt);
    }
}