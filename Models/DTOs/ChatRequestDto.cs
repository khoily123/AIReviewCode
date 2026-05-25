namespace AIReviewerAPI.DTOs
{
    public class ChatRequestDto
    {
        public string OriginalCode { get; set; } = string.Empty;
        public string ChatHistory { get; set; } = string.Empty;
        public string UserMessage { get; set; } = string.Empty;
    }
}
