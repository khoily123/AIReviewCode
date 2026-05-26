using System.ComponentModel.DataAnnotations;

namespace AIReviewerAPI.DTOs
{
    public class SendReportDto
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [RegularExpression(@"^\d{6}$", ErrorMessage = "Mã xác nhận phải gồm đúng 6 chữ số.")]
        public string Code { get; set; } = string.Empty;

        [Required]
        public ReviewResponseDto ReviewData { get; set; } = new();

        public string OriginalCode { get; set; } = string.Empty;
    }
}
