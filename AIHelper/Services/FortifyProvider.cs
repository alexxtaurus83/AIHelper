using AIHelper.Interfaces;
using AIHelper.Models;
using RestSharp;
using System.Text.Json;
using System.Threading.Tasks;

namespace AIHelper.Services
{
    public class FortifyProvider : IScanProvider
    {
        private readonly string _apiUrl;
        private readonly ILogger<FortifyProvider> _logger;
        
        public FortifyProvider(string? apiUrl = null, ILogger<FortifyProvider> logger = null)
        {
            _apiUrl = apiUrl ?? "https://api.ams.fortify.com";
            _logger = logger;
        }
        public async Task<List<RemediationTask>> GetIssuesAsync(string id, string severity, string token, string? taskId = null, string? key = null, string? secret = null)
        {
            _logger?.LogInformation("Fetching Fortify issues for release {Id}, severity {Severity}", id, severity);
            var client = new RestClient(_apiUrl);
            
            // First, get the list of vulnerabilities
            var request = new RestRequest($"/api/v3/releases/{id}/vulnerabilities", Method.Get);
            request.AddHeader("Authorization", $"Bearer {token}");
            request.AddHeader("Accept", "application/json");
            _logger?.LogDebug("Making API call to get vulnerabilities: GET {_apiUrl}/api/v3/releases/{id}/vulnerabilities", _apiUrl, id);
            
            var response = await client.ExecuteGetAsync<VulnerabilityListResponse>(request);
            if (!response.IsSuccessful)
            {
                _logger?.LogError("Failed to get vulnerabilities: {ErrorMessage}", response.ErrorMessage);
                throw new Exception($"Failed to get vulnerabilities: {response.ErrorMessage}");
            }
            
            var vulnerabilityList = response.Data;
            _logger?.LogDebug("Retrieved {VulnerabilityCount} vulnerabilities from Fortify", vulnerabilityList?.Data?.Count ?? 0);
            
            var remediationTasks = new List<RemediationTask>();
            
            foreach (var vuln in vulnerabilityList.Data)
            {
                _logger?.LogDebug("Fetching details for vulnerability {VulnerabilityId}", vuln.Id);
                // Get detailed information for each vulnerability
                var detailRequest = new RestRequest($"/api/v3/releases/{id}/vulnerabilities/{vuln.Id}/all-data", Method.Get);
                detailRequest.AddHeader("Authorization", $"Bearer {token}");
                _logger?.LogDebug("Making API call to get vulnerability details: GET {_apiUrl}/api/v3/releases/{id}/vulnerabilities/{vuln.Id}/all-data", _apiUrl, id, vuln.Id);
                
                var detailResponse = await client.ExecuteGetAsync<VulnerabilityDetailResponse>(detailRequest);
                if (!detailResponse.IsSuccessful)
                {
                    _logger?.LogWarning("Could not fetch details for vulnerability {VulnerabilityId}: {ErrorMessage}", vuln.Id, detailResponse.ErrorMessage);
                    continue; // Skip if we can't get details for this vulnerability
                }
                
                var vulnDetail = detailResponse.Data;
                _logger?.LogDebug("Fetched details for vulnerability {VulnerabilityId}", vulnDetail?.Id);
                
                var remediationTask = new RemediationTask
                {
                    ScannerIssueId = vulnDetail.Id.ToString(),
                    TypeCategory = vulnDetail.Category ?? vulnDetail.SubCategory,
                    FilePath = vulnDetail.PrimaryLocationFull,
                    Name = vulnDetail.IssueName,
                    StartLine = vulnDetail.LineStart,
                    EndLine = vulnDetail.LineEnd,
                    Severity = vulnDetail.Severity,
                    Description = $"{vulnDetail.Summary} {vulnDetail.Details}",
                    RemediationAdvice = vulnDetail.Recommendations,
                    EffortEstimation = 0 // Effort estimation not available in Fortify
                };
                
                remediationTasks.Add(remediationTask);
            }
            
            // Filter by severity after fetching all issues
            if (!string.IsNullOrEmpty(severity))
            {
                _logger?.LogDebug("Filtering issues by severity {Severity}", severity);
                remediationTasks = remediationTasks.Where(t => t.Severity.Equals(severity, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            _logger?.LogInformation("Fetched {IssueCount} Fortify issues for release {Id}", remediationTasks.Count, id);
            
            return remediationTasks;
        }

        public async Task UpdateIssueStatusAsync(string issueId, string status, string token)
        {
            _logger?.LogInformation("Updating Fortify issue {IssueId} status to {Status}", issueId, status);
            // Parse the issueId to get releaseId and vulnId (format: {releaseId}:{vulnId})
            var parts = issueId.Split(':');
            if (parts.Length != 2)
            {
                _logger?.LogError("Invalid issueId format {IssueId}. Expected format: {{releaseId}}:{{vulnId}}", issueId);
                throw new ArgumentException("Invalid issueId format. Expected format: {releaseId}:{vulnId}");
            }
            
            var releaseId = parts[0];
            var vulnId = parts[1];
            _logger?.LogDebug("Parsed issueId {IssueId} into releaseId {ReleaseId} and vulnId {VulnId}", issueId, releaseId, vulnId);
            
            var client = new RestClient(_apiUrl);
            var request = new RestRequest($"/api/v3/releases/{releaseId}/vulnerabilities/{vulnId}", Method.Put);
            request.AddHeader("Authorization", $"Bearer {token}");
            _logger?.LogDebug("Making API call to update vulnerability status: PUT {_apiUrl}/api/v3/releases/{releaseId}/vulnerabilities/{vulnId}", _apiUrl, releaseId, vulnId);
            
            // Map the status to Fortify's state ID
            var stateId = MapStatusToStateId(status);
            var payload = new
            {
                state = new
                {
                    type = "list_node",
                    id = stateId
                }
            };
            _logger?.LogDebug("Mapped status {Status} to state ID {StateId}", status, stateId);
            
            request.AddJsonBody(payload);
            
            var response = await client.ExecutePutAsync<VulnerabilityDetailResponse>(request);
            if (!response.IsSuccessful)
            {
                _logger?.LogError("Failed to update vulnerability status for {IssueId}: {ErrorMessage}", issueId, response.ErrorMessage);
                throw new Exception($"Failed to update vulnerability status: {response.ErrorMessage}");
            }
            _logger?.LogInformation("Updated Fortify issue {IssueId} status to {Status} successfully", issueId, status);
        }
        
        private string MapStatusToStateId(string status)
        {
            return status.ToLower() switch
            {
                "analyzed" => "list_node.issue_state_node.reviewed",
                "code updated" => "list_node.issue_state_node.remediated",
                "not an issue" => "list_node.issue_state_node.not_an_issue",
                _ => "list_node.issue_state_node.remediated" // Default to remediated
            };
        }
    }
}