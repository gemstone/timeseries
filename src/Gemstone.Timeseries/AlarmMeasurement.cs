//******************************************************************************************************
//  AlarmMeasurement.cs - Gbtc
//
//  Copyright © 2025, Grid Protection Alliance.  All Rights Reserved.
//
//  Licensed to the Grid Protection Alliance (GPA) under one or more contributor license agreements. See
//  the NOTICE file distributed with this work for additional information regarding copyright ownership.
//  The GPA licenses this file to you under the MIT License (MIT), the "License"; you may not use this
//  file except in compliance with the License. You may obtain a copy of the License at:
//
//      http://opensource.org/licenses/MIT
//
//  Unless agreed to in writing, the subject software distributed under the License is distributed on an
//  "AS-IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. Refer to the
//  License for the specific language governing permissions and limitations.
//
//  Code Modification History:
//  ----------------------------------------------------------------------------------------------------
//  02/04/2025 - J. Ritchie Carroll
//       Generated original version of source code.
//
//******************************************************************************************************

using System;

namespace Gemstone.Timeseries;

/// <summary>
/// Represents an alarm measurement.
/// </summary>
public class AlarmMeasurement : Measurement
{
    private Ticks? m_alarmTimestamp;

    /// <summary>
    /// Gets or sets Guid-based alarm ID.
    /// </summary>
    public Guid AlarmID { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the day-based shard that holds this alarm's event details.
    /// </summary>
    /// <remarks>
    /// This value is derived from the day the alarm was raised and is carried on both the raise and clear
    /// measurements so that the event details for a cleared alarm resolve to the shard for the raise day. A value
    /// of zero indicates that no event details are associated with the alarm.
    /// </remarks>
    public ushort ShardID { get; set; }

    /// <summary>
    /// Gets or sets alarm timestamp.
    /// </summary>
    public Ticks AlarmTimestamp
    {
        get => m_alarmTimestamp ?? Timestamp; 
        set => m_alarmTimestamp = value;
    }
}
