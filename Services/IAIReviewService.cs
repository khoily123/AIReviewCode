using AIReviewerAPI.DTOs;
using Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Services
{
    public interface IAIReviewService
    {
        Task<ReviewResponseDto> ReviewCode(ReviewRequestDto request);
        Task<string> GenerateFlowchart(string code);
        Task<ChatResponseDto> ChatWithCode(ChatRequestDto request);
        IAsyncEnumerable<string> ChatWithCodeStream(ChatRequestDto request, CancellationToken ct = default);
        Task<TranslateResponseDto> TranslateCode(TranslateRequestDto request);
    }
}
