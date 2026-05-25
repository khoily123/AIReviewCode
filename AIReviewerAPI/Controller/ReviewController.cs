using AIReviewerAPI.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Models;
using Services;
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
    }
}
