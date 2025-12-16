//******************************************************************************************************//  TemplateExpression.cs - Gbtc/
//  Copyright © 2025, Grid Protection Alliance.  All Rights Reserved.
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
//  12/16/2025 - C. Lackner
//       Generated original version of source code.
//
//******************************************************************************************************

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using Gemstone.Caching;
using Gemstone.Collections.CollectionExtensions;
using Gemstone.Data;
using Gemstone.Data.DataExtensions;
using Gemstone.Data.Model;
using Gemstone.Diagnostics;
using Gemstone.IO.Parsing;
using Gemstone.Numeric.EE;
using Gemstone.StringExtensions;
using ConfigSettings = Gemstone.Configuration.Settings;

namespace Gemstone.Timeseries.Model;

/// <summary>
/// Represents a template based token substitution parser that supports binary and advanced expressions. with the Variables geared towards measurements.
/// </summary>
/// <remarks>
/// <para>
/// As an example, this parser can use a templated expression of the form:
/// <code>
/// {CompanyAcronym}_{DeviceAcronym}[?{SignalType.Source}=Phasor[-{SignalType.Suffix}{SignalIndex}]]:{Signal.Acronym}
/// </code>
/// then replace the tokens with actual values and properly evaluate the expressions.
/// Example results could look like: GPA_SHELBY-PA1:IPHA and GPA_SHELBY:FREQ
/// </para>
/// <para>
/// Parser also supports more complex C# style expressions using the "eval{}" function, e.g.:
/// <code>
/// eval{'{CompanyAcronym}'.Substring(0,3)}_{DeviceAcronym}eval{'[?{SignalType.Source}=Phasor[-{SignalType.Suffix}]]'.Length}
/// </code>
/// </para>
/// </remarks>
public class MeasurementExpressionParser
{
    #region [ Members ]

    private TemplatedExpressionParser m_parser;

    /// <summary>
    /// Additional Variable available in this instance.
    /// </summary>
    public Dictionary<string, string> Substitutions { set; private get; }

    #endregion

    #region [ Constructors ]

    /// <summary>
    /// Creates a new <see cref="MeasurementExpressionParser"/>.
    /// </summary>
    /// <param name="expressionParser">Existing <see cref="TemplatedExpressionParser"/> to use.</param>
    public MeasurementExpressionParser(TemplatedExpressionParser expressionParser)
    {
        m_parser = expressionParser;
        Substitutions = new Dictionary<string, string>();
    }

    /// <summary>
    /// Creates a new <see cref="MeasurementExpressionParser"/>.
    /// </summary>
    /// <param name="expression">Templated expression to use.</param>
    public MeasurementExpressionParser(string expression)
    {
        m_parser = new TemplatedExpressionParser()
        { 
            TemplatedExpression = expression
        };
        Substitutions = new Dictionary<string, string>();
    }

    #endregion

    #region [ Methods ]

    /// <summary>
    /// Create a new expression using the provided expression.
    /// </summary>
    /// <param name="companyAcronym">Company name acronym to use for the expression.</param>
    /// <param name="deviceAcronym">Device name acronym to use for the expression.</param>
    /// <param name="vendorAcronym">Vendor name acronym to use for the expression. Can be null.</param>
    /// <param name="signalTypeAcronym">Acronym of signal type of the expression.</param>
    /// <param name="interconnectionAcronym">Interconnection acronym of the expression.</param>
    /// <param name="label">The label associated with the expression, e.g., the phasor or analog label.</param>
    /// <param name="signalIndex">Signal index of the expression, if any.</param>
    /// <param name="phase">Signal phase of the point, if any.</param>
    /// <param name="baseKV">Nominal kV of line associated with phasor.</param>
    /// <returns>A new strting created using the configured expression.</returns>
    [MethodImpl(MethodImplOptions.Synchronized)]
    public string Execute(string? companyAcronym, string? deviceAcronym, string? vendorAcronym, string? signalTypeAcronym, string? interconnectionAcronym, string? label = null, int signalIndex = -1, char phase = '_', int baseKV = 0)
    {
        // Initialize dictionary
        s_signalTypes ??= InitializeSignalTypes();
        s_companies ??= InitializeCompanies();
        s_interconnections ??= InitializeInterconnections();
        s_Vendors ??= IntializeVendor();

        DataRow? signalTypeValues = null, companyValues = null, interconnectionValues = null, vendorValues = null;

        if (!string.IsNullOrWhiteSpace(signalTypeAcronym) && !s_signalTypes.TryGetValue(signalTypeAcronym, out signalTypeValues))
            throw new ArgumentOutOfRangeException(nameof(signalTypeAcronym), $"No database definition was found for signal type \"{signalTypeAcronym}\"");
        

        if (!string.IsNullOrWhiteSpace(companyAcronym) && !s_companies.TryGetValue(companyAcronym, out companyValues))
            throw new ArgumentOutOfRangeException(nameof(companyAcronym), $"No database definition was found for company \"{companyAcronym}\"");

        if (!string.IsNullOrWhiteSpace(interconnectionAcronym) && !s_interconnections.TryGetValue(interconnectionAcronym, out interconnectionValues))
            throw new ArgumentOutOfRangeException(nameof(interconnectionAcronym), $"No database definition was found for interconnection \"{interconnectionAcronym}\"");

        if (!string.IsNullOrWhiteSpace(vendorAcronym) && !s_Vendors.TryGetValue(vendorAcronym, out vendorValues))
            throw new ArgumentOutOfRangeException(nameof(vendorAcronym), $"No database definition was found for vendor \"{vendorAcronym}\"");


        // Validate key acronyms
        label ??= "";
        deviceAcronym ??= "";
        signalTypeAcronym ??= "";

        if (baseKV == 0)
            baseKV = GuessBaseKV(label, deviceAcronym, signalTypeAcronym);

        // Define fixed parameter replacements
        Dictionary<string, string> substitutions = new()
        {
            { "{DeviceAcronym}", deviceAcronym },
            { "{Label}", label },
            { "{SignalIndex}", signalIndex.ToString() },
            { "{Phase}", phase.ToString() },
            { "{BaseKV}", baseKV.ToString() }
        };

        foreach(KeyValuePair<string, string> substitution in Substitutions)
            substitutions.Add(substitution.Key, substitution.Value);

        // Define signal type field value replacements
        DataColumnCollection columns = s_signalTypes.First().Value.Table.Columns;

        for (int i = 0; i < columns.Count; i++)
            substitutions.Add($"{{SignalType.{columns[i].ColumnName}}}", signalTypeValues?[i]?.ToNonNullString() ?? string.Empty);

        // Define company field value replacements
        columns = s_companies.First().Value.Table.Columns;

        for (int i = 0; i < columns.Count; i++)
            substitutions.Add($"{{Company.{columns[i].ColumnName}}}", companyValues?[i]?.ToNonNullString() ?? string.Empty);

        // Define interconnection field value replacements
        columns = s_interconnections.First().Value.Table.Columns;

        for (int i = 0; i < columns.Count; i++)
            substitutions.Add($"{{Interconnection.{columns[i].ColumnName}}}", interconnectionValues?[i]?.ToNonNullString() ?? string.Empty);

        // Define vendor field value replacements
        columns = s_Vendors.First().Value.Table.Columns;

        for (int i = 0; i < columns.Count; i++)
            substitutions.Add($"{{Vendor.{columns[i].ColumnName}}}", vendorValues?[i]?.ToNonNullString() ?? string.Empty);

        return m_parser.Execute(substitutions);
    }


    private static int GuessBaseKV(string? label, string deviceAcronym, string signalTypeAcronym)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            // Calls to CreatePointTag are commonly made in sequence for all measurements, then calls stop, so
            // we create an expiring memory cache with a map of first phasor tags associated with each device
            Dictionary<string, string?> firstPhasorPointTagCache = MemoryCache<Dictionary<string, string?>>.GetOrAdd(nameof(GuessBaseKV), () => new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase));

            label = firstPhasorPointTagCache.GetOrAdd(deviceAcronym, _ =>
            {
                try
                {
                    // Attempt to lookup first phasor magnitude tag associated with this device
                    using AdoDataConnection connection = new(ConfigSettings.Instance);
                    TableOperations<Measurement> measurementTable = new(connection);
                    Measurement? record = measurementTable.QueryRecordWhere("SignalReference = {0}", SignalReference.ToString(deviceAcronym, SignalKind.Magnitude, 1));
                    return record?.PointTag;
                }
                catch (Exception ex)
                {
                    Logger.SwallowException(ex, $"Failed while looking up first phasor tag associated with device '{deviceAcronym}'");
                    return null;
                }
            });
        }

        // Check phasor label for voltage level as a priority over device acronym for better base KV guess
        if (!string.IsNullOrWhiteSpace(label))
        {
            foreach (string voltageLevel in s_commonVoltageLevels)
            {
                if (label.IndexOf(voltageLevel, StringComparison.Ordinal) > -1)
                    return int.Parse(voltageLevel);
            }

            // If label did not contain voltage level and signal type is an analog or digital, try lookup of first phasor tag
            if (signalTypeAcronym.Equals("ALOG", StringComparison.OrdinalIgnoreCase) || signalTypeAcronym.Equals("DIGI", StringComparison.OrdinalIgnoreCase))
                return GuessBaseKV(null, deviceAcronym, "CALC");
        }

        foreach (string voltageLevel in s_commonVoltageLevels)
        {
            if (deviceAcronym.IndexOf(voltageLevel, StringComparison.Ordinal) > -1)
                return int.Parse(voltageLevel);
        }

        return 0;
    }

    #endregion

    #region [ Static ]

    private static Dictionary<string, DataRow>? s_signalTypes;
    private static Dictionary<string, DataRow>? s_companies;
    private static Dictionary<string, DataRow>? s_interconnections;
    private static Dictionary<string, DataRow>? s_Vendors;


    private static readonly string[] s_commonVoltageLevels = CommonVoltageLevels.Values;

    private static Dictionary<string, DataRow> InitializeSignalTypes()
    {
        // It is expected that when a point tag is needing to be created that the database will be available
        using AdoDataConnection database = new(ConfigSettings.Default);
        Dictionary<string, DataRow> signalTypes = new(StringComparer.OrdinalIgnoreCase);

        foreach (DataRow row in database.Connection.RetrieveData("SELECT * FROM SignalType").AsEnumerable())
        {
            if (row is null)
                continue;

            signalTypes.AddOrUpdate(row["Acronym"]?.ToString() ?? "", row);
        }

        return signalTypes;
    }

    private static Dictionary<string, DataRow> InitializeCompanies()
    {
        // It is expected that when a point tag is needing to be created that the database will be available
        using AdoDataConnection database = new(ConfigSettings.Default);
        Dictionary<string, DataRow> companies = new(StringComparer.OrdinalIgnoreCase);

        foreach (DataRow row in database.Connection.RetrieveData("SELECT * FROM Company").AsEnumerable())
        {
            if (row is null)
                continue;

            companies.AddOrUpdate(row["Acronym"]?.ToString() ?? "", row);
        }

        return companies;
    }

    private static Dictionary<string, DataRow> InitializeInterconnections()
    {
        // It is expected that when a point tag is needing to be created that the database will be available
        using AdoDataConnection database = new(ConfigSettings.Default);
        Dictionary<string, DataRow> interconnections = new(StringComparer.OrdinalIgnoreCase);

        foreach (DataRow row in database.Connection.RetrieveData("SELECT * FROM Interconnection").AsEnumerable())
        {
            if (row is null)
                continue;

            interconnections.AddOrUpdate(row["Acronym"]?.ToString() ?? "", row);
        }

        return interconnections;
    }

    private static Dictionary<string, DataRow> IntializeVendor()
    {
        // It is expected that when a point tag is needing to be created that the database will be available
        using AdoDataConnection database = new(ConfigSettings.Default);
        Dictionary<string, DataRow> vendors = new(StringComparer.OrdinalIgnoreCase);

        foreach (DataRow row in database.Connection.RetrieveData("SELECT * FROM Vendor").AsEnumerable())
        {
            if (row is null)
                continue;

            vendors.AddOrUpdate(row["Acronym"]?.ToString() ?? "", row);
        }

        return vendors;
    }
    #endregion
}
