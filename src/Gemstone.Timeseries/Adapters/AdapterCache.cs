//******************************************************************************************************
//  AdapterCache.cs - Gbtc
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
//  01/08/2025 - J. Ritchie Carroll
//       Generated original version of source code.
//
//******************************************************************************************************
// ReSharper disable StaticMemberInGenericType
// ReSharper disable InconsistentNaming

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading;
using Gemstone.EventHandlerExtensions;
using Gemstone.StringExtensions;
using Gemstone.TypeExtensions;

namespace Gemstone.Timeseries.Adapters;

/// <summary>
/// Represents a collection of adpaters, provides a specialized method to retrieve `AdapterInfo` instances while cloning their connection parameters.
/// </summary>
public class AdapterCollection : Dictionary<Type, AdapterInfo>
{
    public AdapterCollection(IDictionary<Type, AdapterInfo> dictionary) : base(dictionary)
    {
    }

    /// <summary>
    /// Attempts to retrieve the <see cref="AdapterInfo"/> associated with the specified <see cref="Type"/> key.
    /// </summary>
    /// <remarks>If the key is found, the returned <see cref="AdapterInfo"/> is a copy with cloned
    /// parameters.</remarks>
    /// <param name="key">The <see cref="Type"/> key to locate in the collection.</param>
    /// <param name="value">When this method returns, contains the <see cref="AdapterInfo"/> associated with the specified key, if the key
    /// is found; otherwise, <see langword="null"/>. This parameter is passed uninitialized.</param>
    /// <returns><see langword="true"/> if the collection contains an element with the specified key; otherwise, <see
    /// langword="false"/>.</returns>
    public new bool TryGetValue(Type key, [MaybeNullWhen(false)] out AdapterInfo value)
    {
        bool result = base.TryGetValue(key, out AdapterInfo val);

        if (result)
            value = val with { Parameters = val.Parameters.Select(param => param.Clone()).ToArray() };
        else
            value = null;

        return result;
    }
}

/// <summary>
/// Represents an adapter type and its key attributes.
/// </summary>
public record AdapterInfo
{
    /// <summary>
    /// Gets the type of the adapter.
    /// </summary>
    [JsonIgnore]
    public required Type Type { get; init; }

    /// <summary>
    /// Gets the user-friendly name for the adapter as loaded from the <see cref="DescriptionAttribute"/>.
    /// </summary>
    /// <remarks>
    /// Maps to 'AdapterName' in the database model.
    /// </remarks>
    public required string AdapterName { get; init; }

    /// <summary>
    /// Gets the assembly file name for the adapter.
    /// </summary>
    /// <remarks>
    /// Maps to 'AssemblyName' in the database model.
    /// </remarks>
    public required string AssemblyName { get; init; }

    /// <summary>
    /// Gets the full name of the adapter type.
    /// </summary>
    /// <remarks>
    /// Maps to 'TypeName' in the database model.
    /// </remarks>
    public required string TypeName { get; init; }

    /// <summary>
    /// Gets the connection string parameters for the adapter.
    /// </summary>
    /// <remarks>
    /// Maps to 'ConnectionString' in the database model.
    /// Use 'ApplyConnectionString' to assign values from a connection string.
    /// Use 'ToConnectionString' to convert values to a connection string.
    /// </remarks>
    public required ConnectionParameter[] Parameters { get; init; }

    /// <summary>
    /// Gets the description for the adapter as loaded from the <see cref="DescriptionAttribute"/>.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Gets the <see cref="EditorBrowsableState"/> for the adapter.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required EditorBrowsableState BrowsableState { get; init; }
}

/// <summary>
/// Represents the set of UI resource attributes for an adapter.
/// </summary>
public record UIResourceInfo
{
    /// <summary>
    /// Gets the adapter type and its key attributes.
    /// </summary>
    public required AdapterInfo Info { get; init; }

    /// <summary>
    /// Gets the UI resource attributes for the adapter.
    /// </summary>
    public required UIResourceAttribute[] Attributes { get; init; }

    /// <summary>
    /// Gets the map of UI resource attributes by resource ID.
    /// </summary>
    public required Dictionary<string, UIResourceAttribute> AttributeMap { get; init; }
}

/// <summary>
/// Represents the set of adapter protocol attributes for an adapter.
/// </summary>
public record AdapterProtocolInfo
{
    /// <summary>
    /// Gets the adapter type and its key attributes.
    /// </summary>
    public required AdapterInfo Info { get; init; }

    /// <summary>
    /// Gets the adapter protocol attributes for the adapter.
    /// </summary>
    public required AdapterProtocolAttribute[] Attributes { get; init; }
}

/// <summary>
/// Represents the set of adapter command attributes for an adapter.
/// </summary>
public record AdapterCommandInfo
{
    /// <summary>
    /// Gets the adapter type and its key attributes.
    /// </summary>
    public required AdapterInfo Info { get; init; }

    /// <summary>
    /// Gets the adapter command method attributes for the adapter.
    /// </summary>
    public required (MethodInfo method, AdapterCommandAttribute attribute)[] MethodAttributes { get; init; }

    /// <summary>
    /// Gets the map of adapter command attributes by name.
    /// </summary>
    public required Dictionary<string, (MethodInfo method, AdapterCommandAttribute attribute)> MethodAttributeMap { get; init; }
}

/// <summary>
/// Represents a cache of loaded adapter types and their attributes.
/// </summary>
/// <remarks>
/// <para>
/// This static class holds a cache of all <see cref="IAdapter"/> types found in
/// the local application directory along with loaded attribute information.
/// </para>
/// <para>
/// Caches are dynamically loaded on first access and can be reloaded at runtime
/// by calling the <see cref="ReloadAdapterTypes"/> method.
/// </para>
/// </remarks>
public static class AdapterCache
{
    // Notifies derived classes that adapters have been reloaded
    internal static EventHandler? AdaptersReloaded;

    private static AdapterCollection? s_allAdapters;
    private static Dictionary<Type, UIResourceInfo>? s_uiResources;
    private static Dictionary<Type, AdapterProtocolInfo>? s_adapterProtocols;
    private static Dictionary<Type, AdapterCommandInfo>? s_adapterCommands;
    private static Dictionary<(string, string), Type>? s_assemblyTypes;
    private static readonly Lock s_loadLock = new();

    private class StringTupleComparer(StringComparison comparison) : IEqualityComparer<(string, string)>
    {
        public bool Equals((string, string) x, (string, string) y)
        {
            return string.Equals(x.Item1, y.Item1, comparison) &&
                   string.Equals(x.Item2, y.Item2, comparison);
        }

        public int GetHashCode((string, string) obj)
        {
            return obj.Item1.ToLowerInvariant().GetHashCode() ^
                   obj.Item2.ToLowerInvariant().GetHashCode();
        }

        public static readonly StringTupleComparer OrdinalIgnoreCase = new(StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets all time-series adapter types in the application directory.
    /// </summary>
    public static AdapterCollection AllAdapters
    {
        get
        {
            // Caching default adapter types so expensive assembly load with type inspections
            // and reflection-based instance creation of types are only done once. If dynamic
            // reload is needed at runtime, call ReloadAdapterTypes() method.
            AdapterCollection? allAdapters = Interlocked.CompareExchange(ref s_allAdapters, null, null);

            if (allAdapters is not null)
                return allAdapters;

            lock (s_loadLock)
            {
                // Check if another thread already loaded the adapter types
                if (s_allAdapters is not null)
                    return s_allAdapters;

                // Load all adapter types in the application directory
                s_allAdapters = new AdapterCollection(typeof(IAdapter).LoadImplementations()
                    .Distinct()
                    .Select(type => (type, info: type.GetDescription()))
                    .Select(item => new AdapterInfo
                    {
                        Type = item.type,
                        AdapterName = item.info.adapterName,                        // Database field name: AdapterName
                        AssemblyName = item.type.GetAssemblyFileName(),             // Database field name: AssemblyName
                        TypeName = item.type.GetFullName(),                         // Database field name: TypeName
                        Parameters = item.type.GetConnectionParameters().ToArray(), // Database field name: ConnectionString
                        Description = item.info.description,
                        BrowsableState = item.type.GetEditorBrowsableState()
                    })
                    .ToDictionary(item => item.Type, item => item));

                // Load adapter types with UI resource attributes
                s_uiResources = s_allAdapters.Values
                    .GetAdapterAttributes<UIResourceAttribute>()
                    .Select(item => new UIResourceInfo
                    {
                        Info = item.info,
                        Attributes = item.attributes,
                        AttributeMap = item.attributes.ToDictionary(attr => attr.ResourceID)
                    })
                    .ToDictionary(item => item.Info.Type, item => item);

                // Only input and action adapters can be protocols
                IEnumerable<AdapterInfo> protocolTypes = s_allAdapters.Values
                    .Where(info =>
                        typeof(IInputAdapter).IsAssignableFrom(info.Type) ||
                        typeof(IActionAdapter).IsAssignableFrom(info.Type));

                // Load adapter types with protocol attributes
                s_adapterProtocols = protocolTypes
                    .GetAdapterAttributes<AdapterProtocolAttribute>()
                    .Select(item => new AdapterProtocolInfo
                    {
                        Info = item.info,
                        Attributes = item.attributes
                    })
                    .ToDictionary(item => item.Info.Type, item => item);

                // Load adapter types with command attributes
                s_adapterCommands = s_allAdapters.Values
                    .GetAdapterMethodAttributes<AdapterCommandAttribute>()
                    .Select(item => new AdapterCommandInfo
                    {
                        Info = item.info,
                        MethodAttributes = item.methodAttributes,
                        MethodAttributeMap = item.methodAttributes.ToDictionary(attr => attr.method.Name)
                    })
                    .ToDictionary(item => item.Info.Type, item => item);

                // Create a cache for faster lookups by assembly file name and type name
                s_assemblyTypes = s_allAdapters.Values.ToDictionary(
                    info => (info.Type.GetAssemblyFileName(), info.Type.GetFullName()),
                    info => info.Type,
                    StringTupleComparer.OrdinalIgnoreCase);

                // Calling event inside lock ensures all subscribers can safely reset their caches
                AdaptersReloaded?.SafeInvoke(typeof(AdapterCache), EventArgs.Empty);
            }

            return s_allAdapters;
        }
    }

    /// <summary>
    /// Gets all UI resource attribute information for adapter types in the application directory.
    /// </summary>
    public static Dictionary<Type, UIResourceInfo> UIResources
    {
        get
        {
            Dictionary<Type, UIResourceInfo>? uiResources = Interlocked.CompareExchange(ref s_uiResources, null, null);

            if (uiResources is not null)
                return uiResources;

            lock (s_loadLock)
            {
                // Get list of adapter types, this establishes cache of UI resource attributes
                _ = AllAdapters;
                return s_uiResources!;
            }
        }
    }

    /// <summary>
    /// Gets all adapter protocol attribute information for adapter types in the application directory.
    /// </summary>
    public static Dictionary<Type, AdapterProtocolInfo> AdapterProtocols
    {
        get
        {
            Dictionary<Type, AdapterProtocolInfo>? adapterProtocols = Interlocked.CompareExchange(ref s_adapterProtocols, null, null);

            if (adapterProtocols is not null)
                return adapterProtocols;

            lock (s_loadLock)
            {
                // Get list of adapter types, this establishes cache of adapter protocol attributes
                _ = AllAdapters;
                return s_adapterProtocols!;
            }
        }
    }

    /// <summary>
    /// Gets all adapter command attribute information for adapter types in the application directory.
    /// </summary>
    public static Dictionary<Type, AdapterCommandInfo> AdapterCommands
    {
        get
        {
            Dictionary<Type, AdapterCommandInfo>? adapterCommands = Interlocked.CompareExchange(ref s_adapterCommands, null, null);

            if (adapterCommands is not null)
                return adapterCommands;

            lock (s_loadLock)
            {
                // Get list of adapter types, this establishes cache of adapter command attributes
                _ = AllAdapters;
                return s_adapterCommands!;
            }
        }
    }

    /// <summary>
    /// Gets a cache of adapter types keyed by assembly file name and type name, e.g.:
    /// <c>("FileAdapters.dll", "FileAdapters.ProcessLauncher")</c> for faster lookups.
    /// </summary>
    public static Dictionary<(string assemblyFileName, string typeName), Type> AssemblyTypes
    {
        get
        {
            Dictionary<(string, string), Type>? assemblyTypes = Interlocked.CompareExchange(ref s_assemblyTypes, null, null);

            if (assemblyTypes is not null)
                return assemblyTypes;

            lock (s_loadLock)
            {
                // Get list of adapter types, this establishes cache of adapter protocol attributes
                _ = AllAdapters;
                return s_assemblyTypes!;
            }
        }
    }

    /// <summary>
    /// Returns all adapters in the application directory filtered by <see cref="EditorBrowsableState"/>.
    /// </summary>
    /// <param name="stateFilter">Filter for <see cref="EditorBrowsableState"/>. Defaults to exclude hidden adapters.</param>
    /// <returns>An <see cref="IEnumerable{AdapterTypeDescription}"/> describing all available adapters.</returns>
    public static IEnumerable<AdapterInfo> GetAdapters(Func<EditorBrowsableState, bool>? stateFilter = null)
    {
        return AllAdapters.GetAdapters(stateFilter);
    }

    /// <summary>
    /// Gets the connection parameters for the adapter and type name, optionally applying connection settings.
    /// </summary>
    /// <param name="assemblyName">Assembly file name for the adapter.</param>
    /// <param name="typeName">Full type name for the adapter.</param>
    /// <param name="connectionSettings">Optional connection settings to apply to connection parameters.</param>
    /// <returns>Connection parameters for the adapter and type name.</returns>
    public static IEnumerable<ConnectionParameter> GetConnectionParameters(string assemblyName, string typeName, string? connectionSettings)
    {
        return GetConnectionParameters(assemblyName, typeName, connectionSettings?.ParseKeyValuePairs());
    }

    /// <summary>
    /// Gets the connection parameters for the adapter and type name, optionally applying connection settings.
    /// </summary>
    /// <param name="assemblyName">Assembly file name for the adapter.</param>
    /// <param name="typeName">Full type name for the adapter.</param>
    /// <param name="settings">Optional connection settings to apply to connection parameters.</param>
    /// <returns>Connection parameters for the adapter and type name.</returns>
    public static IEnumerable<ConnectionParameter> GetConnectionParameters(string assemblyName, string typeName, Dictionary<string, string>? settings = null)
    {
        // Attempt to lookup type by assembly name and type name, then lookup adapter info for the type
        if (!AssemblyTypes.TryGetValue((assemblyName, typeName), out Type? type) || !AllAdapters.TryGetValue(type, out AdapterInfo? info))
            return [];

        ConnectionParameter[] parameters = info.Parameters;

        if (settings is not null)
            parameters.ApplySettings(settings);

        return parameters;
    }

    /// <summary>
    /// Gets the adapter name and description for the given type parsed from the <see cref="DescriptionAttribute"/>.
    /// </summary>
    /// <remarks>
    /// If no <see cref="DescriptionAttribute"/> can be found, adapter name defaults to <see cref="Type.Name"/> and
    /// description defaults to <see cref="Type.FullName"/>.
    /// </remarks>
    public static (string adapterName, string description) GetDescription(this Type type)
    {
        string description = type.GetCustomAttribute<DescriptionAttribute>()?.Description ?? type.GetFullName();

        // Common practice for adapters is to define a user-friendly adapter name
        // as the first part of the description, separated by a colon
        string[] parts = description.Split(':');

        return parts.Length > 1 ? (parts[0].Trim(), string.Join(":", parts[1..]).Trim()) : (type.Name, description);
    }

    /// <summary>
    /// Gets the assembly file name for the given type.
    /// </summary>
    /// <remarks>
    /// This commonly maps to 'AssemblyName' database field for adapters.
    /// </remarks>
    /// <param name="type">Type for which the assembly location is found.</param>
    /// <returns>Assembly file name for the given type.</returns>
    public static string GetAssemblyFileName(this Type type)
    {
        return Path.GetFileName(type.Assembly.Location);
    }

    /// <summary>
    /// Gets the <see cref="Type.FullName"/>> for the given type, falling back on <see cref="Type.Name"/>.
    /// </summary>
    /// <remarks>
    /// This commonly maps to 'TypeName' database field for adapters.
    /// </remarks>
    /// <param name="type">Type for which the full name is found.</param>
    /// <returns>Full name of the given type.</returns>
    public static string GetFullName(this Type type)
    {
        return type.FullName ?? type.Name;
    }

    /// <summary>
    /// Gets the assembly name for the given type.
    /// </summary>
    /// <param name="type">Type for which the assembly name is found.</param>
    /// <returns>Assembly name for the given type.</returns>
    public static string GetAssemblyName(this Type type)
    {
        return type.Assembly.GetName().Name ?? type.Name;
    }

    /// <summary>
    /// Gets the <see cref="EditorBrowsableState"/> for the given type. Defaults to
    /// <see cref="EditorBrowsableState.Always"/> if no attribute is found.
    /// </summary>
    /// <param name="type">Type for which the <see cref="EditorBrowsableState"/> is found.</param>
    /// <returns><see cref="EditorBrowsableState"/> for the given type; defaults to 'Always'.</returns>
    public static EditorBrowsableState GetEditorBrowsableState(this MemberInfo type)
    {
        return type.GetCustomAttribute<EditorBrowsableAttribute>()?.State ?? EditorBrowsableState.Always;
    }

    /// <summary>
    /// Clears all cached adapter types and their attributes which will force a reload on next access.
    /// </summary>
    /// <remarks>
    /// Method should be exposed through an admin only interface.
    /// </remarks>
    public static void ReloadAdapterTypes()
    {
        lock (s_loadLock)
        {
            Interlocked.Exchange(ref s_allAdapters, null);
            Interlocked.Exchange(ref s_uiResources, null);
            Interlocked.Exchange(ref s_adapterProtocols, null);
            Interlocked.Exchange(ref s_adapterCommands, null);
            Interlocked.Exchange(ref s_assemblyTypes, null);

            // Calling event inside lock ensures all subscribers can safely reset their caches
            AdaptersReloaded?.SafeInvoke(typeof(AdapterCache), EventArgs.Empty);
        }
    }

    // Gets all adapters grouped with each of its specified attributes for all adapters that are marked with the attribute.
    internal static IEnumerable<(AdapterInfo info, TAttr[] attributes)> GetAdapterAttributes<TAttr>(this IEnumerable<AdapterInfo> adapters) where TAttr : Attribute
    {
        return adapters
            .Select(info => (info, attributes: info.Type.GetCustomAttributes<TAttr>().ToArray()))
            .Where(item => item.attributes.Length > 0);
    }

    // Gets all adapters grouped with each of its specified attributes for all adapters with methods that are marked with the attribute.
    internal static IEnumerable<(AdapterInfo info, (MethodInfo method, TAttr attribute)[] methodAttributes)> GetAdapterMethodAttributes<TAttr>(this IEnumerable<AdapterInfo> adapters) where TAttr : Attribute
    {
        return adapters.Select(info => (info, methodAttributes: getMethodAttributes(info)));

        (MethodInfo, TAttr)[] getMethodAttributes(AdapterInfo info)
        {
            return info.Type
                .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.IgnoreCase)
                .Select(method => (method, attribute: method.GetCustomAttribute<TAttr>()))
                .Where(item => item.attribute is not null)
                .ToArray()!;
        }
    }

    // Gets all adapters filtered by editor browsable state.
    internal static IEnumerable<AdapterInfo> GetAdapters(this Dictionary<Type, AdapterInfo> allAdapters, Func<EditorBrowsableState, bool>? stateFilter = null)
    {
        // Default state filter to exclude hidden adapters, i.e., browsable state of Never or Advanced
        stateFilter ??= state => state is EditorBrowsableState.Always;

        return allAdapters.Values.Where(item => stateFilter(item.BrowsableState));
    }
}

/// <summary>
/// Represents a cache of loaded adapters of type <typeparamref name="T"/> and their attributes.
/// </summary>
public static class AdapterCache<T> where T : IAdapter
{
    private static AdapterCollection? s_allAdapters;
    private static Dictionary<Type, UIResourceInfo>? s_uiResources;
    private static Dictionary<Type, AdapterProtocolInfo>? s_adapterProtocols;
    private static Dictionary<Type, AdapterCommandInfo>? s_adapterCommands;
    private static readonly object s_loadLock = new();

    static AdapterCache()
    {
        AdapterCache.AdaptersReloaded += (_, _) =>
        {
            // If root AllAdapters cache is reloaded, clear local caches to force reload on next access
            lock (s_loadLock)
            {
                Interlocked.Exchange(ref s_allAdapters, null);
                Interlocked.Exchange(ref s_uiResources, null);
                Interlocked.Exchange(ref s_adapterProtocols, null);
                Interlocked.Exchange(ref s_adapterCommands, null);
            }
        };
    }

    /// <summary>
    /// Gets all time-series adapters of type <typeparamref name="T"/> in the application directory.
    /// </summary>
    public static Dictionary<Type, AdapterInfo> AllAdapters
    {
        get
        {
            Dictionary<Type, AdapterInfo>? adapters = Interlocked.CompareExchange(ref s_allAdapters, null, null);

            if (adapters is not null)
                return adapters;

            lock (s_loadLock)
            {
                // Check if another thread already loaded the adapter types
                if (s_allAdapters is not null)
                    return s_allAdapters;

                // Dynamic load starting with this generic type first loads all adapters in
                // non-generic root 'AdapterAttributeCache' class, then filters to type 'T'.
                // Lock path with via root class 'AllAdapters' property is:
                //   local lock > root lock (via AllAdapters) > local lock (via AdaptersReloaded event handler)
                // which all happens within the same thread, so no deadlock concerns

                // Filter adapter properties to type 'T'
                s_allAdapters = new AdapterCollection(AdapterCache.AllAdapters.Where(pair => typeof(T).IsAssignableFrom(pair.Key)).ToDictionary());
                s_uiResources = AdapterCache.UIResources.Where(pair => typeof(T).IsAssignableFrom(pair.Key)).ToDictionary();
                s_adapterProtocols = AdapterCache.AdapterProtocols.Where(pair => typeof(T).IsAssignableFrom(pair.Key)).ToDictionary();
                s_adapterCommands = AdapterCache.AdapterCommands.Where(pair => typeof(T).IsAssignableFrom(pair.Key)).ToDictionary();
            }

            return s_allAdapters;
        }
    }

    /// <summary>
    /// Gets all UI resource attribute information for adapters of type <typeparamref name="T"/> in the application directory.
    /// </summary>
    public static Dictionary<Type, UIResourceInfo> UIResources
    {
        get
        {
            Dictionary<Type, UIResourceInfo>? uiResources = Interlocked.CompareExchange(ref s_uiResources, null, null);

            if (uiResources is not null)
                return uiResources;

            lock (s_loadLock)
            {
                // Get list of adapter types, this establishes cache of UI resource attributes
                _ = AllAdapters;
                return s_uiResources!;
            }
        }
    }

    /// <summary>
    /// Gets all adapter protocol attribute information for adapters of type <typeparamref name="T"/> in the application directory.
    /// </summary>
    public static Dictionary<Type, AdapterProtocolInfo> AdapterProtocols
    {
        get
        {
            Dictionary<Type, AdapterProtocolInfo>? adapterProtocols = Interlocked.CompareExchange(ref s_adapterProtocols, null, null);

            if (adapterProtocols is not null)
                return adapterProtocols;

            lock (s_loadLock)
            {
                // Get list of adapter types, this establishes cache of adapter protocol attributes
                _ = AllAdapters;
                return s_adapterProtocols!;
            }
        }
    }

    /// <summary>
    /// Gets all adapter command attribute information for adapters of type <typeparamref name="T"/> in the application directory.
    /// </summary>
    public static Dictionary<Type, AdapterCommandInfo> AdapterCommands
    {
        get
        {
            Dictionary<Type, AdapterCommandInfo>? adapterCommands = Interlocked.CompareExchange(ref s_adapterCommands, null, null);

            if (adapterCommands is not null)
                return adapterCommands;

            lock (s_loadLock)
            {
                // Get list of adapter types, this establishes cache of adapter command attributes
                _ = AllAdapters;
                return s_adapterCommands!;
            }
        }
    }

    /// <summary>
    /// Gets a cache of adapter types keyed by assembly file name and type name, e.g.:
    /// <c>("FileAdapters.dll", "FileAdapters.ProcessLauncher")</c> for faster lookups.
    /// </summary>
    public static Dictionary<(string assemblyFileName, string typeName), Type> AssemblyTypes =>
        AdapterCache.AssemblyTypes; // No need to filter by type 'T'

    /// <summary>
    /// Returns all adapters of type <typeparamref name="T"/> in the application directory filtered by <see cref="EditorBrowsableState"/>.
    /// </summary>
    /// <param name="stateFilter">Filter for <see cref="EditorBrowsableState"/>. Defaults to exclude hidden adapters.</param>
    /// <returns>An <see cref="IEnumerable{AdapterTypeDescription}"/> describing all available adapters of type <typeparamref name="T"/>.</returns>
    public static IEnumerable<AdapterInfo> GetAdapters(Func<EditorBrowsableState, bool>? stateFilter = null)
    {
        return AllAdapters.GetAdapters(stateFilter);
    }
}
