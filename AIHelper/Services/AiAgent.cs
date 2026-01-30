using AIHelper.Interfaces;
using AIHelper.Models;
using Microsoft.ML.Tokenizers;
using RestSharp;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;

namespace AIHelper.Services
{
    public class AiAgent : IAiAgent
    {
        private readonly string _apiKey;
        private readonly string _model;
        private readonly string _apiUrl;
        private readonly ILogger<AiAgent> _logger;
    
        public AiAgent(string apiKey, string model, string? apiUrl = null, ILogger<AiAgent> logger = null)
        {
            _apiKey = apiKey;
            _model = model;
            _apiUrl = apiUrl;
            _logger = logger;
        }

        public async Task<AiResponse> FixCodeAsync(string code, List<RemediationTask> issues)
        {
            var stopwatch = Stopwatch.StartNew();
            _logger?.LogInformation("Starting AI code fix for {IssueCount} issues.", issues.Count);
            _logger?.LogDebug("Processing issues: {@Issues}", issues.Select(i => new { i.ScannerIssueId, i.TypeCategory, i.FilePath, i.StartLine, i.Severity }));

            // Use the injected parameters
            var aiSystemPrompt = Environment.GetEnvironmentVariable("AI_SYSTEM_PROMPT") ?? "You are a Senior Security Engineer. You will receive a source file and a list of security issues. You must fix ALL listed issues in the code. Return ONLY the full, valid source code. No markdown, no explanations.";
            var aiModel = _model;
            var aiApiKey = _apiKey; // Use the injected API key, not the hardcoded one
            
            // Use the injected API URL
            var aiApiUrl = _apiUrl;
            
            // Construct the user prompt with file content and issues
            var userPrompt = $"Fix the following {issues.Count} issues in this file. Issues:\n";
            
            foreach (var issue in issues)
            {
                userPrompt += $"[{issue.Severity}] Line {issue.StartLine}: {issue.Description}. ";
                if (!string.IsNullOrEmpty(issue.RemediationAdvice))
                {
                    userPrompt += $"Advice: {issue.RemediationAdvice}.";
                }
                userPrompt += "\n";
            }
            
            userPrompt += $"File Content: {code}";
            _logger?.LogDebug("Sending prompt to AI (first 100 chars): {PromptPreview}...", userPrompt.Substring(0, Math.Min(100, userPrompt.Length)));

            /*
                        // Count tokens in the user prompt using Microsoft.ML.Tokenizers
                        // Implementation depends on specific tokenizer - using basic character count as fallback for now
                        var tokenCount = userPrompt.Length / 4; // Approximate token count (typically 1 token ~ 4 characters)
                        _logger?.LogDebug("User prompt token count (approx): {TokenCount}", tokenCount);


                        // Get the maximum token count for the model
                        var maxTokens = await GetModelMaxTokens(client, aiApiKey, aiModel);
                        _logger?.LogDebug("Model {Model} max tokens: {MaxTokens}", aiModel, maxTokens);

                        // Validate that our prompt doesn't exceed the model's token limit
                        if (tokenCount >= maxTokens * 0.8) // Use 80% of max as safety margin
                        {
                            _logger?.LogWarning("Prompt token count ({TokenCount}) is close to or exceeds recommended limit ({RecommendedLimit}) for model {Model}", tokenCount, (int)(maxTokens * 0.8), aiModel);
                            // We could implement prompt truncation here if needed
                        }
            */
            // Create the RestSharp client for the AI API
            var client = new RestClient(aiApiUrl + "/chat/completions");
            var request = new RestRequest("", Method.Post);
            request.AddHeader("Authorization", $"Bearer {aiApiKey}");
            request.AddHeader("Content-Type", "application/json");

            // Create the request body for the chat completion API
            var requestBody = new
            {
                model = aiModel,
                messages = new[]
                {
                    new { role = "system", content = aiSystemPrompt },
                    new { role = "user", content = userPrompt }
                },                
                temperature = 0.2, // Lower temperature for more deterministic output
                stream = false,  
                top_p = 1
            };
            
            request.AddJsonBody(requestBody);
            
            var response = await client.ExecutePostAsync(request);
            if (!response.IsSuccessful)
            {
                _logger?.LogError("AI API call failed: {ErrorMessage}", response.ErrorMessage);
                throw new Exception($"AI API call failed: {response.ErrorMessage}");
            }
            
            string fixedCode = null;
            // Parse the JSON response to extract the fixed code
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
                    fixedCode = contentElement.GetString();
                }
            }
            
            if (string.IsNullOrEmpty(fixedCode))
            {
                // If the expected structure wasn't found, throw an exception
                _logger?.LogError("AI API response did not contain expected 'choices[0].message.content' structure.");
                throw new Exception("AI API response did not contain expected structure.");
            }

            stopwatch.Stop();
            _logger?.LogDebug("Received response from AI. Duration: {Duration}ms", stopwatch.ElapsedMilliseconds);
            _logger?.LogInformation("AI code fix completed. Duration: {Duration}ms", stopwatch.ElapsedMilliseconds);
            
            return new AiResponse
            {
                FixedCode = fixedCode,
                Duration = stopwatch.Elapsed
            };
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
    }
}