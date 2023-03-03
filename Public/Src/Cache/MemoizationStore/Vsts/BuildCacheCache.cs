// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Vsts;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;
using BuildXL.Cache.MemoizationStore.Vsts.Adapters;
using Microsoft.VisualStudio.Services.BlobStore.Common;

namespace BuildXL.Cache.MemoizationStore.Vsts
{
    /// <summary>
    ///     L3 ICache implemented against the VSTS BuildCache Service.
    /// </summary>
    public sealed class BuildCacheCache : ICache, IMemoizationStore
    {
        private readonly IAbsFileSystem _fileSystem;
        private readonly string _cacheNamespace;
        private readonly IBuildCacheHttpClientFactory _buildCacheHttpClientFactory;
        private readonly BackingContentStore _backingContentStore;
        private readonly TimeSpan _minimumTimeToKeepContentHashLists;
        private readonly TimeSpan _rangeOfTimeToKeepContentHashLists;
        private readonly int _maxFingerprintSelectorsToFetch;
        private readonly IContentStore _writeThroughContentStore;
        private readonly bool _sealUnbackedContentHashLists;
        private readonly bool _useBlobContentHashLists;
        private readonly bool _fingerprintIncorporationEnabled;
        private readonly int _maxDegreeOfParallelismForIncorporateRequests;
        private readonly int _maxFingerprintsPerIncorporateRequest;
        private readonly BuildCacheCacheTracer _tracer;
        private readonly BackingContentStoreConfiguration _backingContentStoreConfiguration;
        private ContentHashListAdapterFactory _contentHashListAdapterFactory;
        private readonly bool _overrideUnixFileAccessMode;
        private readonly bool _enableEagerFingerprintIncorporation;
        private readonly TimeSpan _inlineFingerprintIncorporationExpiry;
        private readonly TimeSpan _eagerFingerprintIncorporationNagleInterval;
        private readonly int _eagerFingerprintIncorporationNagleBatchSize;
        private readonly bool _forceUpdateOnAddContentHashList;
        private readonly bool _includeDownloadUris;

        /// <summary>
        /// BuildCache may be unable to pin the content for us when we want the content to be backed.
        /// This may be because it is in DedupStore or because we're using a custom domain.
        /// BuildCache only supports BlobStore with the default domain.
        /// </summary>
        private readonly bool _manuallyExtendContentLifetime;

        private bool _disposed;

        /// <summary>
        ///     Initializes a new instance of the <see cref="BuildCacheCache"/> class.
        /// </summary>
        /// <param name="backingStoreConfiguration">Configuration for backing content store.</param>
        /// <param name="cacheNamespace">the namespace of the cache that is communicated with.</param>
        /// <param name="buildCacheHttpClientFactory">Factory for creatign a backing BuildCache http client.</param>
        /// <param name="maxFingerprintSelectorsToFetch">Maximum number of selectors to enumerate.</param>
        /// <param name="minimumTimeToKeepContentHashLists">Minimum time-to-live for created or referenced ContentHashLists.</param>
        /// <param name="rangeOfTimeToKeepContentHashLists">Range of time beyond the minimum for the time-to-live of created or referenced ContentHashLists.</param>
        /// <param name="logger">A logger for tracing.</param>
        /// <param name="fingerprintIncorporationEnabled">Feature flag to enable fingerprints incorporation on shutdown</param>
        /// <param name="maxDegreeOfParallelismForIncorporateRequests">Throttle the number of fingerprints chunks sent in parallel</param>
        /// <param name="maxFingerprintsPerIncorporateRequest">Max fingerprints allowed per chunk</param>
        /// <param name="domain">Domain ID to use against BlobStore or DedupStore</param>
        /// <param name="writeThroughContentStoreFunc">Optional write-through store to allow writing-behind to BlobStore</param>
        /// <param name="sealUnbackedContentHashLists">If true, the client will attempt to seal any unbacked ContentHashLists that it sees.</param>
        /// <param name="useBlobContentHashLists">use blob based content hash lists.</param>
        /// <param name="overrideUnixFileAccessMode">If true, overrides default Unix file access modes.</param>
        /// <param name="enableEagerFingerprintIncorporation"><see cref="BuildCacheServiceConfiguration.EnableEagerFingerprintIncorporation"/></param>
        /// <param name="inlineFingerprintIncorporationExpiry"><see cref="BuildCacheServiceConfiguration.InlineFingerprintIncorporationExpiryHours"/></param>
        /// <param name="eagerFingerprintIncorporationNagleInterval"><see cref="BuildCacheServiceConfiguration.EagerFingerprintIncorporationNagleIntervalMinutes"/></param>
        /// <param name="eagerFingerprintIncorporationNagleBatchSize"><see cref="BuildCacheServiceConfiguration.EagerFingerprintIncorporationNagleBatchSize"/></param>
        /// <param name="forceUpdateOnAddContentHashList">Whether to force an update and ignore existing CHLs when adding.</param>
        /// <param name="includeDownloadUris">Whether to request URIs from L3 when retreiving CHLs.</param>
        public BuildCacheCache(
            BackingContentStoreConfiguration backingStoreConfiguration,
            string cacheNamespace,
            IBuildCacheHttpClientFactory buildCacheHttpClientFactory,
            int maxFingerprintSelectorsToFetch,
            TimeSpan minimumTimeToKeepContentHashLists,
            TimeSpan rangeOfTimeToKeepContentHashLists,
            ILogger logger,
            bool fingerprintIncorporationEnabled,
            int maxDegreeOfParallelismForIncorporateRequests,
            int maxFingerprintsPerIncorporateRequest,
            IDomainId domain,
            bool forceUpdateOnAddContentHashList,
            bool includeDownloadUris,
            Func<IContentStore> writeThroughContentStoreFunc = null,
            bool sealUnbackedContentHashLists = false,
            bool useBlobContentHashLists = false,
            bool overrideUnixFileAccessMode = false,
            bool enableEagerFingerprintIncorporation = false,
            TimeSpan inlineFingerprintIncorporationExpiry = default,
            TimeSpan eagerFingerprintIncorporationNagleInterval = default,
            int eagerFingerprintIncorporationNagleBatchSize = 100)
        {
            Contract.Requires(backingStoreConfiguration != null);
            Contract.Requires(backingStoreConfiguration.FileSystem != null);
            Contract.Requires(buildCacheHttpClientFactory != null);
            Contract.Requires(backingStoreConfiguration.ArtifactHttpClientFactory != null);

            _fileSystem = backingStoreConfiguration.FileSystem;
            _cacheNamespace = cacheNamespace;
            _buildCacheHttpClientFactory = buildCacheHttpClientFactory;
            _tracer = new BuildCacheCacheTracer(logger, nameof(BuildCacheCache));

            _backingContentStoreConfiguration = backingStoreConfiguration;
            _backingContentStore = new BackingContentStore(backingStoreConfiguration);

            _manuallyExtendContentLifetime = false;

            if (backingStoreConfiguration.UseDedupStore)
            {
                // Guaranteed content is only available for BlobSessions. (bug 144396)
                _sealUnbackedContentHashLists = false;

                // BuildCache is incompatible with Dedup hashes.
                // This is because BuildCache would not know to look for the blob in DedupStore instead of BlobStore
                _useBlobContentHashLists = false;
                _manuallyExtendContentLifetime = true;
            }
            else
            {
                _sealUnbackedContentHashLists = sealUnbackedContentHashLists;
                _useBlobContentHashLists = useBlobContentHashLists;
            }

            if (!domain.Equals(WellKnownDomainIds.OriginalDomainId))
            {
                // BuildCache is incompatible with multi-domain
                _useBlobContentHashLists = false;
                _manuallyExtendContentLifetime = true;
            }

            _maxFingerprintSelectorsToFetch = maxFingerprintSelectorsToFetch;
            _minimumTimeToKeepContentHashLists = minimumTimeToKeepContentHashLists;
            _rangeOfTimeToKeepContentHashLists = rangeOfTimeToKeepContentHashLists;

            if (writeThroughContentStoreFunc != null)
            {
                _writeThroughContentStore = writeThroughContentStoreFunc();
                Contract.Assert(_writeThroughContentStore != null);
            }

            _fingerprintIncorporationEnabled = fingerprintIncorporationEnabled;
            _maxDegreeOfParallelismForIncorporateRequests = maxDegreeOfParallelismForIncorporateRequests;
            _maxFingerprintsPerIncorporateRequest = maxFingerprintsPerIncorporateRequest;
            _overrideUnixFileAccessMode = overrideUnixFileAccessMode;
            _enableEagerFingerprintIncorporation = enableEagerFingerprintIncorporation;
            _inlineFingerprintIncorporationExpiry = inlineFingerprintIncorporationExpiry;
            _eagerFingerprintIncorporationNagleInterval = eagerFingerprintIncorporationNagleInterval;
            _eagerFingerprintIncorporationNagleBatchSize = eagerFingerprintIncorporationNagleBatchSize;
            _forceUpdateOnAddContentHashList = forceUpdateOnAddContentHashList;
            _includeDownloadUris = includeDownloadUris;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _contentHashListAdapterFactory?.Dispose();
                _writeThroughContentStore?.Dispose();
                _backingContentStore.Dispose();
            }

            _disposed = true;
        }

        /// <inheritdoc />
        public Task<BoolResult> ShutdownAsync(Context context)
        {
            ShutdownStarted = true;
            return ShutdownCall<BuildCacheCacheTracer>.RunAsync(_tracer, context, async () =>
            {
                var statsResult = await GetStatsInternalAsync(context).ConfigureAwait(false);
                if (statsResult.Succeeded)
                {
                    _tracer.TraceStatisticsAtShutdown(context, statsResult.CounterSet, prefix: "BuildCacheCacheStats");
                }
                else
                {
                    _tracer.Debug(context, $"Getting stats failed: [{statsResult}]");
                }

                var backingContentStoreTask = Task.Run(async () => await _backingContentStore.ShutdownAsync(context).ConfigureAwait(false));
                var writeThroughContentStoreResult = _writeThroughContentStore != null
                    ? await _writeThroughContentStore.ShutdownAsync(context)
                    : BoolResult.Success;
                var backingContentStoreResult = await backingContentStoreTask.ConfigureAwait(false);

                BoolResult result;
                if (backingContentStoreResult.Succeeded && writeThroughContentStoreResult.Succeeded)
                {
                    result = BoolResult.Success;
                }
                else
                {
                    var sb = new StringBuilder();
                    if (!backingContentStoreResult.Succeeded)
                    {
                        sb.Append($"Backing content store shutdown failed, error=[{backingContentStoreResult}]");
                    }

                    if (!writeThroughContentStoreResult.Succeeded)
                    {
                        sb.Append($"Write-through content store shutdown failed, error=[{writeThroughContentStoreResult}]");
                    }

                    result = new BoolResult(sb.ToString());
                }

                ShutdownCompleted = true;
                return result;
            });
        }

        /// <inheritdoc />
        public bool ShutdownCompleted { get; private set; }

        /// <inheritdoc />
        public bool ShutdownStarted { get; private set; }

        /// <inheritdoc />
        public Task<BoolResult> StartupAsync(Context context)
        {
            StartupStarted = true;
            return StartupCall<BuildCacheCacheTracer>.RunAsync(_tracer, context, async () =>
            {
                BoolResult result;

                _tracer.Debug(context, $"Creating ContentHashListAdapterFactory with {nameof(_useBlobContentHashLists)}={_useBlobContentHashLists}");
                _contentHashListAdapterFactory = await ContentHashListAdapterFactory.CreateAsync(
                    context, _buildCacheHttpClientFactory, _useBlobContentHashLists);
                Id =
                    await _contentHashListAdapterFactory.BuildCacheHttpClient.GetBuildCacheServiceDeterminism(_cacheNamespace)
                        .ConfigureAwait(false);

                var backingContentStoreTask = Task.Run(async () => await _backingContentStore.StartupAsync(context).ConfigureAwait(false));
                var writeThroughContentStoreResult = _writeThroughContentStore != null
                    ? await _writeThroughContentStore.StartupAsync(context).ConfigureAwait(false)
                    : BoolResult.Success;
                var backingContentStoreResult = await backingContentStoreTask.ConfigureAwait(false);

                if (backingContentStoreResult.Succeeded && writeThroughContentStoreResult.Succeeded)
                {
                    result = BoolResult.Success;
                }
                else
                {
                    var sb = new StringBuilder();
                    if (backingContentStoreResult.Succeeded)
                    {
                        var r = await _backingContentStore.ShutdownAsync(context).ConfigureAwait(false);
                        if (!r.Succeeded)
                        {
                            sb.Append($"Backing content store shutdown failed, error=[{r}]");
                        }
                    }
                    else
                    {
                        sb.Append($"Backing content store startup failed, error=[{backingContentStoreResult}]");
                    }

                    if (writeThroughContentStoreResult.Succeeded)
                    {
                        var r = _writeThroughContentStore != null
                            ? await _writeThroughContentStore.ShutdownAsync(context).ConfigureAwait(false)
                            : BoolResult.Success;
                        if (!r.Succeeded)
                        {
                            sb.Append(sb.Length > 0 ? ", " : string.Empty);
                            sb.Append($"Write-through content store shutdown failed, error=[{r}]");
                        }
                    }
                    else
                    {
                        sb.Append(sb.Length > 0 ? ", " : string.Empty);
                        sb.Append($"Write-through content store startup failed, error=[{writeThroughContentStoreResult}]");
                    }

                    result = new BoolResult(sb.ToString());
                }

                StartupCompleted = true;
                return result;
            });
        }

        /// <inheritdoc />
        public bool StartupCompleted { get; private set; }

        /// <inheritdoc />
        public bool StartupStarted { get; private set; }

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public CreateSessionResult<ICacheSession> CreateSession(Context context, string name, ImplicitPin implicitPin)
        {
            return Tracing.CreateSessionCall.Run(_tracer, context, name, () =>
            {
                var backingContentSessionResult = _backingContentStore.CreateSession(context, name);
                if (!backingContentSessionResult.Succeeded)
                {
                    return new CreateSessionResult<ICacheSession>(backingContentSessionResult);
                }

                IContentSession writeThroughContentSession = null;
                if (_writeThroughContentStore != null)
                {
                    var writeThroughContentSessionResult = _writeThroughContentStore.CreateSession(context, name, implicitPin);
                    if (!writeThroughContentSessionResult.Succeeded)
                    {
                        return new CreateSessionResult<ICacheSession>(writeThroughContentSessionResult);
                    }

                    writeThroughContentSession = writeThroughContentSessionResult.Session;
                }

                return new CreateSessionResult<ICacheSession>(
                    new BuildCacheSession(
                        _fileSystem,
                        name,
                        implicitPin,
                        _cacheNamespace,
                        Id,
                        _contentHashListAdapterFactory.Create(backingContentSessionResult.Session, _includeDownloadUris),
                        backingContentSessionResult.Session,
                        _maxFingerprintSelectorsToFetch,
                        _minimumTimeToKeepContentHashLists,
                        _rangeOfTimeToKeepContentHashLists,
                        _fingerprintIncorporationEnabled,
                        _maxDegreeOfParallelismForIncorporateRequests,
                        _maxFingerprintsPerIncorporateRequest,
                        writeThroughContentSession,
                        _sealUnbackedContentHashLists,
                        _overrideUnixFileAccessMode,
                        _tracer,
                        _enableEagerFingerprintIncorporation,
                        _inlineFingerprintIncorporationExpiry,
                        _eagerFingerprintIncorporationNagleInterval,
                        _eagerFingerprintIncorporationNagleBatchSize,
                        _manuallyExtendContentLifetime,
                        _forceUpdateOnAddContentHashList)
                    {
                        RequiredContentKeepUntil = _backingContentStoreConfiguration.RequiredContentKeepUntil
                    });
            });
        }

        /// <inheritdoc />
        public Task<GetStatsResult> GetStatsAsync(Context context)
        {
            return GetStatsCall<BuildCacheCacheTracer>.RunAsync(_tracer, new OperationContext(context), () => GetStatsInternalAsync(context));
        }

        [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
        private async Task<GetStatsResult> GetStatsInternalAsync(Context context)
        {
            try
            {
                var aggregateStats = new CounterSet();
                var cachestats = _tracer.GetCounters();

                aggregateStats.Merge(cachestats);
                if (_writeThroughContentStore != null)
                {
                    var writeThroughStoreStats = await _writeThroughContentStore.GetStatsAsync(context);
                    if (writeThroughStoreStats.Succeeded)
                    {
                        aggregateStats.Merge(writeThroughStoreStats.CounterSet, "WriteThroughStore.");
                    }
                }
                if (_backingContentStore != null)
                {
                    var backingContentStoreStats = _backingContentStore.GetStats();
                    if (backingContentStoreStats.Succeeded)
                    {
                        aggregateStats.Merge(backingContentStoreStats.CounterSet, "BackingContentStore.");
                    }
                }

                return new GetStatsResult(aggregateStats);
            }
            catch (Exception ex)
            {
                return new GetStatsResult(ex);
            }
        }

        /// <inheritdoc />
        public Guid Id { get; private set; }

        /// <inheritdoc />
        public IAsyncEnumerable<StructResult<StrongFingerprint>> EnumerateStrongFingerprints(Context context)
        {
            return AsyncEnumerable.Empty<StructResult<StrongFingerprint>>();
        }

        /// <inheritdoc />
        public CreateSessionResult<IMemoizationSession> CreateSession(Context context, string name)
        {
            var result = CreateSession(context, name, ImplicitPin.None);

            return result.Succeeded
                ? new CreateSessionResult<IMemoizationSession>(result.Session)
                : new CreateSessionResult<IMemoizationSession>(result);
        }

        /// <inheritdoc />
        public CreateSessionResult<IMemoizationSession> CreateSession(Context context, string name, IContentSession contentSession)
        {
            throw new NotImplementedException();
        }
    }
}
