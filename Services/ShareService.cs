using AIReviewerAPI.DTOs;
using Microsoft.Extensions.Caching.Memory;

namespace Services
{
    public class ShareService : IShareService
    {
        private readonly IMemoryCache _cache;
        private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

        public ShareService(IMemoryCache cache) => _cache = cache;

        public string SaveReview(ReviewResponseDto data, string originalCode)
        {
            var id = Guid.NewGuid().ToString("N")[..10];
            _cache.Set($"share:{id}", (data, originalCode, DateTime.UtcNow), Ttl);
            return id;
        }

        public (ReviewResponseDto? Data, string? OriginalCode, DateTime CreatedAt)? GetReview(string id)
        {
            if (_cache.TryGetValue($"share:{id}", out (ReviewResponseDto data, string code, DateTime at) val))
                return (val.data, val.code, val.at);
            return null;
        }
    }
}
