// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Defines the modes that can be used for determining the filesystem used by the ObservedInputProcessor
    /// </summary>
    public enum FileSystemMode
    {
        /// <summary>
        /// The option is not set and a dynamic value will be used depending on whether the build is DScript.
        /// </summary>
        Unset,

        /// <summary>
        /// Uses the legacy rules. The real filesystem is used for probes and enumerations on non-writeable mounts. Writeable
        /// mounts rely on the full pip graph for a view of the filesystem. This is a tradeoff between honoring reality
        /// with the real filesystem, and preventing churn of fingerprints which would happen if the actual filesystem were
        /// used for output directories. If output directories were to use the real filesystem, the directory fingerprint would
        /// change depending on how many pips had run. Overlaying that with the full pip graph filesystem allows a good
        /// approximation for rerunning tools when directories they consume would change, even if they don't declar a dependency.
        /// </summary>
        RealAndPipGraph,

        /// <summary>
        /// Same as Legacy except PipGraph based queries use a Minimal pip graph view rather than the full PipGraph.
        /// This allows a pip to have a consistent view of a filesystem regardless of how much of the graph is scheduled. This
        /// allows partial evaluation to not churn fingerprints. But it means that a pip may not rerun if it enumerates a
        /// directory that another pip in the same build writes to (if the consuming pip does not declare the dependency)
        /// </summary>
        RealAndMinimalPipGraph,

        /// <summary>
        /// Always uses the minimal PipGraph. This gives the most protection from fingerprint churn and enables the most caching.
        /// But tools that enumerate directories (like robocopy) may not always rerun when their input directories change, if they
        /// don't explicitely declare dependencies on the files they consume.
        /// </summary>
        AlwaysMinimalGraph,

        /// <summary>
        /// Always use MinimalWithAlienFilesGraph for determining directory fingerprints. This is a stricter version of <see cref="AlwaysMinimalGraph"/>
        /// where files alien to the build (including undeclared source reads) are also included. Known outputs that are not part of the immediate
        /// dependencies are excluded, which matches the behavior of PipGraph.
        /// The main use case is avoiding underbuilds when a directory change wrt undeclared source reads.
        /// </summary>
        AlwaysMinimalWithAlienFilesGraph,

        /// <summary>
        /// Always use the empty fingerprint for enumerating directories. This mode essentially means that directory enumerations are ignored for computing
        /// a pip fingerprint.
        /// </summary>
        AlwaysEmpty,
    }
}
