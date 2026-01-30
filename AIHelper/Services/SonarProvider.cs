using AIHelper.Interfaces;
using AIHelper.Models;
using Html2Markdown;
using RestSharp;
using System.Text.Json;

namespace AIHelper.Services
{
    public class SonarProvider : IScanProvider
    {
        private readonly string _apiUrl;
        private readonly ILogger<SonarProvider> _logger;
        
        public SonarProvider(string? apiUrl = null, ILogger<SonarProvider> logger = null)
        {
            _apiUrl = apiUrl ?? "http://localhost:9000";
            _logger = logger;
        }

        public async Task<List<RemediationTask>> GetIssuesAsync(string id, string severity, string token, string? key = null, string? secret = null)
        {
            _logger?.LogInformation("Fetching Sonar issues for project {Id}, severity {Severity}", id, severity);
            var client = new RestClient(_apiUrl);

            // First API call to get issues
            var searchRequest = new RestRequest("/api/issues/search", Method.Get);
            searchRequest.AddParameter("componentKeys", id);
            if (!string.IsNullOrEmpty(severity))
            {
                searchRequest.AddParameter("severities", severity);
            }
            searchRequest.AddParameter("ps", "500"); // Set page size to get more results
            searchRequest.AddHeader("Authorization", $"Bearer {token}");
            _logger?.LogDebug("Making API call to get issues: GET {_apiUrl}/api/issues/search?componentKeys={Id}&severities={Severity}&ps=500", _apiUrl, id, severity);

            var searchResponse = await client.ExecuteGetAsync<SonarIssuesResponse>(searchRequest);
            if (!searchResponse.IsSuccessful)
            {
                _logger?.LogError("Failed to retrieve issues: {ErrorMessage}", searchResponse.ErrorMessage);
                throw new Exception($"Failed to retrieve issues: {searchResponse.ErrorMessage}");
            }

            var searchResult = searchResponse.Data;
            if (searchResult == null)
            {
                _logger?.LogError("Sonar search response data is null");
                throw new Exception("Sonar search response data is null");
            }
            var issues = searchResult.Issues ?? new List<Issue>();
            _logger?.LogDebug("Retrieved {IssueCount} issues from Sonar", issues.Count);

            var remediationTasks = new List<RemediationTask>();

            // Collect unique rule keys to minimize API calls
            var uniqueRuleKeys = issues.Select(i => i.Rule).Distinct().ToList();
            _logger?.LogDebug("Found {UniqueRuleCount} unique rule keys to fetch details for", uniqueRuleKeys.Count);
            
            // Fetch rule details for each unique rule key
            var ruleDetailsMap = new Dictionary<string, RuleDetails?>();
            foreach (var ruleKey in uniqueRuleKeys)
            {
                _logger?.LogDebug("Fetching rule details for rule {RuleKey}", ruleKey);
                var ruleRequest = new RestRequest("/api/rules/show", Method.Get);
                ruleRequest.AddParameter("key", ruleKey);
                ruleRequest.AddHeader("Authorization", $"Bearer {token}");
                _logger?.LogDebug("Making API call to get rule details: GET {_apiUrl}/api/rules/show?key={RuleKey}", _apiUrl, ruleKey);

                var ruleResponse = await client.ExecuteGetAsync<SonarRuleResponse>(ruleRequest);
                if (!ruleResponse.IsSuccessful)
                {
                    // Log warning but continue processing other rules
                    _logger?.LogWarning("Failed to retrieve rule details for rule {RuleKey}: {ErrorMessage}", ruleKey, ruleResponse.ErrorMessage);
                    ruleDetailsMap[ruleKey] = null;
                }
                else if (ruleResponse.Data?.Rule != null)
                {
                    ruleDetailsMap[ruleKey] = ruleResponse.Data.Rule;
                }
                else
                {
                    ruleDetailsMap[ruleKey] = null;
                }
            }

            // Map issues to RemediationTask using the fetched rule details
            foreach (var issue in issues)
            {
                var ruleDetails = ruleDetailsMap.ContainsKey(issue.Rule) ? ruleDetailsMap[issue.Rule] : null;
                
                // Map to RemediationTask
                var remediationTask = new RemediationTask
                {
                    ScannerIssueId = issue.Key,
                    TypeCategory = issue.Type,
                    FilePath = issue.Component.Split(':').Last(), // Extract file path from component
                    Name = issue.Message,
                    StartLine = issue.TextRange?.StartLine ?? issue.Line ?? 0,
                    EndLine = issue.TextRange?.EndLine ?? issue.Line ?? 0,
                    Severity = issue.Severity,
                    Description = issue.Message,
                    RemediationAdvice = this.ConvertHtmlToMarkdown(ruleDetails?.DescriptionSections?.FirstOrDefault()?.Content) ?? "No remediation advice available",
                    EffortEstimation = ParseEffort(issue.Effort)
                };

                remediationTasks.Add(remediationTask);
            }
            _logger?.LogInformation("Fetched {IssueCount} Sonar issues for project {Id}", remediationTasks.Count, id);

            return remediationTasks;
        }

        public async Task UpdateIssueStatusAsync(string issueId, string status, string token)
        {
            _logger?.LogInformation("Updating Sonar issue {IssueId} status to {Status}", issueId, status);
            var client = new RestClient(_apiUrl);
            var request = new RestRequest("/api/issues/set_status", Method.Post);
            request.AddParameter("issue", issueId);
            request.AddParameter("status", status);
            request.AddHeader("Authorization", $"Bearer {token}");
            _logger?.LogDebug("Making API call to update issue status: POST {_apiUrl}/api/issues/set_status", _apiUrl);

            var response = await client.ExecutePostAsync(request);
            if (!response.IsSuccessful)
            {
                _logger?.LogError("Failed to update issue status for {IssueId}: {ErrorMessage}", issueId, response.ErrorMessage);
                throw new Exception($"Failed to update issue status: {response.ErrorMessage}");
            }
            _logger?.LogInformation("Updated Sonar issue {IssueId} status to {Status} successfully", issueId, status);
        }

        private static int ParseEffort(string? effort)
        {
            if (string.IsNullOrEmpty(effort))
                return 0;

            // Parse effort like "2min" to minutes
            var effortText = effort.ToLower();
            if (effortText.EndsWith("min"))
            {
                var minutesStr = effortText.Substring(0, effortText.Length - 3);
                if (int.TryParse(minutesStr, out var minutes))
                    return minutes;
            }
            else if (effortText.EndsWith("h"))
            {
                var hoursStr = effortText.Substring(0, effortText.Length - 1);
                if (int.TryParse(hoursStr, out var hours))
                    return hours * 60; // Convert hours to minutes
            }

            return 0;
        }

        private string? ConvertHtmlToMarkdown(string? htmlContent)
        {
            if (string.IsNullOrEmpty(htmlContent))
                return htmlContent;

            try
            {
                var converter = new Converter();
                return converter.Convert(htmlContent);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to convert HTML to Markdown: {HtmlContent}", htmlContent);
                return htmlContent; // Return original content if conversion fails
            }
        }
    }
}