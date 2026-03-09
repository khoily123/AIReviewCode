namespace AIReviewerAPI.DTOs
{
    public class ReviewResponseDto
    {
        public string? Summary { get; set; }
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
