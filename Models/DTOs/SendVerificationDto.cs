using System.ComponentModel.DataAnnotations;

namespace AIReviewerAPI.DTOs
{
    public class SendVerificationDto
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
    }
}
