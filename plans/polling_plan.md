# Implementation Plan: SonarQube Task Polling

This document outlines the plan to implement a polling mechanism for checking the SonarQube task status.

## 1. Configuration

We will introduce new settings in `appsettings.json` for the polling interval and timeout. A corresponding C# class will be created to model these settings.

### `appsettings.json`

```json
{
  "SonarPollingSettings": {
    "IntervalSeconds": 10,
    "TimeoutSeconds": 300
  }
}
```

### `SonarPollingSettings.cs`

A new file will be created at `AIHelper/Models/SonarPollingSettings.cs`:

```csharp
namespace AIHelper.Models
{
    public class SonarPollingSettings
    {
        public int IntervalSeconds { get; set; } = 10;
        public int TimeoutSeconds { get; set; } = 300;
    }
}
```

## 2. `Program.cs` Updates

The `Program.cs` file will be updated to read the new configuration and inject it into the `ScanProviderFactory`.

```csharp
// In Program.cs

// ... existing code

// Read Sonar polling settings from configuration
var sonarPollingSettings = builder.Configuration.GetSection("SonarPollingSettings").Get<SonarPollingSettings>() ?? new SonarPollingSettings();
builder.Services.AddSingleton(sonarPollingSettings);

builder.Services.AddSingleton<ScanProviderFactory>(provider => 
    new ScanProviderFactory(
        sonarApiUrl, 
        fortifyApiUrl,
        sonarPollingSettings, // Pass the settings here
        provider.GetService<ILogger<ScanProviderFactory>>(), 
        provider.GetService<ILogger<SonarProvider>>(), 
        provider.GetService<ILogger<FortifyProvider>>()
    )
);

// ... existing code
```

## 3. `ScanProviderFactory` and `SonarProvider` Modifications

### `ScanProviderFactory.cs`

The factory will be updated to accept the polling settings and pass them to the `SonarProvider`.

```csharp
// In ScanProviderFactory.cs
private readonly SonarPollingSettings _sonarPollingSettings;

public ScanProviderFactory(string? sonarApiUrl, string? fortifyApiUrl, SonarPollingSettings sonarPollingSettings, ILogger<ScanProviderFactory> logger, ILogger<SonarProvider> sonarLogger, ILogger<FortifyProvider> fortifyLogger)
{
    // ...
    _sonarPollingSettings = sonarPollingSettings;
    // ...
}

public IScanProvider GetProvider(ScannerType scannerType)
{
    switch (scannerType)
    {
        case ScannerType.SONAR:
            return new SonarProvider(_sonarApiUrl, _sonarPollingSettings, _sonarLogger); // Pass settings to SonarProvider
        // ...
    }
}
```

### `SonarProvider.cs`

The `SonarProvider` will be updated to accept the settings and implement the polling logic.

```csharp
// In SonarProvider.cs
private readonly SonarPollingSettings _pollingSettings;

public SonarProvider(string? apiUrl, SonarPollingSettings pollingSettings, ILogger<SonarProvider> logger)
{
    _apiUrl = apiUrl ?? "http://localhost:9000";
    _pollingSettings = pollingSettings;
    _logger = logger;
}

public async Task<List<RemediationTask>> GetIssuesAsync(string id, string severity, string token, string? taskId = null, string? key = null, string? secret = null)
{
    if (!string.IsNullOrEmpty(taskId))
    {
        await PollTaskStatusAsync(taskId, token);
    }

    // ... existing GetIssuesAsync logic
}

private async Task PollTaskStatusAsync(string taskId, string token)
{
    var timeout = TimeSpan.FromSeconds(_pollingSettings.TimeoutSeconds);
    var interval = TimeSpan.FromSeconds(_pollingSettings.IntervalSeconds);
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
```

## 4. Workflow Diagram

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
    
    loop Polling
        SP->>SonarAPI: GET /api/ce/task?id={taskId}
        SonarAPI-->>SP: { "task": { "status": "IN_PROGRESS" } }
        SP->>SP: Wait for interval
    end

    SP->>SonarAPI: GET /api/ce/task?id={taskId}
    SonarAPI-->>SP: { "task": { "status": "SUCCESS" } }

    alt Task Successful
        SP->>SonarAPI: GET /api/issues/search
        SonarAPI-->>SP: Issues
        SP-->>S: returns issues
        S->>S: Continues with remediation...
    else Task Failed or Timed Out
        SP-->>S: throws Exception
        S-->>C: returns error
        C-->>Client: HTTP 500
    end