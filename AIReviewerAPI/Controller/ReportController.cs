using AIReviewerAPI.DTOs;
using Microsoft.AspNetCore.Mvc;
using Services;

namespace AIReviewerAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ReportController : ControllerBase
    {
        private readonly IEmailService _emailService;
        private readonly IPdfReportService _pdfService;
        private readonly IVerificationService _verificationService;

        public ReportController(IEmailService emailService, IPdfReportService pdfService,
            IVerificationService verificationService)
        {
            _emailService = emailService;
            _pdfService = pdfService;
            _verificationService = verificationService;
        }

        /// <summary>Sends a 6-digit OTP to the given email.</summary>
        [HttpPost("send-verification")]
        public async Task<IActionResult> SendVerification([FromBody] SendVerificationDto request)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { message = GetFirstError() });

            try
            {
                var code = _verificationService.GenerateCode(request.Email);
                await _emailService.SendVerificationCodeAsync(request.Email, code);
                return Ok(new { message = $"Mã xác nhận đã được gửi đến {request.Email}." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Không thể gửi email: {ex.Message}" });
            }
        }

        /// <summary>Verifies OTP, generates PDF, and emails it.</summary>
        [HttpPost("send-report")]
        public async Task<IActionResult> SendReport([FromBody] SendReportDto request)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { message = GetFirstError() });

            if (!_verificationService.VerifyCode(request.Email, request.Code))
                return BadRequest(new { message = "Mã xác nhận không đúng hoặc đã hết hạn. Vui lòng thử lại." });

            try
            {
                var pdfBytes = _pdfService.GenerateReviewPdf(request.ReviewData, request.OriginalCode);
                await _emailService.SendReportAsync(request.Email, pdfBytes);
                return Ok(new { message = $"Báo cáo PDF đã được gửi thành công đến {request.Email}! 🎉" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Không thể gửi báo cáo: {ex.Message}" });
            }
        }

        private string GetFirstError() =>
            ModelState.Values.SelectMany(v => v.Errors).FirstOrDefault()?.ErrorMessage ?? "Dữ liệu không hợp lệ.";
    }
}
