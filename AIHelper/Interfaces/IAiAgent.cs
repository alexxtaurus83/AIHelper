using AIHelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AIHelper.Interfaces
{
    public interface IAiAgent
    {
        Task<AiResponse> FixCodeAsync(string code, List<RemediationTask> issues);
    }
}