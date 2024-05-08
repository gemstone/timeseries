﻿//******************************************************************************************************
//  LogicalThread.cs - Gbtc
//
//  Copyright © 2015, Grid Protection Alliance.  All Rights Reserved.
//
//  Licensed to the Grid Protection Alliance (GPA) under one or more contributor license agreements. See
//  the NOTICE file distributed with this work for additional information regarding copyright ownership.
//  The GPA licenses this file to you under the MIT License (MIT), the "License"; you may
//  not use this file except in compliance with the License. You may obtain a copy of the License at:
//
//      http://opensource.org/licenses/MIT
//
//  Unless agreed to in writing, the subject software distributed under the License is distributed on an
//  "AS-IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. Refer to the
//  License for the specific language governing permissions and limitations.
//
//  Code Modification History:
//  ----------------------------------------------------------------------------------------------------
//  09/24/2015 - Stephen C. Wills
//       Generated original version of source code.
//  02/16/2016 - J. Ritchie Carroll
//       Changed default priority level to 1 - normal / lowest.
//
//******************************************************************************************************

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Gemstone.Timeseries;

/// <summary>
/// Represents a set of statistics gathered about
/// the execution time of actions on a logical thread.
/// </summary>
// TODO: Change LogicalThread to Gemstone.Threading.Strand
public class LogicalThreadStatistics
{
    #region [ Members ]

    // Fields
    private TimeSpan m_maxExecutionTime;
    private TimeSpan m_minExecutionTime;
    private TimeSpan m_totalExecutionTime;
    private long m_executionCount;

    #endregion

    #region [ Properties ]

    /// <summary>
    /// Gets the execution time of the longest running action.
    /// </summary>
    public TimeSpan MaxExecutionTime
    {
        get
        {
            return m_maxExecutionTime;
        }
    }

    /// <summary>
    /// Gets the execution time of the shortest running action.
    /// </summary>
    public TimeSpan MinExecutionTime
    {
        get
        {
            return m_minExecutionTime;
        }
    }

    /// <summary>
    /// Gets the total execution time of all actions executed on the logical thread.
    /// </summary>
    public TimeSpan TotalExecutionTime
    {
        get
        {
            return m_totalExecutionTime;
        }
    }

    /// <summary>
    /// Gets the total number of actions executed on the logical thread.
    /// </summary>
    public long ExecutionCount
    {
        get
        {
            return m_executionCount;
        }
    }

    #endregion

    #region [ Methods ]

    /// <summary>
    /// Updates the statistics based on the execution time of a single action.
    /// </summary>
    /// <param name="executionTime">The execution time of the action.</param>
    internal void UpdateStatistics(TimeSpan executionTime)
    {
        if (m_executionCount == 0 || executionTime > m_maxExecutionTime)
            m_maxExecutionTime = executionTime;

        if (m_executionCount == 0 || executionTime < m_minExecutionTime)
            m_minExecutionTime = executionTime;

        m_totalExecutionTime += executionTime;
        m_executionCount++;
    }

    #endregion
}

/// <summary>
/// Represents a thread of execution to which
/// actions can be dispatched from other threads.
/// </summary>
/// <remarks>
/// <para>
/// This class provides a simple alternative to synchronization primitives
/// such as wait handles and locks. Actions dispatched to a logical thread
/// will be processed synchronously as though it was executed as consecutive
/// method calls. All such actions can be dispatched from any thread in the
/// system so method calls coming from multiple threads can be easily
/// synchronized without locks, loops, wait handles, or timeouts.
/// </para>
/// 
/// <para>
/// Note that the <see cref="LogicalThreadScheduler"/> implements its own
/// thread pool to execute tasks pushed to logical threads. Executing
/// long-running processes or using synchronization primitives with high
/// contention or long timeouts can hinder the logical thread scheduler's
/// ability to schedule the actions of other logical threads. Like other
/// thread pool implementations, you can mitigate this by increasing the
/// maximum thread count of the logical thread scheduler, however it is
/// recommended to avoid using synchronization primitives and instead
/// synchronize those operations by running them as separate actions on
/// the same logical thread.
/// </para>
/// </remarks>
public class LogicalThread
{
    #region [ Members ]

    // Events

    /// <summary>
    /// Handler for unhandled exceptions on the thread.
    /// </summary>
    public event EventHandler<EventArgs<Exception>> UnhandledException;

    // Fields
    private LogicalThreadScheduler m_scheduler;
    private ConcurrentQueue<Action>[] m_queues;
    private Dictionary<object, object> m_threadLocalStorage;
    //private ICancellationToken m_nextExecutionToken;
    private int m_activePriority;

    private LogicalThreadStatistics m_statistics;

    #endregion

    #region [ Constructors ]

    /// <summary>
    /// Creates a new instance of the <see cref="LogicalThread"/> class.
    /// </summary>
    public LogicalThread()
        : this(DefaultScheduler)
    {
    }

    /// <summary>
    /// Creates a new instance of the <see cref="LogicalThread"/> class.
    /// </summary>
    /// <param name="scheduler">The <see cref="LogicalThreadScheduler"/> that created this thread.</param>
    /// <exception cref="ArgumentNullException"><paramref name="scheduler"/> is null.</exception>
    internal LogicalThread(LogicalThreadScheduler scheduler)
    {
        if ((object)scheduler == null)
            throw new ArgumentNullException(nameof(scheduler));

        m_scheduler = scheduler;
        m_queues = new ConcurrentQueue<Action>[PriorityLevels];
        m_threadLocalStorage = new Dictionary<object, object>();
        //m_nextExecutionToken = new CancellationToken();
        m_statistics = new LogicalThreadStatistics();

        for (int i = 0; i < m_queues.Length; i++)
            m_queues[i] = new ConcurrentQueue<Action>();
    }

    #endregion

    #region [ Properties ]

    /// <summary>
    /// Gets the number of levels of priority
    /// supported by this logical thread.
    /// </summary>
    public int PriorityLevels
    {
        get
        {
            return m_scheduler.PriorityLevels;
        }
    }

    /// <summary>
    /// Gets a flag that indicates whether the logical
    /// thread has any unprocessed actions left in its queue.
    /// </summary>
    public bool HasAction
    {
        get
        {
            return m_queues.Any(queue => !queue.IsEmpty);
        }
    }

    /// <summary>
    /// Gets or sets the priority at which the
    /// logical thread is queued by the scheduler.
    /// </summary>
    internal int ActivePriority
    {
        get
        {
            return Interlocked.CompareExchange(ref m_activePriority, 0, 0);
        }
        set
        {
            Interlocked.Exchange(ref m_activePriority, value);
        }
    }

    /// <summary>
    /// Gets the priority of the next action to be processed on this logical thread.
    /// </summary>
    internal int NextPriority
    {
        get
        {
            return PriorityLevels - m_queues.TakeWhile(queue => queue.IsEmpty).Count();
        }
    }

    ///// <summary>
    ///// Gets or sets the cancellation token for the next time
    ///// the thread's actions will be executed by the scheduler.
    ///// </summary>
    //internal ICancellationToken NextExecutionToken
    //{
    //    get
    //    {
    //        return Interlocked.CompareExchange(ref m_nextExecutionToken, null, null);
    //    }
    //    set
    //    {
    //        Interlocked.Exchange(ref m_nextExecutionToken, value);
    //    }
    //}

    #endregion

    #region [ Methods ]

    /// <summary>
    /// Pushes an action to the logical thread.
    /// </summary>
    /// <param name="action">The action to be executed on this thread.</param>
    public void Push(Action action)
    {
        Push(1, action);
    }

    /// <summary>
    /// Pushes an action to the logical thread.
    /// </summary>
    /// <param name="priority">The priority the action should be given when executing actions on the thread (higher numbers have higher priority).</param>
    /// <param name="action">The action to be executed on this thread.</param>
    /// <exception cref="ArgumentException"><paramref name="priority"/> is outside the range between 1 and <see cref="PriorityLevels"/>.</exception>
    public void Push(int priority, Action action)
    {
        if (priority < 1 || priority > PriorityLevels)
            throw new ArgumentException($"Priority {priority} is outside the range between 1 and {PriorityLevels}.", nameof(priority));

        m_queues[PriorityLevels - priority].Enqueue(action);
        m_scheduler.SignalItemHandler(this, priority);
    }

    /// <summary>
    /// Clears all actions from the logical thread.
    /// </summary>
    public void Clear()
    {
        Action action;

        foreach (ConcurrentQueue<Action> queue in m_queues)
        {
            while (!queue.IsEmpty)
                queue.TryDequeue(out action);
        }
    }

    /// <summary>
    /// Samples the statistics, providing current statistic
    /// values and resetting statistic counters.
    /// </summary>
    /// <returns>The current statistic values.</returns>
    public LogicalThreadStatistics SampleStatistics()
    {
        return Interlocked.Exchange(ref m_statistics, new LogicalThreadStatistics());
    }

    /// <summary>
    /// Pulls an action from the logical thread's internal queue to be executed on a physical thread.
    /// </summary>
    /// <returns>An action from the logical thread's internal queue.</returns>
    internal Action Pull()
    {
        Action action;

        foreach (ConcurrentQueue<Action> queue in m_queues)
        {
            if (queue.TryDequeue(out action))
                return action;
        }

        return null;
    }

    /// <summary>
    /// Attempts to activate the thread at the given priority.
    /// </summary>
    /// <param name="priority">The priority at which to activate the thread.</param>
    /// <returns>True if the thread's priority needs to be changed to the given priority; false otherwise.</returns>
    // TODO: ICancellationToken
    internal bool TryActivate(int priority)
    {
        // Always get the execution token before the
        // active priority to mitigate race conditions
        //ICancellationToken nextExecutionToken = NextExecutionToken;
        int activePriority = ActivePriority;
        //return (activePriority < priority) && nextExecutionToken.Cancel();
        return true;
    }

    /// <summary>
    /// Completely deactivates the thread, making it available for reactivation at any priority.
    /// </summary>
    internal void Deactivate()
    {
        // Always update the active priority before the
        // execution token to mitigate race conditions
        ActivePriority = 0;
        //NextExecutionToken = new CancellationToken();
    }

    /// <summary>
    /// Returns the thread local object with the given ID.
    /// </summary>
    /// <param name="key">The key used to look up the thread local object.</param>
    /// <returns>The value of the thread local object with the given ID.</returns>
    internal object GetThreadLocal(object key)
    {
        object value;
        m_threadLocalStorage.TryGetValue(key, out value);
        return value;
    }

    /// <summary>
    /// Sets the value of the thread local object with the given ID.
    /// </summary>
    /// <param name="key">The key used to look up the thread local object.</param>
    /// <param name="value">The new value for the thread local object.</param>
    internal void SetThreadLocal(object key, object value)
    {
        if (value != null)
            m_threadLocalStorage[key] = value;
        else
            m_threadLocalStorage.Remove(key);
    }

    /// <summary>
    /// Updates the statistics based on the execution time of a single action.
    /// </summary>
    /// <param name="executionTime">The execution time of the action.</param>
    internal void UpdateStatistics(TimeSpan executionTime)
    {
        m_statistics.UpdateStatistics(executionTime);
    }

    /// <summary>
    /// Raises the <see cref="UnhandledException"/> event.
    /// </summary>
    /// <param name="ex">The unhandled exception.</param>
    /// <returns>True if there are any handlers attached to this event; false otherwise.</returns>
    internal bool OnUnhandledException(Exception ex)
    {
        EventHandler<EventArgs<Exception>> unhandledException = UnhandledException;

        if ((object)unhandledException == null)
            return false;

        unhandledException(this, new EventArgs<Exception>(ex));

        return true;
    }

    #endregion

    #region [ Static ]

    // Static Fields
    private static readonly LogicalThreadScheduler DefaultScheduler = new LogicalThreadScheduler();
    private static readonly ThreadLocal<LogicalThread> LocalThread = new ThreadLocal<LogicalThread>();

    // Static Properties

    /// <summary>
    /// Gets the logical thread that is currently executing.
    /// </summary>
    public static LogicalThread CurrentThread
    {
        get
        {
            return LocalThread.Value;
        }
        internal set
        {
            LocalThread.Value = value;
        }
    }

    #endregion
}

/// <summary>
/// Defines extensions for the <see cref="LogicalThread"/> class.
/// </summary>
public static class LogicalThreadExtensions
{
    /// <summary>
    /// Creates an awaitable that ensures the continuation is running on the logical thread.
    /// </summary>
    /// <param name="thread">The thread to join.</param>
    /// <returns>A context that, when awaited, runs on the logical thread.</returns>
    public static LogicalThreadAwaitable Join(this LogicalThread thread) =>
        new LogicalThreadAwaitable(thread);

    /// <summary>
    /// Creates an awaitable that ensures the continuation is running on the logical thread.
    /// </summary>
    /// <param name="thread">The thread to join.</param>
    /// <param name="priority">The priority of the continuation when completing asynchronously.</param>
    /// <returns>A context that, when awaited, runs on the logical thread.</returns>
    public static LogicalThreadAwaitable Join(this LogicalThread thread, int priority) =>
        new LogicalThreadAwaitable(thread, priority);

    /// <summary>
    /// Creates an awaitable that asynchronously yields
    /// to a new action on the logical thread when awaited.
    /// </summary>
    /// <param name="thread">The thread to yield to.</param>
    /// <returns>A context that, when awaited, will transition to a new action on the logical thread.</returns>
    public static LogicalThreadAwaitable Yield(this LogicalThread thread) =>
        new LogicalThreadAwaitable(thread, forceYield: true);

    /// <summary>
    /// Creates an awaitable that asynchronously yields
    /// to a new action on the logical thread when awaited.
    /// </summary>
    /// <param name="thread">The thread to yield to.</param>
    /// <param name="priority">The priority of the continuation.</param>
    /// <returns>A context that, when awaited, will transition to a new action on the logical thread.</returns>
    public static LogicalThreadAwaitable Yield(this LogicalThread thread, int priority) =>
        new LogicalThreadAwaitable(thread, priority, true);

    /// <summary>
    /// Provides an awaitable context for switching into a target environment.
    /// </summary>
    /// <remarks>
    /// This type is intended for compiler use only.
    /// </remarks>
    public struct LogicalThreadAwaitable
    {
        #region [ Members ]

        // Nested Types

        /// <summary>
        /// Provides an awaiter that switches into a target environment.
        /// </summary>
        /// <remarks>
        /// This type is intended for compiler use only.
        /// </remarks>
        public struct LogicalThreadAwaiter : INotifyCompletion
        {
            private LogicalThreadAwaitable Awaitable { get; }

            /// <summary>
            /// Creates a new instance of the <see cref="LogicalThreadAwaiter"/> class.
            /// </summary>
            /// <param name="awaitable">The awaitable that spawned this awaiter.</param>
            public LogicalThreadAwaiter(LogicalThreadAwaitable awaitable) =>
                Awaitable = awaitable;

            /// <summary>
            /// Gets whether a yield is not required.
            /// </summary>
            /// <remarks>
            /// This property is intended for compiler user rather than use directly in code.
            /// </remarks>
            public bool IsCompleted =>
                !Awaitable.ForceYield &&
                Awaitable.Thread == LogicalThread.CurrentThread;

            /// <summary>
            /// Posts the <paramref name="continuation"/> back to the current context.
            /// </summary>
            /// <param name="continuation">The action to invoke asynchronously.</param>
            /// <exception cref="System.ArgumentNullException">The <paramref name="continuation"/> argument is null.</exception>
            public void OnCompleted(Action continuation)
            {
                if (continuation is null)
                    throw new ArgumentNullException(nameof(continuation));

                LogicalThread thread = Awaitable.Thread
                                       ?? new LogicalThread();

                int priority = Awaitable.Priority;

                if (priority > 0)
                    thread.Push(priority, continuation);
                else
                    thread.Push(continuation);
            }

            /// <summary>
            /// Ends the await operation.
            /// </summary>
            public void GetResult()
            {
            }
        }

        #endregion

        #region [ Constructors ]

        /// <summary>
        /// Creates a new instance of the <see cref="LogicalThreadAwaitable"/> class.
        /// </summary>
        /// <param name="thread">The thread on which to push continuations.</param>
        /// <param name="priority">The priority of continuations pushed to the thread.</param>
        /// <param name="forceYield">Force continuations to complete asynchronously.</param>
        public LogicalThreadAwaitable(LogicalThread thread, int priority = 0, bool forceYield = false)
        {
            Thread = thread;
            Priority = priority;
            ForceYield = forceYield;
        }

        #endregion

        #region [ Properties ]

        private LogicalThread Thread { get; }
        private int Priority { get; }
        private bool ForceYield { get; }

        #endregion

        #region [ Methods ]

        /// <summary>
        /// Gets an awaiter for this <see cref="LogicalThreadAwaitable"/>.
        /// </summary>
        /// <returns>An awaiter for this awaitable.</returns>
        /// <remarks>This method is intended for compiler user rather than use directly in code.</remarks>
        public LogicalThreadAwaiter GetAwaiter() =>
            new LogicalThreadAwaiter(this);

        #endregion
    }
}
