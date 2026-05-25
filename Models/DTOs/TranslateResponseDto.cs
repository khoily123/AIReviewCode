namespace AIReviewerAPI.DTOs
{
    public class TranslateResponseDto
    {
        public string? TranslatedCode { get; set; }
        public string? SourceLanguage { get; set; }
        public string? Notes { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
