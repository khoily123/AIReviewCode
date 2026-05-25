namespace AIReviewerAPI.DTOs
{
    public class ReviewResponseDto
    {
        public string? Summary { get; set; }
        public string? FixedCode { get; set; }
        
        public int PerformanceScore { get; set; }
        public int SecurityScore { get; set; }
        public int MaintainabilityScore { get; set; }

        public string? MermaidChart { get; set; }
        public string? HackerExploit { get; set; }
        public string? UnitTests { get; set; }

        public string? ErrorMessage { get; set; }

        public List<BugDto>? DetectedBugs { get; set; }
        public DateTime ReviewedAt { get; set; } = DateTime.Now;
    }

    public class BugDto
    {
        public int Line { get; set; }
        public string? Description { get; set; }
        public string? Severity { get; set; }
    }
}
