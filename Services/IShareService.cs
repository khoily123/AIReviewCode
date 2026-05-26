using AIReviewerAPI.DTOs;

namespace Services
{
    public interface IShareService
    {
        string SaveReview(ReviewResponseDto data, string originalCode);
        (ReviewResponseDto? Data, string? OriginalCode, DateTime CreatedAt)? GetReview(string id);
    }
}
