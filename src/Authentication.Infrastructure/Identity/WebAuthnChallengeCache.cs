using System.Text.Json;
using Fido2NetLib;
using Microsoft.Extensions.Caching.Distributed;

namespace Authentication.Infrastructure.Identity;

/// <summary>
/// Stashes a WebAuthn ceremony's server-generated options between the
/// "options" call and the matching "verify" call - Fido2NetLib needs the
/// exact original options object back to validate a response against, and
/// it must come from server-side state, never be trusted if echoed back by
/// the client. Reuses the same Redis-backed IDistributedCache the
/// idempotency middleware already relies on, so this works the same way
/// across multiple API instances.
/// </summary>
public class WebAuthnChallengeCache
{
    private static readonly DistributedCacheEntryOptions Expiry =
        new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) };

    private readonly IDistributedCache _cache;

    public WebAuthnChallengeCache(IDistributedCache cache) => _cache = cache;

    public Task StoreCreationOptionsAsync(Guid usuarioId, CredentialCreateOptions options) =>
        SetAsync($"webauthn:enroll:{usuarioId}", options);

    public Task<CredentialCreateOptions?> TakeCreationOptionsAsync(Guid usuarioId) =>
        TakeAsync<CredentialCreateOptions>($"webauthn:enroll:{usuarioId}");

    public Task StoreAssertionOptionsAsync(Guid usuarioId, AssertionOptions options) =>
        SetAsync($"webauthn:assert:{usuarioId}", options);

    public Task<AssertionOptions?> TakeAssertionOptionsAsync(Guid usuarioId) =>
        TakeAsync<AssertionOptions>($"webauthn:assert:{usuarioId}");

    private Task SetAsync<T>(string key, T value) =>
        _cache.SetStringAsync(key, JsonSerializer.Serialize(value), Expiry);

    /// <summary>One-shot: removes the entry once read, so a challenge can never be replayed against a second verify call.</summary>
    private async Task<T?> TakeAsync<T>(string key)
    {
        var json = await _cache.GetStringAsync(key);
        if (json is null)
        {
            return default;
        }

        await _cache.RemoveAsync(key);
        return JsonSerializer.Deserialize<T>(json);
    }
}
