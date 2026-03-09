using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
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
            // Đây là key AIzaSy... bạn đang có trong config
            var apiKey = _config["OpenAI:ApiKey"];

            // Cấu trúc Body của Gemini khác hoàn toàn với OpenAI
            var requestBody = new
            {
                contents = new[]
                {
            new { parts = new[] { new { text = prompt } } }
        }
            };

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}";
            var request = new HttpRequestMessage(HttpMethod.Post, url);

            request.Content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Gemini API Error: {response.StatusCode} - {json}");
            }

            var doc = JsonDocument.Parse(json);

            var text = doc.RootElement
             .GetProperty("candidates")[0]
             .GetProperty("content")
             .GetProperty("parts")[0]
             .GetProperty("text")
             .GetString();

                    var start = text.IndexOf("{");
                    var end = text.LastIndexOf("}");

                    if (start >= 0 && end > start)
                    {
                        text = text.Substring(start, end - start + 1);
                    }

                    return text;
        }
    }
}

