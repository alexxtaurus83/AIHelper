namespace AIHelper.Models
{
    public class SonarTaskResponse
    {
        public SonarTask Task { get; set; }
    }

    public class SonarTask
    {
        public string Status { get; set; }
        /*
        public string Id { get; set; }
        public string Type { get; set; }
        public string ComponentId { get; set; }
        public string ComponentKey { get; set; }
        public string ComponentName { get; set; }
        public string ComponentQualifier { get; set; }
        public string AnalysisId { get; set; }
        
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
        */
    }
}