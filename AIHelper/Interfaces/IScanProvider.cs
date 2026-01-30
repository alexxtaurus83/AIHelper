using AIHelper.Models;

namespace AIHelper.Interfaces
{
    public interface IScanProvider
    {
        Task<List<RemediationTask>> GetIssuesAsync(string id, string severity, string token, string? taskId = null, string? key = null, string? secret = null);
        Task UpdateIssueStatusAsync(string issueId, string status, string token);
    }
}