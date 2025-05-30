﻿//******************************************************************************************************
//  RoutingTables.cs - Gbtc
//
//  Copyright © 2012, Grid Protection Alliance.  All Rights Reserved.
//
//  Licensed to the Grid Protection Alliance (GPA) under one or more contributor license agreements. See
//  the NOTICE file distributed with this work for additional information regarding copyright ownership.
//  The GPA licenses this file to you under the MIT License (MIT), the "License"; you may
//  not use this file except in compliance with the License. You may obtain a copy of the License at:
//
//      http://www.opensource.org/licenses/MIT
//
//  Unless agreed to in writing, the subject software distributed under the License is distributed on an
//  "AS-IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. Refer to the
//  License for the specific language governing permissions and limitations.
//
//  Code Modification History:
//  ----------------------------------------------------------------------------------------------------
//  06/30/2011 - J. Ritchie Carroll
//       Generated original version of source code.
//  07/25/2011 - J. Ritchie Carroll
//       Added code to handle connect on demand adapters (i.e., where AutoStart = false).
//  12/20/2012 - Starlynn Danyelle Gilliam
//       Modified Header.
//  02/11/2013 - Stephen C. Wills
//       Added code to handle queue and notify for adapter synchronization.
//  11/09/2023 - Lillian Gensolin
//       Converted code to .NET core.
//
//******************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using Gemstone.Diagnostics;
using Gemstone.EventHandlerExtensions;
using Gemstone.Threading.SynchronizedOperations;
using Gemstone.Units;

namespace Gemstone.Timeseries.Adapters;

/// <summary>
/// Represents the routing tables for the Iaon adapters.
/// </summary>
public class RoutingTables : IDisposable
{
    #region [ Members ]

    // Events

    /// <summary>
    /// Provides status messages to consumer.
    /// </summary>
    /// <remarks>
    /// <see cref="EventArgs{T}.Argument"/> is new status message.
    /// </remarks>
    public event EventHandler<EventArgs<string>>? StatusMessage;

    /// <summary>
    /// Event is raised when there is an exception encountered while processing.
    /// </summary>
    /// <remarks>
    /// <see cref="EventArgs{T}.Argument"/> is the exception that was thrown.
    /// </remarks>
    public event EventHandler<EventArgs<Exception>>? ProcessException;

    // Fields

    private readonly LongSynchronizedOperation m_calculateRoutingTablesOperation;
    private volatile MeasurementKey[]? m_inputMeasurementKeysRestriction;
    private readonly IRouteMappingTables m_routeMappingTables;

    private HashSet<IAdapter> m_prevCalculatedConsumers;
    private HashSet<IAdapter> m_prevCalculatedProducers;

    private bool m_disposed;

    #endregion

    #region [ Constructors ]

    /// <summary>
    /// Creates a new instance of the <see cref="RoutingTables"/> class.
    /// </summary>
    public RoutingTables(IRouteMappingTables mappingTable = null)
    {
        Log = Logger.CreatePublisher(GetType(), MessageClass.Framework);
        Log.InitialStackMessages = Log.InitialStackMessages.Union("ComponentName", GetType().Name);

        m_prevCalculatedConsumers = [];
        m_prevCalculatedProducers = [];
        m_routeMappingTables = mappingTable ?? new RouteMappingDoubleBufferQueue();
        m_routeMappingTables.Initialize(status => OnStatusMessage(MessageLevel.Info, status, "Initialization"), ex => OnProcessException(MessageLevel.Warning, ex, "Initialization"));

        m_calculateRoutingTablesOperation = new LongSynchronizedOperation(CalculateRoutingTables)
        {
            IsBackground = true
        };
    }

    /// <summary>
    /// Releases the unmanaged resources before the <see cref="RoutingTables"/> object is reclaimed by <see cref="GC"/>.
    /// </summary>
    ~RoutingTables() =>
        Dispose(false);

    #endregion

    #region [ Properties ]

    /// <summary>
    /// Log messages generated by an adapter.
    /// </summary>
    protected LogPublisher Log { get; }

    /// <summary>
    /// Gets or sets the active <see cref="InputAdapterCollection"/>.
    /// </summary>
    public InputAdapterCollection InputAdapters { get; set; }

    /// <summary>
    /// Gets or sets the active <see cref="ActionAdapterCollection"/>.
    /// </summary>
    public ActionAdapterCollection ActionAdapters { get; set; }

    /// <summary>
    /// Gets or sets the active <see cref="OutputAdapterCollection"/>.
    /// </summary>
    public OutputAdapterCollection OutputAdapters { get; set; }

    #endregion

    #region [ Methods ]

    /// <summary>
    /// Releases all the resources used by the <see cref="RoutingTables"/> object.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the unmanaged resources used by the <see cref="RoutingTables"/> object and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (m_disposed)
            return;

        try
        {
            if (!disposing)
                return;

            InputAdapters = null;
            ActionAdapters = null;
            OutputAdapters = null;
        }
        finally
        {
            m_disposed = true; // Prevent duplicate dispose.
        }
    }

    /// <summary>
    /// Spawn routing tables recalculation.
    /// </summary>
    /// <param name="inputMeasurementKeysRestriction">Input measurement keys restriction.</param>
    /// <remarks>
    /// Set the <paramref name="inputMeasurementKeysRestriction"/> to null to use full adapter I/O routing demands.
    /// </remarks>
    public virtual void CalculateRoutingTables(MeasurementKey[]? inputMeasurementKeysRestriction)
    {
        try
        {
            m_inputMeasurementKeysRestriction = inputMeasurementKeysRestriction;
            m_calculateRoutingTablesOperation.RunAsync();
        }
        catch (Exception ex)
        {
            // Process exception for logging
            OnProcessException(MessageLevel.Info, new InvalidOperationException($"Failed to queue routing table calculation: {ex.Message}", ex));
        }
    }

    private void CalculateRoutingTables()
    {
        long startTime = DateTime.UtcNow.Ticks;

        IInputAdapter[]? inputAdapterCollection = null;
        IActionAdapter[]? actionAdapterCollection = null;
        IOutputAdapter[]? outputAdapterCollection = null;
        bool retry = true;

        OnStatusMessage(MessageLevel.Info, "Starting measurement route calculation...");

        // Attempt to cache input, action, and output adapters for routing table calculation.
        // This could fail if another thread modifies the collections while caching is in
        // progress (rare), so retry if the caching fails.
        //
        // We don't attempt to lock here because we don't own the collections.
        while (retry)
        {
            try
            {
                if (InputAdapters is not null)
                    inputAdapterCollection = InputAdapters.ToArray<IInputAdapter>();

                if (ActionAdapters is not null)
                    actionAdapterCollection = ActionAdapters.ToArray<IActionAdapter>();

                if (OutputAdapters is not null)
                    outputAdapterCollection = OutputAdapters.ToArray<IOutputAdapter>();

                retry = false;
            }
            catch (InvalidOperationException)
            {
                // Attempt to catch "Collection was modified; enumeration operation may not execute."
            }
            catch (NullReferenceException)
            {
                // Catch rare exceptions where IaonSession is disposed during a context switch
                inputAdapterCollection = null;
                actionAdapterCollection = null;
                outputAdapterCollection = null;
                retry = false;
            }
        }

        try
        {
            // Get a full list of all producer (input/action) adapters
            HashSet<IAdapter> producerAdapters = new((inputAdapterCollection ?? Enumerable.Empty<IAdapter>())
                .Concat(actionAdapterCollection ?? Enumerable.Empty<IAdapter>()));

            // Get a full list of all consumer (action/output) adapters
            HashSet<IAdapter> consumerAdapters = new((actionAdapterCollection ?? Enumerable.Empty<IAdapter>())
                .Concat(outputAdapterCollection ?? Enumerable.Empty<IAdapter>()));

            RoutingTablesAdaptersList producerChanges = new(m_prevCalculatedProducers, producerAdapters);
            RoutingTablesAdaptersList consumerChanges = new(m_prevCalculatedConsumers, consumerAdapters);

            m_routeMappingTables.PatchRoutingTable(producerChanges, consumerChanges);
            m_prevCalculatedProducers = producerAdapters;
            m_prevCalculatedConsumers = consumerAdapters;

            // Start or stop any connect on demand adapters
            HandleConnectOnDemandAdapters(new HashSet<MeasurementKey>(m_inputMeasurementKeysRestriction ?? Enumerable.Empty<MeasurementKey>()), inputAdapterCollection, actionAdapterCollection, outputAdapterCollection);

            Time elapsedTime = Ticks.ToSeconds(DateTime.UtcNow.Ticks - startTime);

            int routeCount = m_routeMappingTables.RouteCount;
            int destinationCount = consumerAdapters.Count;

            OnStatusMessage(MessageLevel.Info, $"Calculated {routeCount} route{(routeCount == 1 ? "" : "s")} for {destinationCount} destination{(destinationCount == 1 ? "" : "s")} in {elapsedTime.ToString(3)}.");
        }
        catch (ObjectDisposedException)
        {
            // Ignore this error. Seems to happen during normal
            // operation and does not affect the result.
        }
        catch (Exception ex)
        {
            OnProcessException(MessageLevel.Warning, new InvalidOperationException($"Routing tables calculation error: {ex.Message}", ex));
        }
    }

    /// <summary>
    /// This method will directly inject measurements into the routing table and use a shared local input adapter. For
    /// contention reasons, it is not recommended this be its default use case, but it is necessary at times.
    /// </summary>
    /// <param name="sender">the sender object</param>
    /// <param name="measurements">the event arguments</param>
    public void InjectMeasurements(object? sender, EventArgs<ICollection<IMeasurement>>? measurements) =>
        m_routeMappingTables.InjectMeasurements(sender, measurements);

    /// <summary>
    /// Event handler for distributing new measurements in a broadcast fashion.
    /// </summary>
    /// <param name="sender">Event source reference to adapter that generated new measurements.</param>
    /// <param name="e">Event arguments containing a collection of new measurements.</param>
    /// <remarks>
    /// Time-series framework uses this handler to route new measurements to the action and output adapters; adapter will handle filtering.
    /// </remarks>
    public virtual void BroadcastMeasurementsHandler(object? sender, EventArgs<ICollection<IMeasurement>> e)
    {
        ICollection<IMeasurement> newMeasurements = e.Argument;

        ActionAdapters.QueueMeasurementsForProcessing(newMeasurements);
        OutputAdapters.QueueMeasurementsForProcessing(newMeasurements);
    }

    /// <summary>
    /// Raises the <see cref="StatusMessage"/> event and sends this data to the <see cref="Logger"/>.
    /// </summary>
    /// <param name="level">The <see cref="MessageLevel"/> to assign to this message</param>
    /// <param name="status">New status message.</param>
    /// <param name="eventName">A fixed string to classify this event; defaults to <c>null</c>.</param>
    /// <param name="flags"><see cref="MessageFlags"/> to use, if any; defaults to <see cref="MessageFlags.None"/>.</param>
    /// <remarks>
    /// <see pref="eventName"/> should be a constant string value associated with what type of message is being
    /// generated. In general, there should only be a few dozen distinct event names per class. Exceeding this
    /// threshold will cause the EventName to be replaced with a general warning that a usage issue has occurred.
    /// </remarks>
    protected virtual void OnStatusMessage(MessageLevel level, string status, string? eventName = null, MessageFlags flags = MessageFlags.None)
    {
        try
        {
            Log.Publish(level, flags, eventName ?? "CalculateRoutingTables", status);

            using (Logger.SuppressLogMessages())
                StatusMessage?.SafeInvoke(this, new EventArgs<string>(AdapterBase.GetStatusWithMessageLevelPrefix(status, level)));
        }
        catch (Exception ex)
        {
            // We protect our code from consumer thrown exceptions
            OnProcessException(MessageLevel.Info, new InvalidOperationException($"Exception in consumer handler for StatusMessage event: {ex.Message}", ex), "ConsumerEventException");
        }
    }

    /// <summary>
    /// Raises the <see cref="ProcessException"/> event.
    /// </summary>
    /// <param name="level">The <see cref="MessageLevel"/> to assign to this message</param>
    /// <param name="exception">Processing <see cref="Exception"/>.</param>
    /// <param name="eventName">A fixed string to classify this event; defaults to <c>null</c>.</param>
    /// <param name="flags"><see cref="MessageFlags"/> to use, if any; defaults to <see cref="MessageFlags.None"/>.</param>
    /// <remarks>
    /// <see pref="eventName"/> should be a constant string value associated with what type of message is being
    /// generated. In general, there should only be a few dozen distinct event names per class. Exceeding this
    /// threshold will cause the EventName to be replaced with a general warning that a usage issue has occurred.
    /// </remarks>
    protected virtual void OnProcessException(MessageLevel level, Exception exception, string? eventName = null, MessageFlags flags = MessageFlags.None)
    {
        try
        {
            Log.Publish(level, flags, eventName ?? "CalculateRoutingTables", exception.Message, null, exception);

            using (Logger.SuppressLogMessages())
                ProcessException?.SafeInvoke(this, new EventArgs<Exception>(exception));
        }
        catch (Exception ex)
        {
            // We protect our code from consumer thrown exceptions
            Log.Publish(MessageLevel.Info, "ConsumerEventException", $"Exception in consumer handler for ProcessException event: {ex.Message}", null, ex);
        }
    }

    /// <summary>
    /// Starts or stops connect on demand adapters based on current state of demanded input or output signals.
    /// </summary>
    /// <param name="inputMeasurementKeysRestriction">The set of signals to be produced by the chain of adapters to be handled.</param>
    /// <param name="inputAdapterCollection">Collection of input adapters at start of routing table calculation.</param>
    /// <param name="actionAdapterCollection">Collection of action adapters at start of routing table calculation.</param>
    /// <param name="outputAdapterCollection">Collection of output adapters at start of routing table calculation.</param>
    /// <remarks>
    /// Set the <paramref name="inputMeasurementKeysRestriction"/> to null to use full adapter routing demands.
    /// </remarks>
    protected virtual void HandleConnectOnDemandAdapters(ISet<MeasurementKey> inputMeasurementKeysRestriction, IInputAdapter[] inputAdapterCollection, IActionAdapter[]? actionAdapterCollection, IOutputAdapter[]? outputAdapterCollection)
    {
        ISet<IAdapter> dependencyChain;

        ISet<MeasurementKey> requestedInputSignals;
        ISet<MeasurementKey> requestedOutputSignals;

        dependencyChain = inputMeasurementKeysRestriction.Any() ?
            // When an input signals restriction has been defined, determine the set of adapters
            // by walking the dependency chain of the restriction
            TraverseDependencyChain(inputMeasurementKeysRestriction, inputAdapterCollection, actionAdapterCollection, outputAdapterCollection) :

            // Determine the set of adapters in the dependency chain for all adapters in the system
            TraverseDependencyChain(inputAdapterCollection, actionAdapterCollection, outputAdapterCollection);

        // Get the full set of requested input and output signals in the entire dependency chain
        ISet<MeasurementKey> inputSignals = new HashSet<MeasurementKey>(dependencyChain.SelectMany(adapter => adapter.InputMeasurementKeys()));
        ISet<MeasurementKey> outputSignals = new HashSet<MeasurementKey>(dependencyChain.SelectMany(adapter => adapter.OutputMeasurementKeys()));

        // Turn connect on demand input adapters on or off based on whether they are part of the dependency chain
        if (inputAdapterCollection is not null)
        {
            foreach (IInputAdapter inputAdapter in inputAdapterCollection)
            {
                if (!inputAdapter.Initialized || inputAdapter.AutoStart)
                    continue;

                if (dependencyChain.Contains(inputAdapter))
                {
                    requestedOutputSignals = new HashSet<MeasurementKey>(inputAdapter.OutputMeasurementKeys());
                    requestedOutputSignals.IntersectWith(inputSignals);
                    inputAdapter.RequestedOutputMeasurementKeys = requestedOutputSignals.ToArray();
                    inputAdapter.Enabled = true;
                }
                else
                {
                    inputAdapter.RequestedOutputMeasurementKeys = null;
                    inputAdapter.Enabled = false;
                }
            }
        }

        // Turn connect on demand action adapters on or off based on whether they are part of the dependency chain
        if (actionAdapterCollection is not null)
        {
            foreach (IActionAdapter actionAdapter in actionAdapterCollection)
            {
                if (!actionAdapter.Initialized || actionAdapter.AutoStart)
                    continue;

                if (dependencyChain.Contains(actionAdapter))
                {
                    if (actionAdapter.RespectInputDemands)
                    {
                        requestedInputSignals = new HashSet<MeasurementKey>(actionAdapter.InputMeasurementKeys());
                        requestedInputSignals.IntersectWith(outputSignals);
                        actionAdapter.RequestedInputMeasurementKeys = requestedInputSignals.ToArray();
                    }

                    if (actionAdapter.RespectOutputDemands)
                    {
                        requestedOutputSignals = new HashSet<MeasurementKey>(actionAdapter.OutputMeasurementKeys());
                        requestedOutputSignals.IntersectWith(inputSignals);
                        actionAdapter.RequestedOutputMeasurementKeys = requestedOutputSignals.ToArray();
                    }

                    actionAdapter.Enabled = true;
                }
                else
                {
                    actionAdapter.RequestedInputMeasurementKeys = null;
                    actionAdapter.RequestedOutputMeasurementKeys = null;
                    actionAdapter.Enabled = false;
                }
            }
        }

        // Turn connect on demand output adapters on or off based on whether they are part of the dependency chain
        if (outputAdapterCollection is not null)
        {
            foreach (IOutputAdapter outputAdapter in outputAdapterCollection)
            {
                if (!outputAdapter.Initialized || outputAdapter.AutoStart)
                    continue;

                if (dependencyChain.Contains(outputAdapter))
                {
                    requestedInputSignals = new HashSet<MeasurementKey>(outputAdapter.InputMeasurementKeys());
                    requestedInputSignals.IntersectWith(outputSignals);
                    outputAdapter.RequestedInputMeasurementKeys = requestedInputSignals.ToArray();
                    outputAdapter.Enabled = true;
                }
                else
                {
                    outputAdapter.RequestedInputMeasurementKeys = null;
                    outputAdapter.Enabled = false;
                }
            }
        }
    }

    /// <summary>
    /// Determines the set of adapters in the dependency chain that produces the set of signals in the
    /// <paramref name="inputMeasurementKeysRestriction"/> and returns the set of input signals required by the
    /// adapters in the chain and the set of output signals produced by the adapters in the chain.
    /// </summary>
    /// <param name="inputMeasurementKeysRestriction">The set of signals that must be produced by the dependency chain.</param>
    /// <param name="inputAdapterCollection">Collection of input adapters at start of routing table calculation.</param>
    /// <param name="actionAdapterCollection">Collection of action adapters at start of routing table calculation.</param>
    /// <param name="outputAdapterCollection">Collection of output adapters at start of routing table calculation.</param>
    protected virtual ISet<IAdapter> TraverseDependencyChain(ISet<MeasurementKey> inputMeasurementKeysRestriction, IInputAdapter[]? inputAdapterCollection, IActionAdapter[] actionAdapterCollection, IOutputAdapter[]? outputAdapterCollection)
    {
        ISet<IAdapter> dependencyChain = new HashSet<IAdapter>();

        if (inputAdapterCollection is not null)
        {
            foreach (IInputAdapter inputAdapter in inputAdapterCollection)
            {
                if (inputAdapter.Initialized && !dependencyChain.Contains(inputAdapter) && inputMeasurementKeysRestriction.Overlaps(inputAdapter.OutputMeasurementKeys()))
                    AddInputAdapter(inputAdapter, dependencyChain, inputAdapterCollection, actionAdapterCollection, outputAdapterCollection);
            }
        }

        if (actionAdapterCollection is not null)
        {
            foreach (IActionAdapter actionAdapter in actionAdapterCollection)
            {
                if (actionAdapter.Initialized && !dependencyChain.Contains(actionAdapter) && inputMeasurementKeysRestriction.Overlaps(actionAdapter.OutputMeasurementKeys()))
                    AddActionAdapter(actionAdapter, dependencyChain, inputAdapterCollection, actionAdapterCollection, outputAdapterCollection);
            }
        }

        return dependencyChain;
    }

    /// <summary>
    /// Determines the set of adapters in the dependency chain for all adapters in the system which are either not connect or demand or are demanded.
    /// </summary>
    /// <param name="inputAdapterCollection">Collection of input adapters at start of routing table calculation.</param>
    /// <param name="actionAdapterCollection">Collection of action adapters at start of routing table calculation.</param>
    /// <param name="outputAdapterCollection">Collection of output adapters at start of routing table calculation.</param>
    protected virtual ISet<IAdapter> TraverseDependencyChain(IInputAdapter[] inputAdapterCollection, IActionAdapter[] ?actionAdapterCollection, IOutputAdapter[] outputAdapterCollection)
    {
        ISet<IAdapter> dependencyChain = new HashSet<IAdapter>();

        if (inputAdapterCollection is not null)
        {
            foreach (IInputAdapter inputAdapter in inputAdapterCollection)
            {
                if (inputAdapter.Initialized && inputAdapter.AutoStart && !dependencyChain.Contains(inputAdapter))
                    AddInputAdapter(inputAdapter, dependencyChain, inputAdapterCollection, actionAdapterCollection, outputAdapterCollection);
            }
        }

        if (actionAdapterCollection is not null)
        {
            foreach (IActionAdapter actionAdapter in actionAdapterCollection)
            {
                if (actionAdapter.Initialized && actionAdapter.AutoStart && !dependencyChain.Contains(actionAdapter))
                    AddActionAdapter(actionAdapter, dependencyChain, inputAdapterCollection, actionAdapterCollection, outputAdapterCollection);
            }
        }

        if (outputAdapterCollection is not null)
        {
            foreach (IOutputAdapter outputAdapter in outputAdapterCollection)
            {
                if (outputAdapter.Initialized && outputAdapter.AutoStart && !dependencyChain.Contains(outputAdapter))
                    AddOutputAdapter(outputAdapter, dependencyChain, inputAdapterCollection, actionAdapterCollection, outputAdapterCollection);
            }
        }

        return dependencyChain;
    }

    // Adds an input adapter to the dependency chain.
    private void AddInputAdapter(IInputAdapter adapter, ISet<IAdapter> dependencyChain, IInputAdapter[]? inputAdapterCollection, IActionAdapter[] actionAdapterCollection, IOutputAdapter[]? outputAdapterCollection)
    {
        HashSet<MeasurementKey> outputMeasurementKeys = new(adapter.OutputMeasurementKeys());

        // Adds the adapter to the chain
        dependencyChain.Add(adapter);

        if (actionAdapterCollection is not null)
        {
            // Checks all action adapters to determine whether they also need to be
            // added to the chain as a result of this adapter being added to the chain
            foreach (IActionAdapter actionAdapter in actionAdapterCollection)
            {
                if (actionAdapter.Initialized && actionAdapter.RespectInputDemands && !dependencyChain.Contains(actionAdapter) && outputMeasurementKeys.Overlaps(actionAdapter.InputMeasurementKeys()))
                    AddActionAdapter(actionAdapter, dependencyChain, inputAdapterCollection, actionAdapterCollection, outputAdapterCollection);
            }
        }

        if (outputAdapterCollection is not null)
        {
            // Checks all output adapters to determine whether they also need to be
            // added to the chain as a result of this adapter being added to the chain
            foreach (IOutputAdapter outputAdapter in outputAdapterCollection)
            {
                if (outputAdapter.Initialized && !dependencyChain.Contains(outputAdapter) && outputMeasurementKeys.Overlaps(outputAdapter.InputMeasurementKeys()))
                    AddOutputAdapter(outputAdapter, dependencyChain, inputAdapterCollection, actionAdapterCollection, outputAdapterCollection);
            }
        }
    }

    // Adds an action adapter to the dependency chain.
    private void AddActionAdapter(IActionAdapter adapter, ISet<IAdapter> dependencyChain, IInputAdapter[]? inputAdapterCollection, IActionAdapter[]? actionAdapterCollection, IOutputAdapter[] outputAdapterCollection)
    {
        HashSet<MeasurementKey> inputMeasurementKeys = new(adapter.InputMeasurementKeys());
        HashSet<MeasurementKey> outputMeasurementKeys = new(adapter.OutputMeasurementKeys());

        // Adds the adapter to the chain
        dependencyChain.Add(adapter);

        if (inputAdapterCollection is not null)
        {
            // Checks all input adapters to determine whether they also need to be
            // added to the chain as a result of this adapter being added to the chain
            foreach (IInputAdapter inputAdapter in inputAdapterCollection)
            {
                if (inputAdapter.Initialized && !dependencyChain.Contains(inputAdapter) && inputMeasurementKeys.Overlaps(inputAdapter.OutputMeasurementKeys()))
                    AddInputAdapter(inputAdapter, dependencyChain, inputAdapterCollection, actionAdapterCollection, outputAdapterCollection);
            }
        }

        if (actionAdapterCollection is not null)
        {
            // Checks all action adapters to determine whether they also need to be
            // added to the chain as a result of this adapter being added to the chain
            foreach (IActionAdapter actionAdapter in actionAdapterCollection)
            {
                if (actionAdapter.Initialized && !dependencyChain.Contains(actionAdapter))
                {
                    if (actionAdapter.RespectInputDemands && outputMeasurementKeys.Overlaps(actionAdapter.InputMeasurementKeys()))
                        AddActionAdapter(actionAdapter, dependencyChain, inputAdapterCollection, actionAdapterCollection, outputAdapterCollection);
                    else if (actionAdapter.RespectOutputDemands && inputMeasurementKeys.Overlaps(actionAdapter.OutputMeasurementKeys()))
                        AddActionAdapter(actionAdapter, dependencyChain, inputAdapterCollection, actionAdapterCollection, outputAdapterCollection);
                }
            }
        }

        if (outputAdapterCollection is not null)
        {
            // Checks all output adapters to determine whether they also need to be
            // added to the chain as a result of this adapter being added to the chain
            foreach (IOutputAdapter outputAdapter in outputAdapterCollection)
            {
                if (outputAdapter.Initialized && !dependencyChain.Contains(outputAdapter) && outputMeasurementKeys.Overlaps(outputAdapter.InputMeasurementKeys()))
                    AddOutputAdapter(outputAdapter, dependencyChain, inputAdapterCollection, actionAdapterCollection, outputAdapterCollection);
            }
        }
    }

    // Adds an output adapter to the dependency chain.
    private void AddOutputAdapter(IOutputAdapter adapter, ISet<IAdapter> dependencyChain, IInputAdapter[] inputAdapterCollection, IActionAdapter[] actionAdapterCollection, IOutputAdapter[] outputAdapterCollection)
    {
        HashSet<MeasurementKey> inputMeasurementKeys = new(adapter.InputMeasurementKeys());

        // Adds the adapter to the chain
        dependencyChain.Add(adapter);

        if (inputAdapterCollection is not null)
        {
            // Checks all input adapters to determine whether they also need to be
            // added to the chain as a result of this adapter being added to the chain
            foreach (IInputAdapter inputAdapter in inputAdapterCollection)
            {
                if (inputAdapter.Initialized && !dependencyChain.Contains(inputAdapter) && inputMeasurementKeys.Overlaps(inputAdapter.OutputMeasurementKeys()))
                    AddInputAdapter(inputAdapter, dependencyChain, inputAdapterCollection, actionAdapterCollection, outputAdapterCollection);
            }
        }

        if (actionAdapterCollection is not null)
        {
            // Checks all action adapters to determine whether they also need to be
            // added to the chain as a result of this adapter being added to the chain
            foreach (IActionAdapter actionAdapter in actionAdapterCollection)
            {
                if (actionAdapter.Initialized && actionAdapter.RespectOutputDemands && !dependencyChain.Contains(actionAdapter) && inputMeasurementKeys.Overlaps(actionAdapter.OutputMeasurementKeys()))
                    AddActionAdapter(actionAdapter, dependencyChain, inputAdapterCollection, actionAdapterCollection, outputAdapterCollection);
            }
        }
    }

    #endregion
}
