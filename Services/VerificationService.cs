using Microsoft.Extensions.Caching.Memory;

namespace Services
{
    public class VerificationService : IVerificationService
    {
        private readonly IMemoryCache _cache;
        private static readonly TimeSpan CodeTtl = TimeSpan.FromMinutes(5);

        public VerificationService(IMemoryCache cache)
        {
            _cache = cache;
        }

        public string GenerateCode(string email)
        {
            var key = CacheKey(email);
            var code = Random.Shared.Next(100_000, 999_999).ToString();
            _cache.Set(key, code, CodeTtl);
            return code;
        }

        public bool VerifyCode(string email, string code)
        {
            var key = CacheKey(email);
            if (_cache.TryGetValue(key, out string? stored) && stored == code.Trim())
            {
                _cache.Remove(key);
                return true;
            }
            return false;
        }

        private static string CacheKey(string email) => $"otp:{email.Trim().ToLowerInvariant()}";
    }
}
