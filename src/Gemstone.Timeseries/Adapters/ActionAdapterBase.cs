﻿//******************************************************************************************************
//  ActionAdapterBase.cs - Gbtc
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
//  02/08/2011 - J. Ritchie Carroll
//       Added invokable ExamineQueueState method to analyze real-time concentrator frame queue state.
//  12/20/2012 - Starlynn Danyelle Gilliam
//       Modified Header.
//  11/08/2023 - Lillian Gensolin
//       Converted code to .NET core.
//
//******************************************************************************************************

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading;
using Gemstone.ComponentModel.DataAnnotations;
using Gemstone.Diagnostics;
using Gemstone.EventHandlerExtensions;
using Gemstone.Security.AccessControl;
using Gemstone.StringExtensions;

namespace Gemstone.Timeseries.Adapters;

/// <summary>
/// Represents the base class for action adapters.
/// </summary>
/// <remarks>
/// This base class acts as a measurement concentrator which time aligns all incoming measurements for proper processing.
/// Derived classes are expected to override <see cref="ConcentratorBase.PublishFrame"/> to handle time aligned measurements
/// and call <see cref="OnNewMeasurements"/> for any new measurements that may get created.
/// </remarks>
[SuppressMessage("SonarQube.Methods", "S1206", Justification = "Class life-cycle is controlled through library.")]
public abstract class ActionAdapterBase : ConcentratorBase, IActionAdapter
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
    /// Provides new measurements from action adapter.
    /// </summary>
    /// <remarks>
    /// <see cref="EventArgs{T}.Argument"/> is a collection of new measurements for host to process.
    /// </remarks>
    public event EventHandler<EventArgs<ICollection<IMeasurement>>> NewMeasurements;

    // Fields
    private string m_name;
    private uint m_id;
    private string m_connectionString;
    private MeasurementKey[]? m_inputMeasurementKeys;
    private IMeasurement[]? m_outputMeasurements;
    private List<string> m_inputSourceIDs;
    private List<string> m_outputSourceIDs;
    private int m_minimumMeasurementsToUse;
    private DateTime m_startTimeConstraint;
    private DateTime m_stopTimeConstraint;
    private int m_hashCode;

    #endregion

    #region [ Constructors ]

    /// <summary>
    /// Creates a new <see cref="ActionAdapterBase"/>.
    /// </summary>
    protected ActionAdapterBase()
    {
        m_name = GetType().Name;
        Log.InitialStackMessages = Log.InitialStackMessages.Union("AdapterName", m_name);
        Settings = new Dictionary<string, string>();
        m_startTimeConstraint = DateTime.MinValue;
        m_stopTimeConstraint = DateTime.MaxValue;
        GenHashCode();

        // Set incoming measurements to none by default
        m_inputMeasurementKeys = [];

        // For most implementations millisecond resolution will be sufficient
        TimeResolution = Ticks.PerMillisecond;
    }

    #endregion

    #region [ Properties ]

    /// <summary>
    /// Gets name of the action adapter.
    /// </summary>
    public virtual string Name
    {
        get => m_name;
        set
        {
            m_name = value;
            Log.InitialStackMessages = Log.InitialStackMessages.Union("AdapterName", m_name);
            GenHashCode();
        }
    }

    /// <summary>
    /// Gets or sets numeric ID associated with this action adapter.
    /// </summary>
    public virtual uint ID
    {
        get => m_id;
        set
        {
            m_id = value;
            GenHashCode();
        }
    }

    /// <summary>
    /// Gets or sets key/value pair connection information specific to action adapter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Example connection string using manually defined measurements:<br/>
    /// <c>framesPerSecond=30; lagTime=1.0; leadTime=0.5; minimumMeasurementsToUse=-1;
    /// useLocalClockAsRealTime=true; allowSortsByArrival=false;</c><br/>
    /// <c>inputMeasurementKeys={P1:1245;P1:1247;P2:1335};</c><br/>
    /// <c>outputMeasurements={P3:1345,60.0,1.0;P3:1346;P3:1347}</c><br/>
    /// When defined manually, elements in key:<br/>
    /// * inputMeasurementKeys are defined as "ArchiveSource:PointID"<br/>
    /// * outputMeasurements are defined as "ArchiveSource:PointID,Adder,Multiplier", the adder and multiplier are optional
    /// defaulting to 0.0 and 1.0 respectively.
    /// <br/>
    /// </para>
    /// <para>
    /// Example connection string using measurements defined in a <see cref="DataSource"/> table:<br/>
    /// <c>framesPerSecond=30; lagTime=1.0; leadTime=0.5; minimumMeasurementsToUse=-1;
    /// useLocalClockAsRealTime=true; allowSortsByArrival=false;</c><br/>
    /// <c>inputMeasurementKeys={FILTER ActiveMeasurements WHERE Company='Gemstone' AND SignalType='FREQ' ORDER BY ID};</c><br/>
    /// <c>outputMeasurements={FILTER ActiveMeasurements WHERE SignalType IN ('IPHA','VPHA') AND Phase='+'}</c><br/>
    /// <br/>
    /// Basic filtering syntax is as follows:<br/>
    /// <br/>
    ///     {FILTER &lt;TableName&gt; WHERE &lt;Expression&gt; [ORDER BY &lt;SortField&gt;]}<br/>
    /// <br/>
    /// Source tables are expected to have at least the following fields:<br/>
    /// <list type="table">
    ///     <listheader>
    ///         <term>Name</term>
    ///         <term>Type</term>
    ///         <description>Description.</description>
    ///     </listheader>
    ///     <item>
    ///         <term>ID</term>
    ///         <term>NVARCHAR</term>
    ///         <description>Measurement key formatted as: ArchiveSource:PointID.</description>
    ///     </item>
    ///     <item>
    ///         <term>PointTag</term>
    ///         <term>NVARCHAR</term>
    ///         <description>Point tag of measurement.</description>
    ///     </item>
    ///     <item>
    ///         <term>Adder</term>
    ///         <term>FLOAT</term>
    ///         <description>Adder to apply to value, if any (default to 0.0).</description>
    ///     </item>
    ///     <item>
    ///         <term>Multiplier</term>
    ///         <term>FLOAT</term>
    ///         <description>Multiplier to apply to value, if any (default to 1.0).</description>
    ///     </item>
    /// </list>
    /// </para>
    /// <para>
    /// Note that framesPerSecond, lagTime and leadTime are required parameters, all other parameters are optional.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// Gets connection info for adapter, if any.
    /// </summary>
    /// <remarks>
    /// For example, this could return IP or host name of source connection.
    /// </remarks>
    public virtual string ConnectionInfo => null;

    /// <summary>
    /// Gets or sets <see cref="DataSet"/> based data source available to this <see cref="ActionAdapterBase"/>.
    /// </summary>
    public virtual DataSet? DataSource { get; set; }

    /// <summary>
    /// Gets or sets maximum time system will wait during <see cref="Start"/> for initialization.
    /// </summary>
    /// <remarks>
    /// Set to <see cref="Timeout.Infinite"/> to wait indefinitely.
    /// </remarks>
    public virtual int InitializationTimeout { get; set; }

    /// <summary>
    /// Gets or sets flag indicating if adapter should automatically start or otherwise connect on demand.
    /// </summary>
    public virtual bool AutoStart { get; set; }

    /// <summary>
    /// Gets or sets flag indicating if action adapter should respect auto-start requests based on input demands.
    /// </summary>
    /// <remarks>
    /// Action adapters are in the curious position of being able to both consume and produce points, as such the user needs to be able to control how their
    /// adapter will behave concerning routing demands when the adapter is setup to connect on demand. In the case of respecting auto-start input demands,
    /// as an example, this would be <c>false</c> for an action adapter that calculated measurement, but <c>true</c> for an action adapter used to archive inputs.
    /// </remarks>
    public virtual bool RespectInputDemands { get; set; }

    /// <summary>
    /// Gets or sets flag indicating if action adapter should respect auto-start requests based on output demands.
    /// </summary>
    /// <remarks>
    /// Action adapters are in the curious position of being able to both consume and produce points, as such the user needs to be able to control how their
    /// adapter will behave concerning routing demands when the adapter is setup to connect on demand. In the case of respecting auto-start output demands,
    /// as an example, this would be <c>true</c> for an action adapter that calculated measurement, but <c>false</c> for an action adapter used to archive inputs.
    /// </remarks>
    public virtual bool RespectOutputDemands { get; set; }

    /// <summary>
    /// Gets or sets the number of frames per second.
    /// </summary>
    /// <remarks>
    /// Valid frame rates for a <see cref="ConcentratorBase"/> are greater than 0 frames per second.
    /// </remarks>
    [ConnectionStringParameter]
    [Description("Defines the number of frames per second expected by the adapter.")]
    [Label("Frames Per Second")]
    public new int FramesPerSecond
    {
        get => base.FramesPerSecond;
        set => base.FramesPerSecond = value;
    }

    /// <summary>
    /// Gets or sets the allowed past time deviation tolerance, in seconds (can be sub-second).
    /// </summary>
    /// <remarks>
    /// <para>Defines the time sensitivity to past measurement timestamps.</para>
    /// <para>The number of seconds allowed before assuming a measurement timestamp is too old.</para>
    /// <para>This becomes the amount of delay introduced by the concentrator to allow time for data to flow into the system.</para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">LagTime must be greater than zero, but it can be less than one.</exception>
    [ConnectionStringParameter]
    [Description("Defines the allowed past time deviation tolerance, in seconds (can be sub-second).")]
    [Label("Lag Time")]
    public new double LagTime
    {
        get => base.LagTime;
        set => base.LagTime = value;
    }

    /// <summary>
    /// Gets or sets the allowed future time deviation tolerance, in seconds (can be sub-second).
    /// </summary>
    /// <remarks>
    /// <para>Defines the time sensitivity to future measurement timestamps.</para>
    /// <para>The number of seconds allowed before assuming a measurement timestamp is too advanced.</para>
    /// <para>This becomes the tolerated +/- accuracy of the local clock to real-time.</para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">LeadTime must be greater than zero, but it can be less than one.</exception>
    [ConnectionStringParameter]
    [Description("Defines the allowed future time deviation tolerance, in seconds (can be sub-second).")]
    [Label("Lead Time")]
    public new double LeadTime
    {
        get => base.LeadTime;
        set => base.LeadTime = value;
    }

    /// <summary>
    /// Gets or sets primary keys of input measurements the action adapter expects.
    /// </summary>
    /// <remarks>
    /// If your adapter needs to receive all measurements, you must explicitly set InputMeasurementKeys to null.
    /// </remarks>
    [ConnectionStringParameter]
    [DefaultValue(null)]
    [Description("Defines primary keys of input measurements the action adapter expects; can be one of a filter expression, measurement key, point tag or Guid.")]
    public virtual MeasurementKey[]? InputMeasurementKeys
    {
        get => m_inputMeasurementKeys;
        set
        {
            if (m_inputMeasurementKeys == value)
                return;

            if (m_inputMeasurementKeys is not null && value is not null)
            {
                if (new HashSet<MeasurementKey>(m_inputMeasurementKeys).SetEquals(value))
                    return;
            }

            m_inputMeasurementKeys = value;

            // The input measurements typically define the "expected measurements" of the action adapter, so
            // we use the number of these items to define the expected measurement count
            ExpectedMeasurements = m_inputMeasurementKeys?.Length ?? 0;

            OnInputMeasurementKeysUpdated();
        }
    }

    /// <summary>
    /// Gets or sets output measurements that the action adapter will produce, if any.
    /// </summary>
    [ConnectionStringParameter]
    [DefaultValue(null)]
    [Description("Defines primary keys of output measurements the action adapter expects; can be one of a filter expression, measurement key, point tag or Guid.")]
    public virtual IMeasurement[]? OutputMeasurements
    {
        get => m_outputMeasurements;
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
    /// This allows an adapter to associate itself with entire collections of measurements based on the source of the measurement keys.
    /// Set to <c>null</c> apply no filter.
    /// </remarks>
    public virtual string[] InputSourceIDs
    {
        get => m_inputSourceIDs?.ToArray();
        set
        {
            if (value is null)
            {
                m_inputSourceIDs = null;
            }
            else
            {
                m_inputSourceIDs = new List<string>(value);
                m_inputSourceIDs.Sort();
            }

            // Filter measurements to list of specified source IDs
            AdapterBase.LoadInputSourceIDs(this);
        }
    }

    /// <summary>
    /// Gets or sets <see cref="MeasurementKey.Source"/> values used to filter output measurements.
    /// </summary>
    /// <remarks>
    /// This allows an adapter to associate itself with entire collections of measurements based on the source of the measurement keys.
    /// Set to <c>null</c> apply no filter.
    /// </remarks>
    public virtual string[] OutputSourceIDs
    {
        get => m_outputSourceIDs?.ToArray();
        set
        {
            if (value is null)
            {
                m_outputSourceIDs = null;
            }
            else
            {
                m_outputSourceIDs = new List<string>(value);
                m_outputSourceIDs.Sort();
            }

            // Filter measurements to list of specified source IDs
            AdapterBase.LoadOutputSourceIDs(this);
        }
    }

    /// <summary>
    /// Gets or sets input measurement keys that are requested by other adapters based on what adapter says it can provide.
    /// </summary>
    public virtual MeasurementKey[]? RequestedInputMeasurementKeys { get; set; }

    /// <summary>
    /// Gets or sets output measurement keys that are requested by other adapters based on what adapter says it can provide.
    /// </summary>
    public virtual MeasurementKey[]? RequestedOutputMeasurementKeys { get; set; }

    /// <summary>
    /// Gets the flag indicating if this adapter supports temporal processing.
    /// </summary>
    public abstract bool SupportsTemporalProcessing { get; }

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
    /// Gets or sets minimum number of input measurements required for adapter.  Set to -1 to require all.
    /// </summary>
    public virtual int MinimumMeasurementsToUse
    {
        get
        {
            // Default to all measurements if minimum is not specified
            if (m_minimumMeasurementsToUse < 1 && InputMeasurementKeys is not null)
                return InputMeasurementKeys.Length;

            return m_minimumMeasurementsToUse;
        }
        set => m_minimumMeasurementsToUse = value;
    }

    /// <summary>
    /// Gets or sets flag indicating if the action adapter has been initialized successfully.
    /// </summary>
    public virtual bool Initialized { get; set; }

    /// <summary>
    /// Gets settings <see cref="Dictionary{TKey,TValue}"/> parsed when <see cref="ConnectionString"/> was assigned.
    /// </summary>
    public Dictionary<string, string> Settings { get; private set; }

    /// <summary>
    /// Gets flags that determines if <see cref="ConcentratorBase"/> class status should be included in <see cref="ActionAdapterBase"/> status.
    /// </summary>
    protected virtual bool ShowConcentratorStatus => true;

    /// <summary>
    /// Returns the detailed status of the action adapter.
    /// </summary>
    /// <remarks>
    /// Derived classes should extend status with implementation specific information.
    /// </remarks>
    public override string Status
    {
        get
        {
            const int MaxMeasurementsToShow = 10;

            StringBuilder status = new();
            DataSet dataSource = DataSource;

            status.AppendLine($"       Data source defined: {dataSource is not null}");

            if (dataSource is not null)
                status.AppendLine($"    Referenced data source: {dataSource.DataSetName}, {dataSource.Tables.Count:N0} tables");

            status.AppendLine($"    Initialization timeout: {(InitializationTimeout < 0 ? "Infinite" : $"{InitializationTimeout} milliseconds")}");
            status.AppendLine($"       Adapter initialized: {Initialized}");
            status.AppendLine($"         Operational state: {(Enabled ? "Running" : "Stopped")}");
            status.AppendLine($"         Connect on demand: {!AutoStart}");
            status.AppendLine($"  Respecting input demands: {RespectInputDemands}");
            status.AppendLine($" Respecting output demands: {RespectOutputDemands}");
            status.AppendLine($"    Processed measurements: {ProcessedMeasurements}");
            status.AppendLine($"    Total adapter run time: {RunTime.ToString(3)}");
            status.AppendLine($"       Temporal processing: {(SupportsTemporalProcessing ? "Supported" : "Unsupported")}");

            if (SupportsTemporalProcessing)
            {
                status.AppendLine($"     Start time constraint: {(StartTimeConstraint == DateTime.MinValue ? "Unspecified" : StartTimeConstraint.ToString("yyyy-MM-dd HH:mm:ss.fff"))}");
                status.AppendLine($"      Stop time constraint: {(StopTimeConstraint == DateTime.MaxValue ? "Unspecified" : StopTimeConstraint.ToString("yyyy-MM-dd HH:mm:ss.fff"))}");
                status.AppendLine($"       Processing interval: {(ProcessingInterval < 0 ? "Default" : ProcessingInterval == 0 ? "As fast as possible" : $"{ProcessingInterval} milliseconds")}");
            }

            status.AppendLine($"                Adapter ID: {ID}");

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

            if (OutputMeasurements is not null && OutputMeasurements.Length > OutputMeasurements.Count(m => m.Key == MeasurementKey.Undefined))
            {
                status.AppendLine($"       Output measurements: {OutputMeasurements.Length:N0} defined measurements");
                status.AppendLine();

                for (int i = 0; i < Math.Min(OutputMeasurements.Length, MaxMeasurementsToShow); i++)
                {
                    status.Append(OutputMeasurements[i].ToString().TruncateRight(40).PadLeft(40));
                    status.Append(" ");
                    status.AppendLine(OutputMeasurements[i].ID.ToString());
                }

                if (OutputMeasurements.Length > MaxMeasurementsToShow)
                    status.AppendLine("...".PadLeft(26));

                status.AppendLine();
            }

            if (InputMeasurementKeys is not null && InputMeasurementKeys.Length > InputMeasurementKeys.Count(k => k == MeasurementKey.Undefined))
            {
                status.AppendLine($"        Input measurements: {InputMeasurementKeys.Length:N0} defined measurements");
                status.AppendLine();

                for (int i = 0; i < Math.Min(InputMeasurementKeys.Length, MaxMeasurementsToShow); i++)
                    status.AppendLine(InputMeasurementKeys[i].ToString().TruncateRight(25).CenterText(50));

                if (InputMeasurementKeys.Length > MaxMeasurementsToShow)
                    status.AppendLine("...".CenterText(50));

                status.AppendLine();
            }

            if (RequestedInputMeasurementKeys is not null && RequestedInputMeasurementKeys.Length > 0)
            {
                status.AppendLine($"      Requested input keys: {RequestedInputMeasurementKeys.Length:N0} defined measurements");
                status.AppendLine();

                for (int i = 0; i < Math.Min(RequestedInputMeasurementKeys.Length, MaxMeasurementsToShow); i++)
                    status.AppendLine(RequestedInputMeasurementKeys[i].ToString().TruncateRight(25).CenterText(50));

                if (RequestedInputMeasurementKeys.Length > MaxMeasurementsToShow)
                    status.AppendLine("...".CenterText(50));

                status.AppendLine();
            }

            if (RequestedOutputMeasurementKeys is not null && RequestedOutputMeasurementKeys.Length > 0)
            {
                status.AppendLine($"     Requested output keys: {RequestedOutputMeasurementKeys.Length:N0} defined measurements");
                status.AppendLine();

                for (int i = 0; i < Math.Min(RequestedOutputMeasurementKeys.Length, MaxMeasurementsToShow); i++)
                    status.AppendLine(RequestedOutputMeasurementKeys[i].ToString().TruncateRight(25).CenterText(50));

                if (RequestedOutputMeasurementKeys.Length > MaxMeasurementsToShow)
                    status.AppendLine("...".CenterText(50));

                status.AppendLine();
            }

            status.AppendLine($" Minimum measurements used: {MinimumMeasurementsToUse:N0}");

            if (ShowConcentratorStatus)
                status.Append(base.Status);

            return status.ToString();
        }
    }

    #endregion

    #region [ Methods ]

    /// <summary>
    /// Releases the unmanaged resources used by the <see cref="ActionAdapterBase"/> object and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected override void Dispose(bool disposing)
    {
        if (IsDisposed)
            return;

        IsDisposed = true;          // Prevent duplicate dispose.
        base.Dispose(disposing);    // Call base class Dispose().
    }

    /// <summary>
    /// Initializes <see cref="ActionAdapterBase"/>.
    /// </summary>
    public virtual void Initialize()
    {
        Initialized = false;

        Dictionary<string, string> settings = Settings;
        const string ErrorMessage = "{0} is missing from Settings - Example: framesPerSecond=30; lagTime=3; leadTime=1";

        // Load required parameters
        if (!settings.TryGetValue(nameof(FramesPerSecond), out string? setting))
            throw new ArgumentException(string.Format(ErrorMessage, nameof(FramesPerSecond)));

        base.FramesPerSecond = int.Parse(setting);

        if (!settings.TryGetValue(nameof(LagTime), out setting))
            throw new ArgumentException(string.Format(ErrorMessage, nameof(LagTime)));

        base.LagTime = double.Parse(setting);

        if (!settings.TryGetValue(nameof(LeadTime), out setting))
            throw new ArgumentException(string.Format(ErrorMessage, nameof(LeadTime)));

        base.LeadTime = double.Parse(setting);

        // Load optional parameters
        if (settings.TryGetValue(nameof(UsePrecisionTimer), out setting))
            UsePrecisionTimer = setting.ParseBoolean();

        if (settings.TryGetValue(nameof(UseLocalClockAsRealTime), out setting))
            UseLocalClockAsRealTime = setting.ParseBoolean();

        if (settings.TryGetValue(nameof(IgnoreBadTimestamps), out setting))
            IgnoreBadTimestamps = setting.ParseBoolean();

        if (settings.TryGetValue(nameof(AllowSortsByArrival), out setting))
            AllowSortsByArrival = setting.ParseBoolean();

        InputMeasurementKeys = settings.TryGetValue(nameof(InputMeasurementKeys), out setting) ?
            AdapterBase.ParseInputMeasurementKeys(DataSource, true, setting) :
            [];

        if (settings.TryGetValue(nameof(OutputMeasurements), out setting))
            OutputMeasurements = AdapterBase.ParseOutputMeasurements(DataSource, true, setting);

        if (settings.TryGetValue(nameof(MinimumMeasurementsToUse), out setting))
            MinimumMeasurementsToUse = int.Parse(setting);

        if (settings.TryGetValue(nameof(TimeResolution), out setting))
            TimeResolution = long.Parse(setting);

        if (settings.TryGetValue(nameof(RoundToNearestTimestamp), out setting))
            RoundToNearestTimestamp = setting.ParseBoolean();

        if (settings.TryGetValue(nameof(AllowPreemptivePublishing), out setting))
            AllowPreemptivePublishing = setting.ParseBoolean();

        if (settings.TryGetValue(nameof(PerformTimestampReasonabilityCheck), out setting))
            PerformTimestampReasonabilityCheck = setting.ParseBoolean();

        if (settings.TryGetValue(nameof(ProcessByReceivedTimestamp), out setting))
            ProcessByReceivedTimestamp = setting.ParseBoolean();

        if (settings.TryGetValue(nameof(MaximumPublicationTimeout), out setting))
            MaximumPublicationTimeout = int.Parse(setting);

        if (settings.TryGetValue(nameof(DownsamplingMethod), out setting))
        {
            if (Enum.TryParse(setting, true, out DownsamplingMethod method))
            {
                DownsamplingMethod = method;
            }
            else
            {
                OnStatusMessage(MessageLevel.Info, $"No down-sampling method labeled \"{setting}\" exists, \"{nameof(DownsamplingMethod.LastReceived)}\" method was selected.", flags: MessageFlags.UsageIssue);
                DownsamplingMethod = DownsamplingMethod.LastReceived;
            }
        }

        if (settings.TryGetValue(nameof(InputSourceIDs), out setting))
            InputSourceIDs = setting.Split(',');

        if (settings.TryGetValue(nameof(OutputSourceIDs), out setting))
            OutputSourceIDs = setting.Split(',');

        if (settings.TryGetValue("connectOnDemand", out setting))
            AutoStart = !setting.ParseBoolean();
        else
            AutoStart = true;

        RespectInputDemands = settings.TryGetValue(nameof(RespectInputDemands), out setting) && setting.ParseBoolean();
        RespectOutputDemands = !settings.TryGetValue(nameof(RespectOutputDemands), out setting) || setting.ParseBoolean();

        bool startTimeDefined = settings.TryGetValue(nameof(StartTimeConstraint), out string startTime);
        bool stopTimeDefined = settings.TryGetValue(nameof(StopTimeConstraint), out string stopTime);

        if (startTimeDefined || stopTimeDefined)
        {
            settings.TryGetValue("timeConstraintParameters", out string? parameters);
            SetTemporalConstraint(startTime, stopTime, parameters);
        }

        if (settings.TryGetValue(nameof(ProcessingInterval), out setting) && !string.IsNullOrWhiteSpace(setting) && int.TryParse(setting, out int processingInterval))
            ProcessingInterval = processingInterval;
    }

    /// <summary>
    /// Gets a flag that indicates whether the object has been disposed.
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Starts the <see cref="ActionAdapterBase"/> or restarts it if it is already running.
    /// </summary>
    [AdapterCommand("Starts the action adapter or restarts it if it is already running.", ResourceAccessLevel.Admin, ResourceAccessLevel.Edit)]
    public override void Start()
    {
        // Make sure we are stopped (e.g., disconnected) before attempting to start (e.g., connect)
        if (Enabled)
            Stop();

        base.Start();
    }

    /// <summary>
    /// Stops the <see cref="ActionAdapterBase"/>.
    /// </summary>
    [AdapterCommand("Stops the action adapter.", ResourceAccessLevel.Admin, ResourceAccessLevel.Edit)]
    public override void Stop() =>
        base.Stop();

    /// <summary>
    /// Examines the concentrator frame queue state of the <see cref="ActionAdapterBase"/>.
    /// </summary>
    [AdapterCommand("Examines concentration frame queue state.", ResourceAccessLevel.Admin, ResourceAccessLevel.Edit, ResourceAccessLevel.View)]
    [Label("Examine Queue State")]
    public string ExamineQueueState()
    {
        string queueState = QueueState;
        OnStatusMessage(MessageLevel.Info, queueState);
        return queueState;
    }

    /// <summary>
    /// Resets the statistics of the <see cref="ActionAdapterBase"/>.
    /// </summary>
    [AdapterCommand("Resets the statistics of the action adapter.", ResourceAccessLevel.Admin, ResourceAccessLevel.Edit)]
    [Label("Reset Statistics")]
    public override void ResetStatistics()
    {
        base.ResetStatistics();

        if (Enabled)
            OnStatusMessage(MessageLevel.Info, "Action adapter concentration statistics have been reset.");
    }

    /// <summary>
    /// Manually sets the initialized state of the <see cref="ActionAdapterBase"/>.
    /// </summary>
    /// <param name="initialized">Desired initialized state.</param>
    [AdapterCommand("Manually sets the initialized state of the action adapter.", ResourceAccessLevel.Admin, ResourceAccessLevel.Edit)]
    [Label("Set Initialized State")]
    [Parameter(nameof(initialized), "Initialized", "Manually sets the initialized state of the action adapter.")]
    public virtual void SetInitializedState(bool initialized) =>
        Initialized = initialized;

    /// <summary>
    /// Gets a short one-line status of this <see cref="ActionAdapterBase"/>.
    /// </summary>
    /// <param name="maxLength">Maximum number of available characters for display.</param>
    /// <returns>A short one-line summary of the current status of this <see cref="ActionAdapterBase"/>.</returns>
    public virtual string GetShortStatus(int maxLength)
    {
        int inputCount = 0, outputCount = 0;

        if (InputMeasurementKeys is not null)
            inputCount = InputMeasurementKeys.Length;

        if (OutputMeasurements is not null)
            outputCount = OutputMeasurements.Length;

        return $"Total input measurements: {inputCount:N0}, total output measurements: {outputCount:N0}".PadLeft(maxLength);
    }

    /// <summary>
    /// Queues a collection of measurements for processing. Measurements are automatically filtered to the defined <see cref="IAdapter.InputMeasurementKeys"/>.
    /// </summary>
    /// <param name="measurements">Collection of measurements to queue for processing.</param>
    /// <remarks>
    /// Measurements are filtered against the defined <see cref="InputMeasurementKeys"/>.
    /// </remarks>
    public virtual void QueueMeasurementsForProcessing(IEnumerable<IMeasurement> measurements)
    {
        if (IsDisposed)
            return;

        SortMeasurements(measurements);
    }

    /// <summary>
    /// Defines a temporal processing constraint for the adapter.
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
    [AdapterCommand("Defines a temporal processing constraint for the adapter.", ResourceAccessLevel.Admin, ResourceAccessLevel.Edit, ResourceAccessLevel.View)]
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

        if (!this.TemporalConstraintIsDefined())
            return;

        // When processing historical data, timestamps should not be evaluated for reasonability
        PerformTimestampReasonabilityCheck = false;
        LeadTime = double.MaxValue;
    }

    /// <summary>
    /// Serves as a hash function for the current <see cref="ActionAdapterBase"/>.
    /// </summary>
    /// <returns>A hash code for the current <see cref="ActionAdapterBase"/>.</returns>
    // ReSharper disable once NonReadonlyMemberInGetHashCode
    public override int GetHashCode() => m_hashCode;

    /// <summary>
    /// Attempts to retrieve the minimum needed number of measurements from the frame (as specified by MinimumMeasurementsToUse)
    /// </summary>
    /// <param name="frame">Source frame for the measurements</param>
    /// <param name="measurements">Return array of measurements</param>
    /// <returns>True if minimum needed number of measurements were returned in measurement array</returns>
    /// <remarks>
    /// <para>
    /// Remember this function will *only* return the minimum needed number of measurements, no more.  If you want to use
    /// all available measurements in your adapter you should just use Frame.Measurements.Values directly.
    /// </para>
    /// <para>
    /// Note that the measurements array parameter will be created if the reference is null, otherwise if caller creates
    /// array it must be sized to MinimumMeasurementsToUse
    /// </para>
    /// </remarks>
    protected virtual bool TryGetMinimumNeededMeasurements(IFrame frame, out IMeasurement[] measurements)
    {
        int index = 0, minNeeded = MinimumMeasurementsToUse;
        IDictionary<MeasurementKey, IMeasurement> frameMeasurements = frame.Measurements;
        MeasurementKey[]? measurementKeys = InputMeasurementKeys;
        measurements = new IMeasurement[minNeeded];

        if (measurementKeys is null)
        {
            // No input measurements are defined, just get first set of measurements in this frame
            foreach (IMeasurement measurement in frameMeasurements.Values)
            {
                measurements[index++] = measurement;

                if (index == minNeeded)
                    break;
            }
        }
        else
        {
            // Loop through all input measurements to see if they exist in this frame
            foreach (MeasurementKey key in measurementKeys)
            {
                if (!frameMeasurements.TryGetValue(key, out IMeasurement? measurement))
                    continue;

                measurements[index++] = measurement;

                if (index == minNeeded)
                    break;
            }
        }

        return index == minNeeded;
    }

    /// <summary>
    /// Raises the <see cref="NewMeasurements"/> event.
    /// </summary>
    protected virtual void OnNewMeasurements(ICollection<IMeasurement> measurements)
    {
        NewMeasurements?.SafeInvoke(this, new EventArgs<ICollection<IMeasurement>>(measurements));
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

    // We cache hash code during construction or after element value change to speed usage
    private void GenHashCode() =>
        m_hashCode = (Name + ID).GetHashCode();

    #endregion
}
