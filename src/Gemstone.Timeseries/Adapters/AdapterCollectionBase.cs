﻿//******************************************************************************************************
//  AdapterCollectionBase.cs - Gbtc
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
//  09/02/2010 - J. Ritchie Carroll
//       Generated original version of source code.
//  12/20/2012 - Starlynn Danyelle Gilliam
//       Modified Header.
//  11/09/2023 - Lillian Gensolin
//       Converted code to .NET core.
//
//******************************************************************************************************

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Gemstone.ActionExtensions;
using Gemstone.Collections;
using Gemstone.Collections.CollectionExtensions;
using Gemstone.ComponentModel.DataAnnotations;
using Gemstone.Diagnostics;
using Gemstone.EventHandlerExtensions;
using Gemstone.IO;
using Gemstone.Security.AccessControl;
using Gemstone.StringExtensions;
using Gemstone.Threading;
using Gemstone.Threading.LogicalThreads;
using Gemstone.Units;

namespace Gemstone.Timeseries.Adapters;

/// <summary>
/// Represents a collection of <see cref="IAdapter"/> implementations.
/// </summary>
/// <typeparam name="T">Type of <see cref="IAdapter"/> this collection contains.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class AdapterCollectionBase<T> : ListCollection<T>, IAdapterCollection where T : class, IAdapter
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

    /// <summary>
    /// Event is raised when <see cref="InputMeasurementKeys"/> are updated.
    /// </summary>
    public event EventHandler? InputMeasurementKeysUpdated;

    /// <summary>
    /// Event is raised when <see cref="OutputMeasurements"/> are updated.
    /// </summary>
    public event EventHandler? OutputMeasurementsUpdated;

    /// <summary>
    /// Event is raised when adapter is aware of a configuration change.
    /// </summary>
    public event EventHandler? ConfigurationChanged;

    /// <summary>
    /// Event is raised when this <see cref="AdapterCollectionBase{T}"/> is disposed or an <see cref="IAdapter"/> in the collection is disposed.
    /// </summary>
    public event EventHandler? Disposed;

    // Fields
    private string m_name;
    private string m_connectionString;
    private DataSet? m_dataSource;
    private int m_initializationTimeout;
    private bool m_autoStart;
    private IMeasurement[]? m_outputMeasurements;
    private MeasurementKey[]? m_inputMeasurementKeys;
    private MeasurementKey[]? m_requestedInputMeasurementKeys;
    private MeasurementKey[]? m_requestedOutputMeasurementKeys;
    private Ticks m_lastProcessTime;
    private Time m_totalProcessTime;
    private long m_processedMeasurements;
    private DateTime m_startTimeConstraint;
    private DateTime m_stopTimeConstraint;
    private int m_processingInterval;
    private readonly SharedTimer m_monitorTimer;
    private bool m_monitorTimerEnabled;
    private readonly LogicalThreadScheduler m_lifecycleThreadScheduler;
    private readonly Dictionary<uint, LogicalThread> m_lifecycleThreads;
    private readonly LogicalThreadLocal<T> m_activeItem;
    private bool m_enabled;
    private long m_startTime;
    private long m_stopTime;

    #endregion

    #region [ Constructors ]

    /// <summary>
    /// Constructs a new instance of the <see cref="AdapterCollectionBase{T}"/>.
    /// </summary>
    protected AdapterCollectionBase()
    {
        Log = Logger.CreatePublisher(GetType(), MessageClass.Application);
        m_name = GetType().Name;
        Log.InitialStackMessages = Log.InitialStackMessages.Union("AdapterName", m_name);
        Settings = new Dictionary<string, string>();
        m_startTimeConstraint = DateTime.MinValue;
        m_stopTimeConstraint = DateTime.MaxValue;
        m_processingInterval = -1;
        m_initializationTimeout = AdapterBase.DefaultInitializationTimeout;
        m_autoStart = true;

        // We monitor total number of processed measurements every minute
        m_monitorTimer = Common.TimerScheduler.CreateTimer(60000);
        m_monitorTimer.Elapsed += m_monitorTimer_Elapsed;

        m_monitorTimer.AutoReset = true;
        m_monitorTimer.Enabled = false;

        m_lifecycleThreadScheduler = new LogicalThreadScheduler();
        m_lifecycleThreads = new Dictionary<uint, LogicalThread>();
        m_activeItem = new LogicalThreadLocal<T>();

        // Even on a single processor system we want a few threads such that if an
        // adapter is taking a long time to initialize, other adapters can still be
        // initializing in the meanwhile
        if (m_lifecycleThreadScheduler.MaxThreadCount < 4)
            m_lifecycleThreadScheduler.MaxThreadCount = 4;

        m_lifecycleThreadScheduler.UnhandledException += (_, args) => OnProcessException(MessageLevel.Warning, args.Argument);
    }

    /// <summary>
    /// Releases the unmanaged resources before the <see cref="AdapterCollectionBase{T}"/> object is reclaimed by <see cref="GC"/>.
    /// </summary>
    ~AdapterCollectionBase()
    {
        Dispose(false);
    }

    #endregion

    #region [ Properties ]

    /// <summary>
    /// Log messages generated by an adapter.
    /// </summary>
    protected LogPublisher Log { get; }

    /// <summary>
    /// Gets or sets the name of this <see cref="AdapterCollectionBase{T}"/>.
    /// </summary>
    public virtual string Name
    {
        get => m_name;
        set
        {
            m_name = value;
            Log.InitialStackMessages = Log.InitialStackMessages.Union("AdapterName", m_name);
        }
    }

    /// <summary>
    /// Gets or sets numeric ID associated with this <see cref="AdapterCollectionBase{T}"/>.
    /// </summary>
    public virtual uint ID { get; set; }

    /// <summary>
    /// Gets or sets flag indicating if the adapter collection has been initialized successfully.
    /// </summary>
    public virtual bool Initialized { get; set; }

    /// <summary>
    /// Gets or sets key/value pair connection information specific to this <see cref="AdapterCollectionBase{T}"/>.
    /// </summary>
    public virtual string ConnectionString
    {
        get => m_connectionString;
        set
        {
            m_connectionString = value;

            // Pre-parse settings upon connection string assignment
            Settings = string.IsNullOrWhiteSpace(m_connectionString) ?
                new Dictionary<string, string>() :
                m_connectionString.ParseKeyValuePairs();
        }
    }

    string IAdapter.ConnectionInfo => null;

    /// <summary>
    /// Gets or sets <see cref="DataSet"/> based data source used to load each <see cref="IAdapter"/>.
    /// Updates to this property will cascade to all items in this <see cref="AdapterCollectionBase{T}"/>.
    /// </summary>
    /// <remarks>
    /// Table name specified in <see cref="DataMember"/> from <see cref="DataSource"/> is expected
    /// to have the following table column names:<br/>
    /// ID, AdapterName, AssemblyName, TypeName, ConnectionString<br/>
    /// ID column type should be integer based, all other column types are expected to be string based.
    /// </remarks>
    public virtual DataSet? DataSource
    {
        get => m_dataSource;
        set
        {
            m_dataSource = value;

            // Update data source for items in this collection
            lock (this)
            {
                foreach (T item in this)
                    item.DataSource = m_dataSource;
            }
        }
    }

    /// <summary>
    /// Gets or sets specific data member (e.g., table name) in <see cref="DataSource"/> used to <see cref="Initialize()"/> this <see cref="AdapterCollectionBase{T}"/>.
    /// </summary>
    /// <remarks>
    /// Table name specified in <see cref="DataMember"/> from <see cref="DataSource"/> is expected
    /// to have the following table column names:<br/>
    /// ID, AdapterName, AssemblyName, TypeName, ConnectionString<br/>
    /// ID column type should be integer based, all other column types are expected to be string based.
    /// </remarks>
    public virtual string DataMember { get; set; }

    /// <summary>
    /// Gets or sets the default adapter time that represents the maximum time system will wait during <see cref="Start()"/> for initialization.
    /// </summary>
    /// <remarks>
    /// Set to <see cref="System.Threading.Timeout.Infinite"/> to wait indefinitely.
    /// </remarks>
    public virtual int InitializationTimeout
    {
        get => m_initializationTimeout;
        set => m_initializationTimeout = value;
    }

    /// <summary>
    /// Gets or sets flag indicating if adapter collection should automatically start items when <see cref="AutoInitialize"/> is <c>false</c>.
    /// </summary>
    public virtual bool AutoStart
    {
        get => m_autoStart;
        set => m_autoStart = value;
    }

    /// <summary>
    /// Gets or sets primary keys of input measurements the <see cref="AdapterCollectionBase{T}"/> expects, if any.
    /// </summary>
    public virtual MeasurementKey[]? InputMeasurementKeys
    {
        get
        {
            // If a specific set of input measurement keys has been assigned, use that set
            if (m_inputMeasurementKeys is not null)
                return m_inputMeasurementKeys;

            List<MeasurementKey> cumulativeKeys = [];

            // Otherwise return cumulative results of all child adapters
            lock (this)
            {
                foreach (T adapter in this)
                {
                    MeasurementKey[] inputMeasurementKeys = adapter?.InputMeasurementKeys;

                    // If any of the children expects all measurements (i.e., null InputMeasurementKeys)
                    // then the parent collection must expect all measurements
                    if (inputMeasurementKeys?.Length > 0)
                        cumulativeKeys.AddRange(inputMeasurementKeys);
                }
            }

            return cumulativeKeys.Distinct().ToArray();
        }
        set
        {
            m_inputMeasurementKeys = value;
            OnInputMeasurementKeysUpdated();
        }
    }

    /// <summary>
    /// Gets or sets output measurements that the <see cref="AdapterCollectionBase{T}"/> will produce, if any.
    /// </summary>
    public virtual IMeasurement[]? OutputMeasurements
    {
        get
        {
            // If a specific set of output measurements has been assigned, use that set
            if (m_outputMeasurements is not null)
                return m_outputMeasurements;

            // Otherwise return cumulative results of all child adapters
            List<IMeasurement> cumulativeMeasurements = [];

            // Otherwise return cumulative results of all child adapters
            lock (this)
            {
                foreach (T adapter in this)
                {
                    IMeasurement[] outputMeasurements = adapter?.OutputMeasurements;

                    if (outputMeasurements?.Length > 0)
                        cumulativeMeasurements.AddRange(outputMeasurements);
                }
            }

            return cumulativeMeasurements.Distinct().ToArray();
        }
        set
        {
            m_outputMeasurements = value;
            OnOutputMeasurementsUpdated();
        }
    }

    /// <summary>
    /// Gets or sets <see cref="MeasurementKey.Source"/> values used to filter input measurement keys.
    /// </summary>
    /// <remarks>
    /// The collection classes simply track this value if assigned, no automatic action is taken.
    /// </remarks>
    public virtual string[]? InputSourceIDs { get; set; }

    /// <summary>
    /// Gets or sets <see cref="MeasurementKey.Source"/> values used to filter output measurements.
    /// </summary>
    /// <remarks>
    /// The collection classes simply track this value if assigned, no automatic action is taken.
    /// </remarks>
    public virtual string[] OutputSourceIDs { get; set; }

    /// <summary>
    /// Gets or sets input measurement keys that are requested by other adapters based on what adapter says it can provide.
    /// </summary>
    public virtual MeasurementKey[]? RequestedInputMeasurementKeys
    {
        get
        {
            // If a specific set of input measurement keys has been assigned, use that set
            if (m_requestedInputMeasurementKeys is not null)
                return m_requestedInputMeasurementKeys;

            // Otherwise return cumulative results of all child adapters
            lock (this)
            {
                if (typeof(IActionAdapter).IsAssignableFrom(typeof(T)))
                    return this.Cast<IActionAdapter>().Where(item => item.RequestedInputMeasurementKeys is not null).SelectMany(item => item.RequestedInputMeasurementKeys).Distinct().ToArray();

                if (typeof(IOutputAdapter).IsAssignableFrom(typeof(T)))
                    return this.Cast<IOutputAdapter>().Where(item => item.RequestedInputMeasurementKeys is not null).SelectMany(item => item.RequestedInputMeasurementKeys).Distinct().ToArray();
            }

            return null;
        }
        set => m_requestedInputMeasurementKeys = value;
    }

    /// <summary>
    /// Gets or sets output measurement keys that are requested by other adapters based on what adapter says it can provide.
    /// </summary>
    public virtual MeasurementKey[] RequestedOutputMeasurementKeys
    {
        get
        {
            // If a specific set of output measurement keys has been assigned, use that set
            if (m_requestedOutputMeasurementKeys is not null)
                return m_requestedOutputMeasurementKeys;

            // Otherwise return cumulative results of all child adapters
            lock (this)
            {
                if (typeof(IActionAdapter).IsAssignableFrom(typeof(T)))
                    return this.Cast<IActionAdapter>().Where(item => item.RequestedOutputMeasurementKeys is not null).SelectMany(item => item.RequestedOutputMeasurementKeys).Distinct().ToArray();

                if (typeof(IInputAdapter).IsAssignableFrom(typeof(T)))
                    return this.Cast<IInputAdapter>().Where(item => item.RequestedOutputMeasurementKeys is not null).SelectMany(item => item.RequestedOutputMeasurementKeys).Distinct().ToArray();
            }

            return null;
        }
        set => m_requestedOutputMeasurementKeys = value;
    }

    /// <summary>
    /// Gets the flag indicating if this adapter collection supports temporal processing.
    /// </summary>
    /// <remarks>
    /// For collections this defaults to <c>false</c>.
    /// </remarks>
    public virtual bool SupportsTemporalProcessing => false;

    /// <summary>
    /// Gets the start time temporal processing constraint defined by call to <see cref="SetTemporalConstraint"/>.
    /// </summary>
    /// <remarks>
    /// This value will be <see cref="DateTime.MinValue"/> when start time constraint is not set - meaning the adapter
    /// is processing data in real-time.
    /// </remarks>
    public virtual DateTime StartTimeConstraint => m_startTimeConstraint;

    /// <summary>
    /// Gets the stop time temporal processing constraint defined by call to <see cref="SetTemporalConstraint"/>.
    /// </summary>
    /// <remarks>
    /// This value will be <see cref="DateTime.MaxValue"/> when stop time constraint is not set - meaning the adapter
    /// is processing data in real-time.
    /// </remarks>
    public virtual DateTime StopTimeConstraint => m_stopTimeConstraint;

    /// <summary>
    /// Gets or sets the desired processing interval, in milliseconds, for the adapter collection and applies this interval to each adapter.
    /// </summary>
    /// <remarks>
    /// With the exception of the values of -1 and 0, this value specifies the desired processing interval for data, i.e.,
    /// basically a delay, or timer interval, over which to process data. A value of -1 means to use the default processing
    /// interval while a value of 0 means to process data as fast as possible.
    /// </remarks>
    public virtual int ProcessingInterval
    {
        get => m_processingInterval;
        set
        {
            m_processingInterval = value;

            if (m_processingInterval < -1)
                m_processingInterval = -1;

            // Apply this new processing interval for all adapters in the collection
            lock (this)
            {
                foreach (T item in this)
                {
                    item.ProcessingInterval = m_processingInterval;
                }
            }
        }
    }

    /// <summary>
    /// Gets the total amount of time, in seconds, that the adapter has been active.
    /// </summary>
    public virtual Time RunTime
    {
        get
        {
            Ticks processingTime = 0;

            if (m_startTime > 0)
            {
                if (m_stopTime > 0)
                    processingTime = m_stopTime - m_startTime;
                else
                    processingTime = DateTime.UtcNow.Ticks - m_startTime;
            }

            if (processingTime < 0)
                processingTime = 0;

            return processingTime.ToSeconds();
        }
    }

    /// <summary>
    /// Gets the total number of measurements processed thus far by each <see cref="IAdapter"/> implementation
    /// in the <see cref="AdapterCollectionBase{T}"/>.
    /// </summary>
    public virtual long ProcessedMeasurements
    {
        get
        {
            long processedMeasurements = 0;

            // Calculate new total for all adapters
            lock (this)
            {
                foreach (T item in this)
                {
                    processedMeasurements += item.ProcessedMeasurements;
                }
            }

            return processedMeasurements;
        }
    }

    /// <summary>
    /// Gets or sets enabled state of this <see cref="AdapterCollectionBase{T}"/>.
    /// </summary>
    public virtual bool Enabled
    {
        get => m_enabled;
        set
        {
            if (m_enabled && !value)
                Stop();
            else if (!m_enabled && value)
                Start();
        }
    }

    /// <summary>
    /// Gets the UTC time this <see cref="AdapterCollectionBase{T}"/> was started.
    /// </summary>
    public Ticks StartTime => m_startTime;

    /// <summary>
    /// Gets the UTC time this <see cref="AdapterCollectionBase{T}"/> was stopped.
    /// </summary>
    public Ticks StopTime => m_stopTime;

    /// <summary>
    /// Gets a flag that indicates whether the object has been disposed.
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the <see cref="AdapterCollectionBase{T}"/> is read-only.
    /// </summary>
    public virtual bool IsReadOnly => false;

    /// <summary>
    /// Gets or sets flag that determines if monitor timer should be used for monitoring processed measurement statistics for the <see cref="AdapterCollectionBase{T}"/>.
    /// </summary>
    protected virtual bool MonitorTimerEnabled
    {
        get => m_monitorTimerEnabled;
        set
        {
            m_monitorTimerEnabled = value;

            if (m_monitorTimer is not null)
                m_monitorTimer.Enabled = value && Enabled;
        }
    }

    /// <summary>
    /// Gets flag that determines if <see cref="IAdapter"/> implementations are automatically initialized
    /// when they are added to the collection.
    /// </summary>
    protected virtual bool AutoInitialize => true;

    /// <summary>
    /// Gets settings <see cref="Dictionary{TKey,TValue}"/> parsed when <see cref="ConnectionString"/> was assigned.
    /// </summary>
    public Dictionary<string, string> Settings { get; private set; }

    /// <summary>
    /// Gets the descriptive status of this <see cref="AdapterCollectionBase{T}"/>.
    /// </summary>
    public virtual string Status
    {
        get
        {
            StringBuilder status = new();
            DataSet dataSource = DataSource;

            // Show collection status
            status.AppendLine($"  Total adapter components: {Count}");
            status.AppendLine($"    Collection initialized: {Initialized}");
            status.AppendLine($"    Initialization timeout: {(InitializationTimeout < 0 ? "Infinite" : $"{InitializationTimeout:N0} milliseconds")}");
            status.AppendLine($" Current operational state: {(Enabled ? nameof(Enabled) : "Disabled")}");
            status.AppendLine($"       Temporal processing: {(SupportsTemporalProcessing ? "Supported" : "Unsupported")}");

            if (SupportsTemporalProcessing)
            {
                status.AppendLine($"     Start time constraint: {(StartTimeConstraint == DateTime.MinValue ? "Unspecified" : StartTimeConstraint.ToString("yyyy-MM-dd HH:mm:ss.fff"))}");
                status.AppendLine($"      Stop time constraint: {(StopTimeConstraint == DateTime.MaxValue ? "Unspecified" : StopTimeConstraint.ToString("yyyy-MM-dd HH:mm:ss.fff"))}");
                status.AppendLine($"       Processing interval: {(ProcessingInterval < 0 ? "Default" : ProcessingInterval == 0 ? "As fast as possible" : $"{ProcessingInterval} milliseconds")}");
            }

            if (MonitorTimerEnabled)
            {
                status.AppendLine($"    Processed measurements: {m_processedMeasurements:N0}");
                status.AppendLine($"   Average processing rate: {(int)(m_processedMeasurements / m_totalProcessTime):N0} measurements / second");
            }

            status.AppendLine($"       Data source defined: {dataSource is not null}");

            if (dataSource is not null)
                status.AppendLine($"    Referenced data source: {dataSource.DataSetName}, {dataSource.Tables.Count:N0} tables");

            status.AppendLine($"    Data source table name: {DataMember}");

            Dictionary<string, string> keyValuePairs = Settings;

            status.AppendLine($"         Connection string: {keyValuePairs.Count:N0} key/value pairs");
            status.AppendLine();

            //                            1         2         3         4         5         6         7
            //                   123456789012345678901234567890123456789012345678901234567890123456789012345678
            //                                         Key = Value
            //                                                        1         2         3         4         5
            //                                               12345678901234567890123456789012345678901234567890
            foreach (KeyValuePair<string, string> item in keyValuePairs)
            {
                char[] keyChars = item.Key.Trim().ToCharArray();
                keyChars[0] = char.ToUpper(keyChars[0]);
                string value = item.Value.Trim();

                if (value.Length > 50)
                    value = $"{value.TruncateRight(47)}...";

                status.AppendLine($"{new string(keyChars).TruncateRight(25),25} = {value,-50}");
            }

            status.AppendLine();

            if (Count > 0)
            {
                int index = 0;

                status.AppendLine();
                status.AppendLine($"Status of each {Name} component:");
                status.AppendLine(new string('-', 79));

                // Show the status of registered components.
                lock (this)
                {
                    foreach (T item in this)
                    {
                        IProvideStatus statusProvider = item;

                        if (statusProvider is null)
                            continue;

                        // This component provides status information.                       
                        status.AppendLine();
                        status.AppendLine($"Status of {typeof(T).Name} component {++index}, {statusProvider.Name}:");

                        try
                        {
                            status.Append(statusProvider.Status);
                        }
                        catch (Exception ex)
                        {
                            status.AppendLine($"Failed to retrieve status due to exception: {ex.Message}");
                        }
                    }
                }

                status.AppendLine();
                status.AppendLine(new string('-', 79));
            }

            return status.ToString();
        }
    }

    #endregion

    #region [ Methods ]

    /// <summary>
    /// Releases all the resources used by the <see cref="AdapterCollectionBase{T}"/> object.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the unmanaged resources used by the <see cref="AdapterCollectionBase{T}"/> object and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (IsDisposed)
            return;

        try
        {
            if (!disposing)
                return;

            m_monitorTimer.Elapsed -= m_monitorTimer_Elapsed;
            m_monitorTimer.Dispose();

            Clear();        // This disposes all items in collection...
        }
        finally
        {
            IsDisposed = true;  // Prevent duplicate dispose.
            Disposed?.SafeInvoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Loads all <see cref="IAdapter"/> implementations defined in <see cref="DataSource"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Table name specified in <see cref="DataMember"/> from <see cref="DataSource"/> is expected
    /// to have the following table column names:<br/>
    /// ID, AdapterName, AssemblyName, TypeName, ConnectionString<br/>
    /// ID column type should be integer based, all other column types are expected to be string based.
    /// </para>
    /// <para>
    /// Note that when calling this method any existing items will be cleared allowing a "re-initialize".
    /// </para>
    /// </remarks>
    /// <exception cref="NullReferenceException">DataSource is null.</exception>
    /// <exception cref="InvalidOperationException">DataMember is null or empty.</exception>
    public virtual void Initialize()
    {
        if (DataSource is null)
            throw new NullReferenceException($"DataSource is null, cannot load {Name}");

        if (string.IsNullOrWhiteSpace(DataMember))
            throw new InvalidOperationException($"DataMember is null or empty, cannot load {Name}");

        Initialized = false;

        Dictionary<string, string> settings = Settings;

        // Load the default initialization parameter for adapters in this collection
        if (settings.TryGetValue(nameof(InitializationTimeout), out string setting))
            InitializationTimeout = int.Parse(setting);

        lock (this)
        {
            Clear();

            if (DataSource.Tables.Contains(DataMember))
            {
                foreach (DataRow adapterRow in DataSource.Tables[DataMember].Rows)
                {
                    if (TryCreateAdapter(adapterRow, out T item))
                        Add(item);
                }

                Initialized = true;
            }
            else
            {
                throw new InvalidOperationException($"Data set member \"{DataMember}\" was not found in data source, check ConfigurationEntity. Failed to initialize {Name}.");
            }
        }
    }

    /// <summary>
    /// Attempts to create an <see cref="IAdapter"/> from the specified <see cref="DataRow"/>.
    /// </summary>
    /// <param name="adapterRow"><see cref="DataRow"/> containing item information to initialize.</param>
    /// <param name="adapter">Initialized adapter if successful; otherwise null.</param>
    /// <returns><c>true</c> if item was successfully initialized; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// See <see cref="DataSource"/> property for expected <see cref="DataRow"/> column names.
    /// </remarks>
    /// <exception cref="NullReferenceException"><paramref name="adapterRow"/> is null.</exception>
    public virtual bool TryCreateAdapter(DataRow adapterRow, out T adapter)
    {
        if (adapterRow is null)
            throw new NullReferenceException("Cannot initialize from null adapter DataRow");

        string name = "", assemblyName = "", typeName = "";

        try
        {
            name = adapterRow["AdapterName"].ToNonNullString("[IAdapter]");
            assemblyName = FilePath.GetAbsolutePath(adapterRow[nameof(AssemblyName)].ToNonNullString());
            typeName = adapterRow["TypeName"].ToNonNullString();
            string connectionString = adapterRow[nameof(ConnectionString)].ToNonNullString();
            uint id = uint.Parse(adapterRow[nameof(ID)].ToNonNullString("0"));

            if (string.IsNullOrWhiteSpace(assemblyName) || string.IsNullOrWhiteSpace(typeName))
            {
                // If either assembly name or type name is empty, try to pull protocol name from the
                // connection string and load values from the adapter cache
                Dictionary<string, string> settings = connectionString.ParseKeyValuePairs();

                if ((settings.TryGetValue("protocol", out string? protocol) || settings.TryGetValue("phasorProtocol", out protocol)) && !string.IsNullOrWhiteSpace(protocol))
                {
                    foreach (AdapterProtocolInfo adapterProtocol in AdapterCache.AdapterProtocols.Values)
                    {
                        if (adapterProtocol.Attributes.Any(attribute => string.Equals(protocol, attribute.Acronym, StringComparison.OrdinalIgnoreCase)))
                        {
                            assemblyName = adapterProtocol.Info.AssemblyName;
                            typeName = adapterProtocol.Info.TypeName;
                            break;
                        }
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(typeName))
                throw new InvalidOperationException("No adapter type was defined");

            if (!File.Exists(assemblyName))
                throw new InvalidOperationException("Specified adapter assembly does not exist");

            Assembly assembly = Assembly.LoadFrom(assemblyName);
            adapter = (T)Activator.CreateInstance(assembly.GetType(typeName)!)!;

            // Assign critical adapter properties
            adapter.Name = name;
            adapter.ID = id;
            adapter.ConnectionString = connectionString;
            adapter.DataSource = DataSource;

            // Assign adapter initialization timeout   
            adapter.InitializationTimeout = adapter.Settings.TryGetValue(nameof(InitializationTimeout), out string? setting) ?
                int.Parse(setting) :
                InitializationTimeout;

            return true;
        }
        catch (Exception ex)
        {
            // We report any errors encountered during type creation...
            OnProcessException(MessageLevel.Warning, new InvalidOperationException($"Failed to load adapter \"{name}\" [{typeName}] from \"{assemblyName}\": {ex.Message}", ex));
        }

        adapter = null!;
        return false;
    }

    // Explicit IAdapter implementation of TryCreateAdapter
    bool IAdapterCollection.TryCreateAdapter(DataRow adapterRow, out IAdapter adapter)
    {
        bool result = TryCreateAdapter(adapterRow, out T adapterT);
        adapter = adapterT;
        return result;
    }

    /// <summary>
    /// Attempts to get the adapter with the specified <paramref name="id"/>.
    /// </summary>
    /// <param name="id">ID of adapter to get.</param>
    /// <param name="adapter">Adapter reference if found; otherwise null.</param>
    /// <returns><c>true</c> if adapter with the specified <paramref name="id"/> was found; otherwise <c>false</c>.</returns>
    public virtual bool TryGetAdapterByID(uint id, out T adapter) =>
        TryGetAdapter(id, (item, value) => item.ID == value, out adapter);

    /// <summary>
    /// Attempts to get the adapter with the specified <paramref name="name"/>.
    /// </summary>
    /// <param name="name">Name of adapter to get.</param>
    /// <param name="adapter">Adapter reference if found; otherwise null.</param>
    /// <returns><c>true</c> if adapter with the specified <paramref name="name"/> was found; otherwise <c>false</c>.</returns>
    public virtual bool TryGetAdapterByName(string name, out T adapter) =>
        TryGetAdapter(name, (item, value) => item.Name.Equals(value, StringComparison.OrdinalIgnoreCase), out adapter);

    /// <summary>
    /// Attempts to get the adapter with the specified <paramref name="value"/> given <paramref name="testItem"/> function.
    /// </summary>
    /// <param name="value">Value of adapter to get.</param>
    /// <param name="testItem">Function delegate used to test item <paramref name="value"/>.</param>
    /// <param name="adapter">Adapter reference if found; otherwise null.</param>
    /// <returns><c>true</c> if adapter with the specified <paramref name="value"/> was found; otherwise <c>false</c>.</returns>
    protected virtual bool TryGetAdapter<TValue>(TValue value, Func<T, TValue, bool> testItem, out T adapter)
    {
        lock (this)
        {
            foreach (T item in this)
            {
                if (!testItem(item, value))
                    continue;

                adapter = item;
                return true;
            }
        }

        adapter = default;
        return false;
    }

    // Explicit IAdapter implementation of TryGetAdapterByID
    bool IAdapterCollection.TryGetAdapterByID(uint id, out IAdapter adapter)
    {
        bool result = TryGetAdapterByID(id, out T adapterT);
        adapter = adapterT;
        return result;
    }

    // Explicit IAdapter implementation of TryGetAdapterByName
    bool IAdapterCollection.TryGetAdapterByName(string name, out IAdapter adapter)
    {
        bool result = TryGetAdapterByName(name, out T adapterT);
        adapter = adapterT;
        return result;
    }

    /// <summary>
    /// Attempts to initialize (or reinitialize) an individual <see cref="IAdapter"/> based on its ID.
    /// </summary>
    /// <param name="id">The numeric ID associated with the <see cref="IAdapter"/> to be initialized.</param>
    /// <returns><c>true</c> if item was successfully initialized; otherwise <c>false</c>.</returns>
    public virtual bool TryInitializeAdapterByID(uint id)
    {
        foreach (DataRow adapterRow in DataSource.Tables[DataMember].Rows)
        {
            uint rowID = uint.Parse(adapterRow[nameof(ID)].ToNonNullString("0"));

            if (rowID != id)
                continue;

            if (TryCreateAdapter(adapterRow, out T newAdapter))
            {
                // Found and created new item - update collection reference
                bool foundItem = false;

                lock (this)
                {
                    for (int i = 0; i < Count; i++)
                    {
                        T oldAdapter = this[i];

                        if (oldAdapter.ID != id)
                            continue;

                        // Dispose old item, initialize new item
                        this[i] = newAdapter;

                        foundItem = true;
                        break;
                    }

                    // Add item to collection if it didn't exist
                    if (!foundItem)
                        Add(newAdapter);

                    return true;
                }
            }

            break;
        }

        return false;
    }

    /// <summary>
    /// Starts, or restarts, each <see cref="IAdapter"/> implementation in this <see cref="AdapterCollectionBase{T}"/>.
    /// </summary>
    [AdapterCommand("Starts, or restarts, each adapter in the collection.", ResourceAccessLevel.Admin, ResourceAccessLevel.Edit)]
    public virtual void Start()
    {
        // Make sure we are stopped (e.g., disconnected) before attempting to start (e.g., connect)
        if (m_enabled)
            return;

        m_enabled = true;
        m_stopTime = 0;
        m_startTime = DateTime.UtcNow.Ticks;

        ResetStatistics();

        lock (this)
        {
            foreach (T item in this)
            {
                if (!item.Initialized || !item.AutoStart || item.Enabled)
                    continue;

                // Create local reference to the foreach
                // variable to be accessed in the lambda function
                T itemRef = item;

                // Push start command to the lifecycle thread for the adapter
                LogicalThread lifecycleThread = m_lifecycleThreads.GetOrAdd(item.ID, _ => m_lifecycleThreadScheduler.CreateThread());
                lifecycleThread.Push(() => Start(itemRef));
            }
        }

        // Start data monitor...
        if (MonitorTimerEnabled)
            m_monitorTimer.Start();
    }

    /// <summary>
    /// Stops each <see cref="IAdapter"/> implementation in this <see cref="AdapterCollectionBase{T}"/>.
    /// </summary>
    [AdapterCommand("Stops each adapter in the collection.", ResourceAccessLevel.Admin, ResourceAccessLevel.Edit)]
    public virtual void Stop()
    {
        if (!m_enabled)
            return;

        m_enabled = false;
        m_stopTime = DateTime.UtcNow.Ticks;

        lock (this)
        {
            foreach (T item in this)
            {
                if (!item.Initialized || !item.Enabled)
                    continue;

                // Create local reference to the foreach
                // variable to be accessed in the lambda function
                T itemRef = item;

                // Push stop command to the lifecycle thread for the adapter
                LogicalThread lifecycleThread = m_lifecycleThreads.GetOrAdd(item.ID, _ => m_lifecycleThreadScheduler.CreateThread());
                lifecycleThread.Push(() => Stop(itemRef));
            }
        }

        // Stop data monitor...
        m_monitorTimer.Stop();
    }

    /// <summary>
    /// Resets the statistics of this collection.
    /// </summary>
    [AdapterCommand("Resets the statistics of this collection.", ResourceAccessLevel.Admin, ResourceAccessLevel.Edit)]
    [Label("Reset Statistics")]
    public void ResetStatistics()
    {
        m_processedMeasurements = 0;
        m_totalProcessTime = 0.0D;
        m_lastProcessTime = DateTime.UtcNow.Ticks;

        OnStatusMessage(MessageLevel.Info, "Statistics reset for this collection.");
    }

    /// <summary>
    /// Gets a short one-line status of this <see cref="AdapterCollectionBase{T}"/>.
    /// </summary>
    /// <param name="maxLength">Maximum number of available characters for display.</param>
    /// <returns>A short one-line summary of the current status of this <see cref="AdapterCollectionBase{T}"/>.</returns>
    public virtual string GetShortStatus(int maxLength) =>
        $"Total components: {Count:N0}".CenterText(maxLength);

    /// <summary>
    /// Defines a temporal processing constraint for the adapter collection and applies this constraint to each adapter.
    /// </summary>
    /// <param name="startTime">Defines a relative or exact start time for the temporal constraint.</param>
    /// <param name="stopTime">Defines a relative or exact stop time for the temporal constraint.</param>
    /// <param name="constraintParameters">Defines any temporal parameters related to the constraint.</param>
    /// <remarks>
    /// <para>
    /// This method defines a temporal processing constraint for an adapter, i.e., the start and stop time over which an
    /// adapter will process data. Actual implementation of the constraint will be adapter specific. Implementations
    /// should be able to dynamically handle multiple calls to this function with new constraints. Passing in <c>null</c>
    /// for the <paramref name="startTime"/> and <paramref name="stopTime"/> should cancel the temporal constraint and
    /// return the adapter to standard / real-time operation.
    /// </para>
    /// <para>
    /// The <paramref name="startTime"/> and <paramref name="stopTime"/> parameters can be specified in one of the
    /// following formats:
    /// <list type="table">
    ///     <listheader>
    ///         <term>Time Format</term>
    ///         <description>Format Description</description>
    ///     </listheader>
    ///     <item>
    ///         <term>12-30-2000 23:59:59.033</term>
    ///         <description>Absolute date and time.</description>
    ///     </item>
    ///     <item>
    ///         <term>*</term>
    ///         <description>Evaluates to <see cref="DateTime.UtcNow"/>.</description>
    ///     </item>
    ///     <item>
    ///         <term>*-20s</term>
    ///         <description>Evaluates to 20 seconds before <see cref="DateTime.UtcNow"/>.</description>
    ///     </item>
    ///     <item>
    ///         <term>*-10m</term>
    ///         <description>Evaluates to 10 minutes before <see cref="DateTime.UtcNow"/>.</description>
    ///     </item>
    ///     <item>
    ///         <term>*-1h</term>
    ///         <description>Evaluates to 1 hour before <see cref="DateTime.UtcNow"/>.</description>
    ///     </item>
    ///     <item>
    ///         <term>*-1d</term>
    ///         <description>Evaluates to 1 day before <see cref="DateTime.UtcNow"/>.</description>
    ///     </item>
    /// </list>
    /// </para>
    /// </remarks>
    [AdapterCommand("Defines a temporal processing constraint for each adapter in the collection.", ResourceAccessLevel.Admin, ResourceAccessLevel.Edit, ResourceAccessLevel.View)]
    [Label("Set Temporal Constraint")]
    [Parameter(nameof(startTime), "Start Time", "Defines a relative or exact start time for the temporal constraint, defaults to DateTime.MinValue if blank.")]
    [Parameter(nameof(stopTime), "Stop Time", "Defines a relative or exact stop time for the temporal constraint, defaults to DateTime.MaxValue if blank.")]
    [Parameter(nameof(constraintParameters), "Constraint Parameters", "Defines any temporal parameters related to the constraint.")]
    public virtual void SetTemporalConstraint(string? startTime, string? stopTime, string? constraintParameters)
    {
        m_startTimeConstraint = string.IsNullOrWhiteSpace(startTime) ?
            DateTime.MinValue :
            AdapterBase.ParseTimeTag(startTime);

        m_stopTimeConstraint = string.IsNullOrWhiteSpace(stopTime) ?
            DateTime.MaxValue :
            AdapterBase.ParseTimeTag(stopTime);

        // Apply temporal constraint to all adapters in this collection
        lock (this)
        {
            foreach (T adapter in this)
                adapter.SetTemporalConstraint(startTime, stopTime, constraintParameters);
        }
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
    protected internal virtual void OnStatusMessage(MessageLevel level, string status, string? eventName = null, MessageFlags flags = MessageFlags.None)
    {
        try
        {
            Log.Publish(level, flags, eventName, status);

            using (Logger.SuppressLogMessages())
                StatusMessage?.SafeInvoke(this, new EventArgs<string>(AdapterBase.GetStatusWithMessageLevelPrefix(status, level)));
        }
        catch (Exception ex)
        {
            // We protect our code from consumer thrown exceptions
            OnProcessException(MessageLevel.Info, new InvalidOperationException($"Exception in consumer handler for {nameof(StatusMessage)} event: {ex.Message}", ex), "ConsumerEventException");
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
    protected internal virtual void OnProcessException(MessageLevel level, Exception exception, string? eventName = null, MessageFlags flags = MessageFlags.None)
    {
        try
        {
            Log.Publish(level, flags, eventName, exception.Message, null, exception);

            using (Logger.SuppressLogMessages())
                ProcessException?.SafeInvoke(this, new EventArgs<Exception>(exception));
        }
        catch (Exception ex)
        {
            // We protect our code from consumer thrown exceptions
            Log.Publish(MessageLevel.Info, "ConsumerEventException", $"Exception in consumer handler for {nameof(ProcessException)} event: {ex.Message}", null, ex);
        }
    }

    /// <summary>
    /// Raises <see cref="InputMeasurementKeysUpdated"/> event.
    /// </summary>
    protected virtual void OnInputMeasurementKeysUpdated()
    {
        InputMeasurementKeysUpdated?.SafeInvoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Raises <see cref="OutputMeasurementsUpdated"/> event.
    /// </summary>
    protected virtual void OnOutputMeasurementsUpdated()
    {
        OutputMeasurementsUpdated?.SafeInvoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Raises <see cref="ConfigurationChanged"/> event.
    /// </summary>
    protected virtual void OnConfigurationChanged()
    {
        ConfigurationChanged?.SafeInvoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Removes all elements from the <see cref="Collection{T}"/>.
    /// </summary>
    protected override void ClearItems()
    {
        // Dispose each item before clearing the collection
        lock (this)
        {
            foreach (T item in this)
                DisposeItem(item);

            base.ClearItems();
        }
    }

    /// <summary>
    /// Inserts an element into the <see cref="Collection{T}"/> the specified index.
    /// </summary>
    /// <param name="index">The zero-based index at which item should be inserted.</param>
    /// <param name="item">The <see cref="IAdapter"/> implementation to insert.</param>
    protected override void InsertItem(int index, T item)
    {
        lock (this)
        {
            // Wire up item events and handle item initialization
            InitializeItem(item);
            base.InsertItem(index, item);
        }
    }

    /// <summary>
    /// Assigns a new element to the <see cref="Collection{T}"/> at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index for which item should be assigned.</param>
    /// <param name="item">The <see cref="IAdapter"/> implementation to assign.</param>
    protected override void SetItem(int index, T item)
    {
        lock (this)
        {
            // Dispose of existing item
            DisposeItem(this[index]);

            // Wire up item events and handle initialization of new item
            InitializeItem(item);

            base.SetItem(index, item);
        }
    }

    /// <summary>
    /// Removes the element at the specified index of the <see cref="Collection{T}"/>.
    /// </summary>
    /// <param name="index">The zero-based index of the element to remove.</param>
    protected override void RemoveItem(int index)
    {
        // Dispose of item before removing it from the collection
        lock (this)
        {
            DisposeItem(this[index]);
            base.RemoveItem(index);
        }
    }

    /// <summary>
    /// Wires events and initializes new <see cref="IAdapter"/> implementation.
    /// </summary>
    /// <param name="item">New <see cref="IAdapter"/> implementation.</param>
    /// <remarks>
    /// Derived classes should override if more events are defined.
    /// </remarks>
    protected virtual void InitializeItem(T item)
    {
        if (item is null)
            return;

        // Wire up events
        item.StatusMessage += item_StatusMessage;
        item.ProcessException += item_ProcessException;
        item.ConfigurationChanged += item_ConfigurationChanged;
        item.Disposed += item_Disposed;

        try
        {
            if (!AutoInitialize)
                return;

            // If automatically initializing new elements, handle object initialization on
            // its own thread so it can take needed amount of time
            LogicalThread lifecycleThread = GetLifecycleThread(item);

            lifecycleThread.Push(() =>
            {
                m_activeItem.Value = item;
                LogicalThread.CurrentThread.Push(() => Initialize(item));
            });
        }
        catch (Exception ex)
        {
            // Process exception for logging
            string errorMessage = $"Failed to queue initialize operation for adapter {item.Name}: {ex.Message}";
            OnProcessException(MessageLevel.Warning, new InvalidOperationException(errorMessage, ex));
        }
    }

    /// <summary>
    /// Un-wires events and disposes of <see cref="IAdapter"/> implementation.
    /// </summary>
    /// <param name="item"><see cref="IAdapter"/> to dispose.</param>
    /// <remarks>
    /// Derived classes should override if more events are defined.
    /// </remarks>
    protected virtual void DisposeItem(T item)
    {
        if (item is null)
            return;

        LogicalThread lifecycleThread = GetLifecycleThread(item);
        lifecycleThread.Push(() => Dispose(item));
    }

    // Handle item initialization
    private void Initialize(T item)
    {
        Action? initializationTimeoutAction = null;
        Func<bool>? cancelInitializationTimeout = null;

        if (m_activeItem.Value != item)
            return;

        try
        {
            // Initialize item if not initialized already
            if (!item.Initialized)
            {
                // If initialization timeout is specified for this item, start the initialization timeout timer
                if (item.InitializationTimeout > 0)
                {
                    initializationTimeoutAction = () =>
                    {
                        const string MessageFormat = "Initialization of adapter {0} has exceeded" +
                                                     " its timeout of {1} seconds. The adapter may still initialize, however this" +
                                                     " may indicate a problem with the adapter. If you consider this to be normal," +
                                                     " try adjusting the initialization timeout to suppress this message during" +
                                                     " normal operations.";

                        OnStatusMessage(MessageLevel.Warning, string.Format(MessageFormat, item.Name, item.InitializationTimeout / 1000.0), "Initialization");

                        // ReSharper disable once AccessToModifiedClosure
                        cancelInitializationTimeout = initializationTimeoutAction?.DelayAndExecute(item.InitializationTimeout);
                    };

                    cancelInitializationTimeout = initializationTimeoutAction.DelayAndExecute(item.InitializationTimeout);
                }

                // Initialize the item
                item.Initialize();

                // Initialization successfully completed, so stop the timeout timer
                cancelInitializationTimeout?.Invoke();
            }

            // If the item is set to auto-start and not already started, start it now
            if (item.AutoStart && !item.Enabled)
            {
                LogicalThread.CurrentThread.Push(() =>
                {
                    Start(item);

                    // Set item to its final initialized state so that
                    // start and stop commands may be issued to the adapter
                    item.Initialized = true;

                    // Now that the adapter is fully initialized,
                    // attach to these events to react to updates at runtime
                    item.InputMeasurementKeysUpdated += item_InputMeasurementKeysUpdated;
                    item.OutputMeasurementsUpdated += item_OutputMeasurementsUpdated;

                    // If input measurement keys were not updated during initialize of the adapter,
                    // make sure to notify routing tables that adapter is ready for broadcast
                    OnInputMeasurementKeysUpdated();
                });
            }
            else
            {
                // Set item to its final initialized state so that
                // start and stop commands may be issued to the adapter
                item.Initialized = true;

                // Now that the adapter is fully initialized,
                // attach to these events to react to updates at runtime
                item.InputMeasurementKeysUpdated += item_InputMeasurementKeysUpdated;
                item.OutputMeasurementsUpdated += item_OutputMeasurementsUpdated;

                // If input measurement keys were not updated during initialize of the adapter,
                // make sure to notify routing tables that adapter is ready for broadcast
                OnInputMeasurementKeysUpdated();
            }
        }
        catch (Exception ex)
        {
            // We report any errors encountered during initialization...
            OnProcessException(MessageLevel.Warning, new InvalidOperationException($"Failed to initialize adapter {item.Name}: {ex.Message}", ex), "Initialization");

            // Initialization failed, so stop the timeout timer
            cancelInitializationTimeout?.Invoke();
        }
    }

    // Handle item startup
    private void Start(T item)
    {
        if (m_activeItem.Value != item)
            return;

        try
        {
            item.Start();
        }
        catch (Exception ex)
        {
            // We report any errors encountered during startup...
            OnProcessException(MessageLevel.Warning, new InvalidOperationException($"Failed to start adapter {item.Name}: {ex.Message}", ex), "Startup");
        }
    }

    // Handle item stop
    private void Stop(T item)
    {
        if (m_activeItem.Value != item)
            return;

        if (item.Initialized && item.Enabled)
            item.Stop();
    }

    // Handles item disposal
    private void Dispose(T item)
    {
        // Stop item, then un-wire events
        item.Stop();
        item.StatusMessage -= item_StatusMessage;
        item.ProcessException -= item_ProcessException;
        item.InputMeasurementKeysUpdated -= item_InputMeasurementKeysUpdated;
        item.OutputMeasurementsUpdated -= item_OutputMeasurementsUpdated;
        item.ConfigurationChanged -= item_ConfigurationChanged;

        // Dispose of item, then un-wire disposed event
        item.Dispose();
        item.Disposed -= item_Disposed;
    }

    // Gets the lifecycle thread for the given item
    private LogicalThread GetLifecycleThread(T item)
    {
        return m_lifecycleThreads.GetOrAdd(item.ID, _ =>
        {
            LogicalThread thread = m_lifecycleThreadScheduler.CreateThread();
            thread.UnhandledException += (_, args) => item_ProcessException(item, args);
            return thread;
        });
    }

    // Raise status message event on behalf of each item in collection
    private void item_StatusMessage(object? sender, EventArgs<string> e) => StatusMessage?.SafeInvoke(sender, e);

    // Raise process exception event on behalf of each item in collection
    private void item_ProcessException(object? sender, EventArgs<Exception> e) => ProcessException?.SafeInvoke(sender, e);

    // Raise input measurement keys updated event on behalf of each item in collection
    private void item_InputMeasurementKeysUpdated(object? sender, EventArgs e) => InputMeasurementKeysUpdated?.SafeInvoke(sender, e);

    // Raise output measurements updated event on behalf of each item in collection
    private void item_OutputMeasurementsUpdated(object? sender, EventArgs e) => OutputMeasurementsUpdated?.SafeInvoke(sender, e);

    // Raise configuration changed event on behalf of each item in collection
    private void item_ConfigurationChanged(object? sender, EventArgs e) => ConfigurationChanged?.SafeInvoke(sender, e);

    // Raise disposed event on behalf of each item in collection
    private void item_Disposed(object? sender, EventArgs e) => Disposed?.SafeInvoke(sender, e);

    // We monitor the total number of measurements destined for archival here...
    private void m_monitorTimer_Elapsed(object? sender, EventArgs<DateTime> e)
    {
        StringBuilder status = new();
        long processedMeasurements = ProcessedMeasurements;

        // Calculate time since last call
        Ticks currentTime = DateTime.UtcNow.Ticks;
        Ticks totalProcessTime = currentTime - m_lastProcessTime;

        m_totalProcessTime += totalProcessTime.ToSeconds();
        m_lastProcessTime = currentTime;

        // Calculate how many new measurements have been received in the last minute...
        long totalNew = processedMeasurements - m_processedMeasurements;
        m_processedMeasurements = processedMeasurements;

        // Process statistics for 12 hours total runtime:
        //
        //          1              1                 1
        // 12345678901234 12345678901234567 1234567890
        // Time span        Measurements    Per second
        // -------------- ----------------- ----------
        // Entire runtime 9,999,999,999,999 99,999,999
        // Last minute         4,985            83

        status.AppendFormat("\r\nProcess statistics for {0} total runtime:\r\n\r\n", m_totalProcessTime.ToString(3).ToLower());
        status.Append("Time span".PadRight(14));
        status.Append(' ');
        status.Append("Measurements".CenterText(17));
        status.Append(' ');
        status.Append("Per second".CenterText(10));
        status.AppendLine();
        status.Append(new string('-', 14));
        status.Append(' ');
        status.Append(new string('-', 17));
        status.Append(' ');
        status.Append(new string('-', 10));
        status.AppendLine();

        status.Append("Entire runtime".PadRight(14));
        status.Append(' ');
        status.Append(m_processedMeasurements.ToString("N0").CenterText(17));
        status.Append(' ');
        status.Append(((int)(m_processedMeasurements / m_totalProcessTime)).ToString("N0").CenterText(10));
        status.AppendLine();
        status.Append("Last minute".PadRight(14));
        status.Append(' ');
        status.Append(totalNew.ToString("N0").CenterText(17));
        status.Append(' ');
        status.Append(((int)(totalNew / totalProcessTime.ToSeconds())).ToString("N0").CenterText(10));

        // Report updated statistics every minute...
        OnStatusMessage(MessageLevel.Info, "AdapterCollectionBase", status.ToString());
    }

    #region [ Explicit IList<IAdapter> Implementation ]

    void ICollection<IAdapter>.Add(IAdapter item)
    {
        lock (this)
        {
            Add((T)item);
        }
    }

    bool ICollection<IAdapter>.Contains(IAdapter item)
    {
        lock (this)
        {
            return Contains((T)item);
        }
    }

    void ICollection<IAdapter>.CopyTo(IAdapter[] array, int arrayIndex)
    {
        lock (this)
        {
            CopyTo(array.Cast<T>().ToArray(), arrayIndex);
        }
    }

    bool ICollection<IAdapter>.Remove(IAdapter item)
    {
        lock (this)
        {
            return Remove((T)item);
        }
    }

    IEnumerator<IAdapter> IEnumerable<IAdapter>.GetEnumerator()
    {
        IAdapter[] adapters;

        lock (this)
        {
            adapters = new IAdapter[Count];

            for (int i = 0; i < Count; i++)
                adapters[i] = this[i];
        }

        foreach (IAdapter item in adapters)
        {
            yield return item;
        }
    }

    int IList<IAdapter>.IndexOf(IAdapter item)
    {
        lock (this)
        {
            return IndexOf((T)item);
        }
    }

    void IList<IAdapter>.Insert(int index, IAdapter item)
    {
        lock (this)
        {
            Insert(index, (T)item);
        }
    }

    IAdapter IList<IAdapter>.this[int index]
    {
        get
        {
            lock (this)
            {
                return this[index];
            }
        }
        set
        {
            lock (this)
            {
                this[index] = (T)value;
            }
        }
    }

    #endregion

    #endregion
}
