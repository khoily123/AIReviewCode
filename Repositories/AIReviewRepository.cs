using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace Repositories
{
    public class AIReviewRepository : IAIReviewRepository
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;

        public AIReviewRepository(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            _config = config;
        }

        private const int GeminiTimeoutSeconds = 20;
        private const int FallbackTimeoutSeconds = 30;

        public async Task<string> CallAI(string prompt)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(GeminiTimeoutSeconds));
                return await CallGeminiAsync(prompt, cts.Token);
            }
            catch (Exception ex) when (ShouldFallback(ex))
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(FallbackTimeoutSeconds));
                    return await CallGroqAsync(prompt, cts.Token);
                }
                catch (Exception ex2) when (ShouldFallback(ex2))
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(FallbackTimeoutSeconds));
                    return await CallTogetherAsync(prompt, cts.Token);
                }
            }
        }

        private bool ShouldFallback(Exception ex)
        {
            if (ex is OperationCanceledException or TaskCanceledException) return true;
            var msg = ex.Message;
            return msg.Contains("429") || msg.Contains("TooManyRequests") || msg.Contains("Rate limit")
                || msg.Contains("RequestEntityTooLarge") || msg.Contains("413")
                || msg.Contains("tokens per minute") || msg.Contains("too large")
                || msg.Contains("Request too large") || msg.Contains("reduce your message")
                || msg.Contains("503") || msg.Contains("ServiceUnavailable") || msg.Contains("UNAVAILABLE")
                || msg.Contains("high demand") || msg.Contains("overloaded") || msg.Contains("502")
                || msg.Contains("504") || msg.Contains("temporarily");
        }

        // Keep old name as alias so CallAIText can reuse
        private bool IsRateLimit(Exception ex) => ShouldFallback(ex);

        private async Task<string> CallGeminiAsync(string prompt, CancellationToken ct = default)
        {
            var apiKey = _config["OpenAI:ApiKey"];

            var requestBody = new
            {
                contents = new[] { new { parts = new[] { new { text = prompt } } } },
                generationConfig = new { temperature = 0.0, topP = 1.0 }
            };

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = JsonContent.Create(requestBody);

            var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorJson = await response.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException($"Gemini API Error: {response.StatusCode} - {errorJson}");
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            string? text = null;

            // Xử lý an toàn để tránh IndexOutOfRangeException/KeyNotFoundException nếu response rỗng/bị lỗi limit
            if (doc.RootElement.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
            {
                var firstCandidate = candidates[0];
                if (firstCandidate.TryGetProperty("content", out var content) &&
                    content.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0)
                {
                    if (parts[0].TryGetProperty("text", out var textElement))
                    {
                        text = textElement.GetString();
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException("Gemini returned empty response.");
            }

            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');

            if (start >= 0 && end > start)
            {
                return text.Substring(start, end - start + 1);
            }

            throw new InvalidOperationException("Gemini response does not contain valid JSON.");
        }

        public async Task<string> CallAIText(string prompt)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(GeminiTimeoutSeconds));
                return await CallGeminiTextAsync(prompt, cts.Token);
            }
            catch (Exception ex) when (ShouldFallback(ex))
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(FallbackTimeoutSeconds));
                    return await CallGroqAsync(prompt, cts.Token);
                }
                catch (Exception ex2) when (ShouldFallback(ex2))
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(FallbackTimeoutSeconds));
                    return await CallTogetherAsync(prompt, cts.Token);
                }
            }
        }

        private async Task<string> CallGeminiTextAsync(string prompt, CancellationToken ct = default)
        {
            var apiKey = _config["OpenAI:ApiKey"];

            var requestBody = new
            {
                contents = new[] { new { parts = new[] { new { text = prompt } } } },
                generationConfig = new { temperature = 0.0, topP = 1.0 }
            };

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = JsonContent.Create(requestBody);

            var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorJson = await response.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException($"Gemini API Error: {response.StatusCode} - {errorJson}");
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            string? text = null;
            if (doc.RootElement.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
            {
                var firstCandidate = candidates[0];
                if (firstCandidate.TryGetProperty("content", out var content) &&
                    content.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0)
                {
                    if (parts[0].TryGetProperty("text", out var textElement))
                    {
                        text = textElement.GetString();
                    }
                }
            }

            return text ?? "Xin lỗi, AI không thể trả lời câu hỏi của bạn lúc này.";
        }

        public async IAsyncEnumerable<string> CallAIStream(string prompt, [EnumeratorCancellation] CancellationToken ct = default)
        {
            // Phase 1: Determine which provider has capacity (no yield here — try-catch works fine)
            HttpResponseMessage? response = null;
            bool isOpenAIFormat = false;

            try
            {
                using var geminiCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                geminiCts.CancelAfter(TimeSpan.FromSeconds(GeminiTimeoutSeconds));
                response = await BeginGeminiStreamAsync(prompt, geminiCts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    var statusCode = (int)response.StatusCode;
                    var body = await response.Content.ReadAsStringAsync(ct);
                    response.Dispose(); response = null;
                    throw new HttpRequestException($"429 TooManyRequests Gemini: {statusCode} {body}");
                }
            }
            catch (Exception ex) when (ShouldFallback(ex))
            {
                response?.Dispose(); response = null;
                try
                {
                    response = await BeginOpenAIStreamAsync(
                        "https://api.groq.com/openai/v1/chat/completions",
                        _config["Groq:ApiKey"]!, "llama-3.3-70b-versatile", prompt, ct);
                    isOpenAIFormat = true;
                    if (!response.IsSuccessStatusCode)
                    {
                        var statusCode = (int)response.StatusCode;
                        var body = await response.Content.ReadAsStringAsync(ct);
                        response.Dispose(); response = null;
                        throw new HttpRequestException($"429 TooManyRequests Groq: {statusCode} {body}");
                    }
                }
                catch (Exception ex2) when (ShouldFallback(ex2))
                {
                    response?.Dispose(); response = null;
                    try
                    {
                        response = await BeginOpenAIStreamAsync(
                            "https://api.together.xyz/v1/chat/completions",
                            _config["Together:ApiKey"]!, "meta-llama/Llama-3-70b-chat-hf", prompt, ct);
                        isOpenAIFormat = true;
                        if (!response.IsSuccessStatusCode)
                        {
                            response.Dispose(); response = null;
                        }
                    }
                    catch { response?.Dispose(); response = null; }
                }
            }
            catch { response?.Dispose(); response = null; }

            if (response == null) yield break;

            // Phase 2: Stream chunks from the selected provider
            using (response)
            await foreach (var chunk in isOpenAIFormat
                ? ReadOpenAIStreamChunks(response, ct)
                : ReadGeminiStreamChunks(response, ct))
            {
                yield return chunk;
            }
        }

        private async Task<HttpResponseMessage> BeginGeminiStreamAsync(string prompt, CancellationToken ct)
        {
            var apiKey = _config["OpenAI:ApiKey"];
            var requestBody = new { contents = new[] { new { parts = new[] { new { text = prompt } } } } };
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:streamGenerateContent?key={apiKey}&alt=sse";
            var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Content = JsonContent.Create(requestBody);
            return await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        }

        private async Task<HttpResponseMessage> BeginOpenAIStreamAsync(string url, string apiKey, string model, string prompt, CancellationToken ct)
        {
            var requestBody = new Dictionary<string, object>
            {
                { "model", model },
                { "messages", new[] { new { role = "user", content = prompt } } },
                { "stream", true }
            };
            var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Add("Authorization", $"Bearer {apiKey}");
            req.Content = JsonContent.Create(requestBody);
            return await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        }

        private static async IAsyncEnumerable<string> ReadGeminiStreamChunks(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken ct)
        {
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);
            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line?.StartsWith("data: ") != true) continue;
                var json = line.Substring(6).Trim();
                if (string.IsNullOrEmpty(json)) continue;
                string? chunk = null;
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("candidates", out var candidates) &&
                        candidates.GetArrayLength() > 0 &&
                        candidates[0].TryGetProperty("content", out var content) &&
                        content.TryGetProperty("parts", out var parts) &&
                        parts.GetArrayLength() > 0 &&
                        parts[0].TryGetProperty("text", out var textEl))
                        chunk = textEl.GetString();
                }
                catch { continue; }
                if (!string.IsNullOrEmpty(chunk))
                    yield return chunk;
            }
        }

        private static async IAsyncEnumerable<string> ReadOpenAIStreamChunks(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken ct)
        {
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);
            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line?.StartsWith("data: ") != true) continue;
                var json = line.Substring(6).Trim();
                if (json == "[DONE]") yield break;
                if (string.IsNullOrEmpty(json)) continue;
                string? chunk = null;
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                        choices.GetArrayLength() > 0 &&
                        choices[0].TryGetProperty("delta", out var delta) &&
                        delta.TryGetProperty("content", out var contentEl))
                        chunk = contentEl.GetString();
                }
                catch { continue; }
                if (!string.IsNullOrEmpty(chunk))
                    yield return chunk;
            }
        }

        private async Task<string> CallGroqAsync(string prompt, CancellationToken ct = default)
        {
            var apiKey = _config["Groq:ApiKey"];
            var url = "https://api.groq.com/openai/v1/chat/completions";
            var trimmed = TrimPromptForGroq(prompt, maxChars: 18_000);
            return await CallOpenAICompatibleAsync(url, apiKey, "llama-3.3-70b-versatile", trimmed, ct);
        }

        private async Task<string> CallTogetherAsync(string prompt, CancellationToken ct = default)
        {
            var apiKey = _config["Together:ApiKey"];
            var url = "https://api.together.xyz/v1/chat/completions";
            var trimmed = TrimPromptForGroq(prompt, maxChars: 18_000);
            return await CallOpenAICompatibleAsync(url, apiKey, "meta-llama/Llama-3-70b-chat-hf", trimmed, ct);
        }

        // Trim the code section of the prompt to fit smaller context limits (Groq/Together).
        // Keeps the instruction/schema part intact; only shortens the user's code block.
        private static string TrimPromptForGroq(string prompt, int maxChars)
        {
            if (prompt.Length <= maxChars) return prompt;

            // Find the code block boundary — everything after the last ═══ separator line
            var separatorIdx = prompt.LastIndexOf("━━━", StringComparison.Ordinal);
            if (separatorIdx < 0) return prompt[..maxChars];

            // Find the next newline after the separator block to locate code start
            var codeStart = prompt.IndexOf('\n', separatorIdx);
            if (codeStart < 0) return prompt[..maxChars];

            var header = prompt[..codeStart];
            var code = prompt[codeStart..];
            var allowedCodeChars = maxChars - header.Length - 200;
            if (allowedCodeChars < 500) return prompt[..maxChars];

            var trimmedCode = code[..allowedCodeChars] +
                "\n\n[NOTE: Code truncated to fit model context limit — review visible portion only.]";
            return header + trimmedCode;
        }

        private async Task<string> CallOpenAICompatibleAsync(string url, string apiKey, string model, string prompt, CancellationToken ct = default)
        {
            var isJsonExpected = prompt.Contains("ONLY valid JSON");

            var requestBody = new Dictionary<string, object>
            {
                { "model", model },
                { "messages", new[] { new { role = "user", content = prompt } } },
                { "temperature", 0 }
            };

            if (isJsonExpected)
            {
                requestBody["response_format"] = new { type = "json_object" };
            }

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Authorization", $"Bearer {apiKey}");
            request.Content = JsonContent.Create(requestBody);

            var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorJson = await response.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException($"API Error ({model}): {response.StatusCode} - {errorJson}");
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var contentElement))
                {
                    var text = contentElement.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        // Clean up markdown code blocks if the model wrapped it (e.g., ```json ... ```)
                        if (text.StartsWith("```json"))
                        {
                            text = text.Replace("```json", "").Replace("```", "").Trim();
                        }
                        
                        var start = text.IndexOf('{');
                        var end = text.LastIndexOf('}');

                        // Nếu request yêu cầu JSON (nhận diện qua ngoặc nhọn), trích xuất JSON
                        if (start >= 0 && end > start && prompt.Contains("ONLY valid JSON"))
                        {
                            return text.Substring(start, end - start + 1);
                        }

                        // Nếu là chat bình thường, trả về nguyên văn
                        return text;
                    }
                }
            }

            throw new InvalidOperationException($"{model} returned an empty or invalid response.");
        }
    }
}
