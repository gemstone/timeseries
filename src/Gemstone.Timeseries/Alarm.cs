//******************************************************************************************************
//  Alarm.cs - Gbtc
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
//  01/31/2012 - Stephen C. Wills
//       Generated original version of source code.
//  02/10/2012 - Stephen C. Wills
//       Moved to TimeseriesFramework project.
//  12/20/2012 - Starlynn Danyelle Gilliam
//       Modified Header.
//
//******************************************************************************************************

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Gemstone.Collections.CollectionExtensions;
using Gemstone.Data.Model;

// ReSharper disable CompareOfFloatsByEqualityOperator

namespace Gemstone.Timeseries;

#region [ Enumerations ]

/// <summary>
/// Represents the two states that an alarm can be in: raised or cleared.
/// </summary>
public enum AlarmState
{
    /// <summary>
    /// Indicates that an alarm is cleared.
    /// </summary>
    Cleared = 0,

    /// <summary>
    /// Indicates that an alarm has been raised.
    /// </summary>
    Raised = 1
}

/// <summary>
/// Represents the severity of alarms.
/// </summary>
public enum AlarmSeverity
{
    /// <summary>
    /// Indicates that an alarm is of no importance.
    /// </summary>
    [Description("NONE")]
    None = 0,

    /// <summary>
    /// Indicates that an alarm is informative, but not dangerous.
    /// </summary>
    [Description("INFO")]
    Information = 50,

    /// <summary>
    /// Indicates that an alarm is not very important.
    /// </summary>
    [Description("LOW")]
    Low = 150,

    /// <summary>
    /// Indicates that an alarm is somewhat important.
    /// </summary>
    [Description("MEDLOW")]
    MediumLow = 300,

    /// <summary>
    /// Indicates that an alarm is moderately importance.
    /// </summary>
    [Description("MED")]
    Medium = 500,

    /// <summary>
    /// Indicates that an alarm is important.
    /// </summary>
    [Description("MEDHIGH")]
    MediumHigh = 700,

    /// <summary>
    /// Indicates that an alarm is very important.
    /// </summary>
    [Description("HIGH")]
    High = 850,

    /// <summary>
    /// Indicates an alarm for a value that is unreasonable.
    /// </summary>
    [Description("RANGE")]
    Unreasonable = 900,

    /// <summary>
    /// Indicates than an alarm signifies a dangerous situation.
    /// </summary>
    [Description("CRITICAL")]
    Critical = 950,

    /// <summary>
    /// Indicates an alarm for a value that is latched, i.e., flat-lined.
    /// </summary>
    [Description("FLATLINE")]
    Latched = 980,

    /// <summary>
    /// Indicates that an alarm reports bad data.
    /// </summary>
    [Description("ERROR")]
    Error = 1000
}

/// <summary>
/// Represents the operation to be performed
/// when testing values from an incoming signal.
/// </summary>
public enum AlarmOperation
{
    /// <summary>
    /// Internal range test
    /// </summary>
    [Description("Equal to")]
    Equal = 1,

    /// <summary>
    /// External range test
    /// </summary>
    [Description("Not equal to")]
    NotEqual = 2,

    /// <summary>
    /// Upper bound
    /// </summary>
    [Description("Greater than or equal to")]
    GreaterOrEqual = 11,

    /// <summary>
    /// Lower bound
    /// </summary>
    [Description("Less than or equal to")]
    LessOrEqual = 21,

    /// <summary>
    /// Upper limit
    /// </summary>
    [Description("Greater than")]
    GreaterThan = 12,

    /// <summary>
    /// Lower limit
    /// </summary>
    [Description("Less than")]
    LessThan = 22,

    /// <summary>
    /// Latched value
    /// </summary>
    [Description("Latched")]
    Flatline = 3
}

/// <summary>
/// Represents the operation to be performed
/// when testing values from an incoming signal.
/// </summary>
public enum AlarmCombination
{
    /// <summary>
    /// Require ALL singnals to meet Alarm Criteria
    /// </summary>
    [Description("AND")]
    AND = 1,
    /// <summary>
    /// Require ANY signal to meet Alarm Criteria
    /// </summary>
    [Description("OR")]
    OR = 2
}
#endregion

/// <summary>
/// Represents an alarm that tests the values of
/// an incoming signal to determine the state of alarm.
/// </summary>
[Serializable]
public class Alarm : ICloneable
{
    #region [ Members ]

    // Fields
    private int m_id;
    private string? m_tagName;
    private Guid m_signalID;

    private string m_associatedMeasurements;

    private string? m_description;
    private AlarmSeverity m_severity;
    private AlarmOperation m_operation;
    private AlarmCombination m_combination;
    private double? m_setPoint;
    private double? m_tolerance;
    private double? m_delay;
    private double? m_hysteresis;
    private double m_timeout;

    private AlarmState m_state;

    [NonSerialized]
    private Ticks m_timeRaised;

    [NonSerialized]
    private Ticks m_lastNegative;

    [NonSerialized]
    private Dictionary<Guid,double> m_lastValue;

    [NonSerialized]
    private Dictionary<Guid,Ticks> m_lastChanged;

    [NonSerialized]
    private IFrame? m_cause;

    [NonSerialized]
    private Func<IFrame, bool>? m_raiseTest;

    [NonSerialized]
    private Func<IFrame, bool>? m_clearTest;

    [NonSerialized]
    private Func<IFrame, Func<IMeasurement, bool>, bool>? m_combineCleared;

    [NonSerialized]
    private Func<IFrame, Func<IMeasurement, bool>, bool>? m_combineRaised;

    #endregion

    #region [ Constructors ]

    /// <summary>
    /// Creates a new instance of the <see cref="Alarm"/> class.
    /// </summary>
    public Alarm()
        : this(AlarmOperation.Equal)
    {
    }

    /// <summary>
    /// Creates a new instance of the <see cref="Alarm"/> class.
    /// </summary>
    /// <param name="operation">The operation to be performed when testing values from the incoming signal.</param>
    public Alarm(AlarmOperation operation) 
    {
        Operation = operation;
        Combination = AlarmCombination.AND;
        m_lastChanged = new Dictionary<Guid, Ticks>();
        m_lastValue = new Dictionary<Guid, double>();
    }

    #endregion

    #region [ Properties ]

    /// <summary>
    /// Gets or sets the identification number of the alarm.
    /// </summary>
    [PrimaryKey(true)]
    public int ID
    {
        get => m_id;
        set => m_id = value;
    }

    /// <summary>
    /// Gets or sets the Timeout in seconds at which the alarm stops if no data comes in.
    /// </summary>
    public double Timeout
    {
        get => m_timeout;
        set => m_timeout = value;
    }

    /// <summary>
    /// Gets or sets the tag name of the alarm.
    /// </summary>
    public string? TagName
    {
        get => m_tagName;
        set => m_tagName = value;
    }

    /// <summary>
    /// Gets or sets the identification number of the
    /// the measurements generated for alarm events.
    /// </summary>
    public Guid SignalID
    {
        get => m_signalID;
        set => m_signalID = value;
    }

    /// <summary>
    /// Gets or sets the identification
    /// the measurements used to trigger the Alarm.
    /// </summary>
    public string InputMeasurementKeys
    {
        get => m_associatedMeasurements;
        set => m_associatedMeasurements = value;
    }

    /// <summary>
    /// Gets or sets the description of the alarm.
    /// </summary>
    public string? Description
    {
        get => m_description;
        set => m_description = value;
    }

    /// <summary>
    /// Gets or sets the operation to be performed
    /// when testing values from the incoming signal.
    /// </summary>
    public AlarmOperation Operation
    {
        get => m_operation;
        set
        {
            m_operation = value;
            m_raiseTest = GetRaiseTest();
            m_clearTest = GetClearTest();
        }
    }

    /// <summary>
    /// Gets or sets the severity of the alarm.
    /// </summary>
    public AlarmSeverity Severity
    {
        get => m_severity;
        set => m_severity = value;
    }

    /// <summary>
    /// Gets or sets the combination logic of the alarm.
    /// </summary>
    public AlarmCombination Combination
    {
        get => m_combination;
        set
        {
            m_combination = value;
            m_combineCleared = GetClearCombination();
            m_combineRaised = GetRaisedCombination();
        }
    }

    /// <summary>
    /// Gets or sets the value to be compared against
    /// the signal to determine whether to raise the
    /// alarm. This value is irrelevant for the
    /// <see cref="AlarmOperation.Flatline"/> operation.
    /// </summary>
    public double? SetPoint
    {
        get => m_setPoint;
        set => m_setPoint = value;
    }

    /// <summary>
    /// Gets or sets a tolerance window around the
    /// <see cref="SetPoint"/> to use when comparing
    /// against the value of the signal. This value
    /// is only relevant for the <see cref="AlarmOperation.Equal"/>
    /// and <see cref="AlarmOperation.NotEqual"/> operations.
    /// </summary>
    /// <remarks>
    /// <para>The equal and not equal operations are actually
    /// internal and external range tests based on the setpoint
    /// and the tolerance. The two tests are performed as follows.</para>
    /// 
    /// <list type="bullet">
    /// <item>Equal: <c>(value &gt;= SetPoint - Tolerance) &amp;&amp; (value &lt;= SetPoint + Tolerance)</c></item>
    /// <item>Not equal: <c>(value &lt; SetPoint - Tolerance) || (value &gt; SetPoint + Tolerance)</c></item>
    /// </list>
    /// </remarks>
    public double? Tolerance
    {
        get => m_tolerance;
        set => m_tolerance = value;
    }

    /// <summary>
    /// Gets or sets the amount of time that the
    /// signal must be exhibiting alarming behavior
    /// before the alarm is raised.
    /// </summary>
    public double? Delay
    {
        get => m_delay;
        set => m_delay = value;
    }

    /// <summary>
    /// Gets or sets the hysteresis used when clearing
    /// alarms. This value is only relevant in greater
    /// than (or equal) and less than (or equal) operations.
    /// </summary>
    /// <remarks>
    /// <para>The hysteresis is an offset that provides padding between
    /// the point at which the alarm is raised and the point at
    /// which the alarm is cleared. For example, in the case of the
    /// <see cref="AlarmOperation.GreaterOrEqual"/> operation:</para>
    /// 
    /// <list type="bullet">
    /// <item>Raised: <c>value &gt;= SetPoint</c></item>
    /// <item>Cleared: <c>value &lt; SetPoint - Hysteresis</c></item>
    /// </list>
    /// 
    /// <para>The direction of the offset depends on whether the
    /// operation is greater than (or equal) or less than (or equal).
    /// The hysteresis must be greater than zero.</para>
    /// </remarks>
    public double? Hysteresis
    {
        get => m_hysteresis;
        set => m_hysteresis = value;
    }

    /// <summary>
    /// Gets or sets the current state of the alarm (raised or cleared).
    /// </summary>
    [NonRecordField, NonSerialized]
    public AlarmState State
    {
        get => m_state;
        set => m_state = value;
    }

    /// <summary>
    /// Gets or sets the timestamp of the most recent
    /// measurement that caused the alarm to be raised.
    /// </summary>
    [NonRecordField, NonSerialized]
    public Ticks TimeRaised
    {
        get => m_timeRaised;
        set => m_timeRaised = value;
    }

    /// <summary>
    /// Gets the most recent frame
    /// that caused the alarm to be raised.
    /// </summary>
    [NonRecordField, NonSerialized]
    public IFrame? Cause
    {
        get => m_cause;
        private set
        {
            m_cause = value;

            if (m_cause is not null)
                m_timeRaised = m_cause.Timestamp;
        }
    }

    #endregion

    #region [ Methods ]

    /// <summary>
    /// Tests the value of the given frame to determine
    /// whether the alarm should be raised or cleared.
    /// </summary>
    /// <param name="frame">The frame whose value is to be tested.</param>
    /// <returns>true if the alarm's state changed; false otherwise</returns>
    public bool Test(IFrame frame)
    {
        switch (m_state)
        {
            case AlarmState.Raised when m_clearTest?.Invoke(frame) ?? false:
                m_state = AlarmState.Cleared;
                return true;
            case AlarmState.Cleared when m_raiseTest?.Invoke(frame) ?? false:
                m_state = AlarmState.Raised;
                Cause = frame;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Creates a new alarm that is a copy of the current instance.
    /// </summary>
    /// <returns>
    /// A new alarm that is a copy of this instance.
    /// </returns>
    /// <filterpriority>2</filterpriority>
    public Alarm Clone() => 
        (Alarm)MemberwiseClone();

    /// <summary>
    /// Creates a new object that is a copy of the current instance.
    /// </summary>
    /// <returns>
    /// A new object that is a copy of this instance.
    /// </returns>
    /// <filterpriority>2</filterpriority>
    object ICloneable.Clone() => 
        MemberwiseClone();
        
    // Returns the function used to determine when the alarm is raised.
    private Func<IFrame, bool> GetRaiseTest() =>
        m_operation switch
        {
            AlarmOperation.Equal => RaiseIfEqual,
            AlarmOperation.NotEqual => RaiseIfNotEqual,
            AlarmOperation.GreaterOrEqual => RaiseIfGreaterOrEqual,
            AlarmOperation.LessOrEqual => RaiseIfLessOrEqual,
            AlarmOperation.GreaterThan => RaiseIfGreaterThan,
            AlarmOperation.LessThan => RaiseIfLessThan,
            AlarmOperation.Flatline => RaiseIfFlatline,
            _ => throw new ArgumentOutOfRangeException()
        };

    // Returns the function used to determine when the alarm is cleared.
    private Func<IFrame, bool> GetClearTest() =>
        m_operation switch
        {
            AlarmOperation.Equal => ClearIfNotEqual,
            AlarmOperation.NotEqual => ClearIfNotNotEqual,
            AlarmOperation.GreaterOrEqual => ClearIfNotGreaterOrEqual,
            AlarmOperation.LessOrEqual => ClearIfNotLessOrEqual,
            AlarmOperation.GreaterThan => ClearIfNotGreaterThan,
            AlarmOperation.LessThan => ClearIfNotLessThan,
            AlarmOperation.Flatline => ClearIfNotFlatline,
            _ => throw new ArgumentOutOfRangeException()
        };

    // Returns the function used to combine measurements when clearing.
    // Not that this is different than for raising. e.g. If AND is used ANY clearing would result in a CLEAR.
    private Func<IFrame, Func<IMeasurement, bool>, bool> GetClearCombination() =>
        m_combination switch
        {
            AlarmCombination.AND => (IFrame frame, Func<IMeasurement, bool> func) => frame.Measurements.Any((kvp) => func.Invoke(kvp.Value)),
            AlarmCombination.OR => (IFrame frame, Func<IMeasurement, bool> func) => frame.Measurements.All((kvp) => func.Invoke(kvp.Value)),
            _ => throw new ArgumentOutOfRangeException()
        };

    // Returns the function used to combine measurements.
    private Func<IFrame, Func<IMeasurement, bool>, bool> GetRaisedCombination() =>
        m_combination switch
        {
            AlarmCombination.AND => (IFrame frame, Func<IMeasurement, bool> func) => frame.Measurements.All((kvp) => func.Invoke(kvp.Value)),
            AlarmCombination.OR => (IFrame frame, Func<IMeasurement, bool> func) => frame.Measurements.Any((kvp) => func.Invoke(kvp.Value)),
            _ => throw new ArgumentOutOfRangeException()
        };
    // Indicates whether the given measurement is
    // equal to the set point within the tolerance.
    private bool RaiseIfEqual(IFrame frame)
    {
        bool isEqual = m_combineRaised.Invoke(frame,
            (measurement) => measurement.Value <= m_setPoint + m_tolerance &&
                       measurement.Value >= m_setPoint - m_tolerance);

        return CheckDelay(frame, isEqual);
    }

    // Indicates whether the given measurement is outside
    // the range defined by the set point and tolerance.
    private bool RaiseIfNotEqual(IFrame frame)
    {
        bool isNotEqual = m_combineRaised.Invoke(frame,
            (measurement) => measurement.Value < m_setPoint - m_tolerance ||
                          measurement.Value > m_setPoint + m_tolerance);

        return CheckDelay(frame, isNotEqual);
    }

    // Indicates whether the given measurement
    // is greater than or equal to the set point.
    private bool RaiseIfGreaterOrEqual(IFrame frame) => 
        CheckDelay(frame, m_combineRaised.Invoke(frame, (measurement) => measurement.Value >= m_setPoint));

    // Indicates whether the given measurement
    // is less than or equal to the set point.
    private bool RaiseIfLessOrEqual(IFrame frame) => 
        CheckDelay(frame, m_combineRaised.Invoke(frame, (measurement) =>  measurement.Value <= m_setPoint));

    // Indicates whether the given measurement
    // is greater than the set point.
    private bool RaiseIfGreaterThan(IFrame frame) => 
        CheckDelay(frame, m_combineRaised.Invoke(frame, (measurement) => measurement.Value > m_setPoint));

    // Indicates whether the given measurement
    // is less than the set point.
    private bool RaiseIfLessThan(IFrame frame) => 
        CheckDelay(frame, m_combineRaised.Invoke(frame, (measurement) => measurement.Value < m_setPoint));

    // Indicates whether the given measurement has maintained the same
    // value for at least a number of seconds defined by the delay.
    private bool RaiseIfFlatline(IFrame frame)
    {
        foreach (IMeasurement measurement in frame.Measurements.Values)
        {
            double lastValue;

            if (!m_lastValue.TryGetValue(measurement.ID, out lastValue))
                lastValue = default(double);

            if (measurement.Value != lastValue)
            {
                m_lastChanged.AddOrUpdate(measurement.ID, measurement.Timestamp);
                m_lastValue.AddOrUpdate(measurement.ID, measurement.AdjustedValue);
            }

        }

        return m_combineRaised.Invoke(frame, (measurement) =>
        {
           
            Gemstone.Ticks lastChange;

            //Avoid Alarm going off on first comparison
            // This should never happen anyway
            if (!m_lastChanged.TryGetValue(measurement.ID, out lastChange))
                lastChange = measurement.Timestamp;

            long dist = Ticks.FromSeconds(Delay.GetValueOrDefault());
            long diff = measurement.Timestamp - lastChange;

            return diff >= dist;
        });
    }

    // Indicates whether the given measurement is not
    // equal to the set point within the tolerance.
    private bool ClearIfNotEqual(IFrame frame) => m_combineCleared.Invoke(frame, (measurement) =>
        measurement.Value < m_setPoint - m_tolerance ||
        measurement.Value > m_setPoint + m_tolerance);

    // Indicates whether the given measurement is not outside
    // the range defined by the set point and tolerance.
    private bool ClearIfNotNotEqual(IFrame frame) => m_combineCleared.Invoke(frame, (measurement) =>
        measurement.Value <= m_setPoint + m_tolerance &&
        measurement.Value >= m_setPoint - m_tolerance);

    // Indicates whether the given measurement is not greater
    // than or equal to the set point, offset by the hysteresis.
    private bool ClearIfNotGreaterOrEqual(IFrame frame) => m_combineCleared.Invoke(frame, (measurement) =>
        measurement.Value < m_setPoint - m_hysteresis);

    // Indicates whether the given measurement is not less
    // than or equal to the set point, offset by the hysteresis.
    private bool ClearIfNotLessOrEqual(IFrame frame) => m_combineCleared.Invoke(frame, (measurement) =>
        measurement.Value > m_setPoint + m_hysteresis);

    // Indicates whether the given measurement is not greater
    // than the set point, offset by the hysteresis.
    private bool ClearIfNotGreaterThan(IFrame frame) => m_combineCleared.Invoke(frame, (measurement) =>
        measurement.Value <= m_setPoint - m_hysteresis);

    // Indicates whether the given measurement is not less
    // than the set point, offset by the hysteresis.
    private bool ClearIfNotLessThan(IFrame frame) => m_combineCleared.Invoke(frame, (measurement) =>
        measurement.Value >= m_setPoint + m_hysteresis);

    // Indicates whether the given measurement's value has changed.
    private bool ClearIfNotFlatline(IFrame frame)
    {
        foreach (IMeasurement measurement in frame.Measurements.Values)
        {
            double lastValue;

            if (!m_lastValue.TryGetValue(measurement.ID, out lastValue))
                lastValue = measurement.AdjustedValue;

            if (measurement.Value != lastValue)
            {
                m_lastChanged.AddOrUpdate(measurement.ID, measurement.Timestamp);
                m_lastValue.AddOrUpdate(measurement.ID, measurement.AdjustedValue);
            }
        }
        return m_combineCleared(frame, (measurement) =>
        {
            Gemstone.Ticks lastChange;

            if (!m_lastChanged.TryGetValue(measurement.ID, out lastChange))
                lastChange = measurement.Timestamp;

            return lastChange != measurement.Timestamp;
        });
    }

    // Keeps track of the signal's timestamps to determine whether a given
    // measurement is eligible to raise the alarm based on the delay.
    private bool CheckDelay(IFrame frame, bool raiseCondition)
    {
        if (raiseCondition)
        {
            // Get the amount of time since the last
            // time the signal failed the raise test
            Ticks dist = frame.Timestamp - m_lastNegative;

            // If the amount of time is larger than
            // the delay threshold, raise the alarm
            if (dist >= Ticks.FromSeconds(m_delay.GetValueOrDefault()))
                return true;
        }
        else
        {
            // Keep track of the last time
            // the signal failed the raise test
            m_lastNegative = frame.Timestamp;
        }

        return false;
    }

    #endregion
}
