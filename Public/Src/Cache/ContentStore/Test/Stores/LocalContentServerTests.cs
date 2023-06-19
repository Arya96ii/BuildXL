// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.Service.Grpc;
using ContentStoreTest.Extensions;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Stores
{
    [Trait("Category", "Integration")]
    [Trait("Category", "Integration2")]
    public class LocalContentServerTests : TestBase
    {
        private const string CacheName = "cacheName";
        private const string SessionName = "sessionName";
        private const int SessionId = 3;
        private const uint GracefulShutdownSeconds = ServiceConfiguration.DefaultGracefulShutdownSeconds;
        private const int TimeoutSecs = 5;

        public LocalContentServerTests(ITestOutputHelper output)
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger, output)
        {
        }

        [Fact(Skip = "TODO: Failing locally during conversion")]
        [Trait("Category", "QTestSkip")] // Skipped
        public async Task RestoredSessionReleasedAfterInactivity()
        {
            const string scenario = nameof(RestoredSessionReleasedAfterInactivity);
            var fileName = $"{Guid.NewGuid()}.json";

            var context = new Context(Logger);
            using (var directory = new DisposableDirectory(FileSystem))
            {
                var rootPath = directory.Path;
                var contentHash = ContentHash.Random();

                var pins = new List<string> {contentHash.Serialize()};
                var hibernatedSessionInfo = new HibernatedContentSessionInfo(SessionId, SessionName, ImplicitPin.None, CacheName, pins, DateTime.UtcNow.Ticks, Capabilities.None);
                var hibernatedSessions = new HibernatedSessions<HibernatedContentSessionInfo>(new List<HibernatedContentSessionInfo> {hibernatedSessionInfo});
                hibernatedSessions.Write(FileSystem, rootPath, fileName);

                var namedCacheRoots = new Dictionary<string, AbsolutePath> {{ CacheName, rootPath}};

                var grpcPort = PortExtensions.GetNextAvailablePort();
                var grpcPortFileName = Guid.NewGuid().ToString();

                var configuration = new ServiceConfiguration(namedCacheRoots, rootPath, GracefulShutdownSeconds, grpcPort, grpcPortFileName);
                var storeConfig = ContentStoreConfiguration.CreateWithMaxSizeQuotaMB(1);
                Func<AbsolutePath, IContentStore> contentStoreFactory = (path) =>
                    new FileSystemContentStore(
                        FileSystem,
                        SystemClock.Instance,
                        directory.Path,
                        new ConfigurationModel(storeConfig));
                var localContentServerConfiguration = new LocalServerConfiguration(configuration)
                {
                    UnusedSessionHeartbeatTimeout = TimeSpan.FromSeconds(TimeoutSecs),
                    RequestCallTokensPerCompletionQueue = 10,
                };

                using (var server = CreateServer(scenario, contentStoreFactory, localContentServerConfiguration))
                {
                    var r1 = await server.StartupAsync(context);
                    r1.ShouldBeSuccess();

                    var beforeIds = server.GetSessionIds();
                    beforeIds.Should().Contain(SessionId);

                    // Wait one period to ensure that it times out, another to ensure that the checker finds it, and another to give it time to release it.
                    await Task.Delay(TimeSpan.FromSeconds(TimeoutSecs * 3));

                    var afterIds = server.GetSessionIds();
                    afterIds.Count.Should().Be(0);

                    var r2 = await server.ShutdownAsync(context);
                    r2.ShouldBeSuccess();
                }
            }
        }

        [Fact(Skip = "TODO: Failing locally during conversion")]
        [Trait("Category", "QTestSkip")] // Skipped
        public async Task SessionForLegacyClientRetainedLongerAfterInactivity()
        {
            const string scenario = nameof(SessionForLegacyClientRetainedLongerAfterInactivity);
            var fileName = $"{Guid.NewGuid()}.json";

            var context = new Context(Logger);
            using (var directory = new DisposableDirectory(FileSystem))
            {
                var rootPath = directory.Path;
                var contentHash = ContentHash.Random();

                var pins = new List<string> { contentHash.Serialize() };
                var hibernatedSessionInfo = new HibernatedContentSessionInfo(SessionId, SessionName, ImplicitPin.None, CacheName, pins, 0, Capabilities.None);
                var hibernatedSessions = new HibernatedSessions<HibernatedContentSessionInfo>(new List<HibernatedContentSessionInfo> { hibernatedSessionInfo });
                hibernatedSessions.Write(FileSystem, rootPath, fileName);

                var namedCacheRoots = new Dictionary<string, AbsolutePath> { { CacheName, rootPath } };

                var grpcPort = PortExtensions.GetNextAvailablePort();
                var grpcPortFileName = Guid.NewGuid().ToString();

                var configuration = new ServiceConfiguration(namedCacheRoots, rootPath, GracefulShutdownSeconds, grpcPort, grpcPortFileName);
                var storeConfig = ContentStoreConfiguration.CreateWithMaxSizeQuotaMB(1);
                Func<AbsolutePath, IContentStore> contentStoreFactory = (path) =>
                    new FileSystemContentStore(
                        FileSystem,
                        SystemClock.Instance,
                        directory.Path,
                        new ConfigurationModel(storeConfig));
                var localContentServerConfiguration = new LocalServerConfiguration(configuration)
                {
                    UnusedSessionHeartbeatTimeout = TimeSpan.FromSeconds(TimeoutSecs),
                    UnusedSessionTimeout = TimeSpan.FromSeconds(TimeoutSecs * 4),
                    RequestCallTokensPerCompletionQueue = 10,
                };

                using (var server = CreateServer(scenario, contentStoreFactory, localContentServerConfiguration))
                {
                    var r1 = await server.StartupAsync(context);
                    r1.ShouldBeSuccess();

                    var beforeIds = server.GetSessionIds();
                    beforeIds.Should().Contain(SessionId);

                    // Wait one period to ensure that it times out, another to ensure that the checker finds it, and another to give it time to release it if it decides to (it shouldn't).
                    await Task.Delay(TimeSpan.FromSeconds(TimeoutSecs * 3));

                    var afterIds = server.GetSessionIds();
                    afterIds.Count.Should().Be(1);

                    // Wait one more period to ensure that it times out (at 4xtimeoutSecs), another to ensure that the checker finds it, and another to give it time to release.
                    await Task.Delay(TimeSpan.FromSeconds(TimeoutSecs * 3));

                    afterIds = server.GetSessionIds();
                    afterIds.Count.Should().Be(0);

                    var r2 = await server.ShutdownAsync(context);
                    r2.ShouldBeSuccess();
                }
            }
        }

        [Trait("Category", "WindowsOSOnly")] // These use named event handles, which are not supported in .NET core
        [Fact]
        public async Task HibernationDataNotLoadedIfStoreStartupFails()
        {
            const string scenario = nameof(HibernationDataNotLoadedIfStoreStartupFails);
            var fileName = $"{Guid.NewGuid()}.json";

            var context = new Context(Logger);
            using (var directory = new DisposableDirectory(FileSystem))
            {
                var rootPath = directory.Path;
                var contentHash = ContentHash.Random();

                var pins = new List<string> { contentHash.Serialize() };
                var hibernatedSessionInfo = new HibernatedContentSessionInfo(SessionId, SessionName, ImplicitPin.None, CacheName, pins, 0, Capabilities.None);
                var hibernatedSessions = new HibernatedSessions<HibernatedContentSessionInfo>(new List<HibernatedContentSessionInfo> { hibernatedSessionInfo });
                hibernatedSessions.Write(FileSystem, rootPath, fileName);

                var namedCacheRoots = new Dictionary<string, AbsolutePath> { { CacheName, rootPath } };

                var grpcPort = PortExtensions.GetNextAvailablePort();
                var grpcPortFileName = Guid.NewGuid().ToString();

                var configuration = new ServiceConfiguration(namedCacheRoots, rootPath, GracefulShutdownSeconds, grpcPort, grpcPortFileName);

                Func<AbsolutePath, IContentStore> contentStoreFactory =
                    (path) => new TestFailingContentStore();
                var localContentServerConfiguration = new LocalServerConfiguration(configuration)
                {
                    UnusedSessionHeartbeatTimeout = TimeSpan.FromSeconds(TimeoutSecs),
                    UnusedSessionTimeout = TimeSpan.FromSeconds(TimeoutSecs * 4),
                    RequestCallTokensPerCompletionQueue = 10,
                };

                using (var server = CreateServer(scenario, contentStoreFactory, localContentServerConfiguration))
                {
                    var r = await server.StartupAsync(context);

                    r.ShouldBeError(TestFailingContentStore.FailureMessage);
                    FileSystem.HibernatedSessionsExists(rootPath, fileName).Should().BeTrue("The hibernation data should not have been read/deleted");
                }
            }
        }

        // TODO ST: add derived type
        protected virtual IGrpcServerHost<LocalServerConfiguration> GrpcHost => null;

        private LocalContentServer CreateServer(
            string scenario,
            Func<AbsolutePath, IContentStore> contentStoreFactory,
            LocalServerConfiguration configuration)
        {
            return new LocalContentServer(Logger, FileSystem, GrpcHost, scenario, contentStoreFactory, configuration);
        }
    }
}
