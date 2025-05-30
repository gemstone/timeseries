﻿//******************************************************************************************************
//  ActiveMeasurementsTableLookup.cs - Gbtc
//
//  Copyright © 2016, Grid Protection Alliance.  All Rights Reserved.
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
//  10/31/2016 - Steven E. Chisholm
//       Generated original version of source code.
//  11/16/2023 - Lillian Gensolin
//       Converted code to .NET core.
//
//******************************************************************************************************

using System;
using System.Collections.Generic;
using System.Data;
using Gemstone.Data.DataExtensions;

namespace Gemstone.Timeseries.Data;

/// <summary>
/// Represents a table lookup for active measurements.
/// </summary>
public class ActiveMeasurementsTableLookup
{
    private readonly Dictionary<uint, List<DataRow>> m_lookupByDeviceID;
    private readonly Dictionary<string, List<DataRow>> m_lookupByDeviceNameNoStats;
    private readonly List<DataRow> m_emptySet;

    internal ActiveMeasurementsTableLookup(DataSet dataSet)
    {
        m_emptySet = [];
        m_lookupByDeviceID = new Dictionary<uint, List<DataRow>>();
        m_lookupByDeviceNameNoStats = new Dictionary<string, List<DataRow>>(StringComparer.CurrentCultureIgnoreCase);

        if (!dataSet.Tables.Contains("ActiveMeasurements"))
            return;

        DataTable table = dataSet.Tables["ActiveMeasurements"]!;

        foreach (DataRow row in table.Rows)
        {
            uint? id = row.ConvertNullableField<uint>("DeviceID");

            List<DataRow>? rowList;

            if (id.HasValue)
            {
                if (!m_lookupByDeviceID.TryGetValue(id.Value, out rowList))
                {
                    rowList = [];
                    m_lookupByDeviceID[id.Value] = rowList;
                }

                rowList.Add(row);
            }

            if (row.ConvertField("SignalType", string.Empty).Equals("STAT", StringComparison.CurrentCultureIgnoreCase))
                continue;

            string? device = row.ConvertField<string?>("Device");

            if (device is null)
                continue;

            if (!m_lookupByDeviceNameNoStats.TryGetValue(device, out rowList))
            {
                rowList = [];
                m_lookupByDeviceNameNoStats[device] = rowList;
            }

            rowList.Add(row);
        }
    }

    /// <summary>
    /// Gets all of the rows with the provided deviceID. 
    /// Returns an empty set if the deviceID could not be found.
    /// </summary>
    /// <param name="deviceId">the deviceID to lookup.</param>
    /// <returns>
    /// Returns an empty set if the deviceID could not be found.
    /// </returns>
    public IEnumerable<DataRow> LookupByDeviceID(uint deviceId) =>
        m_lookupByDeviceID.TryGetValue(deviceId, out List<DataRow>? rows) ? rows : m_emptySet;

    /// <summary>
    /// Gets all of the rows with the provided device name. This will exclude
    /// all ActiveMeasurements that are classified as SignalType='STAT'. This is because 
    /// 'STAT's are not associated with a device in the database.
    /// Returns an empty set if the deviceID could not be found.
    /// </summary>
    /// <param name="deviceName">the device to lookup.</param>
    /// <returns>
    /// Returns an empty set if the device could not be found.
    /// </returns>
    public IEnumerable<DataRow> LookupByDeviceNameNoStat(string deviceName) =>
        m_lookupByDeviceNameNoStats.TryGetValue(deviceName, out List<DataRow>? rows) ? rows : m_emptySet;
}
