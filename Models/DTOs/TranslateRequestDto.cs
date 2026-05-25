using System.ComponentModel.DataAnnotations;

namespace AIReviewerAPI.DTOs
{
    public class TranslateRequestDto
    {
        [Required]
        public string Code { get; set; } = string.Empty;

        public string TargetLanguage { get; set; } = "Python";
    }
}
