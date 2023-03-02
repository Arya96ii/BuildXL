﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Serialization;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Metadata entry for memoization stores.
    /// </summary>
    public readonly struct MetadataEntry
    {
        /// <summary>
        /// Effective <see cref="ContentHashList"/> that we want to store, along with information about its cache
        /// determinism.
        /// </summary>
        public ContentHashListWithDeterminism ContentHashListWithDeterminism { get; }

        /// <summary>
        /// Last update time
        /// </summary>
        public DateTime LastAccessTimeUtc { get; }

        /// <nodoc />
        public MetadataEntry(ContentHashListWithDeterminism contentHashListWithDeterminism, DateTime lastAccessTimeUtc)
        {
            ContentHashListWithDeterminism = contentHashListWithDeterminism;
            LastAccessTimeUtc = lastAccessTimeUtc;
        }

        /// <nodoc />
        public static MetadataEntry Deserialize(BuildXLReader reader)
        {
            var lastUpdateTimeUtc = reader.ReadInt64Compact();
            var contentHashListWithDeterminism = ContentHashListWithDeterminism.Deserialize(reader);
            return new MetadataEntry(contentHashListWithDeterminism, DateTime.FromFileTimeUtc(lastUpdateTimeUtc));
        }

        /// <nodoc />
        public static MetadataEntry Deserialize(ref SpanReader reader)
        {
            var lastUpdateTimeUtc = reader.ReadInt64Compact();
            var contentHashListWithDeterminism = ContentHashListWithDeterminism.Deserialize(ref reader);
            return new MetadataEntry(contentHashListWithDeterminism, DateTime.FromFileTimeUtc(lastUpdateTimeUtc));
        }

        /// <nodoc />
        public static long DeserializeLastAccessTimeUtc(ref SpanReader reader)
        {
            return reader.ReadInt64Compact();
        }

        /// <nodoc />
        public void Serialize(BuildXLWriter writer)
        {
            writer.WriteCompact(LastAccessTimeUtc.ToFileTimeUtc());
            ContentHashListWithDeterminism.Serialize(writer);
        }

        /// <nodoc />
        public void Serialize(ref SpanWriter writer)
        {
            writer.WriteCompact(LastAccessTimeUtc.ToFileTimeUtc());
            ContentHashListWithDeterminism.Serialize(ref writer);
        }
    }
}
