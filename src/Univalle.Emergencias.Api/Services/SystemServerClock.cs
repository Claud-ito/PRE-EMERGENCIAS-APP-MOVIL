using Univalle.Emergencias.Contracts.Common;

namespace Univalle.Emergencias.Api.Services;

public sealed class SystemServerClock(TimeProvider timeProvider) : IServerClock
{
    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();
}
