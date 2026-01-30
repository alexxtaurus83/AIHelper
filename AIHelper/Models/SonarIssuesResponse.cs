namespace AIHelper.Models
{
    public record SonarIssuesResponse(List<Issue>? Issues = null);
    
    public record Issue(
        string? Key,
        string? Rule,
        string? Severity,
        string? Component,
        int? Line,
        TextRange? TextRange,
        string? Status,
        string? Message,
        string? Effort,
        string? Type
        // ,
        // string Project,
        // string Hash,
        // List<object> Flows,
        // string IssueStatus,
        // string Debt,
        // string Author,
        // List<string> Tags,
        // string Scope,
        // string CreationDate,
        // string UpdateDate,
        // bool QuickFixAvailable,
        // string CleanCodeAttribute,
        // string CleanCodeAttributeCategory,
        // List<Impact> Impacts,
        // bool PrioritizedRule,
        // bool FromSonarQubeUpdate,
        // string LinkedTicketStatus
    );

    public record TextRange(int? StartLine, int? EndLine, int? StartOffset, int? EndOffset);

    public record Impact(string? SoftwareQuality, string? Severity);

    public record SonarRuleResponse(RuleDetails? Rule);

    public record RuleDetails(
        string? Key,
        string? Repo,
        string? Name,
        string? CreatedAt,
        string? Severity,
        string? Status,
        bool? IsTemplate,
        string? Type,
        string? Scope,
        bool? IsExternal,
        List<DescriptionSection>? DescriptionSections = null,
        List<Impact>? Impacts = null
        // ,
        // List<object> Params,
        // List<string> Tags,
        // string SysTags,
        // string CleanCodeAttribute,
        // string CleanCodeAttributeCategory,
    );

    public record DescriptionSection(string? Key, string? Content);
}