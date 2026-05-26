namespace AIReviewerAPI.DTOs
{
    public class ShareRequestDto
    {
        public ReviewResponseDto ReviewData { get; set; } = new();
        public string OriginalCode { get; set; } = "";
    }
}
