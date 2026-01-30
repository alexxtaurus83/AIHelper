using Microsoft.Extensions.Logging;
using AIHelper.Interfaces;
using AIHelper.Models;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace AIHelper.Services
{
    public class RemediationService : IRemediationService
    {
        private readonly ILogger<RemediationService> _logger;
        private readonly ScanProviderFactory _scanProviderFactory;
        private readonly IGitProvider _gitProvider;
        private readonly IAiAgent _aiAgent;

        public RemediationService(
            ILogger<RemediationService> logger,
            ScanProviderFactory scanProviderFactory,
            IGitProvider gitProvider,
            IAiAgent aiAgent)
        {
            _logger = logger;
            _scanProviderFactory = scanProviderFactory;
            _gitProvider = gitProvider;
            _aiAgent = aiAgent;
        }

        public async Task<object> RemediateProjectAsync(RemediationRequestDto request)
        {
            var stopwatch = Stopwatch.StartNew();
            
            _logger.LogInformation("Starting remediation for {ProjectKeyOrReleaseId}...", request.ProjectKeyOrReleaseId);
            _logger.LogDebug("Remediation request details: {@Request}", request);
            
            // Use the ScanProviderFactory to get the correct IScanProvider
            _logger.LogDebug("Getting scan provider for scan type {ScanType}", request.ScanType);
            var scanProvider = _scanProviderFactory.GetProvider(request.ScanType);
            
            // Use ScannerToken for both Sonar and Fortify
            string scannerToken = request.ScannerToken;
            _logger.LogDebug("Using scanner token for {ScanType}", request.ScanType);
            
            // Call the provider's GetIssuesAsync method
            _logger.LogDebug("Fetching issues from scanner...");
            var issues = await scanProvider.GetIssuesAsync(
                request.ProjectKeyOrReleaseId,
                request.MinSeverity,
                scannerToken,
                request.TaskId);
            _logger.LogInformation("Fetched {IssueCount} issues from scanner", issues.Count);
            
            // Group the issues by FilePath (issues are already filtered by the provider based on MinSeverity)
            var fileGroups = issues.GroupBy(x => x.FilePath);
            _logger.LogInformation("Grouped issues into {FileGroupCount} file groups", fileGroups.Count());
            _logger.LogInformation("Grouped issues into {FileGroupCount} file groups", fileGroups.Count());
            
            int filesProcessed = 0;
            int totalFiles = fileGroups.Count();
            _logger.LogInformation("Starting to process {TotalFiles} files", totalFiles);
            
            // Dictionary to store the patched file contents in memory
            var patchedFiles = new Dictionary<string, string>();
            
            // Loop through each file group
            foreach (var fileGroup in fileGroups)
            {
                var filePath = fileGroup.Key;
                _logger.LogInformation("Processing file {FilePath} ({CurrentFile}/{TotalFiles})", filePath, filesProcessed + 1, totalFiles);
                _logger.LogDebug("Processing {IssueCount} issues in file {FilePath}", fileGroup.Count(), filePath);
                
                // Fetch file content using IGitProvider.GetFileContentAsync
                _logger.LogDebug("Fetching content for file {FilePath} from branch {SourceBranch}", filePath, request.SourceBranch);
                var fileContent = await _gitProvider.GetFileContentAsync(
                    request.RepoId,
                    filePath,
                    request.SourceBranch,
                    request.GitlabToken);
                _logger.LogDebug("Fetched content for file {FilePath} (length: {ContentLength})", filePath, fileContent.Length);
                
                // Check if file size exceeds the limit
                var maxFileSizeBytes = long.Parse(Environment.GetEnvironmentVariable("MAX_FILE_SIZE_BYTES") ?? "2097152"); // Default to 2MB
                if (fileContent.Length > maxFileSizeBytes)
                {
                    _logger.LogError("File {FilePath} exceeds size limit of {MaxSizeBytes} bytes. Skipping.", filePath, maxFileSizeBytes);
                    continue;
                }
                
                // Send the code and issues to the AI using IAiAgent.FixCodeAsync
                _logger.LogDebug("Sending file {FilePath} to AI agent for remediation", filePath);
                var aiResponse = await _aiAgent.FixCodeAsync(fileContent, fileGroup.ToList());
                _logger.LogDebug("Received response from AI agent for file {FilePath}. Duration: {Duration}ms", filePath, aiResponse.Duration.TotalMilliseconds);
                
                // Store the patched code in memory instead of committing immediately
                patchedFiles[filePath] = aiResponse.FixedCode;
                
                filesProcessed++;
                _logger.LogInformation("Completed processing file {FilePath} ({CurrentFile}/{TotalFiles})", filePath, filesProcessed, totalFiles);
            }
            _logger.LogInformation("Processed {FilesProcessed} out of {TotalFiles} files", filesProcessed, totalFiles);
            
            // After all files have been successfully patched by the AI, create a new Git branch
            _logger.LogInformation("Creating new branch {NewBranchName} from {SourceBranch}", request.NewBranchName, request.SourceBranch);
            await _gitProvider.CreateBranchAsync(
                request.RepoId,
                request.SourceBranch,
                request.NewBranchName,
                request.GitlabToken);
            _logger.LogInformation("Created new branch {NewBranchName} successfully", request.NewBranchName);
            
            // Now commit all the patched files to the new branch
            foreach (var patchedFile in patchedFiles)
            {
                var filePath = patchedFile.Key;
                var fixedCode = patchedFile.Value;
                
                _logger.LogDebug("Committing fixed file {FilePath} to branch {NewBranchName}", filePath, request.NewBranchName);
                await _gitProvider.CommitFileAsync(
                    request.RepoId,
                    request.NewBranchName,
                    filePath,
                    fixedCode, // Using the stored patched code
                    $"Fix issues in {filePath}",
                    request.GitlabToken);
                _logger.LogDebug("Committed fixed file {FilePath} successfully", filePath);
            }
            
            // After the loop, create a merge request using IGitProvider.CreateMergeRequestAsync
            _logger.LogInformation("Creating merge request from {NewBranchName} to {TargetBranch}", request.NewBranchName, request.TargetBranch);
            await _gitProvider.CreateMergeRequestAsync(
                request.RepoId,
                request.NewBranchName,
                request.TargetBranch,
                "Automated remediation by AI Agent",
                request.GitlabToken);
            _logger.LogInformation("Created merge request successfully");
            
            stopwatch.Stop();
            _logger.LogInformation("Remediation completed. Processed {FilesProcessed} files in {TotalExecutionTimeMs}ms", filesProcessed, stopwatch.ElapsedMilliseconds);
            
            // Return a summary JSON object including the number of files processed and the total execution time
            var summary = new
            {
                FilesProcessed = filesProcessed,
                TotalExecutionTimeMs = stopwatch.ElapsedMilliseconds
            };
            
            return summary;
        }
    }
}