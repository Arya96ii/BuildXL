﻿using System.Collections.Generic;
using System.ComponentModel;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// Dedup type for dedupidentifiers.  Specifies the type of blob.
    /// They denote the last byte in the hash/dedup identifier.
    /// Hash 32 bytes + 1 byte (dedup type).
    /// </summary>
    public static class AlgorithmId 
    {
        /// <summary>
        /// Dedup file
        /// </summary>
        public const byte File = 0;
        /// <summary>
        /// Dedup chunk
        /// </summary>
        public const byte Chunk = 1;
        /// <summary>
        /// Dedup node
        /// </summary>
        public const byte Node = 2;
    }
}
