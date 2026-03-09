using AIReviewerAPI.DTOs;
using Models;
using Repositories;
using System.Text.Json;

namespace Services
{
    public class AIReviewService : IAIReviewService
    {
        private readonly IAIReviewRepository _repository;

        public AIReviewService(IAIReviewRepository repository)
        {
            _repository = repository;
        }

        public async Task<ReviewResponseDto> ReviewCode(string code)
        {
            var prompt = $@"
You are a senior C# code reviewer.

Analyze the following C# code and find bugs.

Return ONLY valid JSON in this exact format:

{{
  ""message"": ""summary of the review"",
  ""bugs"": [
    {{
      ""line"": 0,
      ""description"": ""bug description""
    }}
  ]
}}

Rules:
- Do not include markdown
- Do not include explanations outside JSON
- Always return JSON

Code to analyze:

{code}
";

            var aiResult = await _repository.CallAI(prompt);

            var modelResult = JsonSerializer.Deserialize<ReviewResponse>(
                 aiResult,
                 new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
             ) ?? new ReviewResponse();

            var dtoResult = new ReviewResponseDto
            {
                Summary = modelResult.Message,
                ReviewedAt = DateTime.Now,
                DetectedBugs = modelResult.Bugs?.Select(b => new BugDto
                {
                    Line = b.Line,
                    Description = b.Description,
                    Severity = "High" // Bạn có thể thêm logic phân loại dựa trên Description
                }).ToList() ?? new List<BugDto>()
            };

            return dtoResult;
        }
    }
}
