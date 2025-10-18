using System.Threading;
using System.Threading.Tasks;

namespace FfaasLite.Api.Infrastructure;

public interface IDatabaseInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken);
}
