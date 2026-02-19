using AIHelper.Models;
using System.Threading.Tasks;

namespace AIHelper.Interfaces
{
    public interface IRemediationService
    {
        Task<object> RemediateAsync(RemediationRequestBaseDto request);
    }
}
