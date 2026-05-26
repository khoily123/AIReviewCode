using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;

namespace Services
{
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _config;

        public EmailService(IConfiguration config)
        {
            _config = config;
        }

        public async Task SendVerificationCodeAsync(string toEmail, string code)
        {
            var html = $@"
<!DOCTYPE html>
<html>
<body style=""margin:0;padding:0;background:#f1f5f9;font-family:Arial,sans-serif;"">
  <div style=""max-width:480px;margin:40px auto;background:#fff;border-radius:16px;overflow:hidden;box-shadow:0 4px 24px rgba(0,0,0,0.08);"">
    <div style=""background:linear-gradient(135deg,#4f46e5,#7c3aed);padding:32px;text-align:center;"">
      <div style=""font-size:36px;margin-bottom:8px;"">🤖</div>
      <h1 style=""margin:0;color:#fff;font-size:22px;font-weight:700;"">Mã xác nhận email</h1>
      <p style=""margin:8px 0 0;color:rgba(255,255,255,0.85);font-size:14px;"">AI Code Review Tool</p>
    </div>
    <div style=""padding:32px;text-align:center;"">
      <p style=""color:#475569;font-size:15px;margin:0 0 24px;"">Nhập mã bên dưới để xác nhận email và nhận báo cáo PDF:</p>
      <div style=""display:inline-block;background:#f8fafc;border:2px dashed #7c3aed;border-radius:12px;padding:20px 40px;margin-bottom:24px;"">
        <span style=""font-size:38px;font-weight:800;letter-spacing:10px;color:#4f46e5;font-family:monospace;"">{code}</span>
      </div>
      <p style=""color:#94a3b8;font-size:13px;margin:0;"">⏱ Mã có hiệu lực trong <strong style=""color:#ef4444;"">5 phút</strong>.</p>
      <p style=""color:#94a3b8;font-size:13px;margin:8px 0 0;"">Nếu bạn không yêu cầu mã này, hãy bỏ qua email này.</p>
    </div>
    <div style=""background:#f8fafc;padding:16px;text-align:center;border-top:1px solid #e2e8f0;"">
      <span style=""color:#cbd5e1;font-size:12px;"">© 2026 AI Code Review Tool</span>
    </div>
  </div>
</body>
</html>";

            await SendEmailAsync(toEmail, "🔑 Mã xác nhận — AI Code Review Tool", html);
        }

        public async Task SendReportAsync(string toEmail, byte[] pdfBytes, string recipientName = "")
        {
            var name = string.IsNullOrWhiteSpace(recipientName) ? "bạn" : recipientName;
            var html = $@"
<!DOCTYPE html>
<html>
<body style=""margin:0;padding:0;background:#f1f5f9;font-family:Arial,sans-serif;"">
  <div style=""max-width:560px;margin:40px auto;background:#fff;border-radius:16px;overflow:hidden;box-shadow:0 4px 24px rgba(0,0,0,0.08);"">
    <div style=""background:linear-gradient(135deg,#4f46e5,#7c3aed);padding:32px;text-align:center;"">
      <div style=""font-size:40px;margin-bottom:8px;"">📄</div>
      <h1 style=""margin:0;color:#fff;font-size:22px;font-weight:700;"">Báo cáo review code của {name}</h1>
      <p style=""margin:8px 0 0;color:rgba(255,255,255,0.85);font-size:14px;"">AI Code Review Tool — {DateTime.UtcNow:dd/MM/yyyy HH:mm} UTC</p>
    </div>
    <div style=""padding:32px;"">
      <p style=""color:#1e293b;font-size:15px;margin:0 0 16px;"">Xin chào,</p>
      <p style=""color:#475569;font-size:14px;line-height:1.6;margin:0 0 20px;"">
        Báo cáo phân tích code của bạn đã sẵn sàng. Vui lòng xem file <strong>PDF đính kèm</strong> để xem toàn bộ kết quả bao gồm:
      </p>
      <ul style=""color:#475569;font-size:14px;line-height:2;padding-left:20px;margin:0 0 20px;"">
        <li>📊 Điểm đánh giá (Performance / Security / Maintainability)</li>
        <li>🐛 Danh sách lỗi phát hiện</li>
        <li>✨ Code đã được AI sửa</li>
        <li>🧪 Unit Tests được tạo tự động</li>
      </ul>
      <div style=""background:#f0fdf4;border-left:4px solid #10b981;padding:12px 16px;border-radius:0 8px 8px 0;"">
        <p style=""margin:0;color:#166534;font-size:13px;"">💡 Tip: Dùng tab <strong>Diff</strong> trong ứng dụng để so sánh code trước và sau khi sửa!</p>
      </div>
    </div>
    <div style=""background:#f8fafc;padding:16px;text-align:center;border-top:1px solid #e2e8f0;"">
      <span style=""color:#cbd5e1;font-size:12px;"">© 2026 AI Code Review Tool</span>
    </div>
  </div>
</body>
</html>";

            await SendEmailAsync(toEmail, "📄 Báo cáo AI Code Review của bạn", html, ("AI_Code_Review_Report.pdf", pdfBytes));
        }

        private async Task SendEmailAsync(string toEmail, string subject, string htmlBody,
            (string fileName, byte[] data)? attachment = null)
        {
            var host     = _config["Smtp:Host"]     ?? throw new InvalidOperationException("Smtp:Host not configured.");
            var port     = int.TryParse(_config["Smtp:Port"], out var p) ? p : 587;
            var user     = _config["Smtp:User"]     ?? throw new InvalidOperationException("Smtp:User not configured.");
            var password = _config["Smtp:Password"] ?? throw new InvalidOperationException("Smtp:Password not configured.");
            var fromName = _config["Smtp:FromName"] ?? "AI Code Review";

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(fromName, user));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;

            var builder = new BodyBuilder { HtmlBody = htmlBody };
            if (attachment.HasValue)
                builder.Attachments.Add(attachment.Value.fileName, attachment.Value.data,
                    new ContentType("application", "pdf"));

            message.Body = builder.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(host, port, SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(user, password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
    }
}
