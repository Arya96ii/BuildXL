// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Utilities.Core.Tasks;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using Azure.Messaging.EventHubs.Consumer;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using Azure.Identity;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming
{
    /// <summary>
    /// An event hub client which interacts with Azure Event Hub service
    /// </summary>
    public class AzureEventHubClient : StartupShutdownSlimBase, IEventHubClient
    {
        private const string PartitionId = "0";

        private readonly EventHubContentLocationEventStoreConfiguration _configuration;

        private EventHubProducerClient _partitionSender;
        private PartitionReceiverWrapper _partitionReceiver;

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(AzureEventHubClient));

        /// <nodoc />
        public AzureEventHubClient(EventHubContentLocationEventStoreConfiguration configuration)
        {
            _configuration = configuration;
        }

        /// <inheritdoc />
        public BoolResult StartProcessing(OperationContext context, EventSequencePoint sequencePoint, IPartitionReceiveHandler processor)
        {
            Tracer.Info(context, $"{Tracer.Name}: Initializing event processing for event hub '{_configuration.EventHubName}' and consumer group '{_configuration.ConsumerGroupName}'.");

            if (_partitionReceiver == null)
            {
                if (ManagedIdentityUriHelper.TryParseForManagedIdentity(_configuration.EventHubConnectionString, out string eventHubNamespace, out string eventHubName, out string managedIdentityId))
                {
                    _partitionReceiver = new PartitionReceiverWrapper(
                        _configuration.ConsumerGroupName,
                        PartitionId,
                        GetInitialOffset(context, sequencePoint),
                        eventHubNamespace,
                        eventHubName,
                        new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = managedIdentityId }));
                }
                else
                {
                    _partitionReceiver = new PartitionReceiverWrapper(_configuration.ConsumerGroupName, PartitionId, GetInitialOffset(context, sequencePoint), _configuration.EventHubConnectionString, _configuration.EventHubName);
                }
                _partitionReceiver.SetReceiveHandler(context, processor);
            }

            return BoolResult.Success;
        }

        /// <inheritdoc />
        public BoolResult SuspendProcessing(OperationContext context)
        {
            // In unit tests, hangs sometimes occur for this when running multiple tests in sequence.
            // Adding a timeout to detect when this occurs
            if (_partitionReceiver != null)
            {
                _partitionReceiver.CloseAsync().WithTimeoutAsync(TimeSpan.FromMinutes(1)).GetAwaiter().GetResult();
                _partitionReceiver = null;
            }

            return BoolResult.Success;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            await base.StartupCoreAsync(context).ThrowIfFailure();

            // Retry behavior in the Azure Event Hubs Client Library is controlled by the RetryPolicy property on the EventHubClient class.
            // The default policy retries with exponential backoff when Azure Event Hub returns a transient EventHubsException or an OperationCanceledException.
            if (ManagedIdentityUriHelper.TryParseForManagedIdentity(_configuration.EventHubConnectionString, out string eventHubNamespace, out string eventHubName, out string managedIdentityId))
            {
                _partitionSender = new EventHubProducerClient(eventHubNamespace, eventHubName, new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = managedIdentityId }));
            }
            else
            {
                _partitionSender = new EventHubProducerClient(_configuration.EventHubConnectionString, _configuration.EventHubName);
            }

            return BoolResult.Success;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            SuspendProcessing(context).ThrowIfFailure();

            if (_partitionSender != null)
            {
                await _partitionSender.CloseAsync();
            }

            if (_partitionReceiver != null)
            {
                await _partitionReceiver.CloseAsync();
            }

            return await base.ShutdownCoreAsync(context);
        }

        /// <inheritdoc />
        public async Task SendAsync(OperationContext context, EventData eventData)
        {
            context.Token.ThrowIfCancellationRequested();
            try
            {
                // Each element in a separate call because partial success is not possible
                var eventDataAsList = new List<EventData> {eventData};
                var sendEventOptions = new SendEventOptions
                {
                    PartitionId = PartitionId
                };
                await _partitionSender.SendAsync(eventDataAsList, sendEventOptions);
            }
            catch (InvalidOperationException) when(context.Token.IsCancellationRequested || ShutdownStarted)
            {
                // We started shutting down the instance. The operation may fail in this case.
                // Don't re-throw any errors. All the state changes that were not delivered would be resent during reconciliation process.
            }
        }

        private EventPosition GetInitialOffset(OperationContext context, EventSequencePoint sequencePoint)
        {
            Tracer.Debug(context, $"Consuming events from '{sequencePoint}'.");
            Contract.Requires(sequencePoint.EventStartCursorTimeUtc != null || sequencePoint.SequenceNumber != null);

            var position = sequencePoint.SequenceNumber is not null
                ? EventPosition.FromSequenceNumber(sequencePoint.SequenceNumber!.Value)
                : EventPosition.FromEnqueuedTime(sequencePoint.EventStartCursorTimeUtc!.Value);

            return position; ;
        }
    }
}
