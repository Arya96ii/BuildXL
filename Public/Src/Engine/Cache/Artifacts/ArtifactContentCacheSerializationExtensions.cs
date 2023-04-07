// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.Interfaces;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tasks;
using BuildXL.Utilities.Instrumentation.Common;
using Google.Protobuf;
using static BuildXL.Utilities.Core.FormattableStringEx;

namespace BuildXL.Engine.Cache.Artifacts
{
    /// <summary>
    /// Extensions on <see cref="IArtifactContentCache"/> for common serialization patterns
    /// - Serializing a structure and then storing it (yielding its hash).
    /// - Deserializing a structure by hash.
    /// </summary>
    public static class ArtifactContentCacheSerializationExtensions
    {
        /// <summary>
        /// This is a self-contained operation to obtain and deserialize a previously stored structure by its hash.
        /// First, this tries to load the given content with <see cref="IArtifactContentCache.TryLoadAvailableContentAsync"/>.
        /// If the content is available, returns a Protobuf-deserialized <typeparamref name="T"/>.
        /// If the content is unavailable, returns <c>null</c>.
        /// To store a structure (the inverse of this method), use <see cref="TrySerializeAndStoreContent{T}"/>.
        /// </summary>
        public static async Task<Possible<T, Failure>> TryLoadAndDeserializeContent<T>(
            this IArtifactContentCache contentCache,
            ContentHash contentHash,
            CancellationToken cancellationToken,
            BoxRef<long> contentSize = null)
            where T : IMessage<T>, new()
        {
            var maybeStream = await TryGetStreamFromContentHash(contentCache, contentHash, cancellationToken, contentSize);

            if (!maybeStream.Succeeded)
            {
                return maybeStream.Failure;
            }

            var stream = maybeStream.Result;

            if (stream == null)
            {
                return default(T);
            }

            using (stream)
            {
                // Use default protobuf deserializer
                return DeserializeWithInputStream<T>(stream);
            }
        }

        private static Possible<T, Failure> DeserializeWithInputStream<T>(Stream stream)
            where T : IMessage<T>, new()
        {
            try
            {
                return CacheGrpcExtensions.Deserialize<T>(stream);
            }
            catch (Exception ex)
            {
                return new Possible<T, Failure>(new DeserializationFailure(ex));
            }
        }

        /// <summary>
        /// Runs <see cref="TryLoadAndDeserializeContent{T}(IArtifactContentCache, ContentHash, CancellationToken, BoxRef{long})"/> with some retry logic.
        /// </summary>
        public static async Task<Possible<T, Failure>> TryLoadAndDeserializeContentWithRetry<T>(
            this IArtifactContentCache contentCache,
            LoggingContext loggingContext,
            ContentHash contentHash,
            Func<Possible<T, Failure>, bool> shouldRetry,
            CancellationToken cancellationToken,
            BoxRef<long> contentSize = null, int maxRetry = 1)
            where T : IMessage<T>, new()
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(shouldRetry != null);

            int retryCount = 0;
            Possible<T, Failure> result;

            do
            {
                result = await TryLoadAndDeserializeContent<T>(contentCache, contentHash, cancellationToken, contentSize);

                if (!shouldRetry(result))
                {
                    if (retryCount > 0)
                    {
                        Tracing.Logger.Log.RetryOnLoadingAndDeserializingMetadata(loggingContext, true, retryCount);
                    }

                    return result;
                }

                ++retryCount;
            }
            while (retryCount < maxRetry);

            if (retryCount > 1)
            {
                Tracing.Logger.Log.RetryOnLoadingAndDeserializingMetadata(loggingContext, false, retryCount);
            }

            return result;
        }

        /// <summary>
        /// Try load contants.
        /// </summary>
        public static async Task<Possible<byte[], Failure>> TryLoadContent(
            this IArtifactContentCache contentCache,
            ContentHash contentHash,
            CancellationToken cancellationToken,
            BoxRef<long> contentSize = null,
            bool failOnNonSeekableStream = false, int byteLimit = int.MaxValue)
        {
            var maybeStream = await TryGetStreamFromContentHash(contentCache, contentHash, cancellationToken, contentSize);

            if (!maybeStream.Succeeded)
            {
                return maybeStream.Failure;
            }

            var stream = maybeStream.Result;

            if (stream == null)
            {
                return default(byte[]);
            }

            try
            {
                MemoryStream memoryStream;
                Stream streamToRead;
                if (!stream.CanSeek)
                {
                    if (failOnNonSeekableStream)
                    {
                        return new Failure<string>("Stream is not seekable");
                    }

                    memoryStream = new MemoryStream();
                    streamToRead = memoryStream;
                }
                else
                {
                    memoryStream = null;
                    streamToRead = stream;
                }

                using (memoryStream)
                {
                    if (memoryStream != null)
                    {
                        await stream.CopyToAsync(memoryStream);
                        memoryStream.Position = 0;
                    }

                    Contract.Assert(streamToRead.CanSeek);

                    if (streamToRead.Length > byteLimit)
                    {
                        return new Failure<string>(I($"Stream exceeds limit: Length: {streamToRead.Length} byte(s) | Limit: {byteLimit} byte(s)"));
                    }

                    var length = (int)streamToRead.Length;
                    var contentBytesLocal = new byte[length];
                    int read = 0;
                    while (read < length)
                    {
                        int readThisIteration = await streamToRead.ReadAsync(contentBytesLocal, read, length - read);
                        if (readThisIteration == 0)
                        {
                            return new Failure<string>("Unexpected end of stream");
                        }

                        read += readThisIteration;
                    }

                    return contentBytesLocal;
                }
            }
            catch (Exception e)
            {
                return new Failure<string>(e.GetLogEventMessage());
            }
        }

        private static async Task<Possible<Stream, Failure>> TryGetStreamFromContentHash(
            IArtifactContentCache contentCache,
            ContentHash contentHash,
            CancellationToken cancellationToken,
            BoxRef<long> contentSize = null)
        {
            if (!EngineEnvironmentSettings.SkipExtraneousPins)
            {
                Possible<ContentAvailabilityBatchResult, Failure> maybeAvailable =
                    await contentCache.TryLoadAvailableContentAsync(new[] { contentHash }, cancellationToken);
                if (!maybeAvailable.Succeeded)
                {
                    return maybeAvailable.Failure;
                }

                bool contentIsAvailable = maybeAvailable.Result.AllContentAvailable;
                if (!contentIsAvailable)
                {
                    return default(Stream);
                }
            }

            var maybeStream = await contentCache.TryOpenContentStreamAsync(contentHash);

            if (!maybeStream.Succeeded)
            {
                if (maybeStream.Failure is NoCasEntryFailure)
                {
                    return default(Stream);
                }

                return maybeStream.Failure;
            }

            Stream stream = maybeStream.Result;
            if (contentSize != null)
            {
                contentSize.Value = stream.Length;
            }

            return stream;
        }

        /// <summary>
        /// Protobuf-serializes a given <typeparamref name="T"/> and stores the result to the content cache.
        /// The returned content hash can be used to later deserialize the structure with <see cref="TryLoadAndDeserializeContent{T}"/>.
        /// </summary>
        public static Task<Possible<ContentHash>> TrySerializeAndStoreContent<T>(
            this IArtifactContentCache contentCache,
            T valueToSerialize,
            BoxRef<long> contentSize = null,
            StoreArtifactOptions options = default) where T : IMessage<T>
        {
            return CacheGrpcExtensions.TrySerializeAndStoreContent(
                valueToSerialize,
                async (valueHash, valueBuffer) =>
                {
                    using (var entryStream = new MemoryStream(
                        valueBuffer.Array,
                        valueBuffer.Offset,
                        valueBuffer.Count,
                        writable: false))
                    {
                        Possible<Unit, Failure> maybeStored = await contentCache.TryStoreAsync(
                            entryStream,
                            contentHash: valueHash,
                            options: options);

                        return maybeStored.WithGenericFailure();
                    }
                },
                contentSize);
        }

        /// <nodoc />
        public class DeserializationFailure : Failure
        {
            private readonly ExceptionDispatchInfo m_exceptionDispatchInfo;

            /// <nodoc />
            public DeserializationFailure(Exception exception)
            {
                Contract.Requires(exception != null);
                m_exceptionDispatchInfo = ExceptionDispatchInfo.Capture(exception);
            }

            /// <inheritdoc/>
            public override BuildXLException Throw()
            {
                m_exceptionDispatchInfo.Throw();
                throw new InvalidOperationException("Unreachable");
            }

            /// <inheritdoc/>
            public override BuildXLException CreateException()
            {
                return new BuildXLException("Failed to deserialize metadata.", m_exceptionDispatchInfo.SourceException);
            }

            /// <inheritdoc/>
            public override string Describe()
            {
                return m_exceptionDispatchInfo.SourceException.ToString();
            }
        }
    }
}
