using Microsoft.Extensions.DependencyInjection;
using PatchHound.Core.Interfaces;

namespace PatchHound.Tests.TestData;

internal static class TestServiceProviderFactory
{
    public static IServiceProvider Create(ITenantContext tenantContext)
    {
        var services = new ServiceCollection();
        services.AddSingleton(tenantContext);
        return services.BuildServiceProvider();
    }
}
