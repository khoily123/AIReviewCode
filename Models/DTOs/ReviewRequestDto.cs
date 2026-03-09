using System.ComponentModel.DataAnnotations;

namespace AIReviewerAPI.DTOs
{
    public class ReviewRequestDto
    {
        [Required]
        public string Code { get; set; } = string.Empty;
    }
}
