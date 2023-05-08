﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tasks;
using BuildXL.Utilities.Core.Tracing;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

#nullable enable

namespace BuildXL.Cache.ContentStore.Test.Utils
{
    public class ResourcePoolTests
    {
        private class Resource : StartupShutdownBase
        {
            protected override Tracer Tracer => new Tracer("Dummy");
        }

        private class SlowResource : StartupShutdownSlimBase
        {
            protected override Tracer Tracer => new Tracer("Dummy");

            private readonly TimeSpan _startupDelay;

            private readonly TimeSpan _shutdownDelay;

            public SlowResource(TimeSpan? startupDelay = null, TimeSpan? shutdownDelay = null)
            {
                _startupDelay = startupDelay ?? TimeSpan.Zero;
                _shutdownDelay = shutdownDelay ?? TimeSpan.Zero;
                Contract.Requires(_startupDelay >= TimeSpan.Zero);
                Contract.Requires(_shutdownDelay >= TimeSpan.Zero);
            }

            protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
            {
                if (_startupDelay > TimeSpan.Zero)
                {
                    await Task.Delay(_startupDelay, context.Token);
                }

                return BoolResult.Success;
            }

            protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
            {
                if (_shutdownDelay > TimeSpan.Zero)
                {
                    await Task.Delay(_shutdownDelay, context.Token);
                }

                return BoolResult.Success;
            }
        }

        private class FailingResource : StartupShutdownSlimBase
        {
            protected override Tracer Tracer => new Tracer("Dummy");

            private readonly bool _startupFailure;
            private readonly bool _shutdownFailure;

            public FailingResource(bool startupFailure = false, bool shutdownFailure = false)
            {
                _startupFailure = startupFailure;
                _shutdownFailure = shutdownFailure;
            }

            protected override Task<BoolResult> StartupCoreAsync(OperationContext context)
            {
                if (_startupFailure)
                {
                    throw new Exception("Ceci n'est pas un erreur");
                }

                return BoolResult.SuccessTask;
            }

            protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
            {
                if (_shutdownFailure)
                {
                    throw new Exception("Ceci n'est pas un erreur");
                }

                return BoolResult.SuccessTask;
            }
        }

        private struct Key
        {
            public int Number;
            public Key(int number) => Number = number;
        }

        private static async Task RunTest<TKey, TObject>(Func<OperationContext, ResourcePool<TKey, TObject>, Task> func, Func<TKey, TObject> factory, ResourcePoolConfiguration? configuration = null, IClock? clock = null)
            where TKey : notnull
            where TObject : IStartupShutdownSlim
        {
            configuration ??= new ResourcePoolConfiguration();

            var tracingContext = new Context(TestGlobal.Logger);
            var context = new OperationContext(tracingContext);
            var pool = new ResourcePool<TKey, TObject>(tracingContext, configuration, factory, clock);

            try
            {
                await func(context, pool);
            }
            finally
            {
                pool.Dispose();
                ValidateCounters(pool.Counter);
            }
        }

        private static Task RunTest<TKey, TObject>(Func<OperationContext, ResourcePool<TKey, TObject>, Task> func, ResourcePoolConfiguration? configuration = null, IClock? clock = null)
            where TKey : notnull
            where TObject : IStartupShutdownSlim, new()
        {
            return RunTest(func, _ => new TObject(), configuration, clock);
        }

        private static void ValidateCounters(CounterCollection<ResourcePoolCounters> counters)
        {
            counters[ResourcePoolCounters.CreatedResources].Value.Should().BeGreaterOrEqualTo(counters[ResourcePoolCounters.ResourceInitializationAttempts].Value);
            counters[ResourcePoolCounters.ResourceInitializationAttempts].Value.Should().BeGreaterOrEqualTo(counters[ResourcePoolCounters.ReleasedResources].Value);
            counters[ResourcePoolCounters.ReleasedResources].Value.Should().BeGreaterOrEqualTo(counters[ResourcePoolCounters.ShutdownAttempts].Value);

            counters[ResourcePoolCounters.ResourceInitializationSuccesses].Value.Should().Be(counters[ResourcePoolCounters.ShutdownAttempts].Value);

            counters[ResourcePoolCounters.ResourceInitializationAttempts].Value.Should().Be(counters[ResourcePoolCounters.ReleasedResources].Value);

            counters[ResourcePoolCounters.ResourceInitializationAttempts].Value.Should().Be(counters[ResourcePoolCounters.ResourceInitializationSuccesses].Value + counters[ResourcePoolCounters.ResourceInitializationFailures].Value);

            counters[ResourcePoolCounters.ShutdownAttempts].Value.Should().Be(counters[ResourcePoolCounters.ShutdownSuccesses].Value + counters[ResourcePoolCounters.ShutdownFailures].Value);

            counters[ResourcePoolCounters.ReleasedResources].Value.Should().Be(counters[ResourcePoolCounters.ShutdownAttempts].Value + counters[ResourcePoolCounters.ResourceInitializationFailures].Value);
            counters[ResourcePoolCounters.GarbageCollectionAttempts].Value.Should().BeGreaterOrEqualTo(1);
            counters[ResourcePoolCounters.GarbageCollectionSuccesses].Value.Should().BeGreaterOrEqualTo(1);
        }

        [Fact]
        public Task ResourceAcquisitionImpliesInitialization()
        {
            return RunTest<Key, Resource>(async (context, pool) =>
            {
                var key = new Key(0);

                await pool.UseAsync(context, key, async wrapper =>
                {
                    wrapper.Invalid.Should().BeFalse();
                    wrapper.ReferenceCount.Should().Be(1);

                    var resource = await wrapper.LazyValue;
                    resource.StartupCompleted.Should().BeTrue();
                    resource.ShutdownStarted.Should().BeFalse();
                    return BoolResult.Success;
                }).ShouldBeSuccess();
            });
        }

        [Fact]
        public Task ResourceInvalidationRegeneratesInstance()
        {
            return RunTest<Key, Resource>(async (context, pool) =>
            {
                var key = new Key(0);

                await pool.UseAsync(context, key, wrapper =>
                {
                    pool.Counter[ResourcePoolCounters.CreatedResources].Value.Should().Be(1);
                    pool.Counter[ResourcePoolCounters.ResourceInitializationAttempts].Value.Should().Be(1);
                    pool.Counter[ResourcePoolCounters.RemovedResources].Value.Should().Be(0);
                    wrapper.Invalidate(context);
                    pool.Counter[ResourcePoolCounters.RemovedResources].Value.Should().Be(1);

                    // Invalidating again should NOT cause resource to be removed again
                    wrapper.Invalidate(context);
                    pool.Counter[ResourcePoolCounters.RemovedResources].Value.Should().Be(1);

                    return BoolResult.SuccessTask;
                }).ShouldBeSuccess();

                await pool.UseAsync(context, key, wrapper =>
                {
                    pool.Counter[ResourcePoolCounters.ReleasedResources].Value.Should().Be(1);
                    pool.Counter[ResourcePoolCounters.CreatedResources].Value.Should().Be(2);
                    pool.Counter[ResourcePoolCounters.ResourceInitializationAttempts].Value.Should().Be(2);
                    return BoolResult.SuccessTask;
                }).ShouldBeSuccess();
            });
        }

        [Fact]
        public async Task ResourceInvalidationShutsDownOutstandingOperations()
        {
            var stopLatch = TaskUtilities.CreateMutex(taken: true);
            Task? witnessTask = null;
            var witnessedCancellation = false;

            await RunTest<Key, Resource>(async (context, pool) =>
            {
                var key = new Key(0);

                await pool.UseAsync(context, key, async wrapper =>
                {
                    await Task.Yield();

                    witnessTask = Task.Run(async () =>
                    {
                        // Ensuring it doesn't run on the UseAsync call
                        await Task.Yield();

                        // Allow the UseAsync call to invalidate
                        stopLatch.Release();

                        // Ensure we get cancelled
                        try
                        {
                            await Task.Delay(Timeout.InfiniteTimeSpan, wrapper.ShutdownToken);
                        }
                        catch (TaskCanceledException) { }

                        witnessedCancellation = true;
                    }).FireAndForgetErrorsAsync(context);

                    using var token = await stopLatch.AcquireAsync();
                    wrapper.Invalidate(context);
                    return BoolResult.Success;
                }).ShouldBeSuccess();
            });

            Contract.AssertNotNull(witnessTask);
            await witnessTask;
            witnessedCancellation.Should().Be(true);
        }

        [Fact]
        public async Task ResourceInvalidationRespectsReferenceCountBeforeShutdown()
        {
            var stopLatch = TaskUtilities.CreateMutex(taken: true);
            var firstUseIsDone = new TaskCompletionSource<object>();
            Task? outstandingTask = null;
            
            CounterCollection<ResourcePoolCounters>? counters = null;

            await RunTest<Key, Resource>(async (context, pool) =>
            {
                counters = pool.Counter;

                var key = new Key(0);
                await pool.UseAsync(context, key, async wrapper =>
                {
                    outstandingTask = pool.UseAsync(context, key, async wrapper2 =>
                    {
                        wrapper2.ReferenceCount.Should().Be(2);
                        stopLatch.Release();

                        // Waiting for the first UseAsync to be done before checking the states
                        await firstUseIsDone.Task;

                        wrapper2.ReferenceCount.Should().Be(1);
                        counters[ResourcePoolCounters.CreatedResources].Value.Should().Be(1);
                        counters[ResourcePoolCounters.ReleasedResources].Value.Should().Be(0);
                        counters[ResourcePoolCounters.ShutdownAttempts].Value.Should().Be(0);

                        return BoolResult.Success;
                    }).ShouldBeSuccess();

                    using var token = await stopLatch.AcquireAsync();
                    wrapper.Invalidate(context);

                    return BoolResult.Success;
                }).ShouldBeSuccess();

                // Notifying that the first UseAsync is done.
                firstUseIsDone.SetResult(string.Empty);
            });

            Contract.AssertNotNull(counters);
            Contract.AssertNotNull(outstandingTask);

            // This should throw any exceptions that haven't been caught
            await outstandingTask;
        }

        [Fact]
        public Task DuplicateClientsAreTheSameObject()
        {
            return RunTest<Key, Resource>(async (context, pool) =>
            {
                var key = new Key(0);

                await pool.UseAsync(context, key, async wrapper =>
                {
                    return await pool.UseAsync(context, key, wrapper2 =>
                    {
                        wrapper.ReferenceCount.Should().Be(2);
                        wrapper.Should().Be(wrapper2);
                        return BoolResult.SuccessTask;
                    });
                }).ShouldBeSuccess();

                Resource? cachedResource = null;
                await pool.UseAsync(context, key, wrapper =>
                {
                    cachedResource = wrapper.Value;
                    return BoolResult.SuccessTask;
                }).ShouldBeSuccess();

                await pool.UseAsync(context, key, wrapper =>
                {
                    Assert.Same(cachedResource, wrapper.Value);
                    return BoolResult.SuccessTask;
                }).ShouldBeSuccess();
            });
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public Task ExpiredInstancesGetReleasedOnReuse(bool invalidate)
        {
            var clock = new MemoryClock();
            var configuration = new ResourcePoolConfiguration() { MaximumAge = TimeSpan.FromSeconds(1) };

            return RunTest<Key, Resource>(async (context, pool) =>
            {
                var key = new Key(0);

                Resource? lastResource = null;
                await pool.UseAsync(context, key, wrapper =>
                {
                    if (invalidate)
                    {
                        wrapper.Invalidate(context);
                    }

                    lastResource = wrapper.Value;
                    return BoolResult.SuccessTask;
                }).ShouldBeSuccess();

                if (!invalidate)
                {
                    clock.Increment(TimeSpan.FromMinutes(1));
                }

                await pool.UseAsync(context, key, wrapper =>
                {
                    lastResource.Should().NotBe(wrapper.Value);
                    pool.Counter[ResourcePoolCounters.CreatedResources].Value.Should().Be(2);
                    pool.Counter[ResourcePoolCounters.ReleasedResources].Value.Should().Be(1);
                    return BoolResult.SuccessTask;
                }).ShouldBeSuccess();
            },
            configuration,
            clock);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public Task ExpiredInstancesGetReleasedOnUnrelatedUse(bool invalidate)
        {
            var clock = new MemoryClock();
            var configuration = new ResourcePoolConfiguration() { MaximumAge = TimeSpan.FromSeconds(1) };

            return RunTest<Key, Resource>(async (context, pool) =>
            {
                var key = new Key(0);
                var key1 = new Key(1);

                await pool.UseAsync(context, key, wrapper =>
                {
                    if (invalidate)
                    {
                        wrapper.Invalidate(context);
                    }

                    return BoolResult.SuccessTask;
                }).ShouldBeSuccess();

                if (!invalidate)
                {
                    clock.Increment(TimeSpan.FromMinutes(1));
                }

                await pool.UseAsync(context, key1, wrapper =>
                {
                    pool.Counter[ResourcePoolCounters.CreatedResources].Value.Should().Be(2);
                    pool.Counter[ResourcePoolCounters.ReleasedResources].Value.Should().Be(1);
                    return BoolResult.SuccessTask;
                }).ShouldBeSuccess();
            },
            configuration,
            clock);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public Task ExpiredInstancesGetGarbageCollected(bool invalidate)
        {
            var clock = new MemoryClock();
            var configuration = new ResourcePoolConfiguration() { MaximumAge = TimeSpan.FromSeconds(1) };

            return RunTest<Key, Resource>(async (context, pool) =>
            {
                var key = new Key(0);

                await pool.UseAsync(context, key, wrapper =>
                {
                    if (invalidate)
                    {
                        wrapper.Invalidate(context);
                    }

                    return BoolResult.SuccessTask;
                }).ShouldBeSuccess();

                if (!invalidate)
                {
                    clock.Increment(TimeSpan.FromMinutes(1));
                }

                await pool.GarbageCollectAsync(context).IgnoreFailure();

                pool.Counter[ResourcePoolCounters.CreatedResources].Value.Should().Be(1);
                pool.Counter[ResourcePoolCounters.ReleasedResources].Value.Should().Be(1);
                pool.Counter[ResourcePoolCounters.ShutdownSuccesses].Value.Should().Be(1);
                pool.Counter[ResourcePoolCounters.GarbageCollectionSuccesses].Value.Should().Be(1);
            },
            configuration,
            clock);
        }

        [Fact]
        public Task ReleasedInstancesArentRemovedIfTheyHaveAliveReferences()
        {
            var clock = new MemoryClock();
            var configuration = new ResourcePoolConfiguration() { MaximumAge = TimeSpan.FromSeconds(1) };

            return RunTest<Key, Resource>(async (context, pool) =>
            {
                var key = new Key(0);

                await pool.UseAsync(context, key, async wrapper =>
                {
                    // Invalidate resource. This will force GC to release it. Notice time doesn't change, so this isn't
                    // removed due to TTL
                    await pool.UseAsync(context, key, wrapper =>
                    {
                        wrapper.Invalidate(context);
                        return BoolResult.SuccessTask;
                    }).ShouldBeSuccess();

                    await pool.GarbageCollectAsync(context).IgnoreFailure();

                    wrapper.ShutdownToken.IsCancellationRequested.Should().BeTrue();
                    pool.Counter[ResourcePoolCounters.ReleasedResources].Value.Should().Be(1);
                    pool.Counter[ResourcePoolCounters.ShutdownAttempts].Value.Should().Be(0);

                    return BoolResult.Success;
                }).ShouldBeSuccess();
            },
            configuration,
            clock);
        }

        [Fact]
        public Task GarbageCollectionRunsInTheBackground()
        {
            var configuration = new ResourcePoolConfiguration()
            {
                MaximumAge = TimeSpan.FromSeconds(1),
                GarbageCollectionPeriod = TimeSpan.FromSeconds(0.3),
            };

            return RunTest<Key, Resource>(async (context, pool) =>
            {
                var key = new Key(0);
                var key1 = new Key(1);

                await pool.UseAsync(context, key, wrapper =>
                {
                    wrapper.Invalidate(context);
                    return BoolResult.SuccessTask;
                }).ShouldBeSuccess();

                await Task.Delay(TimeSpan.FromSeconds(1));

                pool.Counter[ResourcePoolCounters.ReleasedResources].Value.Should().Be(1);
                pool.Counter[ResourcePoolCounters.ShutdownSuccesses].Value.Should().Be(1);
                pool.Counter[ResourcePoolCounters.GarbageCollectionAttempts].Value.Should().BeGreaterOrEqualTo(1);
            },
            configuration);
        }

        [Fact]
        public Task SlowStartupBlocksUseThread()
        {
            var configuration = new ResourcePoolConfiguration()
            {
                MaximumAge = TimeSpan.FromSeconds(1),
                GarbageCollectionPeriod = Timeout.InfiniteTimeSpan,
            };

            return RunTest<Key, SlowResource>(async (context, pool) =>
            {
                var key = new Key(0);

                var timeToStartup = StopwatchSlim.Start();
                await pool.UseAsync(context, key, wrapper =>
                {
                    // Don't use 5s, because we may wake up slightly earlier than that
                    timeToStartup.Elapsed.TotalSeconds.Should().BeGreaterOrEqualTo(4.9);
                    return BoolResult.SuccessTask;
                }).ShouldBeSuccess();
            },
            _ => new SlowResource(startupDelay: TimeSpan.FromSeconds(5)),
            configuration);
        }

        [Fact]
        public async Task SlowShutdownDoesntBlockUse()
        {
            var configuration = new ResourcePoolConfiguration()
            {
                MaximumAge = TimeSpan.FromSeconds(1),
                GarbageCollectionPeriod = Timeout.InfiniteTimeSpan,
            };

            var testTime = StopwatchSlim.Start();
            await RunTest<Key, SlowResource>(async (context, pool) =>
            {
                var key = new Key(0);

                var timeToStartup = StopwatchSlim.Start();
                await pool.UseAsync(context, key, wrapper =>
                {
                    timeToStartup.Elapsed.TotalSeconds.Should().BeLessThan(1);
                    return BoolResult.SuccessTask;
                }).ShouldBeSuccess();
            },
            _ => new SlowResource(shutdownDelay: TimeSpan.FromSeconds(5)),
            configuration);

            // We should bear with shutdown slowness as we dispose the instance
            testTime.Elapsed.TotalSeconds.Should().BeGreaterOrEqualTo(4.9);
        }

        [Fact]
        public async Task FailingStartupThrowsOnThreadAndDoesntCache()
        {
            CounterCollection<ResourcePoolCounters>? counters = null;

            var configuration = new ResourcePoolConfiguration()
            {
                MaximumAge = TimeSpan.FromSeconds(1),
                GarbageCollectionPeriod = Timeout.InfiniteTimeSpan,
            };

            await RunTest<Key, FailingResource>(async (context, pool) =>
            {
                counters = pool.Counter;

                var key = new Key(0);

                _ = await Assert.ThrowsAsync<ResultPropagationException>(() =>
                {
                    return pool.UseAsync<BoolResult>(context, key, wrapper =>
                    {
                        throw new NotImplementedException("This should not happen!");
                    });
                });

                // This is just to ensure that retrying effectively does a retry
                _ = await Assert.ThrowsAsync<ResultPropagationException>(() =>
                {
                    return pool.UseAsync<BoolResult>(context, key, wrapper =>
                    {
                        throw new NotImplementedException("This should not happen!");
                    });
                });
            },
            _ => new FailingResource(startupFailure: true),
            configuration);

            Contract.AssertNotNull(counters);

            // We do 2 attempts to create a failing resource, so 4 total
            counters[ResourcePoolCounters.ResourceInitializationAttempts].Value.Should().Be(2);
            counters[ResourcePoolCounters.ResourceInitializationFailures].Value.Should().Be(2);
            counters[ResourcePoolCounters.CreatedResources].Value.Should().Be(2);
            counters[ResourcePoolCounters.ReleasedResources].Value.Should().Be(2);
        }

        [Fact]
        public Task FailingStartupThrowsResultPropagationException()
        {
            var configuration = new ResourcePoolConfiguration();

            return RunTest<Key, FailingResource>(async (context, pool) =>
            {
                _ = await Assert.ThrowsAsync<ResultPropagationException>(() =>
                {
                    return pool.UseAsync(context, new Key(0), wrapper => BoolResult.SuccessTask);
                });
            },
            _ => new FailingResource(startupFailure: true),
            configuration);
        }

        [Fact]
        public async Task FailingShutdownDoesNotThrow()
        {
            CounterCollection<ResourcePoolCounters>? counters = null;

            var configuration = new ResourcePoolConfiguration()
            {
                MaximumAge = TimeSpan.FromSeconds(1),
                GarbageCollectionPeriod = Timeout.InfiniteTimeSpan,
            };

            await RunTest<Key, FailingResource>((context, pool) =>
            {
                counters = pool.Counter;

                var key = new Key(0);
                return pool.UseAsync(context, key, wrapper =>
                {
                    return BoolResult.SuccessTask;
                });
            },
            _ => new FailingResource(shutdownFailure: true),
            configuration);

            Contract.AssertNotNull(counters);

            counters[ResourcePoolCounters.ResourceInitializationAttempts].Value.Should().Be(1);
            counters[ResourcePoolCounters.ResourceInitializationFailures].Value.Should().Be(0);
            counters[ResourcePoolCounters.CreatedResources].Value.Should().Be(1);
            counters[ResourcePoolCounters.ReleasedResources].Value.Should().Be(1);
            counters[ResourcePoolCounters.ShutdownSuccesses].Value.Should().Be(0);
            counters[ResourcePoolCounters.ShutdownFailures].Value.Should().Be(1);
        }
    }
}
