// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
#if !NET_STANDARD_20

using System.Threading.Tasks;

namespace BuildXL.Utilities.Core.Tasks
{
    /// <summary>
    /// Factory class for constructing <see cref="ValueTask{TResult}"/>.
    /// </summary>
    public static class ValueTaskFactory
    {
        /// <nodoc />
        public static ValueTask<T> FromResult<T>(T value)
        {
            return new ValueTask<T>(value);
        }
    }
}

#endif