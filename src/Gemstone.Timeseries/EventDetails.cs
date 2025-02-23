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

namespace Gemstone.Timeseries;

/// <summary>
/// Represents the details for an event.
/// </summary>
public class EventDetails
{
    /* Create Table EventID 
      (
	[ID] [int] IDENTITY(1, 1) NOT NULL ,
	[EventGuid] [uniqueidentifier] NOT NULL,
	[StartTime] [datetime] NOT NULL,
	[EndTime] [datetime] NULL,
	[MeasurementID] [uniqueidentifier] NULL,
	[Details] [varchar](max) NOT NULL,
	[TYPE] [varchar](50) NULL
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
        );
    */

    /// <summary>
    /// Gets or stes the Primary Key used in the Database
    /// </summary>
    [PrimaryKey(true)]
    public int ID { get; set; }

    /// <summary>
    /// Gets or sets Guid-based event ID.
    /// </summary>
    public Guid EventGuid { get; set; }
    public DateTime StartTime { get; set; }

    public DateTime? EndTime { get; set; }

    public Guid MeasurementID { get; set; }

    public string Details { get; set; }

    public string Type { get; set; }

}
