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
        private readonly int _pollingTimeoutSeconds;
        private readonly int _pollingIntervalSeconds;
        private readonly ILogger<SonarProvider> _logger;
        
        public SonarProvider(string? apiUrl = null, int pollingTimeoutSeconds = 300, int pollingIntervalSeconds = 10, ILogger<SonarProvider> logger = null)
        {
            _apiUrl = apiUrl ?? "http://localhost:9000";
            _pollingTimeoutSeconds = pollingTimeoutSeconds;
            _pollingIntervalSeconds = pollingIntervalSeconds;
            _logger = logger;
        }

        public async Task<List<RemediationTask>> GetIssuesAsync(
            string projectKeyOrReleaseId,
            string? severities,
            string? impactSeverities,
            string? impactSoftwareQualities,
            string token,            
            string? taskId = null,
            string? key = null,
            string? secret = null)
        {
            if (!string.IsNullOrEmpty(taskId))
            {
                await PollTaskStatusAsync(taskId, token);
            }
                       

            _logger?.LogInformation(
                "Fetching Sonar issues for project {Id}, severities {Severities}, impactSeverities {ImpactSeverities}, impactSoftwareQualities {ImpactSoftwareQualities}",
                projectKeyOrReleaseId,
                severities,
                impactSeverities,
                impactSoftwareQualities);

            var client = new RestClient(_apiUrl);

            // First API call to get issues
            var searchRequest = new RestRequest("/api/issues/search", Method.Get);
            searchRequest.AddParameter("componentKeys", projectKeyOrReleaseId);
            if (!string.IsNullOrWhiteSpace(severities))
            {
                searchRequest.AddParameter("severities", severities);
            }
            if (!string.IsNullOrWhiteSpace(impactSeverities))
            {
                searchRequest.AddParameter("impactSeverities", impactSeverities);
            }
            if (!string.IsNullOrWhiteSpace(impactSoftwareQualities))
            {
                searchRequest.AddParameter("impactSoftwareQualities", impactSoftwareQualities);
            }
            searchRequest.AddParameter("ps", "500"); // Set page size to get more results
            searchRequest.AddHeader("Authorization", $"Bearer {token}");
            _logger?.LogDebug(
                "Making API call to get issues: GET {_apiUrl}/api/issues/search?componentKeys={Id}&severities={Severities}&impactSeverities={ImpactSeverities}&impactSoftwareQualities={ImpactSoftwareQualities}&ps=500",
                _apiUrl,
                projectKeyOrReleaseId,
                severities,
                impactSeverities,
                impactSoftwareQualities);

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
                //_logger?.LogDebug("Making API call to get rule details: GET {_apiUrl}/api/rules/show?key={RuleKey}", _apiUrl, ruleKey);

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
            _logger?.LogInformation("Fetched {IssueCount} Sonar issues for project {Id}", remediationTasks.Count, projectKeyOrReleaseId);

            return remediationTasks;
        }

        private async Task<string> CheckTaskStatusAsync(string taskId, string token)
        {
            _logger?.LogInformation("Checking SonarQube task status for {TaskId}", taskId);
            var client = new RestClient(_apiUrl);
            var request = new RestRequest("/api/ce/task", Method.Get);
            request.AddParameter("id", taskId);
            request.AddHeader("Authorization", $"Bearer {token}");

            var response = await client.ExecuteGetAsync<SonarTaskResponse>(request);

            if (!response.IsSuccessful || response.Data?.Task == null)
            {
                _logger?.LogError("Failed to retrieve SonarQube task status for {TaskId}: {ErrorMessage}", taskId, response.ErrorMessage);
                throw new Exception($"Failed to retrieve SonarQube task status: {response.ErrorMessage}");
            }

            _logger?.LogInformation("SonarQube task {TaskId} status is {Status}", taskId, response.Data.Task.Status);
            return response.Data.Task.Status;
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

        private async Task PollTaskStatusAsync(string taskId, string token)
        {
            var timeout = TimeSpan.FromSeconds(_pollingTimeoutSeconds);
            var interval = TimeSpan.FromSeconds(_pollingIntervalSeconds);
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            while (stopwatch.Elapsed < timeout)
            {
                var status = await CheckTaskStatusAsync(taskId, token);
                switch (status)
                {
                    case "SUCCESS":
                        _logger?.LogInformation("SonarQube task {TaskId} completed successfully.", taskId);
                        return;
                    case "FAILED":
                    case "CANCELED":
                        throw new Exception($"SonarQube task {taskId} failed with status: {status}");
                    case "PENDING":
                    case "IN_PROGRESS":
                        _logger?.LogInformation("SonarQube task {TaskId} is still in progress with status: {status}. Waiting for {Interval} seconds.", taskId, status, interval.TotalSeconds);
                        await Task.Delay(interval);
                        break;
                    default:
                        throw new Exception($"Unknown SonarQube task status: {status}");
                }
            }

            throw new TimeoutException($"Timed out waiting for SonarQube task {taskId} to complete.");
        }
    }
}