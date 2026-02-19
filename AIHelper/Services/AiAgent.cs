using AIHelper.Interfaces;
using AIHelper.Models;
using RestSharp;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;


namespace AIHelper.Services
{
    public class AiAgent : IAiAgent
    {
        private readonly string _apiKey;
        private readonly string _localizeApiUrl;
        private readonly string _localizeModel;
        private readonly string _applyApiUrl;
        private readonly string _applyModel;
        private readonly ILogger<AiAgent> _logger;
    
        public AiAgent(string apiKey, string localizeApiUrl, string localizeModel, string applyApiUrl, string applyModel, ILogger<AiAgent> logger = null)
        {
            _apiKey = apiKey;
            _localizeApiUrl = localizeApiUrl;
            _localizeModel = localizeModel;
            _applyApiUrl = applyApiUrl;
            _applyModel = applyModel;
            _logger = logger;
        }

        /// <summary>
        /// Performs the HTTP POST to /chat/completions and returns the assistant content string.
        /// </summary>
        /// <param name="systemPrompt">The system prompt to use.</param>
        /// <param name="userPrompt">The user prompt to send.</param>
        /// <param name="temperature">Temperature setting (0-0.2 recommended for deterministic output).</param>
        /// <param name="jsonOnly">If true, instructs the model to output JSON only and validates the response is valid JSON.</param>
        /// <returns>The assistant's content from the response.</returns>
        private async Task<string> PostChatCompletionAsync(string apiUrl, string model, string systemPrompt, string userPrompt, float temperature = 0.2f, bool jsonOnly = false)
        {
            var stopwatch = Stopwatch.StartNew();
            _logger?.LogDebug("Calling AI chat completion API.");

            if (string.IsNullOrEmpty(systemPrompt))
            {
                throw new ArgumentException("System prompt is required and must not be null or empty.", nameof(systemPrompt));
            }

            var aiApiKey = _apiKey;

            var client = new RestClient(apiUrl + "/chat/completions");
            var request = new RestRequest("", Method.Post);
            request.AddHeader("Authorization", $"Bearer {aiApiKey}");
            request.AddHeader("Content-Type", "application/json");

            // Build request body - when jsonOnly is true, use response_format to enforce JSON schema
            var messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            };

            if (jsonOnly)
            {
                var requestBodyWithFormat = new
                {
                    model = model,
                    messages = messages,
                    temperature = temperature,
                    stream = false,
                    top_p = 1,
                    response_format = new { type = "json_object" }
                };
                request.AddJsonBody(requestBodyWithFormat);
            }
            else
            {
                var requestBody = new
                {
                    model = model,
                    messages = messages,
                    temperature = temperature,
                    stream = false,
                    top_p = 1
                };
                request.AddJsonBody(requestBody);
            }

            var response = await client.ExecutePostAsync(request);
            if (!response.IsSuccessful)
            {
                _logger?.LogError("AI API call failed: {ErrorMessage}", response.ErrorMessage);
                throw new Exception($"AI API call failed: {response.ErrorMessage}");
            }

            string resultFromAI = null;
            using var jsonDocument = JsonDocument.Parse(response.Content);
            var rootElement = jsonDocument.RootElement;

            if (rootElement.TryGetProperty("choices", out var choicesElement) &&
                choicesElement.ValueKind == JsonValueKind.Array &&
                choicesElement.GetArrayLength() > 0)
            {
                var choiceElement = choicesElement[0];
                if (choiceElement.TryGetProperty("message", out var messageElement) &&
                    messageElement.TryGetProperty("content", out var contentElement))
                {
                    resultFromAI = contentElement.GetString();
                }
            }

            if (string.IsNullOrEmpty(resultFromAI))
            {
                _logger?.LogError("AI API response did not contain expected 'choices[0].message.content' structure.");
                throw new Exception("AI API response did not contain expected structure.");
            }

            // When jsonOnly is true, validate the response is valid JSON after extraction
            if (jsonOnly)
            {
                var cleanJson = ExtractJsonFromResponse(resultFromAI);
                try
                {
                    using var validationDoc = JsonDocument.Parse(cleanJson);
                    // Valid JSON - continue
                }
                catch (JsonException ex)
                {
                    _logger?.LogError(ex, "AI API response with jsonOnly=true did not contain valid JSON. Extracted: {CleanJson}", cleanJson);
                    throw new Exception($"Expected JSON response but got invalid JSON: {ex.Message}");
                }
            }

            stopwatch.Stop();
            _logger?.LogDebug("Received response from AI. Duration: {Duration}ms", stopwatch.ElapsedMilliseconds);

            return resultFromAI;
        }

        public async Task<AiResponse> FixCodeAsync(string code, List<RemediationTask> issues, string systemPrompt)
        {
            var stopwatch = Stopwatch.StartNew();
            _logger?.LogInformation("Starting AI code fix for {IssueCount} issues.", issues.Count);

            // Construct the user prompt with file content and issues
            var userPrompt = $"Fix ALL {issues.Count} security issues in this file.\n\n";

            userPrompt += "**REQUIREMENTS:**\n";
            userPrompt += "1. Detect the programming language from the code\n";
            userPrompt += "2. For EACH issue, apply the fix at the specified line\n";
            userPrompt += "3. Add a comment ABOVE the fix using the CORRECT syntax for the language\n";
            userPrompt += "4. Comment format should be:\n";
            userPrompt += "   [Language-appropriate comment] Issue: [from Description]\n";
            userPrompt += "   [Language-appropriate comment] Fix: [what you changed]\n\n";

            userPrompt += "**EXAMPLES OF CORRECT COMMENT SYNTAX:**\n";
            userPrompt += "- C#/Java/JavaScript: // Comment\n";
            userPrompt += "- Python: # Comment\n";
            userPrompt += "- SQL: -- Comment\n";
            userPrompt += "- HTML: <!-- Comment -->\n";
            userPrompt += "- PowerShell: # Comment\n";
            userPrompt += "- XML/Config: <!-- Comment -->\n";
            userPrompt += "- Bash/Shell: # Comment\n\n";

            userPrompt += "**ISSUES TO FIX:**\n";

            foreach (var issue in issues)
            {
                userPrompt += $"---\n";
                userPrompt += $"LINE {issue.StartLine} [{issue.Severity}]: {issue.Description}\n";
            }

            userPrompt += $"\n**ORIGINAL CODE:**\n```\n{code}\n```\n\n";
            userPrompt += "**FINAL CHECK:**\n";
            userPrompt += "- Did you detect the language correctly?\n";
            userPrompt += "- Did you use the right comment syntax for that language?\n";
            userPrompt += "- Did you add comment for EVERY fix?\n";
            userPrompt += "- Did you fix ALL issues?\n";

            var resultFromAI = await PostChatCompletionAsync(_applyApiUrl, _applyModel, systemPrompt, userPrompt, 0.2f, false);

            // Strip markdown code block fences from the AI's response
            var cleanResultFromAI = resultFromAI.Trim();
            if (cleanResultFromAI.StartsWith("```csharp"))
            {
                cleanResultFromAI = cleanResultFromAI.Substring(9);
            }
            if (cleanResultFromAI.EndsWith("```"))
            {
                cleanResultFromAI = cleanResultFromAI.Substring(0, cleanResultFromAI.Length - 3);
            }
            cleanResultFromAI = cleanResultFromAI.Trim();

            stopwatch.Stop();
            _logger?.LogDebug("Received response from AI and applied patch. Duration: {Duration}ms", stopwatch.ElapsedMilliseconds);
            _logger?.LogInformation("AI code fix completed. Duration: {Duration}ms", stopwatch.ElapsedMilliseconds);

            return new AiResponse
            {
                OriginalCode = code,
                FixedCode = cleanResultFromAI,
                Duration = stopwatch.Elapsed
            };
        }



        /// <summary>
        /// Localizes an issue in the code and returns anchors identifying the location.
        /// </summary>
        /// <param name="filePath">The path to the file being analyzed.</param>
        /// <param name="code">The source code content.</param>
        /// <param name="issue">The remediation task describing the issue.</param>
        /// <param name="contextLines">Number of context lines to include.</param>
        /// <param name="systemPrompt">Required system prompt for LOCALIZE mode. Must be provided; cannot be null or empty.</param>
        public async Task<AiLocalizationResponse> LocalizeIssueAsync(string filePath, string code, RemediationTask issue, int contextLines, string systemPrompt)
        {
            var stopwatch = Stopwatch.StartNew();
            _logger?.LogInformation("Starting AI issue localization for issue {IssueId} in {FilePath}.", issue.ScannerIssueId, filePath);

            var userPrompt = BuildLocalizationPrompt(filePath, code, issue, contextLines);
            const int maxAttempts = 4;
            string lastError = null;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                string resultFromAI;
                try
                {
                    resultFromAI = await PostChatCompletionAsync(_localizeApiUrl, _localizeModel, systemPrompt, userPrompt, temperature: 0.1f, jsonOnly: true);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "AI localization call failed for issue {IssueId} (attempt {Attempt}/{MaxAttempts}).", issue.ScannerIssueId, attempt, maxAttempts);
                    lastError = ex.Message;
                    if (attempt < maxAttempts)
                    {
                        userPrompt += "\n\nReturn ONLY JSON matching the schema; no wrapper; no prose.";
                    }
                    continue;
                }

                try
                {
                    var response = ParseLocalizationResponse(resultFromAI, issue.ScannerIssueId);
                    if (string.IsNullOrEmpty(response.Status) || (response.Anchors == null && response.Status != "blocked"))
                    {
                        _logger?.LogDebug("ParseLocalizationResponse returned missing required fields (attempt {Attempt}/{MaxAttempts}).", attempt, maxAttempts);
                        lastError = "Missing required fields: status or anchors";
                        if (attempt < maxAttempts)
                        {
                            userPrompt += "\n\nReturn ONLY JSON matching the schema; no wrapper; no prose.";
                            continue;
                        }
                    }

                    stopwatch.Stop();
                    _logger?.LogInformation("AI issue localization completed. Duration: {Duration}ms", stopwatch.ElapsedMilliseconds);
                    return response;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to parse AI localization response for issue {IssueId} (attempt {Attempt}/{MaxAttempts}).", issue.ScannerIssueId, attempt, maxAttempts);
                    lastError = $"Parse error: {ex.Message}";
                    if (attempt < maxAttempts)
                    {
                        userPrompt += "\n\nReturn ONLY JSON matching the schema; no wrapper; no prose.";
                        continue;
                    }
                }
            }

            stopwatch.Stop();
            _logger?.LogError("AI issue localization failed after {MaxAttempts} attempts for issue {IssueId}.", maxAttempts, issue.ScannerIssueId);
            return new AiLocalizationResponse
            {
                IssueId = issue.ScannerIssueId,
                Status = "blocked",
                Confidence = 0.0,
                Anchors = null,
                EditPlan = null,
                BlockedReason = $"Failed after {maxAttempts} attempts: {lastError}"
            };
        }

        /// <summary>
        /// Applies a fix to a localized issue using provided anchors.
        /// </summary>
        /// <param name="filePath">The path to the file being modified.</param>
        /// <param name="code">The source code content.</param>
        /// <param name="issue">The remediation task describing the issue.</param>
        /// <param name="anchors">The anchors identifying the issue location.</param>
        /// <param name="systemPrompt">Required system prompt for APPLY mode. Must be provided; cannot be null or empty.</param>
        public async Task<AiApplyResponse> ApplyIssueFixAsync(string filePath, string code, RemediationTask issue, IssueAnchors anchors, string systemPrompt)
        {
            var stopwatch = Stopwatch.StartNew();
            _logger?.LogInformation("Starting AI fix application for issue {IssueId} in {FilePath}.", issue.ScannerIssueId, filePath);

            var userPrompt = BuildApplyPrompt(filePath, code, issue, anchors);
            const int maxAttempts = 4;
            string lastError = null;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                string resultFromAI;
                try
                {
                    resultFromAI = await PostChatCompletionAsync(_applyApiUrl, _applyModel, systemPrompt, userPrompt, temperature: 0.1f, jsonOnly: true);
                    _logger.LogDebug(userPrompt);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "AI apply call failed for issue {IssueId} (attempt {Attempt}/{MaxAttempts}).", issue.ScannerIssueId, attempt, maxAttempts);
                    lastError = ex.Message;
                    if (attempt < maxAttempts)
                    {
                        userPrompt += "\n\nReturn ONLY JSON matching the schema; no wrapper; no prose.";
                    }
                    continue;
                }

                try
                {
                    var response = ParseApplyResponse(resultFromAI, issue.ScannerIssueId, code);
                    if (string.IsNullOrEmpty(response.Status) || (string.IsNullOrEmpty(response.UpdatedFile) && response.Status != "blocked"))
                    {
                        _logger?.LogDebug("ParseApplyResponse returned missing required fields (attempt {Attempt}/{MaxAttempts}).", attempt, maxAttempts);
                        lastError = "Missing required fields: status or updatedFile";
                        if (attempt < maxAttempts)
                        {
                            userPrompt += "\n\nReturn ONLY JSON matching the schema; no wrapper; no prose.";
                            _logger.LogDebug(userPrompt);
                            continue;
                        }
                    }

                    stopwatch.Stop();
                    _logger?.LogInformation("AI fix application completed. Duration: {Duration}ms", stopwatch.ElapsedMilliseconds);
                    return response;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to parse AI apply response for issue {IssueId} (attempt {Attempt}/{MaxAttempts}).", issue.ScannerIssueId, attempt, maxAttempts);
                    lastError = $"Parse error: {ex.Message}";
                    if (attempt < maxAttempts)
                    {
                        userPrompt += "\n\nReturn ONLY JSON matching the schema; no wrapper; no prose.";
                        _logger.LogDebug(userPrompt);
                        continue;
                    }
                }
            }

            stopwatch.Stop();
            _logger?.LogError("AI fix application failed after {MaxAttempts} attempts for issue {IssueId}.", maxAttempts, issue.ScannerIssueId);
            return new AiApplyResponse
            {
                IssueId = issue.ScannerIssueId,
                Status = "blocked",
                UpdatedFile = null,
                Patch = null,
                BlockedReason = $"Failed after {maxAttempts} attempts: {lastError}"
            };
        }

        /// <summary>
        /// Builds the prompt for issue localization.
        /// </summary>
        private string BuildLocalizationPrompt(string filePath, string code, RemediationTask issue, int contextLines)
        {
            var prompt = $@"**TASK**: Locate the security issue in the code and provide line anchors.

**FILE**: {filePath}

**ISSUE TO LOCATE**:
- ID: {issue.ScannerIssueId}
- Line: {issue.StartLine}
- Severity: {issue.Severity}
- Description: {issue.Description}
";

            if (!string.IsNullOrEmpty(issue.RemediationAdvice))
            {
                prompt += $"- Advice: {issue.RemediationAdvice}\n";
            }

            prompt += $@"
**REQUIREMENTS**:
1. Analyze the code and identify the exact location of the issue.
2. Provide anchors (BeforeLines, TargetLines, AfterLines) to pinpoint the issue.
3. Include approximately {contextLines} lines of context before and after.
4. Describe the edit plan to fix the issue.
5. Respond with JSON ONLY in this format:
{{
  ""issueId"": ""..."",
  ""status"": ""localized|not_found|error"",
  ""confidence"": 0.0-1.0,
  ""anchors"": {{
    ""beforeLines"": [""..."", ...],
    ""targetLines"": [""..."", ...],
    ""afterLines"": [""..."", ...]
  }},
  ""editPlan"": ""..."",
  ""blockedReason"": null or ""reason""
}}

**CODE**:
```
{code}
```

**RESPOND WITH JSON ONLY**:";

            return prompt;
        }

        /// <summary>
        /// Builds the prompt for applying a fix.
        /// </summary>
        private string BuildApplyPrompt(string filePath, string code, RemediationTask issue, IssueAnchors anchors)
        {
            var beforeContext = anchors.BeforeLines != null ? string.Join("\n", anchors.BeforeLines) : "";
            var targetSection = anchors.TargetLines != null ? string.Join("\n", anchors.TargetLines) : "";
            var afterContext = anchors.AfterLines != null ? string.Join("\n", anchors.AfterLines) : "";

            var prompt = $@"**TASK**: Apply a fix to the identified security issue.

**FILE**: {filePath}

**ISSUE TO FIX**:
- ID: {issue.ScannerIssueId}
- Severity: {issue.Severity}
- Description: {issue.Description}
";

            if (!string.IsNullOrEmpty(issue.RemediationAdvice))
            {
                prompt += $"- Remediation Advice: {issue.RemediationAdvice}\n";
            }

            prompt += $@"
**ISSUE LOCATION (ANCHORS)**:
--- BEFORE ---
{beforeContext}
--- TARGET (ISSUE LOCATION) ---
{targetSection}
--- AFTER ---
{afterContext}

**REQUIREMENTS**:
1. Apply the fix to the target section.
2. Preserve all code outside the target section unchanged.
3. **REQUIRED**: Place a 3-line comment block directly ABOVE each changed code:
   - Line 1: `// Issue: <short issue summary>`
   - Line 2: `// Remediation: <short remediation guidance>`
   - Line 3: `// Fix: <what changed>`
   - Use language-appropriate comment syntax:
     - C#/Java/JavaScript/Go: `// Comment`
     - Python/YAML/PowerShell/Bash: `# Comment`
     - SQL: `-- Comment`
     - HTML/XML/Config: `<!-- Comment -->`
4. Respond with JSON ONLY in this format:
{{
  ""issueId"": ""..."",
  ""status"": ""applied|error"",
  ""updatedFile"": ""full file content after fix"",
  ""patch"": ""optional unified diff"",
  ""blockedReason"": null or ""reason""
}}

**FULL CODE**:
```
{code}
```

**RESPOND WITH JSON ONLY**:";

            return prompt;
        }

        /// <summary>
        /// Parses the AI response for localization.
        /// </summary>
        private AiLocalizationResponse ParseLocalizationResponse(string jsonResponse, string issueId)
        {
            var rawResponse = jsonResponse;
            var cleanJson = ExtractJsonFromResponse(rawResponse);

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            
            // Try to parse as raw JSON element first to handle wrapper objects
            var rootElement = JsonDocument.Parse(cleanJson).RootElement;
            
            // Handle legacy wrapper format: { "issues": [...] }
            if (rootElement.TryGetProperty("issues", out var issuesArray) && issuesArray.ValueKind == JsonValueKind.Array && issuesArray.GetArrayLength() > 0)
            {
                _logger?.LogDebug("Detected legacy wrapper format with 'issues' array, extracting first element.");
                var firstIssue = issuesArray[0];
                cleanJson = firstIssue.GetRawText();
            }

            var response = JsonSerializer.Deserialize<AiLocalizationResponse>(cleanJson, options);

            if (response == null)
            {
                _logger?.LogDebug("Raw AI response (first 500 chars): {RawResponse}",
                    rawResponse.Length > 500 ? rawResponse.Substring(0, 500) + "..." : rawResponse);
                _logger?.LogDebug("Cleaned JSON (first 500 chars): {CleanJson}",
                    cleanJson.Length > 500 ? cleanJson.Substring(0, 500) + "..." : cleanJson);
                throw new Exception("Failed to deserialize localization response");
            }

            // Normalize legacy anchor format: if anchors has "before"/"target"/"after" strings, split into arrays
            if (response.Anchors != null)
            {
                response.Anchors = NormalizeAnchors(response.Anchors, rawResponse);
            }

            // Validate required fields and log if missing
            if (response.Anchors == null && response.Status != "blocked")
            {
                _logger?.LogDebug("Raw AI response (first 500 chars): {RawResponse}",
                    rawResponse.Length > 500 ? rawResponse.Substring(0, 500) + "..." : rawResponse);
                _logger?.LogDebug("Parsed response: Status={Status}, Anchors=null, Missing field: anchors", response.Status);
            }
            else
            {
                _logger?.LogDebug("Parsed localization response: Status={Status}, Confidence={Confidence}, Anchors present={AnchorsPresent}",
                    response.Status, response.Confidence, response.Anchors != null);
            }

            response.IssueId = issueId;
            return response;
        }

        /// <summary>
        /// Normalizes anchor formats, converting legacy string fields to line arrays.
        /// </summary>
        private IssueAnchors NormalizeAnchors(IssueAnchors anchors, string rawResponse)
        {
            var normalized = new IssueAnchors
            {
                BeforeLines = anchors.BeforeLines ?? Array.Empty<string>(),
                TargetLines = anchors.TargetLines ?? Array.Empty<string>(),
                AfterLines = anchors.AfterLines ?? Array.Empty<string>()
            };

            // Check for legacy format where before/target/after might be single strings
            // This requires re-parsing from raw JSON to detect string fields
            try
            {
                var anchorElement = JsonDocument.Parse(rawResponse).RootElement;
                
                // Handle wrapper format
                if (anchorElement.TryGetProperty("issues", out var issuesArray) && issuesArray.ValueKind == JsonValueKind.Array && issuesArray.GetArrayLength() > 0)
                {
                    anchorElement = issuesArray[0];
                }

                if (anchorElement.TryGetProperty("anchors", out var anchorsElement))
                {
                    // Check for legacy "before" string field
                    if (anchorsElement.TryGetProperty("before", out var beforeElement) && beforeElement.ValueKind == JsonValueKind.String)
                    {
                        _logger?.LogDebug("Normalizing legacy anchor field 'before' (string) to 'beforeLines' (array).");
                        normalized.BeforeLines = beforeElement.GetString().Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                    }
                    // Check for legacy "target" string field
                    if (anchorsElement.TryGetProperty("target", out var targetElement) && targetElement.ValueKind == JsonValueKind.String)
                    {
                        _logger?.LogDebug("Normalizing legacy anchor field 'target' (string) to 'targetLines' (array).");
                        normalized.TargetLines = targetElement.GetString().Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                    }
                    // Check for legacy "after" string field
                    if (anchorsElement.TryGetProperty("after", out var afterElement) && afterElement.ValueKind == JsonValueKind.String)
                    {
                        _logger?.LogDebug("Normalizing legacy anchor field 'after' (string) to 'afterLines' (array).");
                        normalized.AfterLines = afterElement.GetString().Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to normalize legacy anchor format, using parsed values as-is.");
            }

            return normalized;
        }

        /// <summary>
        /// Parses the AI response for apply.
        /// </summary>
        private AiApplyResponse ParseApplyResponse(string jsonResponse, string issueId, string originalCode)
        {
            var rawResponse = jsonResponse;
            var cleanJson = ExtractJsonFromResponse(rawResponse);

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            
            // Try to parse as raw JSON element first to handle wrapper objects
            var rootElement = JsonDocument.Parse(cleanJson).RootElement;
            
            // Handle legacy wrapper format: { "issues": [...] }
            if (rootElement.TryGetProperty("issues", out var issuesArray) && issuesArray.ValueKind == JsonValueKind.Array && issuesArray.GetArrayLength() > 0)
            {
                _logger?.LogDebug("Detected legacy wrapper format with 'issues' array, extracting first element.");
                var firstIssue = issuesArray[0];
                cleanJson = firstIssue.GetRawText();
            }

            var response = JsonSerializer.Deserialize<AiApplyResponse>(cleanJson, options);

            if (response == null)
            {
                _logger?.LogDebug("Raw AI response (first 500 chars): {RawResponse}",
                    rawResponse.Length > 500 ? rawResponse.Substring(0, 500) + "..." : rawResponse);
                _logger?.LogDebug("Cleaned JSON (first 500 chars): {CleanJson}",
                    cleanJson.Length > 500 ? cleanJson.Substring(0, 500) + "..." : cleanJson);
                throw new Exception("Failed to deserialize apply response");
            }

            // Handle legacy "updated_file" field (snake_case) mapping to "UpdatedFile" (PascalCase)
            // This is done via re-parsing since PropertyNameCaseInsensitive should handle it,
            // but we explicitly check for snake_case variant
            if (string.IsNullOrEmpty(response.UpdatedFile))
            {
                try
                {
                    var applyElement = JsonDocument.Parse(cleanJson).RootElement;
                    if (applyElement.TryGetProperty("updated_file", out var updatedFileElement))
                    {
                        _logger?.LogDebug("Normalizing legacy field 'updated_file' (snake_case) to 'UpdatedFile' (PascalCase).");
                        response.UpdatedFile = updatedFileElement.GetString();
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to check for legacy 'updated_file' field.");
                }
            }

            // Validate required fields and log if missing
            if (string.IsNullOrEmpty(response.UpdatedFile) && response.Status != "blocked")
            {
                _logger?.LogDebug("Raw AI response (first 500 chars): {RawResponse}",
                    rawResponse.Length > 500 ? rawResponse.Substring(0, 500) + "..." : rawResponse);
                _logger?.LogDebug("Parsed response: Status={Status}, UpdatedFile=null/empty, Missing field: updatedFile", response.Status);
            }
            else
            {
                _logger?.LogDebug("Parsed apply response: Status={Status}, UpdatedFile length={Length}",
                    response.Status, response.UpdatedFile?.Length ?? 0);
            }

            response.IssueId = issueId;
            return response;
        }

        /*private async Task<long> GetModelMaxTokens(RestClient client, string apiKey, string model)
        {
            try
            {
                // Create a request to get model information
                // Note: This endpoint may vary depending on the API provider
                // For OpenRouter, we may need to use their models endpoint
                //var modelsEndpoint = _apiUrl + "/models/" + HttpUtility.UrlEncode(model);
                var modelsEndpoint = _apiUrl + "/models";

                var modelsRequest = new RestRequest(modelsEndpoint, Method.Get);
                modelsRequest.AddHeader("Authorization", $"Bearer {apiKey}");
                modelsRequest.AddHeader("Content-Type", "application/json");

                var response = await client.ExecuteGetAsync(modelsRequest);
                if (response.IsSuccessful && !string.IsNullOrEmpty(response.Content))
                {
                    using var jsonDocument = JsonDocument.Parse(response.Content);
                    var rootElement = jsonDocument.RootElement;
                    
                    // Try to find the model in the response and get its max tokens
                    if (rootElement.TryGetProperty("data", out var dataArray) && dataArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var modelData in dataArray.EnumerateArray())
                        {
                            if (modelData.TryGetProperty("id", out var idElement) && idElement.GetString() == model)
                            {
                                if (modelData.TryGetProperty("max_tokens", out var maxTokensElement))
                                {
                                    return maxTokensElement.GetInt64();
                                }
                                // Some APIs might have different property names for max tokens
                                else if (modelData.TryGetProperty("context_length", out var contextLengthElement))
                                {
                                    return contextLengthElement.GetInt64();
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Could not retrieve model max tokens for {Model}, using default value", model);
            }

            // Return a reasonable default if we can't get the specific model info
            return 4096; // Common default for many models
        }*/

        /// <summary>
        /// Extracts valid JSON from an AI response by stripping code fences and finding balanced braces.
        /// This handles cases where the model outputs braces inside strings or nested objects.
        /// </summary>
        /// <param name="response">The raw AI response string.</param>
        /// <returns>Cleaned JSON string ready for deserialization.</returns>
        private string ExtractJsonFromResponse(string response)
        {
            var cleanJson = response.Trim();

            // Strip markdown code block fences (any language variant)
            // Matches: ```json, ```csharp, ```text, ```, etc.
            var codeFenceRegex = new Regex(@"^```\w*\s*|\s*```$", RegexOptions.Multiline);
            cleanJson = codeFenceRegex.Replace(cleanJson, "");
            cleanJson = cleanJson.Trim();

            // Find the first '{' that starts the JSON object
            var jsonStartIndex = cleanJson.IndexOf('{');
            if (jsonStartIndex < 0)
            {
                return cleanJson; // No JSON object found, return as-is
            }

            // Find the matching closing '}' by tracking brace balance and string context
            int braceCount = 0;
            bool inString = false;
            bool escaped = false;
            int jsonEndIndex = -1;

            for (int i = jsonStartIndex; i < cleanJson.Length; i++)
            {
                char c = cleanJson[i];

                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\' && inString)
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (!inString)
                {
                    if (c == '{')
                    {
                        braceCount++;
                    }
                    else if (c == '}')
                    {
                        braceCount--;
                        if (braceCount == 0)
                        {
                            jsonEndIndex = i;
                            break;
                        }
                    }
                }
            }

            if (jsonEndIndex > jsonStartIndex)
            {
                cleanJson = cleanJson.Substring(jsonStartIndex, jsonEndIndex - jsonStartIndex + 1);
            }

            return cleanJson;
        }
    }
}