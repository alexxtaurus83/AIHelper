# Design Document: AI-Powered Code Remediation Agent (AutoRemediator)

## 1. Project Overview

Build a .NET 8 Web API (`AutoRemediator`) that automates the fixing of static analysis issues (SonarQube and Fortify). The API accepts scan details and credentials, fetches issues, retrieves source code via GitLab API, uses an AI Agent to generate fixes, and commits the changes back to GitLab via a Merge Request.

## 2. Technical Stack

* **Framework:** .NET 8 Web API
* **Protocol:** REST (Swagger enabled, HTTP only/No HTTPS)
* **Logging:** Serilog (Console + File)
* **Database:** Entity Framework Core with PostgreSQL (Code First) - *NuGets only, logic implementation skipped for Phase 1.*
* **HTTP Client:** RestSharp
* **API Standards:** Microsoft.AspNetCore.OpenApi
* **AI:** OpenAI

* **External APIs:**

* AI: `https://agentrouter.org/v1` (Model: `glm-4.6`)
* SonarQube: `http://localhost:9000/web_api`
* Fortify: `https://api.ams.fortify.com/swagger/ui/index#/`
* GitLab: `https://gitlab.com/api/v4`



## 3. Architecture & Data Flow

1. **Request:** User sends POST request with Project keys, Repository info, and **all authentication tokens** (Stateless).
2. **Analyze:** `IScanProvider` fetches issues from Sonar or Fortify.
3. **Group:** Issues are grouped by **File Path** to handle "shifting lines" correctly.
4. **Fetch:** `IGitProvider` downloads the raw file content via GitLab API.
5. **Remediate:** `IAiAgent` sends the file + list of issues to the LLM. LLM returns the *full* fixed file.
6. **Commit:** `IGitProvider` pushes the new file to a feature branch.
7. **Finalize:** After all files are processed, `IGitProvider` creates a Merge Request.

## 4. Implementation Steps for the AI Agent

### Step 1: Project Setup & NuGets

* Use existing empty .NET 8 Web API project.
* Install required NuGet packages:
* `Serilog.AspNetCore`, `Serilog.Sinks.Console`, `Serilog.Sinks.File`
* `RestSharp`
* `Npgsql.EntityFrameworkCore.PostgreSQL`
* `Microsoft.EntityFrameworkCore.Design`
* `Swashbuckle.AspNetCore`



* Configure **Serilog** in `Program.cs` 
* Levels: `Debug` and `Information`.
* Sinks: Console and File (`logs/log-.txt`).

### Step 2: Configuration & Environment

* Do not Use `appsettings.json` for static config (URLs, Model Names). Use environment varibales. Like 
  `AI_SYSTEM_PROMPT`: "You are a Senior Security Engineer. You will receive a source file and a list of security issues. You must fix ALL listed issues in the code. Return ONLY the full, valid source code. No markdown, no explanations."
 `AI_API_URL`, `AI_MODEL`.
* Add `#if DEBUG` block in `Program.cs` section, create default values and variables
* **Important:** Tokens are **NOT** stored; they are passed in the API Request.

### Step 3: Domain Models (Unified)

Create a `RemediationTask` class to normalize data from Sonar and Fortify.

```csharp
public class RemediationTask
{
    public string ScannerIssueId { get; set; }
    public string TypeCategory { get; set; } //  Issue type (sonar) \ category (frotify)
    public string FilePath { get; set; } // path + name
    public string Name { get; set; }
    public int StartLine { get; set; } 
    public int EndLine { get; set; } 
    public string Severity { get; set; }
    public string Description { get; set; }
    public string RemediationAdvice { get; set; }    
    public int EffortEstimation { get; set; } // only for sonar		 
}

```

### Step 4: API Request/Response DTOs

Create `RemediationRequestDto` in the `Controllers` or `Models` folder.

```csharp
public class RemediationRequestDto
{
    // Scan Details
    public ENUM ScanType { get; set; } // "SONAR" or "FORTIFY"
    public string ProjectKeyOrReleaseId { get; set; }
    public string MinSeverity { get; set; } // e.g., "High"
    
    // Git Details
    public string RepoId { get; set; } // Project ID in GitLab
    public string SourceBranch { get; set; }
    public string TargetBranch { get; set; } // Branch to create MR into
    public string NewBranchName { get; set; } // Branch to create for fixes
    
    // Credentials (Passed per request)
    public string GitlabToken { get; set; }
    public string ScannerToken { get; set; } 
    public string FortifyToken { get; set; } 
    
}

```

### Step 5: Interfaces (The Core Abstractions)

Create these interfaces to decouple logic:

1. **`IScanProvider`**
* `Task<List<RemediationTask>> GetIssuesAsync(string id, string severity, string token, string key = null, string secret = null);`
* `Task UpdateIssueStatusAsync(string issueId, string status, string token);`


2. **`IGitProvider`**
* `Task<string> GetFileContentAsync(string repoId, string filePath, string branch, string token);`
* `Task CreateBranchAsync(string repoId, string source, string newBranch, string token);`
* `Task CommitFileAsync(string repoId, string branch, string filePath, string content, string message, string token);`
* `Task CreateMergeRequestAsync(string repoId, string source, string target, string title, string token);`


3. **`IAiAgent`**
* `Task<AiResponse> FixCodeAsync(string code, List<RemediationTask> issues);`
* *Note:* `AiResponse` should contain the fixed code and the `TimeSpan` duration.



### Step 6: Implement `IScanProvider` (Sonar & Fortify) (read details from file 'sonar and fortify api.md')

* **SonarProvider:** Use RestSharp to query `/api/issues/search`. Map response to `RemediationTask`. 
* **FortifyProvider:** Use RestSharp to query `/api/v1/projectVersions/{id}/issues`. Map response.
* **Factory:** Create a simple factory or strategy to pick the right provider based on the Enum/String.

### Step 7: Implement `IGitProvider` (GitLab)

* Use GitLab REST API v4.
* **Do not use git CLI.**
* Implement `GetFile` (GET `/projects/:id/repository/files/:file_path`)
 - ref (Name of branch, tag, or commit)
 RESPONSE:
  ```json
 {
  "file_name": "key.rb",
  "file_path": "app/models/key.rb",
  "size": 1476,
  "encoding": "base64",
  "content": "IyA9PSBTY2hlbWEgSW5mb3...",
  "content_sha256": "4c294617b60715c1d218e61164a3abd4808a4284cbc30e6728a01ad9aada4481",
  "ref": "main",
  "blob_id": "79f7bbd25901e8334750839545a9bd021f0e4c83",
  "commit_id": "d5a3ff139356ce33e37e73add446f16869741b50",
  "last_commit_id": "570e7b2abdd848b95f2f578043fc23bd6f6fd24d",
  "execute_filemode": false
}
 ```

* Implement `Commit` (POST `/projects/:id/repository/commits`).
 - branch
 - commit_message
 - author_email
 - author_name
 - action (The action to perform: create, delete, move, update, or chmod)
 - file_path
 - content (File content, required for all except delete, chmod, and move)
POST example:
 ```json
{
  "branch": "main",
  "commit_message": "some commit message",
  "actions": [
    {
      "action": "create",
      "file_path": "foo/bar",
      "content": "some content"
    },
    {
      "action": "delete",
      "file_path": "foo/bar2"
    },
    {
      "action": "move",
      "file_path": "foo/bar3",
      "previous_path": "foo/bar4",
      "content": "some content"
    },
    {
      "action": "update",
      "file_path": "foo/bar5",
      "content": "new content"
    },
    {
      "action": "chmod",
      "file_path": "foo/bar5",
      "execute_filemode": true
    }
  ]
}
 ```
 RESPONSE: 
  ```json
{
  "id": "ed899a2f4b50b4370feeea94676502b42383c746",
  "short_id": "ed899a2f4b5",
  "title": "some commit message",
  "author_name": "Example User",
  "author_email": "user@example.com",
  "committer_name": "Example User",
  "committer_email": "user@example.com",
  "created_at": "2016-09-20T09:26:24.000-07:00",
  "message": "some commit message",
  "parent_ids": [
    "ae1d9fb46aa2b07ee9836d49862ec4e2c46fbbba"
  ],
  "committed_date": "2016-09-20T09:26:24.000-07:00",
  "authored_date": "2016-09-20T09:26:24.000-07:00",
  "stats": {
    "additions": 2,
    "deletions": 2,
    "total": 4
  },
  "status": null,
  "web_url": "https://gitlab.example.com/janedoe/gitlab-foss/-/commit/ed899a2f4b50b4370feeea94676502b42383c746"
}
 ```

* Implement `Merge` (POST `/projects/:id/merge_requests`).
 - source_branch
 - target_branch
 - title
 - description (Limited to 1,048,576 characters.)
 RESPONSE :
 ```json
 {
  "id": 1,
  "iid": 1,
  "project_id": 3,
  "title": "test1",
  "description": "fixed login page css paddings",
  "state": "merged",
  "imported": false,
  "imported_from": "none",
  "created_at": "2017-04-29T08:46:00Z",
  "updated_at": "2017-04-29T08:46:00Z",
  "target_branch": "main",
  "source_branch": "test1",
  "upvotes": 0,
  "downvotes": 0,
  "author": {
    "id": 1,
    "name": "Administrator",
    "username": "admin",
    "state": "active",
    "avatar_url": null,
    "web_url" : "https://gitlab.example.com/admin"
  },
  "assignee": {
    "id": 1,
    "name": "Administrator",
    "username": "admin",
    "state": "active",
    "avatar_url": null,
    "web_url" : "https://gitlab.example.com/admin"
  },
  "source_project_id": 2,
  "target_project_id": 3,
  "labels": [
    "Community contribution",
    "Manage"
  ],
  "draft": false,
  "work_in_progress": false,
  "milestone": {
    "id": 5,
    "iid": 1,
    "project_id": 3,
    "title": "v2.0",
    "description": "Assumenda aut placeat expedita exercitationem labore sunt enim earum.",
    "state": "closed",
    "created_at": "2015-02-02T19:49:26.013Z",
    "updated_at": "2015-02-02T19:49:26.013Z",
    "due_date": "2018-09-22",
    "start_date": "2018-08-08",
    "web_url": "https://gitlab.example.com/my-group/my-project/milestones/1"
  },
  "merge_when_pipeline_succeeds": true,
  "merge_status": "can_be_merged",
  "detailed_merge_status": "not_open",
  "merge_error": null,
  "sha": "8888888888888888888888888888888888888888",
  "merge_commit_sha": null,
  "squash_commit_sha": null,
  "user_notes_count": 1,
  "discussion_locked": null,
  "should_remove_source_branch": true,
  "force_remove_source_branch": false,
  "allow_collaboration": false,
  "allow_maintainer_to_push": false,
  "web_url": "http://gitlab.example.com/my-group/my-project/merge_requests/1",
  "references": {
    "short": "!1",
    "relative": "!1",
    "full": "my-group/my-project!1"
  },
  "time_stats": {
    "time_estimate": 0,
    "total_time_spent": 0,
    "human_time_estimate": null,
    "human_total_time_spent": null
  },
  "squash": false,
  "subscribed": false,
  "changes_count": "1", 
  "merge_user": {
    "id": 87854,
    "name": "Douwe Maan",
    "username": "DouweM",
    "state": "active",
    "avatar_url": "https://gitlab.example.com/uploads/-/system/user/avatar/87854/avatar.png",
    "web_url": "https://gitlab.com/DouweM"
  },
  "merged_at": "2018-09-07T11:16:17.520Z",
  "merge_after": "2018-09-07T11:16:00.000Z",
  "prepared_at": "2018-09-04T11:16:17.520Z",
  "closed_by": null,
  "closed_at": null,
  "latest_build_started_at": "2018-09-07T07:27:38.472Z",
  "latest_build_finished_at": "2018-09-07T08:07:06.012Z",
  "first_deployed_to_production_at": null,
  "pipeline": {
    "id": 29626725,
    "sha": "2be7ddb704c7b6b83732fdd5b9f09d5a397b5f8f",
    "ref": "patch-28",
    "status": "success",
    "web_url": "https://gitlab.example.com/my-group/my-project/pipelines/29626725"
  },
  "diff_refs": {
    "base_sha": "c380d3acebd181f13629a25d2e2acca46ffe1e00",
    "head_sha": "2be7ddb704c7b6b83732fdd5b9f09d5a397b5f8f",
    "start_sha": "c380d3acebd181f13629a25d2e2acca46ffe1e00"
  },
  "diverged_commits_count": 2,
  "task_completion_status":{
    "count":0,
    "completed_count":0
  }
}
```

### Step 8: Implement `IAiAgent`

* **Client:** Use * OpenAI connectimg to `https://agentrouter.org/v1`.
* **Timer:** Use `System.Diagnostics.Stopwatch` to record processing time.
* **Prompts:** Genearted by 
* **System Prompt:** from variable
* **User Prompt:** "Fix the following {n} issues in this file. Issues:
[Critical] Line 45: SQL Injection vulnerability. Advice: Use Parameterized queries.
[Major] Line 102: Unused variable.
File Content: {code}
Construct a string containing the **Source Code** + a list of **Issues** (Line # + Description + Advice)."
Create it based on `RemediationTask`

### Step 9: The Controller (`RemediationController`)

Create `POST /api/remediate`. Logic flow:

1. **Log Start:** "Starting remediation for {Project}..."
2. **Fetch Issues:** Call `IScanProvider.GetIssuesAsync`.
3. **Filter:** Filter out issues below `MinSeverity`.
4. **Grouping:** `var fileGroups = issues.GroupBy(x => x.FilePath);`
5. **Git Setup:** Call `IGitProvider.CreateBranchAsync`.
6. **Loop Groups:**
* Fetch file content (`IGitProvider`).
* Send to AI (`IAiAgent`).
* Receive fixed code.
* Commit file (`IGitProvider`).
* (Optional) Update Issue Status (`IScanProvider`).


7. **Finalize:** Call `IGitProvider.CreateMergeRequestAsync`.
8. **Return:** JSON summary (Files processed, Execution Time).

### Step 10: Database (EF Core)

* Create a `AppDbContext` inheriting from `DbContext`.
* Add it to `Program.cs` services.
* **STOP:** Do not create migrations or tables yet. Just have the plumbing ready.

## 5. Misc
* **Testing:** Do not forget to create all classes required for app.
* **Testing:** No unit tests are defined yet.
* **Large Files:** Check if file size exceeds 2MB (add asenv  variable). Do not pass to AI. Log error.

## 6. Execution Strategy Clarifications

*   **GitLab Commit Author:** For the Proof of Concept, the `author_name` and `author_email` required for GitLab commits will be hardcoded with placeholder values. They will not be passed in the request.
*   **Error Handling (File Commit):** If a file commit fails during the remediation process, the operation will stop immediately. The feature branch will be left in its partially-committed state for manual inspection and will not be automatically roll back.
*   **Large File Handling:** Files exceeding the configured size limit (e.g., 2MB, managed via an environment variable) will be skipped. An error will be logged, and the process will continue to the next file.
*   **Merge Request Description:** The description for the final merge request will be a simple, static summary (e.g., "Automated remediation by AI Agent."). It will not contain a dynamic list of changed files or fixed issues for this phase.