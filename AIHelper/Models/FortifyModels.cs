namespace AIHelper.Models
{
    // Record/Class models for Fortify deserialization
    public class VulnerabilityListResponse
    {
        public List<VulnerabilitySummary>? Data { get; set; } = [];
        public Meta? Meta { get; set; }
    }
    
    public class VulnerabilitySummary
    {
        public long Id { get; set; }
        public string? IssueName { get; set; }
        public string? Severity { get; set; }
        public string? PrimaryLocationFull { get; set; }
        public int LineStart { get; set; }
        public int LineEnd { get; set; }
        // public string State { get; set; }
        // public string Confidence { get; set; }
        public string? Category { get; set; }
        public string? SubCategory { get; set; }
    }
    
    public class Meta
    {
        public int Total { get; set; }
    }
    
    public class VulnerabilityDetailResponse
    {
        public long Id { get; set; }
        public string? IssueName { get; set; }
        public string? Severity { get; set; }
        public string? PrimaryLocationFull { get; set; }
        public int LineStart { get; set; }
        public int LineEnd { get; set; }
        public string? Summary { get; set; }
        public string? Details { get; set; }
        public string? Recommendations { get; set; }
        public string? Category { get; set; }
        public string? SubCategory { get; set; }
        // public string State { get; set; }
        // public string Confidence { get; set; }
        // public string Probability { get; set; }
        // public string ScanDate { get; set; }
    }
}