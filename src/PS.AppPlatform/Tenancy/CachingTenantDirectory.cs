using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PS.AppPlatform.Tenancy;

/// <summary>
/// Caches ITenantDirectory results with stale-if-error and concurrent miss coalescing.
/// Positive hits: 5 min (from TenancyOptions.CacheDuration).
/// Null results: 1 min (min of CacheDuration and NullCacheDuration).
/// Stale-if-error: 1 hour (TenancyOptions.StaleIfError).
/// </summary>
public sealed class CachingTenantDirectory : ITenantDirectory
{
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly Type? _innerDirectoryType;
    private readonly TimeSpan _cacheDuration;
    private readonly TimeSpan _nullCacheDuration;
    private readonly TimeSpan _staleIfError;
    private readonly ILogger<CachingTenantDirectory> _logger;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    private sealed class CacheEntry
    {
        public Lazy<Task<UserMemberships?>> Pending = null!;
        public UserMemberships? Value;
        public DateTimeOffset FetchedAt;
        public bool IsNull;
    }

    /// <summary>
    /// Ctor for use when inner is resolved from a scope (e.g., scoped services).
    /// </summary>
    public CachingTenantDirectory(
        IServiceScopeFactory scopeFactory,
        IOptions<TenancyOptions> options,
        ILogger<CachingTenantDirectory> logger,
        Type innerDirectoryType)
    {
        _scopeFactory = scopeFactory;
        _innerDirectoryType = innerDirectoryType;
        _cacheDuration = options.Value.CacheDuration;
        _nullCacheDuration = TimeSpan.FromMinutes(1);
        if (_cacheDuration < _nullCacheDuration)
            _nullCacheDuration = _cacheDuration;
        _staleIfError = options.Value.StaleIfError;
        _logger = logger;
    }

    /// <summary>
    /// Ctor for use when inner is already resolved (e.g., singleton inner passed in).
    /// </summary>
    public CachingTenantDirectory(
        ITenantDirectory inner,
        IOptions<TenancyOptions> options,
        ILogger<CachingTenantDirectory> logger)
    {
        _scopeFactory = null;
        _innerDirectoryType = null;
        _cacheDuration = options.Value.CacheDuration;
        _nullCacheDuration = TimeSpan.FromMinutes(1);
        if (_cacheDuration < _nullCacheDuration)
            _nullCacheDuration = _cacheDuration;
        _staleIfError = options.Value.StaleIfError;
        _logger = logger;
        // Store inner in a dummy way; we'll override GetMembershipsAsync behavior
        _inner = inner;
    }

    private readonly ITenantDirectory? _inner;

    public async Task<UserMemberships?> GetMembershipsAsync(IdentityKey identity, CancellationToken ct)
    {
        var key = $"{identity.ObjectId}:{identity.IssuerTenantId}";
        var now = DateTimeOffset.UtcNow;

        // Try to get cached entry
        var hasEntry = _cache.TryGetValue(key, out var entry);

        if (hasEntry && entry != null)
        {
            // Fresh hit
            var age = now - entry.FetchedAt;
            var expireDuration = entry.IsNull ? _nullCacheDuration : _cacheDuration;
            if (age < expireDuration)
            {
                return entry.Value;
            }

            // Expired; check if within stale-if-error window
            if (age < _staleIfError)
            {
                try
                {
                    var inner = GetInnerDirectory();
                    var fresh = await inner.GetMembershipsAsync(identity, ct);
                    // Success; update cache
                    entry.Value = fresh;
                    entry.FetchedAt = now;
                    entry.IsNull = fresh is null;
                    return fresh;
                }
                catch (TenantDirectoryUnavailableException ex)
                {
                    _logger.LogWarning(ex, "Directory unavailable; returning stale entry for {Identity}", key);
                    return entry.Value;
                }
            }
            else
            {
                // Beyond stale-if-error; try fresh and fail if unavailable
                try
                {
                    var inner = GetInnerDirectory();
                    var fresh = await inner.GetMembershipsAsync(identity, ct);
                    entry.Value = fresh;
                    entry.FetchedAt = now;
                    entry.IsNull = fresh is null;
                    return fresh;
                }
                catch (TenantDirectoryUnavailableException ex)
                {
                    _logger.LogError(ex, "Directory unavailable; no stale entry for {Identity}", key);
                    throw;
                }
            }
        }

        // Cache miss; use Lazy to coalesce concurrent calls
        var newEntry = new CacheEntry
        {
            Pending = new Lazy<Task<UserMemberships?>>(async () =>
            {
                try
                {
                    var inner = GetInnerDirectory();
                    var result = await inner.GetMembershipsAsync(identity, ct);
                    // Update cache with result
                    if (_cache.TryGetValue(key, out var e) && e != null)
                    {
                        e.Value = result;
                        e.FetchedAt = now;
                        e.IsNull = result is null;
                    }
                    return result;
                }
                catch (TenantDirectoryUnavailableException)
                {
                    // Remove the pending entry so next retry doesn't get the faulted Lazy
                    _cache.TryRemove(key, out _);
                    throw;
                }
            }),
            FetchedAt = now,
            IsNull = false,
            Value = null
        };

        // GetOrAdd ensures only one Lazy per key
        var cached = _cache.GetOrAdd(key, newEntry);

        try
        {
            var result = await cached.Pending.Value;
            return result;
        }
        catch (TenantDirectoryUnavailableException)
        {
            // Evict on failure so next call retries
            _cache.TryRemove(key, out _);
            throw;
        }
    }

    private ITenantDirectory GetInnerDirectory()
    {
        if (_inner != null)
            return _inner;

        if (_scopeFactory == null || _innerDirectoryType == null)
            throw new InvalidOperationException("CachingTenantDirectory not properly initialized");

        var scope = _scopeFactory.CreateScope();
        try
        {
            return (ITenantDirectory)scope.ServiceProvider.GetRequiredService(_innerDirectoryType);
        }
        finally
        {
            scope.Dispose();
        }
    }
}
