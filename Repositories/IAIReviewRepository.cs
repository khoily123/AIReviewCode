using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repositories
{
    public interface IAIReviewRepository
    {
        Task<string> CallAI(string prompt);
        Task<string> CallAIText(string prompt);
        IAsyncEnumerable<string> CallAIStream(string prompt, CancellationToken ct = default);
    }
}
