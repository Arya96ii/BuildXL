// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;

#nullable disable // The design of this type is not null friendly.

namespace BuildXL.Cache.ContentStore.Tracing
{
    /// <summary>
    ///     Machinery for tracing the execution of some call/code.
    /// </summary>
    public abstract class TracedCall<TTracer, TResult>
        where TResult : ResultBase
    {
        private readonly Stopwatch _stopwatch = new Stopwatch();

        /// <summary>
        ///     The tracer instance.
        /// </summary>
        protected TTracer Tracer { get; }

        /// <summary>
        ///     The call tracing context.
        /// </summary>
        protected Context Context { get; }

        /// <summary>
        ///     An optional cancellation token for the current operation.
        /// </summary>
        protected CancellationToken Token { get; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="TracedCall{TTracer, TResult}"/> class without a counter.
        /// </summary>
        protected TracedCall(TTracer tracer, Context context)
        {
            Tracer = tracer;
            Context = context;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="TracedCall{TTracer, TResult}"/> class without a counter.
        /// </summary>
        protected TracedCall(TTracer tracer, OperationContext context)
        {
            Tracer = tracer;
            Context = context;
            Token = context.Token;
        }

        /// <summary>
        ///     Gets result of the called code.
        /// </summary>
        protected TResult Result { get; private set; }

        /// <summary>
        ///     Create a result given an exception that has occurred.
        /// </summary>
        protected abstract TResult CreateErrorResult(Exception exception);

        /// <summary>
        ///     Run the call/code.
        /// </summary>
        protected internal async Task<TResult> RunAsync(Func<Task<TResult>> asyncFunc)
        {
            try
            {
                _stopwatch.Start();
                Result = await asyncFunc();
            }
            catch (Exception exception)
            {
                Result = CreateErrorResult(exception);

                if (Token.IsCancellationRequested && ResultBase.NonCriticalForCancellation(exception))
                {
                    Result.MarkCancelled();
                }
            }
            finally
            {
                _stopwatch.Stop();
                Result!.SetDuration(_stopwatch.Elapsed);
            }

            return Result;
        }

        /// <summary>
        ///     Run the call/code.
        /// </summary>
        protected TResult Run(Func<TResult> func)
        {
            try
            {
                _stopwatch.Start();
                Result = func();
            }
            catch (Exception exception)
            {
                Result = CreateErrorResult(exception);

                if (Token.IsCancellationRequested && ResultBase.NonCriticalForCancellation(exception))
                {
                    Result.MarkCancelled();
                }
            }
            finally
            {
                _stopwatch.Stop();
                Result.SetDuration(_stopwatch.Elapsed);
            }

            return Result;
        }
    }
}
