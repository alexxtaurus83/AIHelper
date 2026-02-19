namespace AIHelper.Models
{
    public class AiResponse
    {
        public string? FixedCode { get; set; }
        public string? OriginalCode { get; set; }
        public TimeSpan Duration { get; set; }
    }
}