using AIReviewerAPI.DTOs;
using Models;
using Repositories;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Services
{
    public class AIReviewService : IAIReviewService
    {
        // ✅ Tái sử dụng options
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly IAIReviewRepository _repository;

        public AIReviewService(IAIReviewRepository repository)
        {
            _repository = repository;
        }

        public async Task<ReviewResponseDto> ReviewCode(ReviewRequestDto request)
        {
            try
            {
                string personaInstruction = request.Persona switch
                {
                    "GordonRamsay" => "You are Gordon Ramsay, but a senior C# code reviewer. You are extremely harsh, swear a bit (in a safe, funny way), criticize bad code ruthlessly, but your technical advice is excellent. You MUST write in Vietnamese.",
                    "AnimeWaifu" => "You are a cute, supportive Anime Waifu C# code reviewer. You use cute emoticons like (´• ω •`) and call the user 'Senpai'. You are very encouraging but still point out bugs accurately. You MUST write in Vietnamese.",
                    _ => "You are a senior C# code reviewer. You are professional and helpful. You MUST write in Vietnamese."
                };

                var prompt = $@"
{personaInstruction}

Analyze the following C# code and find bugs. Also, rate the code on a scale of 0 to 100 for Performance, Security, and Maintainability.

Return ONLY valid JSON in this exact format:

{{
  ""message"": ""summary of the review"",
  ""fixedCode"": ""the complete fixed code snippet"",
  ""performanceScore"": 0,
  ""securityScore"": 0,
  ""maintainabilityScore"": 0,
  ""mermaidChart"": ""graph TD\n A-->B"",
  ""hackerExploit"": ""exploit code or explanation (if any security bug is found, else empty)"",
  ""unitTests"": ""// complete xUnit test class here"",
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
- You MUST write the 'message' and 'description' fields in Vietnamese (Tiếng Việt)
- CRITICAL: The 'fixedCode' MUST resolve all issues completely and be production-ready. You MUST preserve proper indentation and newlines (\n) within the 'fixedCode' string. Do not minify or one-line the code.
- CRITICAL: If the provided code is already perfect and has no bugs, DO NOT invent bugs. Return an empty array [] for 'bugs' and score 100 for all metrics.
- CRITICAL: 'mermaidChart' MUST contain a valid Mermaid.js graph syntax representing the code's logic flow. Do not use markdown backticks (```mermaid) inside the JSON value. Keep it as raw text.
- CRITICAL: If a security vulnerability is found (like SQL Injection), provide a specific attack payload example in 'hackerExploit'. If no security vulnerability exists, return an empty string """".
- CRITICAL: 'unitTests' MUST contain a complete, compilable xUnit test class (using Xunit; namespace) that covers all public methods. Include proper using statements. Use \n for newlines inside the JSON string value.

Code to analyze:

{request.Code}
";

                var aiResult = await _repository.CallAI(prompt);

                var modelResult = JsonSerializer.Deserialize<ReviewResponse>(aiResult, _jsonOptions)
                    ?? new ReviewResponse();

                var dtoResult = new ReviewResponseDto
                {
                    Summary = modelResult.Message,
                    FixedCode = modelResult.FixedCode,
                    PerformanceScore = modelResult.PerformanceScore,
                    SecurityScore = modelResult.SecurityScore,
                    MaintainabilityScore = modelResult.MaintainabilityScore,
                    MermaidChart = modelResult.MermaidChart,
                    HackerExploit = modelResult.HackerExploit,
                    UnitTests = modelResult.UnitTests,
                    ReviewedAt = DateTime.UtcNow,
                    DetectedBugs = modelResult.Bugs?.Select(b => new BugDto
                    {
                        Line = b.Line,
                        Description = b.Description,
                        Severity = "High"
                    }).ToList() ?? []
                };

                return dtoResult;
            }
            catch (Exception ex)
            {
                string errorMessage = ex.Message;
                if (errorMessage.Contains("TooManyRequests") || errorMessage.Contains("429"))
                {
                    errorMessage = "Hệ thống AI đang quá tải do nhiều người sử dụng. Vui lòng chờ khoảng 1 phút rồi thử lại nhé!";
                }
                else
                {
                    errorMessage = $"Lỗi từ AI (có thể do nhập code không hợp lệ): {ex.Message}";
                }

                return new ReviewResponseDto
                {
                    ErrorMessage = errorMessage,
                    ReviewedAt = DateTime.UtcNow,
                    DetectedBugs = null
                };
            }
        }

        public async Task<ChatResponseDto> ChatWithCode(ChatRequestDto request)
        {
            try
            {
                var prompt = BuildChatPrompt(request);
                var aiResult = await _repository.CallAIText(prompt);
                return new ChatResponseDto { Reply = aiResult };
            }
            catch (Exception ex)
            {
                string errorMessage = ex.Message;
                if (errorMessage.Contains("TooManyRequests") || errorMessage.Contains("429"))
                    errorMessage = "Hệ thống AI đang bận. Vui lòng đợi 1 phút và hỏi lại nhé!";
                else
                    errorMessage = $"Lỗi từ AI: {ex.Message}";

                return new ChatResponseDto { Reply = errorMessage };
            }
        }

        public async IAsyncEnumerable<string> ChatWithCodeStream(ChatRequestDto request, [EnumeratorCancellation] CancellationToken ct = default)
        {
            var prompt = BuildChatPrompt(request);

            bool streamYieldedSomething = false;
            await foreach (var chunk in _repository.CallAIStream(prompt, ct))
            {
                streamYieldedSomething = true;
                yield return chunk;
            }

            // If Gemini stream returned nothing (rate-limited / error), fall back to blocking call
            if (!streamYieldedSomething)
            {
                string fallback;
                try { fallback = await _repository.CallAIText(prompt); }
                catch (Exception ex) { fallback = $"Lỗi từ AI: {ex.Message}"; }
                yield return fallback;
            }
        }

        public async Task<TranslateResponseDto> TranslateCode(TranslateRequestDto request)
        {
            try
            {
                var prompt = $@"
You are an expert multilingual programmer. Translate the following code to {request.TargetLanguage}.

Return ONLY valid JSON in this exact format:
{{
  ""translatedCode"": ""the complete translated code"",
  ""sourceLanguage"": ""auto-detected source language name"",
  ""notes"": ""brief explanation of key translation decisions""
}}

Rules:
- Preserve exact logic and structure
- Use idiomatic {request.TargetLanguage} patterns, naming conventions, and type system
- Include all required imports / package declarations at the top
- Adapt C#-specific patterns (properties, LINQ, delegates) to {request.TargetLanguage} equivalents
- You MUST write the 'notes' field in Vietnamese (Tiếng Việt)
- CRITICAL: Preserve proper indentation and newlines (\n) inside the JSON string values
- Do not include markdown, do not include anything outside the JSON

Code to translate:
{request.Code}
";
                var aiResult = await _repository.CallAI(prompt);
                var model = JsonSerializer.Deserialize<TranslateResponse>(aiResult, _jsonOptions) ?? new TranslateResponse();

                return new TranslateResponseDto
                {
                    TranslatedCode = model.TranslatedCode,
                    SourceLanguage = model.SourceLanguage,
                    Notes = model.Notes
                };
            }
            catch (Exception ex)
            {
                var errorMessage = ex.Message.Contains("TooManyRequests") || ex.Message.Contains("429")
                    ? "Hệ thống AI đang quá tải. Vui lòng thử lại sau 1 phút!"
                    : $"Lỗi khi dịch code: {ex.Message}";

                return new TranslateResponseDto { ErrorMessage = errorMessage };
            }
        }

        private static string BuildChatPrompt(ChatRequestDto request) => $@"
You are a senior C# developer assisting another developer.
Below is the original code they submitted for review:
```csharp
{request.OriginalCode}
```

Here is the conversation history regarding this code so far:
{request.ChatHistory}

User's new message: {request.UserMessage}

Please answer the user's question directly, clearly, and concisely in Vietnamese.
";
    }
}
