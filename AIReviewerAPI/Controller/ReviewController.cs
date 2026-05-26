using AIReviewerAPI.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Models;
using Services;
using System.IO.Compression;
using System.Text.Json;

namespace AIReviewerAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ReviewController : ControllerBase
    {
            private readonly IAIReviewService _aiService;

            public ReviewController(IAIReviewService aiService)
            {
                _aiService = aiService;
            }

        [HttpPost]
        public async Task<IActionResult> Review([FromBody] ReviewRequestDto request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);
            var result = await _aiService.ReviewCode(request);
            return Ok(result);
        }

        [HttpPost("chat")]
        public async Task<IActionResult> Chat([FromBody] ChatRequestDto request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);
            var result = await _aiService.ChatWithCode(request);
            return Ok(result);
        }

        [HttpPost("translate")]
        public async Task<IActionResult> Translate([FromBody] TranslateRequestDto request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);
            var result = await _aiService.TranslateCode(request);
            return Ok(result);
        }

        [HttpPost("chat/stream")]
        public async Task ChatStream([FromBody] ChatRequestDto request, CancellationToken ct)
        {
            Response.ContentType = "text/event-stream; charset=utf-8";
            Response.Headers["Cache-Control"] = "no-cache";
            Response.Headers["X-Accel-Buffering"] = "no";
            Response.Headers["Connection"] = "keep-alive";

            try
            {
                await foreach (var chunk in _aiService.ChatWithCodeStream(request, ct))
                {
                    var data = JsonSerializer.Serialize(chunk);
                    await Response.WriteAsync($"data: {data}\n\n", ct);
                    await Response.Body.FlushAsync(ct);
                }
                await Response.WriteAsync("data: [DONE]\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                var errData = JsonSerializer.Serialize($"⚠️ Lỗi: {ex.Message}");
                await Response.WriteAsync($"data: {errData}\n\n");
                await Response.Body.FlushAsync();
            }
        }

        [HttpPost("files")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> ReviewFiles(
            [FromForm] string? persona,
            [FromForm] string? customRules,
            IFormFileCollection files)
        {
            if (files == null || files.Count == 0)
                return BadRequest(new { message = "Vui lòng upload ít nhất một file." });

            var sb = new System.Text.StringBuilder();
            foreach (var file in files)
            {
                if (file.Length > 1_000_000) continue; // skip files > 1MB
                var ext = Path.GetExtension(file.FileName).ToLower();

                if (ext == ".zip")
                {
                    using var zipStream = file.OpenReadStream();
                    using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);
                    foreach (var entry in zip.Entries)
                    {
                        if (entry.Length > 500_000) continue;
                        var entryExt = Path.GetExtension(entry.Name).ToLower();
                        if (!IsCodeFile(entryExt)) continue;
                        using var reader = new StreamReader(entry.Open());
                        var content = await reader.ReadToEndAsync();
                        sb.AppendLine($"// === FILE: {entry.FullName} ===");
                        sb.AppendLine(content);
                        sb.AppendLine();
                    }
                }
                else if (IsCodeFile(ext))
                {
                    using var reader = new StreamReader(file.OpenReadStream());
                    var content = await reader.ReadToEndAsync();
                    sb.AppendLine($"// === FILE: {file.FileName} ===");
                    sb.AppendLine(content);
                    sb.AppendLine();
                }
            }

            if (sb.Length == 0)
                return BadRequest(new { message = "Không đọc được nội dung code từ các file đã upload." });

            var rules = string.IsNullOrWhiteSpace(customRules)
                ? null
                : customRules.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();

            var request = new ReviewRequestDto
            {
                Code = sb.ToString(),
                Persona = persona ?? "Standard",
                CustomRules = rules
            };

            var result = await _aiService.ReviewCode(request);
            return Ok(result);
        }

        private static bool IsCodeFile(string ext) =>
            ext is ".cs" or ".py" or ".ts" or ".js" or ".java" or ".go" or ".rs" or ".kt"
                or ".cpp" or ".c" or ".h" or ".rb" or ".php" or ".swift" or ".dart" or ".vue" or ".tsx" or ".jsx";
    }
}
