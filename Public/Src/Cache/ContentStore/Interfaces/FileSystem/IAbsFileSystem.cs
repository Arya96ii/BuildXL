// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;

namespace BuildXL.Cache.ContentStore.Interfaces.FileSystem;

/// <summary>
/// An interface for a file system. This exists mainly for testing.
/// </summary>
public interface IAbsFileSystem : IDisposable
{
    /// <summary>
    ///     enumerates the files (does not include directories) underneath the input directory path
    /// </summary>
    /// <param name="path">Path to directory</param>
    /// <param name="options">options in how to enumerate; can be null to use defaults</param>
    /// <returns>the set of files matching the enumerate options under the path</returns>
    IEnumerable<FileInfo> EnumerateFiles(AbsolutePath path, EnumerateOptions options);

    /// <summary>
    ///     Check if named directory exists.
    /// </summary>
    /// <param name="path">Path to directory</param>
    /// <returns>true if directory exists; false otherwise.</returns>
    bool DirectoryExists(AbsolutePath path);

    /// <summary>
    ///     Check if named file exists.
    /// </summary>
    /// <param name="path">Path to file</param>
    /// <returns>true if file exists; false otherwise.</returns>
    bool FileExists(AbsolutePath path);

    /// <summary>
    ///     Read the contents of an existing file.
    /// </summary>
    /// <param name="path">Path to the file.</param>
    /// <returns>All contents of the existing file.</returns>
    byte[] ReadAllBytes(AbsolutePath path);

    /// <summary>
    /// [Obsolete] Creates a stream to an existing file.
    /// Note that the permissions are such that it is readable, but not
    /// writable by the caller.
    /// Please use <see cref="AbsFileSystemExtension.TryOpenReadOnly"/> instead.
    /// </summary>
    /// <param name="path">The path to open a stream to.</param>
    /// <param name="share">the file sharing permissions for the given path</param>
    /// <returns>A stream to the file that is requested</returns>
    Task<StreamWithLength?> OpenReadOnlyAsync(AbsolutePath path, FileShare share);

    /// <summary>
    ///     Create a new directory.
    /// </summary>
    /// <param name="path">Path to new directory</param>
    /// <remarks>
    ///     An exception is thrown on error.
    /// </remarks>
    void CreateDirectory(AbsolutePath path);

    /// <summary>
    ///     Delete an existing directory.
    /// </summary>
    /// <param name="path">Path to directory</param>
    /// <param name="deleteOptions">Options for delete operation which can be null to use defaults</param>
    /// <remarks>
    ///     Default behavior is to not recurse and not delete read-only files.
    ///     An exception is thrown on error.
    /// </remarks>
    void DeleteDirectory(AbsolutePath path, DeleteOptions deleteOptions);

    /// <summary>
    ///     Delete an existing file.
    /// </summary>
    /// <param name="path">Path to file</param>
    /// <remarks>
    ///     No op if the file does not exist.
    /// </remarks>
    void DeleteFile(AbsolutePath path);

    /// <summary>
    ///     Write the contents of a new file or overwrite the contents of an existing file.
    /// </summary>
    /// <param name="path">Path to the file.</param>
    /// <param name="content">Contents to replace any existing content.</param>
    void WriteAllBytes(AbsolutePath path, byte[] content);

    /// <summary>
    ///     Move a file from one location to another.
    /// </summary>
    /// <param name="sourceFilePath">Path to source file.</param>
    /// <param name="destinationFilePath">Path to destination file.</param>
    /// <param name="replaceExisting">Replace a file if it already exists at the destination path.</param>
    void MoveFile(AbsolutePath sourceFilePath, AbsolutePath destinationFilePath, bool replaceExisting);

    /// <summary>
    ///     Rename a directory.
    /// </summary>
    /// <param name="sourcePath">Path to existing directory.</param>
    /// <param name="destinationPath">Path to new, not yet existing directory.</param>
    void MoveDirectory(AbsolutePath sourcePath, AbsolutePath destinationPath);

    /// <summary>
    ///     [Obsolete] Open the named file asynchronously for reading.
    /// </summary>
    /// <param name="path">Path to the existing file that is to be read.</param>
    /// <param name="fileAccess">Read, write, or both</param>
    /// <param name="fileMode">File creation options</param>
    /// <param name="share">Control of other object access to the same file.</param>
    /// <param name="options">Minimum required options.</param>
    /// <param name="bufferSize">Size of the stream's buffer.</param>
    /// <returns>Null if the file or directory does not exist, otherwise the stream.</returns>
    /// <remarks>
    /// Unlike System.IO.FileStream, this provides a way to atomically check for the existence of a file and open it.
    /// This method throws the same set of exceptions that <see cref="FileStream"/> constructor does.
    ///
    /// The method is obsolete, because there is no asynchrony in it. Please use <see cref="TryOpen"/>
    /// </remarks>
    Task<StreamWithLength?> OpenAsync(AbsolutePath path, FileAccess fileAccess, FileMode fileMode, FileShare share, FileOptions options, int bufferSize);

    /// <summary>
    ///     Open the named file asynchronously for reading.
    /// </summary>
    /// <param name="path">Path to the existing file that is to be read.</param>
    /// <param name="fileAccess">Read, write, or both</param>
    /// <param name="fileMode">File creation options</param>
    /// <param name="share">Control of other object access to the same file.</param>
    /// <param name="options">Minimum required options.</param>
    /// <param name="bufferSize">Size of the stream's buffer.</param>
    /// <returns>Null if the file or directory does not exist, otherwise the stream.</returns>
    /// <remarks>
    /// Unlike System.IO.FileStream, this provides a way to atomically check for the existence of a file and open it.
    /// This method throws the same set of exceptions that <see cref="FileStream"/> constructor does.
    /// </remarks>
    /// <exception cref="IOException">An I/O error occurred.</exception>
    /// <exception cref="UnauthorizedAccessException">
    /// Unlike <see cref="FileStream"/>'s ctor, this method fails with <see cref="UnauthorizedAccessException"/> in case of
    /// sharing violation and not with <see cref="IOException"/>. Plus it tries to find an active process that owns the handle and add such information into the error's text message.
    /// </exception>
    StreamWithLength? TryOpen(AbsolutePath path, FileAccess fileAccess, FileMode fileMode, FileShare share, FileOptions options, int bufferSize);

    /// <summary>
    ///     Copy a file from one path to another.
    /// </summary>
    Task CopyFileAsync(AbsolutePath sourcePath, AbsolutePath destinationPath, bool replaceExisting);

    /// <summary>
    ///     Copy a file from one path to another synchronously.
    /// </summary>
    void CopyFile(AbsolutePath sourcePath, AbsolutePath destinationPath, bool replaceExisting);

    /// <summary>
    ///     Get the attributes of a file.
    /// </summary>
    /// <param name="path">Path to the file</param>
    /// <returns>Attributes of the file</returns>
    FileAttributes GetFileAttributes(AbsolutePath path);

    /// <summary>
    ///     Set the attributes of a file.
    /// </summary>
    /// <param name="path">Path to the file</param>
    /// <param name="attributes">Attributes to set</param>
    void SetFileAttributes(AbsolutePath path, FileAttributes attributes);

    /// <summary>
    ///     Checks whether the attributes of the file (including any "unsupported" ones) are a subset of the given attributes
    /// </summary>
    /// <param name="path">Path to the file</param>
    /// <param name="attributes">Attributes to check against</param>
    /// <returns>If the file's attributes are a subset</returns>
    bool FileAttributesAreSubset(AbsolutePath path, FileAttributes attributes);

    /// <summary>
    ///     Enumerates directories under the given path
    /// </summary>
    /// <param name="path">Root path under which directories are enumerated.</param>
    /// <param name="options">Whether to recurse or not.</param>
    /// <returns>The directories under the root.</returns>
    IEnumerable<AbsolutePath> EnumerateDirectories(AbsolutePath path, EnumerateOptions options);

    /// <summary>
    /// Enumerates files under the given <paramref name="path"/> and calls the <paramref name="fileHandler"/> for every file that matches the given <paramref name="pattern"/>.
    /// </summary>
    /// <exception cref="IOException">Throw if IO error occurs.</exception>
    /// <remarks>
    /// Unlike <see cref="EnumerateDirectories"/> this method uses push-approach (callback-based) instead of using pull-based approach (based on IEnumerable).
    /// This is an example of leaky abstraction because the underlying layer is implemented based on callbacks as well.
    /// </remarks>
    void EnumerateFiles(AbsolutePath path, string pattern, bool recursive, Action<FileInfo> fileHandler);

    /// <summary>
    ///     Creates a hard link pointing to an existing file.
    /// </summary>
    /// <param name="sourceFileName">Path to existing file</param>
    /// <param name="destinationFileName">Path to new file</param>
    /// <param name="replaceExisting">True to overwrite a file at the destination.</param>
    /// <returns>A CreateHardLinkResult enum with the result of the operation</returns>
    CreateHardLinkResult CreateHardLink(AbsolutePath sourceFileName, AbsolutePath destinationFileName, bool replaceExisting);

    /// <summary>
    ///     Gets the number of hard links to the file at the path.
    /// </summary>
    /// <param name="path">Path to file</param>
    /// <returns>Number of hard links to the file</returns>
    /// <remarks>Throws if the file does not exist</remarks>
    int GetHardLinkCount(AbsolutePath path);

    /// <summary>
    ///     Gets the volume unique file id of the file at the path.
    /// </summary>
    /// <param name="path">Path to file</param>
    /// <returns>Id of the file</returns>
    /// <remarks>Throws if the file does not exist</remarks>
    ulong GetFileId(AbsolutePath path);

    /// <summary>
    ///     Gets the size of a file in bytes.
    /// </summary>
    /// <param name="path">Path to the file.</param>
    /// <returns>Size of a file in bytes.</returns>
    long GetFileSize(AbsolutePath path);

    /// <summary>
    ///     Gets the size of each cluster in bytes.
    /// </summary>
    /// <param name="path">Path to the disk.</param>
    /// <returns>Size of a cluster in bytes.</returns>
    long GetClusterSize(AbsolutePath path);

    /// <summary>
    ///     Gets the last access time of the file.
    /// </summary>
    /// <param name="path">Path to the file.</param>
    /// <returns>last access time</returns>
    /// <remarks>Is not automatically set depending on NTFS volume settings.</remarks>
    DateTime GetLastAccessTimeUtc(AbsolutePath path);

    /// <summary>
    ///     Sets the last access time of the file.
    /// </summary>
    /// <param name="path">Path to the file.</param>
    /// <param name="lastAccessTimeUtc">Last access time</param>
    /// <remarks>Is not automatically set depending on NTFS volume settings.</remarks>
    void SetLastAccessTimeUtc(AbsolutePath path, DateTime lastAccessTimeUtc);

    /// <summary>
    ///     Add an ACL on the given file which disallows writing or appending data.
    /// </summary>
    /// <param name="path">Path to the file</param>
    /// <param name="disableInheritance">Whether to disable inheritance for the file.</param>
    void DenyFileWrites(AbsolutePath path, bool disableInheritance = false);

    /// <summary>
    ///     Add an ACL on the given file which allows writing or appending data.
    /// </summary>
    /// <param name="path">Path to the file</param>
    void AllowFileWrites(AbsolutePath path);

    /// <summary>
    ///     Add an ACL on the given file which disallows writing attributes.
    /// </summary>
    /// <param name="path">Path to the file</param>
    void DenyAttributeWrites(AbsolutePath path);

    /// <summary>
    ///     Add an ACL on the given file which allows writing attributes.
    /// </summary>
    /// <param name="path">Path to the file</param>
    void AllowAttributeWrites(AbsolutePath path);

    /// <summary>
    ///     Get the temporary directory.
    /// </summary>
    AbsolutePath GetTempPath();

    // ReSharper disable once UnusedMember.Global

    /// <summary>
    ///     Flushes all disk buffers associated with the volume.
    /// </summary>
    /// <param name="driveLetter">Drive letter of the volume to flush</param>
    void FlushVolume(char driveLetter);

    /// <summary>
    ///     Get information on the volume hosting the given path.
    /// </summary>
    VolumeInfo GetVolumeInfo(AbsolutePath path);

    /// <summary>
    ///     Gets the creation time of the directory, in UTC
    /// </summary>
    DateTime GetDirectoryCreationTimeUtc(AbsolutePath path);

    /// <summary>
    ///     Disables audit rule inheritance for a given file.
    /// </summary>
    void DisableAuditRuleInheritance(AbsolutePath path);

    /// <summary>
    ///     Whether file access rule inheritance is disabled for a given file.
    /// </summary>
    bool IsAclInheritanceDisabled(AbsolutePath path);
}
