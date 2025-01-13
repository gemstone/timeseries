//******************************************************************************************************
//  ConnectionParameter.cs - Gbtc
//
//  Copyright © 2024, Grid Protection Alliance.  All Rights Reserved.
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
//  07/28/2024 - J. Ritchie Carroll
//       Generated original version of source code.
//
//******************************************************************************************************

using Gemstone.Expressions.Model;
using Gemstone.Reflection.MemberInfoExtensions;
using Gemstone.StringExtensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace Gemstone.Timeseries.Adapters;

/// <summary>
/// Defines the basic data types for connection parameters.
/// </summary>
/// <remarks>
/// Enumeration is intended to provide user interfaces with basic data
/// type constraints for possible encountered connection parameters.
/// </remarks>
// Common types are defined here, if needed others can be defined,
// if the type is not defined here, it will be treated as a string
public enum DataType
{
    /// <summary>
    /// Represents a <see cref="String"/> data type.
    /// </summary>
    String,
    /// <summary>
    /// Represents a <see cref="Int16"/> data type.
    /// </summary>
    Int16,
    /// <summary>
    /// Represents a <see cref="UInt16"/> data type.
    /// </summary>
    UInt16,
    /// <summary>
    /// Represents a <see cref="Int32"/> data type.
    /// </summary>
    Int32,
    /// <summary>
    /// Represents a <see cref="UInt32"/> data type.
    /// </summary>
    UInt32,
    /// <summary>
    /// Represents a <see cref="Int64"/> data type.
    /// </summary>
    Int64,
    /// <summary>
    /// Represents a <see cref="UInt64"/> data type.
    /// </summary>
    UInt64,
    /// <summary>
    /// Represents a <see cref="Single"/> data type.
    /// </summary>
    Single,
    /// <summary>
    /// Represents a <see cref="Double"/> data type.
    /// </summary>
    Double,
    /// <summary>
    /// Represents a <see cref="DateTime"/> data type.
    /// </summary>
    DateTime,
    /// <summary>
    /// Represents a <see cref="Boolean"/> data type.
    /// </summary>
    Boolean,
    /// <summary>
    /// Represents an <see cref="Enum"/> data type.
    /// </summary>
    Enum
}

/// <summary>
/// Represents a connection parameter.
/// </summary>
/// <remarks>
/// Intended to provide user interfaces with a structured representation of
/// a connection string parameter, i.e., adapter properties marked with the
/// <see cref="ConnectionStringParameterAttribute"/>.
/// </remarks>
public class ConnectionParameter
{
    /// <summary>
    /// Gets the name of the parameter.
    /// </summary>
    public string Name { get; init; } = default!;

    /// <summary>
    /// Gets the category of the parameter.
    /// </summary>
    /// <remarks>
    /// Category is used for UI grouping of parameters.
    /// </remarks>
    public string Category { get; init; } = default!;

    /// <summary>
    /// Gets the description of the parameter.
    /// </summary>
    public string Description { get; init; } = default!;

    /// <summary>
    /// Gets the basic type of the parameter.
    /// </summary>
    //[JsonConverter(typeof(JsonStringEnumConverter))]
    public DataType DataType { get; init; }

    /// <summary>
    /// Gets the available values, e.g., when <see cref="DataType"/> is "Enum".
    /// </summary>
    public string[] AvailableValues { get; init; } = [];

    /// <summary>
    /// Gets the default value of the parameter.
    /// </summary>
    public string DefaultValue { get; init; } = default!;

    /// <summary>
    /// Gets or sets the parameter value.
    /// </summary>
    public string Value { get; set; } = default!;

    /// <summary>
    /// Gets a <see cref="ConnectionParameter"/> instance from a <see cref="PropertyInfo"/>.
    /// </summary>
    public static ConnectionParameter GetConnectionParameter(PropertyInfo info)
    {
        return new ConnectionParameter() 
        {
            Name = info.Name,
            Category = getCategory(info),
            Description = getDescription(info),
            DataType = getDataType(info),
            DefaultValue = getDefaultValue(info)?.ToString() ?? "",
            AvailableValues = getAvailableValues(info)
        };

        static string getCategory(PropertyInfo value)
        {
            return value.TryGetAttribute(out CategoryAttribute? attribute) ? attribute.Category : "General";
        }

        static string getDescription(PropertyInfo value)
        {
            return value.TryGetAttribute(out DescriptionAttribute? attribute) ? attribute.Description : string.Empty;
        }

        static object? getDefaultValue(PropertyInfo value)
        {
            if (!value.TryGetAttribute(out DefaultValueExpressionAttribute? expressionAttribute))
                return value.TryGetAttribute(out DefaultValueAttribute? attribute) ? attribute.Value : null;

            ValueExpressionParser parser = new(expressionAttribute.Expression);
            return parser.ExecuteFunction();
        }

        static DataType getDataType(PropertyInfo value)
        {
            return value.PropertyType switch
            {
                { } type when type == typeof(string) => DataType.String,
                { } type when type == typeof(short) => DataType.Int16,
                { } type when type == typeof(ushort) => DataType.UInt16,
                { } type when type == typeof(int) => DataType.Int32,
                { } type when type == typeof(uint) => DataType.UInt32,
                { } type when type == typeof(long) => DataType.Int64,
                { } type when type == typeof(ulong) => DataType.UInt64,
                { } type when type == typeof(float) => DataType.Single,
                { } type when type == typeof(double) => DataType.Double,
                { } type when type == typeof(DateTime) => DataType.DateTime,
                { } type when type == typeof(bool) => DataType.Boolean,
                { IsEnum: true } => DataType.Enum,
                _ => DataType.String
            };
        }

        static string[] getAvailableValues(PropertyInfo value)
        {
            return value.PropertyType.IsEnum ? Enum.GetNames(value.PropertyType) : [];
        }
    }

    /// <summary>
    /// Implicitly converts a <see cref="PropertyInfo"/> to a <see cref="ConnectionParameter"/>.
    /// </summary>
    /// <param name="info">Property info to convert.</param>
    public static implicit operator ConnectionParameter(PropertyInfo info)
    {
        return GetConnectionParameter(info);
    }
}

/// <summary>
/// Defines extension methods related to <see cref="ConnectionParameter"/>.
/// </summary>
public static class ConnectionParameterExtensions
{
    /// <summary>
    /// Gets the value of a connection parameter.
    /// </summary>
    /// <param name="parameters">Collection of connection parameters.</param>
    /// <param name="name">Name of the connection parameter.</param>
    /// <returns>Value of the connection parameter; or, <c>null</c> if <paramref name="name"/> is not found.</returns>
    public static string? GetValue(this IEnumerable<ConnectionParameter> parameters, string name)
    {
        return parameters.FirstOrDefault(param => param.Name == name)?.Value;
    }

    /// <summary>
    /// Sets the value of a connection parameter.
    /// </summary>
    /// <param name="parameters">Collection of connection parameters.</param>
    /// <param name="name">Name of the connection parameter.</param>
    /// <param name="value">Value to set for the connection parameter.</param>
    public static bool SetValue(this IEnumerable<ConnectionParameter> parameters, string name, string value)
    {
        ConnectionParameter? parameter = parameters.FirstOrDefault(param => param.Name == name);
        
        if (parameter is null)
            return false;
        
        parameter.Value = value;
        return true;
    }

    /// <summary>
    /// Assigns values to a collection of connection parameters from a connection string.
    /// </summary>
    /// <param name="parameters">Target connection parameters.</param>
    /// <param name="connectionString">Connection string to parse.</param>
    public static void ApplyConnectionString(this IEnumerable<ConnectionParameter> parameters, string connectionString)
    {
        parameters.ApplySettings(connectionString.ParseKeyValuePairs());
    }

    /// <summary>
    /// Assigns values to a collection of connection parameters from a dictionary of settings.
    /// </summary>
    /// <param name="parameters">Target connection parameters.</param>
    /// <param name="settings">Settings to parse.</param>
    public static void ApplySettings(this IEnumerable<ConnectionParameter> parameters, Dictionary<string, string> settings)
    {
        foreach (ConnectionParameter parameter in parameters)
        {
            if (settings.TryGetValue(parameter.Name, out string? value))
                parameter.Value = value;
        }
    }

    /// <summary>
    /// Converts a collection of connection parameters to a connection string.
    /// </summary>
    /// <param name="parameters">Source connection parameters.</param>
    /// <returns>Connection string representation of the connection parameters.</returns>
    public static string ToConnectionString(this IEnumerable<ConnectionParameter> parameters)
    {
        return parameters.ToSettings().JoinKeyValuePairs();
    }

    /// <summary>
    /// Converts a collection of connection parameters to a dictionary of settings.
    /// </summary>
    /// <param name="parameters">Source connection parameters.</param>
    /// <returns>Dictionary of settings representing the connection parameters.</returns>
    public static Dictionary<string, string> ToSettings(this IEnumerable<ConnectionParameter> parameters)
    {
        return parameters.ToDictionary(param => param.Name, parameter => parameter.Value);
    }

    /// <summary>
    /// Gets connection parameters from a specified assembly and type using default values.
    /// </summary>
    /// <param name="src">Tuple containing assembly name and type name.</param>
    /// <remarks>
    /// <para>
    /// The <paramref name="src"/> tuple should contain the assembly name and type name of the
    /// desired adapter. The assembly will be loaded, if needed, and the type will be used to get
    /// connection string parameters. Note that the 'assemblyName' and 'typeName' parameters
    /// correspond to the 'AssemblyName' and 'TypeName' fields in the database model used to
    /// load a specifically configured adapter instance.
    /// </para>
    /// <para>
    /// Only public, instance properties are considered for returned parameter list. The
    /// <see cref="ConnectionParameter.DefaultValue"/> will be populated with the configured default
    /// value of the property while the <see cref="ConnectionParameter.Value"/> remains unassigned.
    /// Use <see cref="ApplyConnectionString"/> or <see cref="ApplySettings"/> to assign values from
    /// a connection string or settings dictionary, respectively.
    /// </para>
    /// <para>
    /// Call method like this:
    /// <code>
    /// var assemblyName = "FileAdapters.dll";
    /// var typeName = "FileAdapters.ProcessLauncher";
    /// var params = (assemblyName, typeName).GetConnectionParameters();
    /// </code>
    /// </para>
    /// <para>
    /// Note that the <see cref="AdapterCache"/> automatically loads and caches adapter types
    /// along with their connection parameters, so this method is not typically directly used.
    /// </para>
    /// </remarks>
    /// <returns>Collection of connection parameters for the specified adapter.</returns>
    public static IEnumerable<ConnectionParameter> GetConnectionParameters(this (string assemblyName, string typeName) src)
    {
        try
        {
            (string assemblyName, string typeName) = src;
            Type? type = Assembly.Load(assemblyName).GetType(typeName);
            return type is null ? [] : type.GetConnectionParameters();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Gets connection parameters from a specified type, i.e., set of new <see cref="ConnectionParameter"/>
    /// instances derived from properties marked with <see cref="ConnectionStringParameterAttribute"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only public, instance properties are considered for returned parameter list. The
    /// <see cref="ConnectionParameter.DefaultValue"/> will be populated with the configured default
    /// value of the property while the <see cref="ConnectionParameter.Value"/> remains unassigned.
    /// Use <see cref="ApplyConnectionString"/> or <see cref="ApplySettings"/> to assign values from
    /// a connection string or settings dictionary, respectively.
    /// </para>
    /// <para>
    /// Note that the <see cref="AdapterCache"/> automatically loads and caches adapter types
    /// along with their connection parameters, so this method is not typically directly used.
    /// </para>
    /// </remarks>
    /// <param name="type">Type to get connection parameters from.</param>
    /// <returns>Collection of connection parameters for the specified type.</returns>
    public static IEnumerable<ConnectionParameter> GetConnectionParameters(this Type type)
    {
        try
        {
            return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.GetCustomAttribute<ConnectionStringParameterAttribute>() is not null)
                .Select(property => (ConnectionParameter)property);
        }
        catch
        {
            return [];
        }
    }
}
