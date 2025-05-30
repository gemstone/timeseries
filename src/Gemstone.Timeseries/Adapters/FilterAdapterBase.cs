﻿//******************************************************************************************************
//  FilterAdapterBase.cs - Gbtc
//
//  Copyright © 2017, Grid Protection Alliance.  All Rights Reserved.
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
//  11/10/2017 - Stephen C. Wills
//       Generated original version of source code.
//  11/09/2023 - Lillian Gensolin
//       Converted code to .NET core.
//
//******************************************************************************************************


using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Gemstone.StringExtensions;

namespace Gemstone.Timeseries.Adapters;

/// <summary>
/// Base class for the <see cref="IFilterAdapter"/> interface.
/// </summary>
public abstract class FilterAdapterBase : AdapterBase, IFilterAdapter
{
    #region [ Members ]

    // Fields
    private HashSet<MeasurementKey> m_inputMeasurementKeys;

    #endregion

    #region [ Constructors ]

    /// <summary>
    /// Creates a new instance of the <see cref="FilterAdapterBase"/> class.
    /// </summary>
    protected FilterAdapterBase() =>
        m_inputMeasurementKeys = [];

    #endregion

    #region [ Properties ]

    /// <summary>
    /// Gets or sets primary keys of input measurements the <see cref="AdapterBase"/> expects, if any.
    /// </summary>
    /// <remarks>
    /// If your adapter needs to receive all measurements, you must explicitly set InputMeasurementKeys to null.
    /// </remarks>
    [ConnectionStringParameter]
    [DefaultValue(null)]
    [Description("Defines primary keys of input measurements the adapter expects; can be one of a filter expression, measurement key, point tag or Guid.")]
    public override MeasurementKey[] InputMeasurementKeys
    {
        get => base.InputMeasurementKeys;
        set
        {
            base.InputMeasurementKeys = value;

            if (!m_inputMeasurementKeys.SetEquals(value))
                m_inputMeasurementKeys = [..value];
        }
    }

    /// <summary>
    /// Gets or sets output measurements that the <see cref="AdapterBase"/> will produce, if any.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override IMeasurement[] OutputMeasurements
    {
        get => base.OutputMeasurements;
        set => base.OutputMeasurements = value;
    }

    /// <summary>
    /// Gets or sets the values that determines the order in which filter adapters are executed.
    /// </summary>
    public virtual int ExecutionOrder { get; set; }

    #endregion

    #region [ Methods ]

    /// <summary>
    /// Handler for new measurements that have not yet been routed.
    /// </summary>
    /// <param name="measurements">Measurements that have not yet been routed.</param>
    public virtual void HandleNewMeasurements(ICollection<IMeasurement> measurements)
    {
        ProcessMeasurements(measurements.Where(measurement => IsInputMeasurement(measurement.Key)));
        IncrementProcessedMeasurements(measurements.Count);
    }

    /// <summary>
    /// Determines if the given measurement key represents
    /// a signal that is bound for this filter adapter.
    /// </summary>
    /// <param name="key">The key that identifies the signal.</param>
    /// <returns>True if measurements for the given signal are bound for this filter adapter.</returns>
    public virtual bool IsInputMeasurement(MeasurementKey key)
    {
        HashSet<MeasurementKey> inputMeasurementKeys = m_inputMeasurementKeys;
        return inputMeasurementKeys.Contains(key);
    }

    /// <summary>
    /// Gets a short one-line status of this <see cref="AdapterBase"/>.
    /// </summary>
    /// <param name="maxLength">Maximum number of available characters for display.</param>
    /// <returns>A short one-line summary of the current status of this <see cref="AdapterBase"/>.</returns>
    public override string GetShortStatus(int maxLength) =>
        $"{ProcessedMeasurements} measurements processed so far...".CenterText(maxLength);

    /// <summary>
    /// Processes the new measurements before they have been routed to other adapters.
    /// </summary>
    /// <param name="measurements">The new measurements that have not yet been routed.</param>
    protected abstract void ProcessMeasurements(IEnumerable<IMeasurement> measurements);

    #endregion
}
