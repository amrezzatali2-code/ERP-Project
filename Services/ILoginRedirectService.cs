namespace ERP.Services
{
    public interface ILoginRedirectService
    {
        Task<LoginRedirectTarget> GetTargetAsync(int userId);
    }

    public sealed record LoginRedirectTarget(string Action, string Controller);
}
