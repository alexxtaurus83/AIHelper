using AIHelper.Models;

namespace AIHelper.Interfaces
{
    public interface IScanProvider
    {
        Task<List<RemediationTask>> GetIssuesAsync(
            string projectKeyOrReleaseId,
            string? severities,
            string? impactSeverities,
            string? impactSoftwareQualities,
            string token,           
            string? taskId = null,
            string? key = null,
            string? secret = null);
        Task UpdateIssueStatusAsync(string issueId, string status, string token);
    }
}