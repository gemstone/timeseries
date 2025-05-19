//******************************************************************************************************
//  IMeasurementExtensions.cs - Gbtc
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
//  09/02/2010 - J. Ritchie Carroll
//       Generated original version of source code.
//
//******************************************************************************************************

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Gemstone.Collections.CollectionExtensions;
using Gemstone.Configuration;
using Gemstone.Data;
using Gemstone.Diagnostics;
using Gemstone.Numeric.EE;

namespace Gemstone.Timeseries;

/// <summary>
/// Defines static extension functions for <see cref="IMeasurement"/> implementations.
/// </summary>
/// <remarks>
/// These helper functions map to the previously defined corresponding properties to help with the transition of <see cref="MeasurementStateFlags"/>.
/// </remarks>
public static class IMeasurementExtensions
{
    private static readonly ConcurrentDictionary<Guid, int> s_signalTypeIDs = new();

    /// <summary>
    /// Returns <c>true</c> if <see cref="MeasurementStateFlags.BadData"/> is not set.
    /// </summary>
    /// <param name="measurement"><see cref="IMeasurement"/> instance to test.</param>
    /// <returns><c>true</c> if <see cref="MeasurementStateFlags.BadData"/> is not set.</returns>
    public static bool ValueQualityIsGood(this IMeasurement measurement)
    {
        return (measurement.StateFlags & MeasurementStateFlags.BadData) == 0;
    }

    /// <summary>
    /// Returns <c>true</c> if <see cref="MeasurementStateFlags.BadTime"/> is not set.
    /// </summary>
    /// <param name="measurement"><see cref="IMeasurement"/> instance to test.</param>
    /// <returns><c>true</c> if <see cref="MeasurementStateFlags.BadTime"/> is not set.</returns>
    public static bool TimestampQualityIsGood(this IMeasurement measurement)
    {
        return (measurement.StateFlags & MeasurementStateFlags.BadTime) == 0;
    }

    /// <summary>
    /// Returns <c>true</c> if <see cref="MeasurementStateFlags.SuspectTime"/> is set.
    /// </summary>
    /// <param name="measurement"><see cref="IMeasurement"/> instance to test.</param>
    /// <returns><c>true</c> if <see cref="MeasurementStateFlags.SuspectTime"/> is set.</returns>
    public static bool TimestampQualityIsSuspect(this IMeasurement measurement)
    {
        return (measurement.StateFlags & MeasurementStateFlags.SuspectTime) > 0;
    }

    /// <summary>
    /// Gets derived quality flags from a set of source measurements.
    /// </summary>
    /// <param name="measurements">Source measurements.</param>
    /// <returns>Derived quality flags.</returns>
    public static MeasurementStateFlags DeriveQualityFlags(this IEnumerable<IMeasurement> measurements)
    {
        MeasurementStateFlags derivedQuality = MeasurementStateFlags.Normal;
        MeasurementStateFlags[] stateFlags = measurements.Select(measurement => measurement.StateFlags).ToArray();

        bool dataQualityIsBad(MeasurementStateFlags flags)
        {
            return (flags & MeasurementStateFlags.BadData) > 0;
        }

        bool timeQualityIsBad(MeasurementStateFlags flags)
        {
            return (flags & MeasurementStateFlags.BadTime) > 0;
        }

        if (stateFlags.Any(dataQualityIsBad))
            derivedQuality |= MeasurementStateFlags.BadData;

        if (stateFlags.Any(timeQualityIsBad))
            derivedQuality |= MeasurementStateFlags.BadTime;

        return derivedQuality;
    }

    /// <summary>
    /// Returns <c>true</c> if <see cref="MeasurementStateFlags.DiscardedValue"/> is set.
    /// </summary>
    /// <param name="measurement"><see cref="IMeasurement"/> instance to test.</param>
    /// <returns><c>true</c> if <see cref="MeasurementStateFlags.DiscardedValue"/> is not set.</returns>
    public static bool IsDiscarded(this IMeasurement measurement)
    {
        return (measurement.StateFlags & MeasurementStateFlags.DiscardedValue) > 0;
    }

    /// <summary>
    /// Returns <c>true</c> if <see cref="MeasurementStateFlags.CalculatedValue"/> is set.
    /// </summary>
    /// <param name="measurement"><see cref="IMeasurement"/> instance to test.</param>
    /// <returns><c>true</c> if <see cref="MeasurementStateFlags.CalculatedValue"/> is not set.</returns>
    public static bool IsCalculated(this IMeasurement measurement)
    {
        return (measurement.StateFlags & MeasurementStateFlags.CalculatedValue) > 0;
    }

    /// <summary>
    /// Returns the <see cref="MeasurementKey"/> values of a <see cref="IMeasurement"/> enumeration.
    /// </summary>
    /// <param name="measurements"><see cref="IMeasurement"/> enumeration to convert.</param>
    /// <returns><see cref="MeasurementKey"/> values of the <see cref="IMeasurement"/> enumeration.</returns>
    public static MeasurementKey[] MeasurementKeys(this IEnumerable<IMeasurement>? measurements)
    {
        return measurements is null ? [] : measurements.Select(m => m.Key).ToArray();
    }

    /// <summary>
    /// Gets the signal type ID for the given <paramref name="measurement"/> as queried from the database.
    /// </summary>
    /// <param name="measurement"><see cref="IMeasurement"/> to obtain signal type for.</param>
    /// <param name="dataSource"><see cref="DataSet"/> that contains measurement metadata.</param>
    /// <param name="defaultSignalTypeID">Default signal type ID to use if lookup fails.</param>
    /// <returns>Signal type ID for the given <paramref name="measurement"/> if found in database; otherwise, <paramref name="defaultSignalTypeID"/>.</returns>
    public static int GetSignalTypeID(this IMeasurement measurement, DataSet dataSource, int defaultSignalTypeID)
    {
        Guid signalID = measurement.ID;

        return signalID == Guid.Empty ? 
            defaultSignalTypeID : 
            s_signalTypeIDs.GetOrAdd(signalID, _ => LookupSignalTypeID(LookupSignalType(signalID, dataSource), defaultSignalTypeID));
    }

    /// <summary>
    /// Gets the signal type ID for the given <paramref name="measurement"/> as queried from the database.
    /// </summary>
    /// <param name="measurement"><see cref="IMeasurement"/> to obtain signal type for.</param>
    /// <param name="dataSource"><see cref="DataSet"/> that contains measurement metadata.</param>
    /// <param name="defaultSignalType">Default signal type to use if lookup fails -- uses enum value as ID.</param>
    /// <returns>Signal type ID for the given <paramref name="measurement"/> if found in database; otherwise, <paramref name="defaultSignalType"/>.</returns>
    public static int GetSignalTypeID(this IMeasurement measurement, DataSet dataSource, SignalType defaultSignalType)
    {
        // Enum value for SignalType enumeration match common defaults used in database schema,
        // this is a convenience method to improve code readability without a required cast
        return measurement.GetSignalTypeID(dataSource, (int)defaultSignalType);
    }

    /// <summary>
    /// Gets the <see cref="SignalType"/> for the given <paramref name="measurement"/> as queried from the database - or -
    /// <see cref="SignalType.NONE"/> if the signal type is not found in the database, the measurement ID is empty, or the
    /// signal type ID is not a valid <see cref="SignalType"/> enumeration value.
    /// </summary>
    /// <param name="measurement"><see cref="IMeasurement"/> to obtain signal type for.</param>
    /// <param name="dataSource"><see cref="DataSet"/> that contains measurement metadata.</param>
    /// <returns><see cref="SignalType"/> for the given <paramref name="measurement"/> if found in database; otherwise, <see cref="SignalType.NONE"/>.</returns>
    public static SignalType GetSignalType(this IMeasurement measurement, DataSet dataSource)
    {
        return Enum.TryParse(measurement.GetSignalTypeID(dataSource, SignalType.NONE).ToString(), true, out SignalType signalType) ?
            signalType : 
            SignalType.NONE;
    }

    /// <summary>
    /// Sets the tag name for a <see cref="IMeasurement"/>.
    /// </summary>
    /// <param name="measurement"><see cref="IMeasurement"/> to create new <see cref="MeasurementMetadata"/> for.</param>
    /// <param name="tagName">New tag name value to assign to measurement's metadata.</param>
    public static void SetTagName(this IMeasurement measurement, string tagName)
    {
        measurement.Metadata = measurement.Metadata.ChangeTagName(tagName);
    }

    /// <summary>
    /// Sets the associated <see cref="MeasurementKey"/> for a <see cref="IMeasurement"/>.
    /// </summary>
    /// <param name="measurement"><see cref="IMeasurement"/> to create new <see cref="MeasurementMetadata"/> for.</param>
    /// <param name="key">New measurement key value to assign to measurement's metadata.</param>
    public static void SetKey(this IMeasurement measurement, MeasurementKey key)
    {
        measurement.Metadata = measurement.Metadata.ChangeKey(key);
    }

    /// <summary>
    /// Sets the adder (i.e., "b" of y = mx + b) for a <see cref="IMeasurement"/>.
    /// </summary>
    /// <param name="measurement"><see cref="IMeasurement"/> to create new <see cref="MeasurementMetadata"/> for.</param>
    /// <param name="adder">New adder value to assign to measurement's metadata.</param>
    public static void SetAdder(this IMeasurement measurement, double adder)
    {
        measurement.Metadata = measurement.Metadata.ChangeAdder(adder);
    }

    /// <summary>
    /// Sets the multiplier (i.e., "m" of y = mx + b) for a <see cref="IMeasurement"/>.
    /// </summary>
    /// <param name="measurement"><see cref="IMeasurement"/> to create new <see cref="MeasurementMetadata"/> for.</param>
    /// <param name="multiplier">New multiplier value to assign to measurement's metadata.</param>
    public static void SetMultiplier(this IMeasurement measurement, double multiplier)
    {
        measurement.Metadata = measurement.Metadata.ChangeMultiplier(multiplier);
    }

    /// <summary>
    /// Gets a single column of measurement data from a two-dimensional data window.
    /// </summary>
    /// <param name="dataWindow">Target data window.</param>
    /// <param name="columnIndex">Index of column to return.</param>
    /// <returns>All values from <paramref name="columnIndex"/> in <paramref name="dataWindow"/>.</returns>
    public static IMeasurement[] GetDataColumn(this IMeasurement[,] dataWindow, int columnIndex)
    {
        return dataWindow.GetColumn(columnIndex).ToArray();
    }

    // Lookup signal type for given measurement ID
    private static string LookupSignalType(Guid signalID, DataSet dataSource)
    {
        try
        {
            DataRow[] filteredRows = dataSource.Tables["ActiveMeasurements"]!.Select($"SignalID = '{signalID}'");
            return filteredRows.Length > 0 ? filteredRows[0]["SignalType"].ToString()!.ToUpper().Trim() : "NONE";
        }
        catch (Exception ex)
        {
            Logger.SwallowException(ex, $"Failed to lookup signal type in data source for measurement ID '{signalID}', defaulting to 'NONE'.");
            return "NONE";
        }
    }

    // Lookup signal type ID for given signal type
    private static int LookupSignalTypeID(string signalType, int defaultID)
    {
        try
        {
            using AdoDataConnection connection = new(Settings.Instance);
            return connection.ExecuteScalar<int>("SELECT ID FROM SignalType WHERE Acronym = {0}", signalType);
        }
        catch (Exception ex)
        {
            Logger.SwallowException(ex, $"Failed to lookup signal type ID for \"SignalType.Acronym = '{signalType}'\", defaulting to {defaultID}.");
            return defaultID;
        }
    }
}
