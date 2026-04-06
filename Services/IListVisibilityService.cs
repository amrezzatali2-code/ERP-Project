using System.Collections.Generic;
using System.Threading.Tasks;

namespace ERP.Services
{
    public interface IListVisibilityService
    {
        Task<bool> CanViewAllOperationalListsAsync();
        Task<List<string>> GetCurrentUserCreatorNamesAsync();
    }
}
