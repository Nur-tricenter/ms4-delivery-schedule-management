using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace delivery_schedule_management.Infrastructure.Catalog;

/// <summary>
/// Logs MS4's service account into nur-tricenter-identity and caches the bearer token until shortly
/// before it expires. One instance per process (singleton): every outbound call to MS2 shares it.
/// </summary>
public sealed class ServiceAccountTokenProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<IdentityClientOptions> options,
    TimeProvider timeProvider) : IDisposable
{
    public const string HttpClientName = "identity";

    private static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt;

    public async Task<string> GetTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && _token is not null && timeProvider.GetUtcNow() < _expiresAt)
            return _token;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && _token is not null && timeProvider.GetUtcNow() < _expiresAt)
                return _token;

            var settings = options.Value;
            var client = httpClientFactory.CreateClient(HttpClientName);

            using var response = await client.PostAsJsonAsync(settings.LoginPath,
                new { userName = settings.ServiceUserName, password = settings.ServicePassword }, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new InvalidOperationException(
                    $"Identity rejected MS4's service account '{settings.ServiceUserName}'. Check Identity:ServiceUserName/ServicePassword " +
                    "and that the account exists (nur-tricenter-local start-stack creates it).");

            response.EnsureSuccessStatusCode();

            var login = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken)
                ?? throw new InvalidOperationException("Identity returned an empty login response.");

            _token = login.AccessToken;
            var lifetime = TimeSpan.FromSeconds(Math.Max(login.ExpiresIn, 0));
            _expiresAt = timeProvider.GetUtcNow() + (lifetime > RefreshMargin ? lifetime - RefreshMargin : TimeSpan.Zero);

            return _token;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose() => _lock.Dispose();

    private sealed record LoginResponse(string AccessToken, long ExpiresIn);
}

/// <summary>
/// Attaches the service-account bearer token to every MS2 request. On a 401 (token revoked, key
/// rotated) it logs in again once and retries.
/// </summary>
public sealed class ServiceAccountTokenHandler(ServiceAccountTokenProvider tokenProvider) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
            await tokenProvider.GetTokenAsync(forceRefresh: false, cancellationToken));

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized || request.Content is not null)
            return response;

        response.Dispose();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
            await tokenProvider.GetTokenAsync(forceRefresh: true, cancellationToken));

        return await base.SendAsync(request, cancellationToken);
    }
}
