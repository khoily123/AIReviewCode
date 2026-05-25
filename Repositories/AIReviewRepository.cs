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

        public async Task<string> CallAI(string prompt)
        {
            try 
            {
                return await CallGeminiAsync(prompt);
            }
            catch (Exception ex) when (IsRateLimit(ex))
            {
                try 
                {
                    return await CallGroqAsync(prompt);
                }
                catch (Exception ex2) when (IsRateLimit(ex2))
                {
                    return await CallTogetherAsync(prompt);
                }
            }
        }

        private bool IsRateLimit(Exception ex)
        {
            return ex.Message.Contains("429") || ex.Message.Contains("TooManyRequests") || ex.Message.Contains("Rate limit");
        }

        private async Task<string> CallGeminiAsync(string prompt)
        {
            var apiKey = _config["OpenAI:ApiKey"];

            var requestBody = new
            {
                contents = new[]
                {
            new { parts = new[] { new { text = prompt } } }
        }
            };

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}";
            var request = new HttpRequestMessage(HttpMethod.Post, url);

            request.Content = JsonContent.Create(requestBody);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorJson = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"Gemini API Error: {response.StatusCode} - {errorJson}");
            }

            // Dùng stream thay vì đọc chuỗi để tối ưu bộ nhớ
            using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);

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
                return await CallGeminiTextAsync(prompt);
            }
            catch (Exception ex) when (IsRateLimit(ex))
            {
                try 
                {
                    return await CallGroqAsync(prompt);
                }
                catch (Exception ex2) when (IsRateLimit(ex2))
                {
                    return await CallTogetherAsync(prompt);
                }
            }
        }

        private async Task<string> CallGeminiTextAsync(string prompt)
        {
            var apiKey = _config["OpenAI:ApiKey"];

            var requestBody = new
            {
                contents = new[]
                {
                    new { parts = new[] { new { text = prompt } } }
                }
            };

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = JsonContent.Create(requestBody);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorJson = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"Gemini API Error: {response.StatusCode} - {errorJson}");
            }

            using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);

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
                response = await BeginGeminiStreamAsync(prompt, ct);
                if (!response.IsSuccessStatusCode)
                {
                    var statusCode = (int)response.StatusCode;
                    var body = await response.Content.ReadAsStringAsync(ct);
                    response.Dispose(); response = null;
                    throw new HttpRequestException($"429 TooManyRequests Gemini: {statusCode} {body}");
                }
            }
            catch (Exception ex) when (IsRateLimit(ex))
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
                catch (Exception ex2) when (IsRateLimit(ex2))
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

        private async Task<string> CallGroqAsync(string prompt)
        {
            var apiKey = _config["Groq:ApiKey"];
            var url = "https://api.groq.com/openai/v1/chat/completions";
            // Sử dụng LLaMA 3.3 70B của Groq (Model mới nhất)
            return await CallOpenAICompatibleAsync(url, apiKey, "llama-3.3-70b-versatile", prompt);
        }

        private async Task<string> CallTogetherAsync(string prompt)
        {
            var apiKey = _config["Together:ApiKey"];
            var url = "https://api.together.xyz/v1/chat/completions";
            // Sử dụng LLaMA 3 70B của Together
            return await CallOpenAICompatibleAsync(url, apiKey, "meta-llama/Llama-3-70b-chat-hf", prompt);
        }

        private async Task<string> CallOpenAICompatibleAsync(string url, string apiKey, string model, string prompt)
        {
            var isJsonExpected = prompt.Contains("ONLY valid JSON");

            var requestBody = new Dictionary<string, object>
            {
                { "model", model },
                { "messages", new[] { new { role = "user", content = prompt } } }
            };

            if (isJsonExpected)
            {
                requestBody["response_format"] = new { type = "json_object" };
            }

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Authorization", $"Bearer {apiKey}");
            request.Content = JsonContent.Create(requestBody);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorJson = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"API Error ({model}): {response.StatusCode} - {errorJson}");
            }

            using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);

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
