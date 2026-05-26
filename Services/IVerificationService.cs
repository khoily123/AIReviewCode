namespace Services
{
    public interface IVerificationService
    {
        /// <summary>Generates and stores a 6-digit OTP for the given email. Returns the code.</summary>
        string GenerateCode(string email);

        /// <summary>Returns true if the code is valid and not expired, then invalidates it.</summary>
        bool VerifyCode(string email, string code);
    }
}
