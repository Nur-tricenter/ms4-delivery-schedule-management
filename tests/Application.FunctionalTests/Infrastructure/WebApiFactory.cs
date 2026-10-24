using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace delivery_schedule_management.Application.FunctionalTests.Infrastructure;

public class WebApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder
            .UseSetting("ConnectionStrings:delivery-schedule-management-db", connectionString)
            // Same values TestTokens signs with, so tokens minted in tests validate like identity's.
            .UseSetting("Jwt:SecretKey", TestTokens.SecretKey)
            .UseSetting("Jwt:ValidIssuer", TestTokens.Issuer)
            .UseSetting("Jwt:ValidAudience", TestTokens.Audience)
            // Deterministic fixture, no MS2/identity on the test host.
            .UseSetting("Catalog:UseMock", "true");
    }
}
