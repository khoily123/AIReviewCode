using AIReviewerAPI.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Models;
using Services;

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
            var result = await _aiService.ReviewCode(request.Code);
            return Ok(result);
        }
    }
}
