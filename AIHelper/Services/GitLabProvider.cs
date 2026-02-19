using AIHelper.Interfaces;
using AIHelper.Models;
using RestSharp;
using System.Text;
using System.Text.Json;

namespace AIHelper.Services
{
    public class GitLabProvider : IGitProvider
    {
        private readonly string _apiUrl;
        private readonly ILogger<GitLabProvider> _logger;

        public GitLabProvider(string? apiUrl = null, ILogger<GitLabProvider> logger = null)
        {
            _apiUrl = apiUrl ?? "https://gitlab.com/api/v4";
            _logger = logger;
        }

        public async Task<string> GetFileContentAsync(string repoId, string filePath, string branch, string token)
        {
            _logger?.LogInformation("Getting file content for {FilePath} from branch {Branch} in repo {RepoId}", filePath, branch, repoId);
            // URL-encode the filePath
            string encodedFilePath = Uri.EscapeDataString(filePath);
            _logger?.LogDebug("Encoded file path: {EncodedFilePath}", encodedFilePath);
            
            var client = new RestClient(_apiUrl);
            var request = new RestRequest($"/projects/{repoId}/repository/files/{encodedFilePath}", Method.Get);
            request.AddParameter("ref", branch);
            request.AddHeader("PRIVATE-TOKEN", token);
            _logger?.LogDebug("Making API call to get file content: GET {_apiUrl}/projects/{RepoId}/repository/files/{EncodedFilePath}?ref={Branch}", _apiUrl, repoId, encodedFilePath, branch);
            
            var response = await client.ExecuteGetAsync<GitLabFileContentResponse>(request);
            if (!response.IsSuccessful)
            {
                _logger?.LogError("Failed to get file content for {FilePath} from branch {Branch} in repo {RepoId}: {ErrorMessage}", filePath, branch, repoId, response.ErrorMessage);
                throw new Exception($"Failed to get file content: {response.ErrorMessage}");
            }
            
            var fileResponse = response.Data;
            _logger?.LogDebug("Received file content response for {FilePath}", filePath);
            
            // Check if response data is null
            if (fileResponse?.content == null)
            {
                _logger?.LogError("File content is null in the response for {FilePath}", filePath);
                throw new Exception("File content is null in the response");
            }
            
            // Decode the Base64 content
            byte[] data = Convert.FromBase64String(fileResponse.content);
            var content = Encoding.UTF8.GetString(data);
            _logger?.LogInformation("Successfully retrieved content for {FilePath} from branch {Branch} in repo {RepoId}", filePath, branch, repoId);
            return content;
        }

        public async Task CreateBranchAsync(string repoId, string source, string newBranch, string token)
        {
            _logger?.LogInformation("Creating branch {NewBranch} from {Source} in repo {RepoId}", newBranch, source, repoId);
            var client = new RestClient(_apiUrl);
            var request = new RestRequest($"/projects/{repoId}/repository/branches", Method.Post);
            request.AddParameter("branch", newBranch);
            request.AddParameter("ref", source);
            request.AddHeader("PRIVATE-TOKEN", token);
            _logger?.LogDebug("Making API call to create branch: POST {_apiUrl}/projects/{RepoId}/repository/branches", _apiUrl, repoId);
            
            var response = await client.ExecutePostAsync(request);
            if (!response.IsSuccessful)
            {
                _logger?.LogError("Failed to create branch {NewBranch} from {Source} in repo {RepoId}: {ErrorMessage}", newBranch, source, repoId, response.ErrorMessage);
                throw new Exception($"Failed to create branch: {response.ErrorMessage}");
            }
            _logger?.LogInformation("Successfully created branch {NewBranch} from {Source} in repo {RepoId}", newBranch, source, repoId);
        }

        public async Task CommitFileAsync(string repoId, string branch, string filePath, string content, string message, string token)
        {
            _logger?.LogInformation("Committing file {FilePath} to branch {Branch} in repo {RepoId}", filePath, branch, repoId);
            var client = new RestClient(_apiUrl);
            var request = new RestRequest($"/projects/{repoId}/repository/commits", Method.Post);
            request.AddParameter("branch", branch); //branch
            request.AddParameter("commit_message", message);
            request.AddParameter("actions[][action]", "update");
            request.AddParameter("actions[][file_path]", filePath);
            request.AddParameter("actions[][content]", content);
            request.AddParameter("author_name", "AI-Remediator");
            request.AddParameter("author_email", "ai-remediator@example.com");
            request.AddHeader("PRIVATE-TOKEN", token);
            _logger?.LogDebug("Making API call to commit file: POST {_apiUrl}/projects/{RepoId}/repository/commits", _apiUrl, repoId);
            
            var response = await client.ExecutePostAsync(request);
            if (!response.IsSuccessful)
            {
                _logger?.LogError("Failed to commit file {FilePath} to branch {Branch} in repo {RepoId}: {ErrorMessage}", filePath, branch, repoId, response.ErrorMessage);
                throw new Exception($"Failed to commit file: {response.ErrorMessage}");
            }
            _logger?.LogInformation("Successfully committed file {FilePath} to branch {Branch} in repo {RepoId}", filePath, branch, repoId);
        }

        public async Task CreateMergeRequestAsync(string repoId, string source, string target, string title, string token)
        {
            _logger?.LogInformation("Creating merge request from {Source} to {Target} in repo {RepoId}", source, target, repoId);
            var client = new RestClient(_apiUrl);
            var request = new RestRequest($"/projects/{repoId}/merge_requests", Method.Post);
            request.AddParameter("source_branch", source); //source
            request.AddParameter("target_branch", target); ///target
            request.AddParameter("title", title);
            request.AddHeader("PRIVATE-TOKEN", token);
            _logger?.LogDebug("Making API call to create merge request: POST {_apiUrl}/projects/{RepoId}/merge_requests", _apiUrl, repoId);
            
            var response = await client.ExecutePostAsync(request);
            if (!response.IsSuccessful)
            {
                _logger?.LogError("Failed to create merge request from {Source} to {Target} in repo {RepoId}: {ErrorMessage}", source, target, repoId, response.ErrorMessage);
                throw new Exception($"Failed to create merge request: {response.ErrorMessage}");
            }
            _logger?.LogInformation("Successfully created merge request from {Source} to {Target} in repo {RepoId}", source, target, repoId);
        }
    }
}