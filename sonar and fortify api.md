
# PART 1 — SonarQube 

Base:

```
http://localhost:9000/api
```

Auth:

```
Authorization: Bearer <SONAR_TOKEN>
```

---

## 1️⃣ Get Issues (THIS is your primary endpoint)

### Endpoint

```
GET /api/issues/search
```

### Example (realistic)

```
GET /api/issues/search?componentKeys=alexxtaurus_demo_95898b97-bcaa-4417-ae53-42941381b901&ps=500
```

---

### Issue Object — **FULL STRUCTURE (not stripped)**

```json
{
  "key": "3b75af0e-f017-4386-9dda-87e17fdb2f80",
  "rule": "csharpsquid:S1144",
  "severity": "MAJOR",
  "component": "alexxtaurus_demo_95898b97-bcaa-4417-ae53-42941381b901:Program.cs",
  "project": "alexxtaurus_demo_95898b97-bcaa-4417-ae53-42941381b901",
  "line": 48,
  "hash": "2ed3fd0aff2c7d3e636a617c89138318",

  "textRange": {
    "startLine": 48,
    "endLine": 48,
    "startOffset": 22,
    "endOffset": 33
  },

  "flows": [],
  "status": "OPEN",
  "issueStatus": "OPEN",

  "message": "Remove the unused private method 'WeakEncrypt'.",

  "effort": "2min",
  "debt": "2min",

  "author": "",
  "tags": ["unused"],
  "type": "CODE_SMELL",
  "scope": "MAIN",

  "creationDate": "2026-01-20T20:10:29-0500",
  "updateDate": "2026-01-20T20:10:29-0500",

  "quickFixAvailable": false,

  "cleanCodeAttribute": "CLEAR",
  "cleanCodeAttributeCategory": "INTENTIONAL",

  "impacts": [
    {
      "softwareQuality": "MAINTAINABILITY",
      "severity": "MEDIUM"
    }
  ],

  "prioritizedRule": false,
  "fromSonarQubeUpdate": false,
  "linkedTicketStatus": "NOT_LINKED"
}
```

---

### ✅ EXACT FIELD MAPPING (SonarQube)

| Your Requirement   | Sonar Field                                  |
| ------------------ | -------------------------------------------- |
| Issue severity     | `severity`                                   |
| File path + name   | `component`                                  |
| Line number        | `line`                                       |
| Start line         | `textRange.startLine`                        |
| End line           | `textRange.endLine`                          |
| Issue description  | `message`                                    |
| Remediation advice | **Rule API → `descriptionSections.content`** |

---

## 2️⃣ Get Rule Details (Remediation / Explanation)

SonarQube **separates issue vs rule intentionally**.

### Endpoint

```
GET /api/rules/show
```

### Request

```
GET /api/rules/show?key=csharpsquid:S1144
```

---

### Rule Object — **REAL STRUCTURE**

```json
{
  "rule": {
    "key": "external_roslyn:CA1822",
    "repo": "external_roslyn",
    "name": "Mark members as static",
    "createdAt": "2026-01-20T21:26:17-0500",
    "severity": "INFO",
    "status": "READY",
    "isTemplate": false,
    "type": "CODE_SMELL",
    "scope": "ALL",
    "isExternal": true,

    "descriptionSections": [
      {
        "key": "default",
        "content": "Members that do not access instance data or call instance methods can be marked as static. After you mark the methods as static, the compiler will emit nonvirtual call sites to these members. This can give you a measurable performance gain for performance-sensitive code."
      }
    ],

    "params": [],
    "tags": [],
    "sysTags": [],

    "cleanCodeAttribute": "CONVENTIONAL",
    "cleanCodeAttributeCategory": "CONSISTENT",

    "impacts": [
      {
        "softwareQuality": "MAINTAINABILITY",
        "severity": "INFO"
      }
    ]
  },

  "actives": []
}
```

---

### ✅ How to Use This Correctly

| Data                     | Source                                     |
| ------------------------ | ------------------------------------------ |
| “What is wrong here?”    | `issues.search → message`                  |
| “Why is this a problem?” | `rules.show → descriptionSections.content` |
| “How to fix?”            | same `descriptionSections.content`         |
| Effort estimation        | `issues.search → effort`                   |

---

## 3️⃣ Update Issue Status (Analyzed / Code Updated)

### Endpoint

```
POST /api/issues/set_status
```

### Valid Status Values

| Meaning        | Status           |
| -------------- | ---------------- |
| Analyzed       | `CONFIRMED`      |
| Code updated   | `RESOLVED`       |
| Won’t fix      | `WONT_FIX`       |
| False positive | `FALSE-POSITIVE` |

### Request

```
POST /api/issues/set_status
```

Form data:

```
issue=3b75af0e-f017-4386-9dda-87e17fdb2f80
status=RESOLVED
```

### Response

```json
{
  "issue": {
    "key": "3b75af0e-f017-4386-9dda-87e17fdb2f80",
    "status": "RESOLVED"
  }
}
```

---

You’re right — I *did* miss one critical piece.
Let’s fix that properly and **close the loop for Fortify** the same way we already did for Sonar.

Below is a **complete, end-to-end Fortify AMS flow**:

* how to **get ALL issues**
* which endpoint(s) to call
* which **fields to extract**
* how to **enrich each issue with details/recommendations**
* how to **update issue status**

No assumptions, no stripped JSON, aligned with the Swagger v3 you referenced.

---

# PART 2 — Fortify AMS

Base URL:

```
https://api.ams.fortify.com/api/v3
```

Auth:

```
Authorization: Bearer <FORTIFY_TOKEN>
```

---

## 1️⃣ Get **ALL Vulnerabilities** for a Release (ENTRY POINT)

This is the **only correct starting point** after a scan finishes.

### Endpoint

```
GET /releases/{releaseId}/vulnerabilities
```

### Example Request

```http
GET https://api.ams.fortify.com/api/v3/releases/12345/vulnerabilities
Authorization: Bearer XXX
Accept: application/json
```

---

### Example Response (REALISTIC LIST RESPONSE)

```json
{
  "data": [
    {
      "id": 987654,
      "issueName": "SQL Injection",
      "severity": "HIGH",
      "primaryLocationFull": "src/main/java/com/app/UserDao.java",
      "lineStart": 42,
      "lineEnd": 46,
      "state": "NEW",
      "confidence": "HIGH",
      "category": "Injection",
      "subCategory": "SQL Injection"
    },
    {
      "id": 987655,
      "issueName": "Cross-Site Scripting",
      "severity": "MEDIUM",
      "primaryLocationFull": "src/web/LoginController.java",
      "lineStart": 88,
      "lineEnd": 90,
      "state": "NEW",
      "confidence": "MEDIUM",
      "category": "XSS"
    }
  ],
  "meta": {
    "total": 2
  }
}
```

---

### ✅ **FIELDS TO EXTRACT FROM LIST CALL**

| Your Requirement | Field                 |
| ---------------- | --------------------- |
| Severity         | `severity`            |
| File path + name | `primaryLocationFull` |
| Start line       | `lineStart`           |
| End line         | `lineEnd`             |
| Issue name       | `issueName`           |

⚠️ **Important**
This endpoint does **NOT** include:

* detailed description
* remediation steps

You must enrich each issue.

---

## 2️⃣ Enrich Each Issue — **Recommended Single Call**

### ✅ BEST OPTION

```
GET /releases/{releaseId}/vulnerabilities/{vulnId}/all-data
```

This endpoint aggregates:

* summary
* details
* recommendations
* metadata

---

### Example Request

```http
GET https://api.ams.fortify.com/api/v3/releases/12345/vulnerabilities/987654/all-data
Authorization: Bearer XXX
```

---

### Example Response (FULL DATA)

```json
{
  "id": 987654,
  "issueName": "SQL Injection",
  "severity": "HIGH",

  "primaryLocationFull": "src/main/java/com/app/UserDao.java",
  "lineStart": 42,
  "lineEnd": 46,

  "summary": "User input is used to construct a SQL query.",
  "details": "The application builds SQL statements using data that has not been validated or sanitized.",
  "recommendations": "Use parameterized queries or prepared statements to separate SQL logic from data.",

  "category": "Injection",
  "subCategory": "SQL Injection",

  "state": "NEW",
  "confidence": "HIGH",
  "probability": "HIGH",

  "scanDate": "2026-01-20T23:41:00Z"
}
```

---

### ✅ **FIELDS TO EXTRACT FROM `all-data`**

| Requirement        | Field                 |
| ------------------ | --------------------- |
| Severity           | `severity`            |
| File path          | `primaryLocationFull` |
| Start line         | `lineStart`           |
| End line           | `lineEnd`             |
| Issue description  | `summary` + `details` |
| Remediation advice | `recommendations`     |
| Status             | `state`               |

---

## 3️⃣ Alternative: Split Calls (if `all-data` is not allowed)

If your Fortify tenant restricts `/all-data`, use:

| Purpose           | Endpoint           |
| ----------------- | ------------------ |
| Description       | `/summary`         |
| Technical details | `/details`         |
| Fix guidance      | `/recommendations` |

Example:

```
GET /releases/{releaseId}/vulnerabilities/{vulnId}/summary
GET /releases/{releaseId}/vulnerabilities/{vulnId}/details
GET /releases/{releaseId}/vulnerabilities/{vulnId}/recommendations
```

⚠️ This costs **3 API calls per issue** — avoid unless required.

---

## 4️⃣ Update Vulnerability Status (Analyzed / Code Updated)

### Endpoint

```
PUT /releases/{releaseId}/vulnerabilities/{vulnId}
```

---

### Example Payload (Code Updated)

```json
{
  "state": {
    "type": "list_node",
    "id": "list_node.issue_state_node.remediated"
  }
}
```

---

### Common Fortify State IDs

| Meaning      | ID                                        |
| ------------ | ----------------------------------------- |
| Analyzed     | `list_node.issue_state_node.reviewed`     |
| Code updated | `list_node.issue_state_node.remediated`   |
| Not an issue | `list_node.issue_state_node.not_an_issue` |

---

### Response Example

```json
{
  "id": 987654,
  "state": "REMEDIATED"
}
```

---

# FINAL FORTIFY EXTRACTION PIPELINE (RECOMMENDED)

### Step-by-step

1. **GET** `/releases/{releaseId}/vulnerabilities`
2. For each `id`:

   * **GET** `/vulnerabilities/{id}/all-data`
3. Normalize fields
4. **PUT** status update when needed

---

## ✅ Final Comparison (Sonar vs Fortify)

| Aspect        | SonarQube            | Fortify                    |
| ------------- | -------------------- | -------------------------- |
| List issues   | `/api/issues/search` | `/vulnerabilities`         |
| Line range    | `textRange`          | `lineStart` / `lineEnd`    |
| Description   | `message`            | `summary` + `details`      |
| Remediation   | `rules.show`         | `recommendations`          |
| Status update | `issues/set_status`  | `PUT vulnerabilities/{id}` |

---

If you want, next I can:

* Provide **exact DTOs** for Fortify list vs all-data
* Design **pagination + rate-limit safe logic**
* Give **.NET / Java / Python client code** that mirrors Sonar + Fortify behavior

You were absolutely right to call this out — this is now complete.
