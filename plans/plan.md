# Implementation Plan: SonarQube Task Status Check

This document outlines the plan to modify the application to check the SonarQube task status before processing remediations.

## 1. Define a To-Do List

Here is the breakdown of the tasks to be performed:

- [ ] **Update `RemediationRequestDto`:** Add a `taskId` property to the request body.
- [ ] **Define SonarQube Task API Response Models:** Create C# classes to represent the JSON response from the SonarQube `api/ce/task` endpoint.
- [ ] **Modify `SonarProvider`:**
    - Create a new method `CheckTaskStatusAsync(string taskId, string token)` to call the `api/ce/task` endpoint.
    - This method will return the status of the task.
    - Update `GetIssuesAsync` to accept the `taskId`.
- [ ] **Modify `RemediationService`:**
    - Update the `RemediateProjectAsync` method signature to accept the new `RemediationRequestDto` with `taskId`.
    - Before fetching issues, call the new `SonarProvider.CheckTaskStatusAsync` method.
    - If the task status is not "SUCCESS", throw an exception or return an error response.
- [ ] **Modify `RemediationController`:**
    - Pass the `taskId` from the request to the `RemediationService`.
- [ ] **Error Handling:** Implement robust error handling for cases where the SonarQube task has not completed successfully.

## 2. API Contract Changes

### `RemediationRequestDto.cs`

The `RemediationRequestDto` class will be updated to include the `taskId`.

```csharp
public class RemediationRequestDto
{
    // ... existing properties
    
    /// <summary>
    /// Gets or sets the SonarQube task ID.
    /// </summary>
    public string? TaskId { get; set; }
}
```

## 3. New Data Models for SonarQube Task API

We need to create C# classes to deserialize the JSON response from `http://localhost:9000/api/ce/task?id={taskId}`.

### `SonarTaskResponse.cs`

```csharp
namespace AIHelper.Models
{
    public class SonarTaskResponse
    {
        public SonarTask Task { get; set; }
    }

    public class SonarTask
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public string ComponentId { get; set; }
        public string ComponentKey { get; set; }
        public string ComponentName { get; set; }
        public string ComponentQualifier { get; set; }
        public string AnalysisId { get; set; }
        public string Status { get; set; }
        public DateTime SubmittedAt { get; set; }
        public string SubmitterLogin { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime ExecutedAt { get; set; }
        public int ExecutionTimeMs { get; set; }
        public bool Logs { get; set; }
        public bool HasScannerContext { get; set; }
        public string Organization { get; set; }
        public string PullRequest { get; set; }
        public string Warning { get; set; }
        public int Warnings { get; set; }
    }
}
```

## 4. Service Layer Modifications

### `IScanProvider.cs`

The `GetIssuesAsync` method signature in the `IScanProvider` interface will be updated to include the `taskId`.

```csharp
public interface IScanProvider
{
    Task<List<RemediationTask>> GetIssuesAsync(string id, string severity, string token, string? taskId = null, string? key = null, string? secret = null);
    // ... other methods
}
```

### `SonarProvider.cs`

The `SonarProvider` will implement the logic to check the task status.

```csharp
public class SonarProvider : IScanProvider
{
    // ... existing code

    public async Task<List<RemediationTask>> GetIssuesAsync(string id, string severity, string token, string? taskId = null, string? key = null, string? secret = null)
    {
        if (!string.IsNullOrEmpty(taskId))
        {
            var taskStatus = await CheckTaskStatusAsync(taskId, token);
            if (taskStatus != "SUCCESS")
            {
                throw new Exception($"SonarQube task {taskId} did not complete successfully. Status: {taskStatus}");
            }
        }

        // ... existing GetIssuesAsync logic
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
    
    // ... existing code
}
```

### `FortifyProvider.cs`
The `GetIssuesAsync` in `FortifyProvider` will also need to be updated to match the interface, but the `taskId` will be ignored.

```csharp
public async Task<List<RemediationTask>> GetIssuesAsync(string id, string severity, string token, string? taskId = null, string? key = null, string? secret = null)
{
    // taskId is ignored for Fortify
    // ... existing implementation
}
```

### `RemediationService.cs`

The `RemediationService` will call the provider with the `taskId`.

```csharp
public class RemediationService : IRemediationService
{
    // ... existing code

    public async Task<object> RemediateProjectAsync(RemediationRequestDto request)
    {
        // ...
        var issues = await scanProvider.GetIssuesAsync(
            request.ProjectKeyOrReleaseId,
            request.MinSeverity,
            scannerToken,
            request.TaskId); // Pass the taskId here
        // ...
    }
}
```

## 5. Controller Modifications

### `RemediationController.cs`

The controller will remain largely the same, as the `RemediationRequestDto` binding will handle the new `taskId` field automatically. The `remediationRequest` object passed to `_remediationService.RemediateProjectAsync` will now contain the `taskId`.

## 6. Error Handling

When `CheckTaskStatusAsync` finds a status other than "SUCCESS", it will throw an exception. This exception will be caught by the global exception handling middleware (or propagated up to the controller), which should return a meaningful error message to the client, such as a `400 Bad Request` or `500 Internal Server Error` with a descriptive message.

## 7. Workflow Diagram

Here is a Mermaid diagram illustrating the new workflow:

```mermaid
sequenceDiagram
    participant C as RemediationController
    participant S as RemediationService
    participant F as ScanProviderFactory
    participant SP as SonarProvider
    participant SonarAPI as SonarQube API

    C->>S: RemediateProjectAsync(remediationRequest with taskId)
    S->>F: GetProvider(SONAR)
    F-->>S: returns SonarProvider instance
    S->>SP: GetIssuesAsync(..., taskId)
    SP->>SonarAPI: GET /api/ce/task?id={taskId}
    SonarAPI-->>SP: { "task": { "status": "SUCCESS" } }
    alt Task Successful
        SP->>SonarAPI: GET /api/issues/search
        SonarAPI-->>SP: Issues
        SP-->>S: returns issues
        S->>S: Continues with remediation...
    else Task Not Successful
        SP-->>S: throws Exception
        S-->>C: returns error
        C-->>Client: HTTP 500
    end