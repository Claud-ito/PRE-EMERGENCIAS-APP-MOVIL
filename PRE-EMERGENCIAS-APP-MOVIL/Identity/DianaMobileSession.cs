using System.Net.Http;
using PRE_EMERGENCIAS_APP_MOVIL.Diana;
using Univalle.Emergencias.Contracts.Identity;

namespace PRE_EMERGENCIAS_APP_MOVIL.Identity;

public sealed class DianaMobileSession(MobileSession session) : IDianaMobileSession
{
    public Task<AuthenticatedUser?> GetCurrentAsync() => Task.FromResult(session.CurrentUser);
    public Task AuthorizeAsync(HttpRequestMessage request) => session.AuthorizeAsync(request);
}
