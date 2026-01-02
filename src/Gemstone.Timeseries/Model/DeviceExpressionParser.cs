//******************************************************************************************************
//  DeviceExpressionParser.cs - Gbtc
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
//  12/16/2025 - Preston Crawford
//       Generated original version of source code.
//
//******************************************************************************************************

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using Gemstone.Collections.CollectionExtensions;
using Gemstone.Data;
using Gemstone.Data.DataExtensions;
using Gemstone.Data.Model;
using Gemstone.IO.Parsing;
using Gemstone.Numeric.EE;
using Gemstone.StringExtensions;
using Newtonsoft.Json.Linq;
using ConfigSettings = Gemstone.Configuration.Settings;

namespace Gemstone.Timeseries.Model;

/// <summary>
/// Represents a template based token substitution parser that supports binary and advanced expressions. with the Variables geared towards devices.
/// </summary>
/// <remarks>
/// <para>
/// As an example, this parser can use a templated expression of the form:
/// <code>
/// {Device.Acronym}:{Device.ID}
/// </code>
/// then replace the tokens with actual values and properly evaluate the expressions.
/// Example results could look like: GPA_PMU:41
/// </para>
/// <para>
/// Parser also supports more complex C# style expressions using the "eval{}" function
/// </para>
/// </remarks>
public class DeviceExpressionParser
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
    /// Creates a new <see cref="DeviceExpressionParser"/>.
    /// </summary>
    /// <param name="expressionParser">Existing <see cref="TemplatedExpressionParser"/> to use.</param>
    public DeviceExpressionParser(TemplatedExpressionParser expressionParser)
    {
        m_parser = expressionParser;
        Substitutions = new Dictionary<string, string>();
    }

    /// <summary>
    /// Creates a new <see cref="DeviceExpressionParser"/>.
    /// </summary>
    /// <param name="expression">Templated expression to use.</param>
    public DeviceExpressionParser(string expression)
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
    /// <param name="deviceName">Name of the device.</param>
    /// <param name="deviceAcronym">Acronym of the device.</param>
    /// <param name="parentID">ID of the parent device.</param>
    /// <param name="companyID">ID of the company.</param>
    /// <param name="vendorDeviceID">ID of the vendor device.</param>
    /// <param name="interconnectionID">ID of the interconnection.</param>
    /// <returns>A new string created using the configured expression.</returns>
    [MethodImpl(MethodImplOptions.Synchronized)]
    public string Execute(
        string deviceName,
        string deviceAcronym,
        int? parentID,
        int? companyID,
        int? vendorDeviceID,
        int? interconnectionID
        )
    {
        // Update static fields
        SetCompanies();
        SetInterconnections();
        SetVendors();
        SetVendorDevices();

        Dictionary<int, DataRow> companies = s_companies ?? [];
        Dictionary<int, DataRow> interconnections = s_interconnections ?? [];
        Dictionary<int, DataRow> vendors = s_vendors ?? [];
        Dictionary<int, DataRow> vendorDevices = s_vendorDevices ?? [];

        Device? parentDevice = null;

        DataRow? companyValues = null, interconnectionValues = null, vendorValues = null, vendorDeviceValues = null;

        using AdoDataConnection connection = new(ConfigSettings.Default);
        {
            TableOperations<Device> deviceTable = new(connection);
            parentDevice = deviceTable.QueryRecordWhere("ID = {0}", parentID);
        }

        //VendorDevice validation
        if (vendorDeviceID is not null && vendorDeviceID != -1 && !vendorDevices.TryGetValue((int)vendorDeviceID, out vendorDeviceValues))
            throw new Exception($"No database definition was found for {deviceAcronym}'s vendor device");

        //Vendor validation
        if (vendorDeviceValues is not null)
        {
            object vendorIDVal = vendorDeviceValues["VendorID"];
            int vendorID = CastToInt(vendorIDVal) ?? -1;

            if (vendorID != -1 && !vendors.TryGetValue(vendorID, out vendorValues))
                throw new Exception($"No database definition was found for {deviceAcronym}'s vendor");
        }

        //Company validation
        if (companyID is not null && companyID != -1 && !companies.TryGetValue((int)companyID, out companyValues))
            throw new Exception($"No database definition was found for {deviceAcronym}'s company");

        //Interconnection validation
        if (interconnectionID is not null && interconnectionID != -1 && !interconnections.TryGetValue((int)interconnectionID, out interconnectionValues))
            throw new Exception($"No database definition was found for {deviceAcronym}'s interconnection");

        // Define fixed parameter replacements
        Dictionary<string, string> substitutions = new()
        {
            { "{Acronym}", deviceAcronym },
            { "{Name}", deviceName },
        };

        // Define parent device replacements
        if (parentDevice is not null)
        {
            substitutions.Add("{ParentDevice.Acronym}", parentDevice.Acronym);
            substitutions.Add("{ParentDevice.Name}", parentDevice.Name);
        }

        // Define additional substitutions
        foreach (KeyValuePair<string, string> substitution in Substitutions)
            substitutions.Add(substitution.Key, substitution.Value);

        // Define company field value replacements
        DataColumnCollection columns = companies.First().Value.Table.Columns;
        for (int i = 0; i < columns.Count; i++)
            substitutions.Add($"{{Company.{columns[i].ColumnName}}}", companyValues?[i]?.ToNonNullString() ?? string.Empty);

        // Define interconnection field value replacements
        columns = interconnections.First().Value.Table.Columns;
        for (int i = 0; i < columns.Count; i++)
            substitutions.Add($"{{Interconnection.{columns[i].ColumnName}}}", interconnectionValues?[i]?.ToNonNullString() ?? string.Empty);

        // Define vendor device field value replacements
        columns = vendorDevices.First().Value.Table.Columns;
        for (int i = 0; i < columns.Count; i++)
            substitutions.Add($"{{VendorDevice.{columns[i].ColumnName}}}", vendorDeviceValues?[i]?.ToNonNullString() ?? string.Empty);

        // Define vendor field value replacements
        columns = vendors.First().Value.Table.Columns;
        for (int i = 0; i < columns.Count; i++)
            substitutions.Add($"{{Vendor.{columns[i].ColumnName}}}", vendorValues?[i]?.ToNonNullString() ?? string.Empty);

        return m_parser.Execute(substitutions);
    }

    #endregion

    #region [ Static ]

    private static Dictionary<int, DataRow>? s_companies;
    private static Dictionary<int, DataRow>? s_interconnections;
    private static Dictionary<int, DataRow>? s_vendors;
    private static Dictionary<int, DataRow>? s_vendorDevices;

    private static void SetCompanies()
    {
        try
        {
            // It is expected that when a point tag is needing to be created that the database will be available
            using AdoDataConnection database = new(ConfigSettings.Default);
            Dictionary<int, DataRow> companies = new();

            foreach (DataRow row in database.Connection.RetrieveData("SELECT * FROM Company").AsEnumerable())
            {
                if (row is null)
                    continue;

                object idVal = row["ID"];
                companies.AddOrUpdate(CastToInt(idVal) ?? -1, row);
            }

            s_companies = companies;
        }
        catch
        {
            // Swallow
        }
    }

    private static void SetInterconnections()
    {
        try
        {
            // It is expected that when a point tag is needing to be created that the database will be available
            using AdoDataConnection database = new(ConfigSettings.Default);
            Dictionary<int, DataRow> interconnections = new();

            foreach (DataRow row in database.Connection.RetrieveData("SELECT * FROM Interconnection").AsEnumerable())
            {
                if (row is null)
                    continue;

                object idVal = row["ID"];
                interconnections.AddOrUpdate(CastToInt(idVal) ?? -1, row);
            }

            s_interconnections = interconnections;
        }
        catch
        {
            // Swallow
        }
    }

    private static void SetVendors()
    {
        try
        {
            // It is expected that when a point tag is needing to be created that the database will be available
            using AdoDataConnection database = new(ConfigSettings.Default);
            Dictionary<int, DataRow> vendors = new();

            foreach (DataRow row in database.Connection.RetrieveData("SELECT * FROM Vendor").AsEnumerable())
            {
                if (row is null)
                    continue;

                object idVal = row["ID"];
                vendors.AddOrUpdate(CastToInt(idVal) ?? -1, row);
            }

            s_vendors = vendors;
        }
        catch
        {
            // Swallow
        }
    }

    private static void SetVendorDevices()
    {
        try
        {
            using AdoDataConnection database = new(ConfigSettings.Default);
            Dictionary<int, DataRow> vendorDevices = new();

            foreach (DataRow row in database.Connection.RetrieveData("SELECT * FROM VendorDevice").AsEnumerable())
            {
                if (row is null)
                    continue;

                vendorDevices.AddOrUpdate(CastToInt(row["ID"]) ?? -1, row);
            }

            s_vendorDevices = vendorDevices;
        }
        catch
        {
            // Swallow
        }
    }

    private static int? CastToInt(object val)
    {
        int? casted = val switch
        {
            int i => i,
            long l => checked((int)l),
            _ => null
        };

        return casted;
    }

    #endregion
}
