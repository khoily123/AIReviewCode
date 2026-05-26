using AIReviewerAPI.DTOs;
using Microsoft.AspNetCore.Mvc;
using Services;

namespace AIReviewerAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ShareController : ControllerBase
    {
        private readonly IShareService _share;

        public ShareController(IShareService share) => _share = share;

        [HttpPost]
        public IActionResult Save([FromBody] ShareRequestDto request)
        {
            var id = _share.SaveReview(request.ReviewData, request.OriginalCode);
            return Ok(new ShareResponseDto { Id = id, CreatedAt = DateTime.UtcNow });
        }

        [HttpGet("{id}")]
        public IActionResult Get(string id)
        {
            var result = _share.GetReview(id);
            if (result == null) return NotFound(new { message = "Link đã hết hạn hoặc không tồn tại." });
            return Ok(new
            {
                reviewData = result.Value.Data,
                originalCode = result.Value.OriginalCode,
                createdAt = result.Value.CreatedAt
            });
        }
    }
}
