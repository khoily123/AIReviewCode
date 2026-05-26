namespace Services
{
    public interface IEmailService
    {
        Task SendVerificationCodeAsync(string toEmail, string code);
        Task SendReportAsync(string toEmail, byte[] pdfBytes, string recipientName = "");
    }
}
