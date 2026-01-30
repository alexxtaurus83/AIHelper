namespace AIHelper.Models
{
    public class SonarPollingSettings
    {
        public int IntervalSeconds { get; set; } = 10;
        public int TimeoutSeconds { get; set; } = 300;
    }
}