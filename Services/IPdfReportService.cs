using AIReviewerAPI.DTOs;

namespace Services
{
    public interface IPdfReportService
    {
        byte[] GenerateReviewPdf(ReviewResponseDto data, string originalCode);
    }
}
