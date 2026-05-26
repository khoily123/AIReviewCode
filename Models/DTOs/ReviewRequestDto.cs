using System.ComponentModel.DataAnnotations;

namespace AIReviewerAPI.DTOs
{
    public class ReviewRequestDto
    {
        [Required]
        public string Code { get; set; } = string.Empty;

        public string Persona { get; set; } = "Standard";

        public List<string>? CustomRules { get; set; }
    }
}
