//******************************************************************************************************
//  EventDetails.cs - Gbtc
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
//  02/18/2025 - C. Lackner
//       Generated original version of source code.
//
//******************************************************************************************************

using System;
using Gemstone.Data.Model;

namespace Gemstone.Timeseries.Model;

/// <summary>
/// Represents the details for an event.
/// </summary>
public class EventDetails
{
    /// <summary>
    /// Gets or sets the ID, used as the primary key, for the event details.
    /// </summary>
    [PrimaryKey(true)]
    public int ID { get; set; }

    /// <summary>
    /// Gets or sets Guid-based event ID.
    /// </summary>
    public Guid EventGuid { get; set; }

    /// <summary>
    /// Gets or sets the start time of the event.
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// Gets or sets the end time of the event.
    /// </summary>
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// Gets or sets the SignalID of the measurement associated with the event.
    /// </summary>
    public Guid MeasurementID { get; set; }

    /// <summary>
    /// Gets or sets the description details of the event.
    /// </summary>
    public string Details { get; set; } = "";

    /// <summary>
    /// Gets or sets the event type.
    /// </summary>
    public string Type { get; set; } = "";
}
