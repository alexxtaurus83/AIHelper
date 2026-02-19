# AIHelper - AI-Powered Code Remediation Agent

An automated code remediation system that integrates with SonarQube and Fortify static analysis tools to fetch security issues, uses AI to generate fixes, and creates merge requests in GitLab.

## Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Project Structure](#project-structure)
- [Components](#components)
  - [Controllers](#controllers)
  - [Services](#services)
  - [Interfaces](#interfaces)
  - [Models](#models)
- [API Endpoints](#api-endpoints)
- [Configuration](#configuration)
- [Data Flow](#data-flow)
- [Setup](#setup)
- [Usage](#usage)
- [Limitations and Considerations](#limitations-and-considerations)

---

## Overview

AIHelper is a .NET 8 Web API that automates the remediation of static analysis issues from SonarQube and Fortify. The system:

1. Fetches security/code quality issues from SonarQube or Fortify
2. Groups issues by file path
3. Retrieves source code from GitLab
4. Uses AI to localize and fix each issue
5. Commits fixes to a feature branch
6. Creates a merge request for review

### Key Features

- **Multi-scanner support**: Works with both SonarQube and Fortify
- **Two-phase AI remediation**: Localize → Apply workflow for accurate fixes
- **GitLab integration**: Automatic branch creation, commits, and merge requests
- **Line ending preservation**: Detects and preserves original file line endings
- **Severity-based prioritization**: Processes high-severity issues first
- **Retry logic**: Built-in retry mechanism for AI calls

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              AIHelper Architecture                          │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌─────────────┐    ┌──────────────────────┐    ┌─────────────────────┐    │
│  │   Client    │───▶│  RemediationController│───▶│  RemediationService │    │
│  └─────────────┘    └──────────────────────┘    └──────────┬──────────┘    │
│                                                            │               │
│                         ┌──────────────────────────────────┼──────────┐    │
│                         │                                  │          │    │
│                         ▼                                  ▼          ▼    │
│              ┌──────────────────┐              ┌──────────────┐ ┌───────┐  │
│              │   SonarProvider  │              │ FortifyProvider│AiAgent│  │
│              │   FortifyProvider│              │   SonarProvider│       │  │
│              └────────┬─────────┘              └───────┬──────┘ └───┬───┘  │
│                       │                                │            │      │
│                       ▼                                ▼            ▼      │
│              ┌──────────────────┐              ┌──────────────┐ ┌───────┐  │
│              │   SonarQube API  │              │  Fortify API │ │ AI API│  │
│              └──────────────────┘              └──────────────┘ └───────┘  │
│                                                                             │
│              ┌──────────────────┐              ┌──────────────┐            │
│              │   GitLabProvider │◀─────────────│ Remediation  │            │
│              └────────┬─────────┘              │   Service    │            │
│                       │                        └──────────────┘            │
│                       ▼                                                    │
│              ┌──────────────────┐                                          │
│              │   GitLab API     │                                          │
│              └──────────────────┘                                          │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Project Structure

```
AIHelper/
├── Program.cs                    # Application entry point and DI configuration
├── AIHelper.csproj              # Project file with NuGet dependencies
├── system_prompt_localize.md    # AI system prompt for localization
├── system_prompt_apply.md       # AI system prompt for applying fixes
├── Controllers/
│   └── RemediationController.cs # HTTP endpoints for remediation
├── Data/
│   └── AppDbContext.cs          # Entity Framework DbContext (placeholder)
├── Interfaces/
│   ├── IAiAgent.cs              # AI agent interface
│   ├── IGitProvider.cs          # Git provider interface
│   ├── IRemediationService.cs   # Remediation service interface
│   └── IScanProvider.cs         # Scanner provider interface
├── Models/
│   ├── AiApplyResponse.cs       # AI apply response model
│   ├── AiLocalizationResponse.cs# AI localization response model
│   ├── AiResponse.cs            # Basic AI response model
│   ├── FortifyModels.cs         # Fortify API response models
│   ├── FortifyRemediationRequestDto.cs
│   ├── GitLabFileContentResponse.cs
│   ├── IssueAnchors.cs          # Code location anchors
│   ├── RemediationRequestBaseDto.cs
│   ├── RemediationRequestDto.cs
│   ├── RemediationTask.cs       # Unified issue model
│   ├── SonarCETaskModels.cs     # SonarQube CE task models
│   ├── SonarIssuesResponse.cs   # SonarQube issues response
│   ├── SonarPollingSettings.cs  # SonarQube polling configuration
│   └── SonarRemediationRequestDto.cs
└── Services/
    ├── AiAgent.cs               # AI integration service
    ├── FortifyProvider.cs       # Fortify API client
    ├── GitLabProvider.cs        # GitLab API client
    ├── RemediationService.cs    # Core remediation orchestration
    ├── ScanProviderFactory.cs   # Factory for scan providers
    └── SonarProvider.cs         # SonarQube API client
```

---

## Components

### Controllers

#### [`RemediationController`](AIHelper/Controllers/RemediationController.cs:13)

Exposes HTTP endpoints for initiating remediation workflows.

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/remediation/sonar` | POST | Start SonarQube-based remediation |
| `/api/remediation/fortify` | POST | Start Fortify-based remediation |

---

### Services

#### [`RemediationService`](AIHelper/Services/RemediationService.cs:13)

The core orchestration service that coordinates the entire remediation workflow.

**Key responsibilities:**
- Validates incoming requests
- Fetches issues from scan providers
- Creates GitLab feature branch before processing
- Processes each file's issues sequentially
- Commits fixed files immediately after processing
- Creates merge request after all files are processed

**Processing flow per file:**
1. Fetch file content from GitLab
2. Sort issues by severity (highest first) and line number
3. For each issue:
   - Call AI to localize the issue (get anchors)
   - Call AI to apply the fix using anchors
   - Update working copy of the file
4. Commit the fixed file to the feature branch

**Configuration constants:**
- `MaxAttemptsPerIssue`: 3
- `MaxTotalEditsPerFile`: 50
- `MaxNoChangeCount`: 2

---

#### [`AiAgent`](AIHelper/Services/AiAgent.cs:14)

Handles all AI interactions for code remediation using a two-phase approach.

**Methods:**

| Method | Description |
|--------|-------------|
| [`LocalizeIssueAsync()`](AIHelper/Services/AiAgent.cs:218) | Locates an issue in code and returns line anchors |
| [`ApplyIssueFixAsync()`](AIHelper/Services/AiAgent.cs:296) | Applies a fix to a localized issue |
| [`FixCodeAsync()`](AIHelper/Services/AiAgent.cs:142) | Legacy single-call fix method (not currently used) |

**Features:**
- JSON response format enforcement via `response_format: { type: "json_object" }`
- Robust JSON extraction with brace-balancing and string/escape tracking
- Retry logic (up to 4 attempts) with progressive prompt refinement
- Legacy format normalization for backward compatibility

---

#### [`SonarProvider`](AIHelper/Services/SonarProvider.cs:9)

Implements [`IScanProvider`](AIHelper/Interfaces/IScanProvider.cs:5) for SonarQube integration.

**API endpoints used:**
- `/api/issues/search` - Fetch issues for a project
- `/api/rules/show` - Get rule details for remediation advice
- `/api/ce/task` - Check computation task status
- `/api/issues/set_status` - Update issue status

**Features:**
- Task polling with configurable timeout and interval
- HTML to Markdown conversion for rule descriptions
- Effort estimation parsing (minutes/hours)
- Batch rule details fetching to minimize API calls

---

#### [`FortifyProvider`](AIHelper/Services/FortifyProvider.cs:9)

Implements [`IScanProvider`](AIHelper/Interfaces/IScanProvider.cs:5) for Fortify AMS integration.

**API endpoints used:**
- `/api/v3/releases/{id}/vulnerabilities` - List vulnerabilities
- `/api/v3/releases/{id}/vulnerabilities/{vulnId}/all-data` - Get vulnerability details
- `/api/v3/releases/{releaseId}/vulnerabilities/{vulnId}` (PUT) - Update vulnerability status

**Features:**
- Severity filtering (Critical, High, Medium, Low)
- Automatic status mapping to Fortify state IDs

---

#### [`GitLabProvider`](AIHelper/Services/GitLabProvider.cs:9)

Implements [`IGitProvider`](AIHelper/Interfaces/IGitProvider.cs:3) for GitLab integration.

**API endpoints used:**
- `/projects/{id}/repository/files/{file_path}` (GET) - Get file content
- `/projects/{id}/repository/branches` (POST) - Create branch
- `/projects/{id}/repository/commits` (POST) - Commit file
- `/projects/{id}/merge_requests` (POST) - Create merge request

**Features:**
- Base64 content decoding
- URL-encoded file paths
- Hardcoded commit author (AI-Remediator)

---

#### [`ScanProviderFactory`](AIHelper/Services/ScanProviderFactory.cs:6)

Factory class for creating scan provider instances. Currently not used by `RemediationService` (providers are injected directly).

---

### Interfaces

#### [`IScanProvider`](AIHelper/Interfaces/IScanProvider.cs:5)

```csharp
public interface IScanProvider
{
    Task<List<RemediationTask>> GetIssuesAsync(
        string projectKeyOrReleaseId,
        string? severities,
        string? impactSeverities,
        string? impactSoftwareQualities,
        string token,
        string? taskId = null,
        string? key = null,
        string? secret = null);
    Task UpdateIssueStatusAsync(string issueId, string status, string token);
}
```

#### [`IGitProvider`](AIHelper/Interfaces/IGitProvider.cs:3)

```csharp
public interface IGitProvider
{
    Task<string> GetFileContentAsync(string repoId, string filePath, string branch, string token);
    Task CreateBranchAsync(string repoId, string source, string newBranch, string token);
    Task CommitFileAsync(string repoId, string branch, string filePath, string content, string message, string token);
    Task CreateMergeRequestAsync(string repoId, string source, string target, string title, string token);
}
```

#### [`IAiAgent`](AIHelper/Interfaces/IAiAgent.cs:7)

```csharp
public interface IAiAgent
{
    Task<AiResponse> FixCodeAsync(string code, List<RemediationTask> issues, string systemPrompt);
    Task<AiLocalizationResponse> LocalizeIssueAsync(string filePath, string code, RemediationTask issue, int contextLines, string systemPrompt);
    Task<AiApplyResponse> ApplyIssueFixAsync(string filePath, string code, RemediationTask issue, IssueAnchors anchors, string systemPrompt);
}
```

#### [`IRemediationService`](AIHelper/Interfaces/IRemediationService.cs:6)

```csharp
public interface IRemediationService
{
    Task<object> RemediateAsync(RemediationRequestBaseDto request);
}
```

---

### Models

#### [`RemediationTask`](AIHelper/Models/RemediationTask.cs:1)

Unified model representing an issue from any scanner.

| Property | Type | Description |
|----------|------|-------------|
| `ScannerIssueId` | string? | Unique issue identifier |
| `TypeCategory` | string? | Issue type (Sonar) or category (Fortify) |
| `FilePath` | string? | Path to the file containing the issue |
| `Name` | string? | Issue name/title |
| `StartLine` | int | Starting line number |
| `EndLine` | int | Ending line number |
| `Severity` | string? | Issue severity level |
| `Description` | string? | Detailed description |
| `RemediationAdvice` | string? | Guidance for fixing the issue |
| `EffortEstimation` | int | Estimated effort in minutes (Sonar only) |

#### [`IssueAnchors`](AIHelper/Models/IssueAnchors.cs:9)

Represents code location anchors for issue localization.

| Property | Type | Description |
|----------|------|-------------|
| `BeforeLines` | string[] | Context lines before the issue |
| `TargetLines` | string[] | Lines containing the issue |
| `AfterLines` | string[] | Context lines after the issue |

#### [`AiLocalizationResponse`](AIHelper/Models/AiLocalizationResponse.cs:8)

Response from AI localization operation.

| Property | Type | Description |
|----------|------|-------------|
| `IssueId` | string? | Scanner issue ID |
| `Status` | string? | "localized", "not_found", or "blocked" |
| `Confidence` | double | Confidence score (0.0-1.0) |
| `Anchors` | IssueAnchors? | Located code anchors |
| `EditPlan` | string? | Description of planned fix |
| `BlockedReason` | string? | Reason if blocked |

#### [`AiApplyResponse`](AIHelper/Models/AiApplyResponse.cs:8)

Response from AI fix application.

| Property | Type | Description |
|----------|------|-------------|
| `IssueId` | string? | Scanner issue ID |
| `Status` | string? | "applied" or "blocked" |
| `UpdatedFile` | string? | Full file content after fix |
| `Patch` | string? | Optional unified diff |
| `BlockedReason` | string? | Reason if blocked |

---

## API Endpoints

### POST /api/remediation/sonar

Initiates remediation for a SonarQube project.

**Request Body ([`SonarRemediationRequestDto`](AIHelper/Models/SonarRemediationRequestDto.cs:4)):**

```json
{
  "projectKeyOrReleaseId": "my-project-key",
  "repoId": "12345",
  "sourceBranch": "feature/my-feature",
  "targetBranch": "main",
  "gitlabToken": "glpat-xxx",
  "scannerToken": "squ-xxx",
  "taskId": "optional-sonar-task-id",
  "impactSoftwareQualities": "SECURITY,RELIABILITY",
  "impactSeverities": "HIGH,BLOCKER",
  "systemPromptLocalize": "...",
  "systemPromptApply": "..."
}
```

### POST /api/remediation/fortify

Initiates remediation for a Fortify release.

**Request Body ([`FortifyRemediationRequestDto`](AIHelper/Models/FortifyRemediationRequestDto.cs:4)):**

```json
{
  "projectKeyOrReleaseId": "release-id",
  "repoId": "12345",
  "sourceBranch": "feature/my-feature",
  "targetBranch": "main",
  "gitlabToken": "glpat-xxx",
  "scannerToken": "fortify-token",
  "severities": "Critical,High",
  "systemPromptLocalize": "...",
  "systemPromptApply": "..."
}
```

### Response

Both endpoints return:

```json
{
  "filesProcessed": 5,
  "totalExecutionTimeMs": 123456
}
```

---

## Configuration

### Environment Variables

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `LOCALIZE_AI_API_URL` | Yes | - | AI API URL for localization |
| `LOCALIZE_AI_MODEL_NAME` | Yes | - | Model name for localization |
| `APPLY_AI_API_URL` | Yes | - | AI API URL for applying fixes |
| `APPLY_AI_MODEL_NAME` | Yes | - | Model name for applying fixes |
| `AI_API_KEY` | No | "your-api-key-6" | API key for AI service |
| `SONAR_API_URL` | No | "http://localhost:9000" | SonarQube server URL |
| `FORTIFY_API_URL` | No | "https://api.ams.fortify.com" | Fortify AMS API URL |
| `GITLAB_API_URL` | No | "https://gitlab.com/api/v4" | GitLab API URL |
| `MAX_FILE_SIZE_BYTES` | No | "2097152" (2MB) | Maximum file size to process |
| `SONAR_POLLING_TIMEOUT_SECONDS` | No | "300" | SonarQube task polling timeout |
| `SONAR_POLLING_INTERVAL_SECONDS` | No | "10" | SonarQube task polling interval |

### DEBUG Mode

In DEBUG mode, default values are set automatically in [`Program.cs`](AIHelper/Program.cs:23).

---

## Data Flow

```
1. Request Validation
   └── ValidateRequest() checks all required fields

2. Issue Fetching
   ├── Sonar: GetIssuesAsync() → /api/issues/search + /api/rules/show
   └── Fortify: GetIssuesAsync() → /vulnerabilities + /all-data

3. Branch Creation
   └── CreateBranchAsync() creates feature branch

4. File Processing (per file)
   ├── GetFileContentAsync() fetches source
   ├── Sort issues by severity and line
   └── For each issue:
       ├── LocalizeIssueAsync() → get anchors
       └── ApplyIssueFixAsync() → get fixed code

5. Commit
   └── CommitFileAsync() commits fixed file

6. Merge Request
   └── CreateMergeRequestAsync() creates MR
```

---

## Setup

### Prerequisites

- .NET 8 SDK
- PostgreSQL database (for future persistence)
- Access to:
  - SonarQube server or Fortify AMS
  - GitLab instance
  - AI API endpoint (OpenAI-compatible)

### Installation

```bash
# Clone the repository
git clone <repository-url>
cd AIHelper

# Restore dependencies
dotnet restore

# Set environment variables
export LOCALIZE_AI_API_URL="https://your-ai-api/v1"
export LOCALIZE_AI_MODEL_NAME="your-model"
export APPLY_AI_API_URL="https://your-ai-api/v1"
export APPLY_AI_MODEL_NAME="your-model"
export AI_API_KEY="your-api-key"

# Run the application
dotnet run
```

### Swagger UI

When running in development mode, Swagger UI is available at:
```
http://localhost:5000/swagger
```

---

## Usage

### Starting a Remediation

**Using curl:**

```bash
curl -X POST http://localhost:5000/api/remediation/sonar \
  -H "Content-Type: application/json" \
  -d '{
    "projectKeyOrReleaseId": "my-project",
    "repoId": "12345",
    "sourceBranch": "main",
    "targetBranch": "main",
    "gitlabToken": "glpat-xxx",
    "scannerToken": "squ-xxx",
    "systemPromptLocalize": "You are a code analyzer...",
    "systemPromptApply": "You are a code fixer..."
  }'
```

### System Prompts

System prompts are loaded from markdown files:
- [`system_prompt_localize.md`](AIHelper/system_prompt_localize.md) - Instructions for localization
- [`system_prompt_apply.md`](AIHelper/system_prompt_apply.md) - Instructions for applying fixes

---

## Limitations and Considerations

### Current Limitations

1. **No verification**: Fixed code is not compiled or tested before commit
2. **Sequential processing**: Files and issues are processed one at a time
3. **No caching**: Rule details are fetched on every request
4. **Fortify N+1**: Each vulnerability requires a separate API call
5. **No rollback**: If processing fails mid-way, partial commits remain

### Security Considerations

- Tokens are passed per-request (not stored)
- Source code is sent to external AI API
- Logs may contain sensitive information in DEBUG mode

### Performance Considerations

- AI calls scale as 2× issues (localize + apply)
- Large files (>2MB) are skipped
- Maximum 50 edits per file

---

## Future Improvements

- [ ] Async job processing with status endpoint
- [ ] Sonar rule details caching with TTL
- [ ] Parallel processing with rate limiting
- [ ] Build/test verification before commit
- [ ] Idempotency keys for resumability
- [ ] Fortify pagination and filtering optimization

---

## License

See [LICENSE](LICENSE) file.
