// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Core;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    /// <summary>
    /// Creates a gRPC connection to the CASaaS master, and returns a client that implements <typeparamref name="T"/>
    /// by talking via gRPC with it.
    /// </summary>
    public class MasterClientFactory<T> : StartupShutdownComponentBase, IFixedClientAccessor<T>
        where T : class
    {
        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(MasterClientFactory<T>));

        /// <inheritdoc />
        public MachineLocation Location
        {
            get
            {
                try
                {
                    return _currentMasterLocationLazy.Value.Value;
                }
                catch
                {
#pragma warning disable ERP022 // Swallowing an unobserved exception on purpose. This is only used for telemetry.
                    return MachineLocation.Invalid;
#pragma warning restore ERP022
                }
            }
        }

        private readonly IMasterElectionMechanism _masterElectionMechanism;
        private readonly IClientAccessor<MachineLocation, T> _clientAccessor;

        private AsyncLazy<Result<MachineLocation>> _currentMasterLocationLazy = AsyncLazy<Result<MachineLocation>>.FromResult(null);

        public MasterClientFactory(
            IClientAccessor<MachineLocation, T> clientAccessor,
            IMasterElectionMechanism masterElectionMechanism)
        {
            _masterElectionMechanism = masterElectionMechanism;
            _clientAccessor = clientAccessor;
            LinkLifetime(clientAccessor);
        }

        private async ValueTask<MachineLocation> GetOrQueryMasterLocationAsync(OperationContext context)
        {
            var lazy = _currentMasterLocationLazy;
            var handleResult = await lazy.GetValueAsync();
            var masterMachineLocation = handleResult?.GetValueOrDefault();

            if (masterMachineLocation is null || !masterMachineLocation.Value.IsValid || !_masterElectionMechanism.Master.Equals(masterMachineLocation.Value))
            {
                handleResult = await context.PerformOperationAsync(
                    Tracer,
                    async () =>
                    {
                        AsyncLazy<Result<MachineLocation>> oldValue = Interlocked.CompareExchange(
                            ref _currentMasterLocationLazy,
                            new AsyncLazy<Result<MachineLocation>>(() => QueryMasterLocationAsync(context)),
                            lazy);

                        lazy = _currentMasterLocationLazy;
                        return await lazy.GetValueAsync();
                    },
                    extraEndMessage: r => r.GetValueOrDefault().ToString());
            }

            return handleResult.ThrowIfFailure();
        }

        private Task<Result<MachineLocation>> QueryMasterLocationAsync(OperationContext context)
        {
            return context.PerformOperationAsync<Result<MachineLocation>>(
                Tracer,
                async () =>
                {
                    var masterElectionState = await _masterElectionMechanism.GetRoleAsync(context);
                    if (masterElectionState.Succeeded)
                    {
                        if (!masterElectionState.Value.Master.IsValid)
                        {
                            return new ErrorResult("Unknown master");
                        }

                        return masterElectionState.Value.Master;
                    }
                    else
                    {
                        return new ErrorResult(masterElectionState);
                    }
                },
                extraEndMessage: r => $"Master={r.GetValueOrDefault()}");
        }

        public async Task<TResult> UseAsync<TResult>(OperationContext context, Func<T, Task<TResult>> operation)
        {
            var masterMachineLocation = await GetOrQueryMasterLocationAsync(context);
            return await _clientAccessor.UseAsync(context, masterMachineLocation, operation);
        }
    }
}
