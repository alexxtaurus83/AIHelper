using AIHelper.Interfaces;
using AIHelper.Models;
using DiffMatchPatch;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Intrinsics.Arm;
using System.Threading.Tasks;
using TextDiff;

namespace AIHelper.Services {
    public class RemediationService : IRemediationService {
        private readonly ILogger<RemediationService> _logger;
        private readonly SonarProvider _sonarProvider;
        private readonly FortifyProvider _fortifyProvider;
        private readonly IGitProvider _gitProvider;
        private readonly IAiAgent _aiAgent;

        public RemediationService(
            ILogger<RemediationService> logger,
            SonarProvider sonarProvider,
            FortifyProvider fortifyProvider,
            IGitProvider gitProvider,
            IAiAgent aiAgent) {
            _logger = logger;
            _sonarProvider = sonarProvider;
            _fortifyProvider = fortifyProvider;
            _gitProvider = gitProvider;
            _aiAgent = aiAgent;
        }

        public async Task<object> RemediateAsync(RemediationRequestBaseDto request) {
            var stopwatch = Stopwatch.StartNew();

            _logger.LogInformation("Starting remediation for {ProjectKeyOrReleaseId}...", request.ProjectKeyOrReleaseId);
            _logger.LogDebug("Remediation request details: {@Request}", request);

            // Validate required inputs before any external calls
            ValidateRequest(request);

            List<RemediationTask> issues;

            // Determine which provider to use based on the request type
            if (request is SonarRemediationRequestDto sonarRequest)
            {
                _logger.LogDebug("Processing as Sonar request");
                string scannerToken = sonarRequest.ScannerToken;
                issues = await _sonarProvider.GetIssuesAsync(
                    sonarRequest.ProjectKeyOrReleaseId,
                    null, // severities not used for Sonar
                    sonarRequest.ImpactSeverities,
                    sonarRequest.ImpactSoftwareQualities,
                    scannerToken,
                    sonarRequest.TaskId);
                _logger.LogInformation("Fetched {IssueCount} issues from Sonar", issues.Count);
            }
            else if (request is FortifyRemediationRequestDto fortifyRequest)
            {
                _logger.LogDebug("Processing as Fortify request");
                string scannerToken = fortifyRequest.ScannerToken;
                issues = await _fortifyProvider.GetIssuesAsync(
                    fortifyRequest.ProjectKeyOrReleaseId,
                    fortifyRequest.Severities,
                    null, // impactSeverities not used for Fortify
                    null, // impactSoftwareQualities not used for Fortify
                    scannerToken);
                _logger.LogInformation("Fetched {IssueCount} issues from Fortify", issues.Count);
            }
            else
            {
                throw new ArgumentException($"Unknown request type: {request.GetType().Name}");
            }

            // Group the issues by FilePath (issues are already filtered by the provider based on severity filters)
            var fileGroups = issues.GroupBy(x => x.FilePath);
            _logger.LogInformation("Grouped issues into {FileGroupCount} file groups", fileGroups.Count());
            int filesProcessed = 0;
            int totalFiles = fileGroups.Count();
            _logger.LogInformation("Starting to process {TotalFiles} files", totalFiles);

            // Configuration constants
            const int MaxAttemptsPerIssue = 3;
            const int MaxTotalEditsPerFile = 50;
            const int MaxNoChangeCount = 2;

            // Generate new branch name dynamically in the format {SourceBranch}-{YYYYMMDD-HHmm}
            string newBranchName = $"{request.SourceBranch}-{DateTime.UtcNow:yyyyMMdd-HHmm}";

            // Create the GitLab feature branch BEFORE any AI processing
            _logger.LogInformation("Creating new branch {NewBranchName} from {SourceBranch}", newBranchName, request.SourceBranch);
            await _gitProvider.CreateBranchAsync(
                request.RepoId,
                request.SourceBranch,
                newBranchName,
                request.GitlabToken);
            _logger.LogInformation("Created new branch {NewBranchName} successfully", newBranchName);

            // Loop through each file group
            foreach (var fileGroup in fileGroups) {
                var filePath = fileGroup.Key;
                _logger.LogInformation("Processing file {FilePath} ({CurrentFile}/{TotalFiles})", filePath, filesProcessed + 1, totalFiles);
                
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
                if (fileContent.Length > maxFileSizeBytes) {
                    _logger.LogError("File {FilePath} exceeds size limit of {MaxSizeBytes} bytes. Skipping.", filePath, maxFileSizeBytes);
                    continue;
                }

                // Sort issues by severity using explicit severity ranking (descending) then by StartLine (ascending)
                var sortedIssues = fileGroup
                    .OrderByDescending(x => GetSeverityRank(x.Severity))
                    .ThenBy(x => x.StartLine)
                    .ToList();
                _logger.LogDebug("Processing {IssueCount} issues in file {FilePath}, sorted by severity and line", sortedIssues.Count, filePath);

                // Per-issue tracking structure
                var issueStatuses = new Dictionary<string, IssueProcessingStatus>();
                foreach (var issue in sortedIssues) {
                    _logger.LogDebug(issue.ToString());
                    issueStatuses[issue.ScannerIssueId ?? $"{issue.FilePath}:{issue.StartLine}"] = new IssueProcessingStatus {
                        Status = "pending",
                        Attempts = 0,
                        LastError = null
                    };
                }

                // Working copy of the file content that gets updated after each successful fix
                string currentCode = fileContent;
                int totalEditsApplied = 0;

                // Store original content for line ending detection (avoid re-fetch)
                string originalFileContent = fileContent;

                // Iterative per-issue processing loop
                foreach (var issue in sortedIssues) {
                    var issueKey = issue.ScannerIssueId ?? $"{issue.FilePath}:{issue.StartLine}";
                    var status = issueStatuses[issueKey];

                    // Check max total edits per file
                    if (totalEditsApplied >= MaxTotalEditsPerFile) {
                        _logger.LogWarning("File {FilePath} reached max total edits limit ({MaxEdits}). Stopping processing.", filePath, MaxTotalEditsPerFile);
                        break;
                    }

                    // Check if issue is already blocked
                    if (status.Status == "blocked") {
                        _logger.LogDebug("Skipping blocked issue {IssueKey}", issueKey);
                        continue;
                    }

                    // Attempt loop for this issue
                    bool issueFixed = false;
                    int noChangeCount = 0;

                    while (status.Attempts < MaxAttemptsPerIssue && !issueFixed) {
                        status.Attempts++;
                        _logger.LogDebug("Processing issue {IssueKey} (attempt {Attempt}/{MaxAttempts})", issueKey, status.Attempts, MaxAttemptsPerIssue);

                        try {
                            // Step 1: Localize the issue
                            _logger.LogDebug("Localizing issue {IssueKey} in file {FilePath}", issueKey, filePath);
                            var localizationResponse = await _aiAgent.LocalizeIssueAsync(
                                filePath,
                                currentCode,
                                issue,
                                contextLines: 30,
                                systemPrompt: request.SystemPromptLocalize);

                            // Check if localization was blocked
                            if (!string.IsNullOrEmpty(localizationResponse.BlockedReason)) {
                                _logger.LogWarning("Issue {IssueKey} localization blocked: {BlockedReason}", issueKey, localizationResponse.BlockedReason);
                                status.Status = "blocked";
                                status.LastError = localizationResponse.BlockedReason;
                                break;
                            }

                            // Validate localization response
                            if (localizationResponse.Anchors == null) {
                                _logger.LogWarning("Issue {IssueKey} localization returned null anchors. Marking as blocked.", issueKey);
                                status.Status = "blocked";
                                status.LastError = "Null anchors from localization";
                                break;
                            }

                            _logger.LogDebug("Localized issue {IssueKey} with confidence {Confidence}. Edit plan: {EditPlan}",
                                issueKey,
                                localizationResponse.Confidence,
                                localizationResponse.EditPlan);

                            // Step 2: Apply the fix
                            _logger.LogDebug("Applying fix for issue {IssueKey} in file {FilePath}", issueKey, filePath);
                            var applyResponse = await _aiAgent.ApplyIssueFixAsync(
                                filePath,
                                currentCode,
                                issue,
                                localizationResponse.Anchors,
                                systemPrompt: request.SystemPromptApply);

                            // Check if apply was blocked
                            if (!string.IsNullOrEmpty(applyResponse.BlockedReason)) {
                                _logger.LogWarning("Issue {IssueKey} apply blocked: {BlockedReason}", issueKey, applyResponse.BlockedReason);
                                status.Status = "blocked";
                                status.LastError = applyResponse.BlockedReason;
                                break;
                            }

                            // Validate apply response - ensure UpdatedFile is non-empty
                            if (string.IsNullOrEmpty(applyResponse.UpdatedFile)) {
                                _logger.LogWarning("Issue {IssueKey} apply returned empty UpdatedFile. Marking as blocked.", issueKey);
                                status.Status = "blocked";
                                status.LastError = "Empty UpdatedFile from apply";
                                break;
                            }

                            // Check for no-change (same content returned)
                            if (applyResponse.UpdatedFile == currentCode) {
                                noChangeCount++;
                                _logger.LogWarning("Issue {IssueKey} apply returned same content (no-change count: {NoChangeCount}/{MaxNoChange})", issueKey, noChangeCount, MaxNoChangeCount);
                                
                                if (noChangeCount >= MaxNoChangeCount) {
                                    _logger.LogWarning("Issue {IssueKey} returned no-change twice. Marking as blocked.", issueKey);
                                    status.Status = "blocked";
                                    status.LastError = "No-change detected twice";
                                    break;
                                }
                                
                                // Retry the same issue
                                continue;
                            }

                            // Successful fix applied
                            _logger.LogDebug("Successfully applied fix for issue {IssueKey}", issueKey);
                            currentCode = applyResponse.UpdatedFile;
                            totalEditsApplied++;
                            issueFixed = true;
                            status.Status = "fixed";

                        } catch (Exception ex) {
                            _logger.LogError(ex, "Error processing issue {IssueKey} (attempt {Attempt})", issueKey, status.Attempts);
                            status.LastError = ex.Message;
                            
                            if (status.Attempts >= MaxAttemptsPerIssue) {
                                status.Status = "blocked";
                            }
                        }
                    }

                    if (!issueFixed && status.Status != "blocked") {
                        status.Status = "failed";
                        _logger.LogWarning("Issue {IssueKey} failed after {Attempts} attempts", issueKey, status.Attempts);
                    }
                }

                // Log per-file summary
                var fixedCount = issueStatuses.Count(x => x.Value.Status == "fixed");
                var blockedCount = issueStatuses.Count(x => x.Value.Status == "blocked");
                var failedCount = issueStatuses.Count(x => x.Value.Status == "failed");
                _logger.LogInformation("File {FilePath} summary: {FixedCount} fixed, {BlockedCount} blocked, {FailedCount} failed. Total edits: {TotalEdits}",
                    filePath, fixedCount, blockedCount, failedCount, totalEditsApplied);

                // Commit the fixed file immediately after processing
                // 1. Detect line ending mode from original file content (stored in memory)
                var lineEndingMode = DetectLineEndingMode(originalFileContent);
                
                // 2. Normalize FixedCode to match the detected line ending mode
                string normalizedFixedCode = NormalizeLineEndings(currentCode, lineEndingMode);
                
                // Log diff summary before committing
                var diffSummary = CalculateDiffSummary(originalFileContent, normalizedFixedCode);
                _logger.LogInformation("File {FilePath}: {LinesAdded} lines added, {LinesRemoved} lines removed, {LinesUnchanged} unchanged",
                    filePath, diffSummary.LinesAdded, diffSummary.LinesRemoved, diffSummary.LinesUnchanged);

                _logger.LogDebug("Committing fixed file {FilePath} to branch {NewBranchName}", filePath, newBranchName);
                await _gitProvider.CommitFileAsync(
                    request.RepoId,
                    newBranchName,
                    filePath,
                    normalizedFixedCode,
                    $"Fix issues in {filePath}",
                    request.GitlabToken);
                _logger.LogDebug("Committed fixed file {FilePath} successfully", filePath);

                filesProcessed++;
                _logger.LogInformation("Completed processing file {FilePath} ({CurrentFile}/{TotalFiles})", filePath, filesProcessed, totalFiles);
            }
            _logger.LogInformation("Processed {FilesProcessed} out of {TotalFiles} files", filesProcessed, totalFiles);

            // After the loop, create a merge request using IGitProvider.CreateMergeRequestAsync
            _logger.LogInformation("Creating merge request from {NewBranchName} to {TargetBranch}", newBranchName, request.TargetBranch);
            await _gitProvider.CreateMergeRequestAsync(
                request.RepoId,
                newBranchName,
                request.TargetBranch,
                "Automated remediation by AI Agent",
                request.GitlabToken);
            _logger.LogInformation("Created merge request successfully");

            stopwatch.Stop();
            _logger.LogInformation("Remediation completed. Processed {FilesProcessed} files in {TotalExecutionTimeMs}ms", filesProcessed, stopwatch.ElapsedMilliseconds);

            // Return a summary JSON object including the number of files processed and the total execution time
            var summary = new {
                FilesProcessed = filesProcessed,
                TotalExecutionTimeMs = stopwatch.ElapsedMilliseconds
            };

            return summary;
        }

        /// <summary>
        /// Validates required inputs in the remediation request.
        /// Throws ArgumentException if any required field is missing.
        /// </summary>
        private static void ValidateRequest(RemediationRequestBaseDto request) {
            var missingFields = new List<string>();

            if (string.IsNullOrWhiteSpace(request.ProjectKeyOrReleaseId)) {
                missingFields.Add("ProjectKeyOrReleaseId");
            }
            if (string.IsNullOrWhiteSpace(request.RepoId)) {
                missingFields.Add("RepoId");
            }
            if (string.IsNullOrWhiteSpace(request.SourceBranch)) {
                missingFields.Add("SourceBranch");
            }
            if (string.IsNullOrWhiteSpace(request.TargetBranch)) {
                missingFields.Add("TargetBranch");
            }
            if (string.IsNullOrWhiteSpace(request.GitlabToken)) {
                missingFields.Add("GitlabToken");
            }
            if (string.IsNullOrWhiteSpace(request.ScannerToken)) {
                missingFields.Add("ScannerToken");
            }
            if (string.IsNullOrWhiteSpace(request.SystemPromptLocalize)) {
                missingFields.Add("SystemPromptLocalize");
            }
            if (string.IsNullOrWhiteSpace(request.SystemPromptApply)) {
                missingFields.Add("SystemPromptApply");
            }

            if (missingFields.Count > 0) {
                throw new ArgumentException($"Missing required fields: {string.Join(", ", missingFields)}");
            }
        }

        /// <summary>
        /// Gets the severity rank for sorting. Higher values = higher priority.
        /// Supports both Sonar (BLOCKER/HIGH/MEDIUM/LOW/INFO) and Fortify (Critical/High/Medium/Low) severities.
        /// Unknown severities return 0 (lowest priority).
        /// </summary>
        private static int GetSeverityRank(string? severity) {
            if (string.IsNullOrEmpty(severity)) {
                return 0;
            }

            // Normalize to uppercase for case-insensitive comparison
            var normalized = severity.ToUpperInvariant();

            return normalized switch {
                // Sonar severities
                "BLOCKER" => 6,
                "CRITICAL" => 5,  // Also handles Fortify "Critical"
                "HIGH" => 4,      // Also handles Fortify "High"
                "MAJOR" => 3,     // Sonar-specific
                "MEDIUM" => 2,    // Fortify "Medium" and Sonar impact severity
                "LOW" => 1,       // Fortify "Low" and Sonar impact severity
                "MINOR" => 1,     // Sonar-specific
                "INFO" => 0,      // Lowest
                // Unknown severities sort last
                _ => 0
            };
        }

        /// <summary>
        /// Calculates a summary of changes between original and fixed code using DiffMatchPatch.
        /// </summary>
        /// <param name="original">The original file content.</param>
        /// <param name="fixed">The fixed file content.</param>
        /// <returns>A DiffSummary with line counts.</returns>
        private static DiffSummary CalculateDiffSummary(string original, string fixedCode) {
            var dmp = new diff_match_patch();
            var diffs = dmp.diff_main(original, fixedCode);
            dmp.diff_cleanupSemantic(diffs);

            var addedCount = 0;
            var removedCount = 0;
            var unchangedCount = 0;

            foreach (var diff in diffs) {
                // Count lines in this diff operation
                var lineCount = diff.text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).Length;

                switch (diff.operation) {
                    case Operation.INSERT:
                        addedCount += lineCount;
                        break;
                    case Operation.DELETE:
                        removedCount += lineCount;
                        break;
                    case Operation.EQUAL:
                        unchangedCount += lineCount;
                        break;
                }
            }

            return new DiffSummary {
                LinesAdded = addedCount,
                LinesRemoved = removedCount,
                LinesUnchanged = unchangedCount
            };
        }

        /// <summary>
        /// Detects the dominant line ending mode from the given code.
        /// </summary>
        /// <param name="code">The code to analyze.</param>
        /// <returns>The detected LineEndingMode.</returns>
        private static LineEndingMode DetectLineEndingMode(string code) {
            if (string.IsNullOrEmpty(code)) {
                return LineEndingMode.Unix; // Default to Unix if empty
            }

            int windowsCount = 0;
            int macCount = 0;
            int unixCount = 0;

            for (int i = 0; i < code.Length; i++) {
                if (code[i] == '\r') {
                    if (i + 1 < code.Length && code[i + 1] == '\n') {
                        windowsCount++;
                        i++; // Skip the next '\n'
                    } else {
                        macCount++;
                    }
                } else if (code[i] == '\n') {
                    unixCount++;
                }
            }

            // Determine the dominant line ending mode
            if (windowsCount >= macCount && windowsCount >= unixCount) {
                return LineEndingMode.Windows;
            } else if (macCount >= unixCount) {
                return LineEndingMode.Mac;
            } else {
                return LineEndingMode.Unix;
            }
        }

        /// <summary>
        /// Normalizes the line endings in the given code to match the specified line ending mode.
        /// </summary>
        /// <param name="code">The code to normalize.</param>
        /// <param name="mode">The target line ending mode.</param>
        /// <returns>The code with normalized line endings.</returns>
        private static string NormalizeLineEndings(string code, LineEndingMode mode) {
            if (string.IsNullOrEmpty(code)) {
                return code;
            }

            string targetEnding = mode switch {
                LineEndingMode.Windows => "\r\n",
                LineEndingMode.Mac => "\r",
                LineEndingMode.Unix => "\n",
                _ => "\n"
            };

            // First, normalize all line endings to a common placeholder
            // Replace Windows line endings first (to avoid double-processing \r and \n)
            string normalized = code.Replace("\r\n", "\n");
            // Then replace old Mac line endings
            normalized = normalized.Replace("\r", "\n");
            // Now all line endings are \n, replace with target
            return normalized.Replace("\n", targetEnding);
        }
    }

    /// <summary>
    /// Tracks the processing status of an individual issue during remediation.
    /// </summary>
    public class IssueProcessingStatus {
        /// <summary>
        /// Current status of the issue processing (pending, fixed, blocked, failed).
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// Number of attempts made to process this issue.
        /// </summary>
        public int Attempts { get; set; }

        /// <summary>
        /// Last error message encountered while processing this issue.
        /// </summary>
        public string LastError { get; set; }
    }

    public enum LineEndingMode {
        Windows = 0,
        Mac = 1,
        Unix = 2
    }

    /// <summary>
    /// Summary of changes between two file versions.
    /// </summary>
    public class DiffSummary
    {
        public int LinesAdded { get; set; }
        public int LinesRemoved { get; set; }
        public int LinesUnchanged { get; set; }
    }
}
