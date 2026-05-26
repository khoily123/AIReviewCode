namespace AIReviewerAPI.DTOs
{
    public class GitHubPrRequestDto
    {
        public string PrUrl { get; set; } = "";
        public string? GithubToken { get; set; }
        public string Persona { get; set; } = "Standard";
    }
}
