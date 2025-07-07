//******************************************************************************************************
//  MeasurementDictionary.cs - Gbtc
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
//  07/07/2025 - J. Ritchie Carroll
//       Generated original version of source code.
//
//******************************************************************************************************
#if DEBUG
// ReSharper disable PossibleMultipleEnumeration
#endif

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Gemstone.Timeseries;

/// <summary>
/// Represents a measurement dictionary used with a <see cref="Frame"/>.
/// </summary>
public class MeasurementDictionary : IDictionary<MeasurementKey, IMeasurement>, IReadOnlyDictionary<MeasurementKey, IMeasurement>
{
    private readonly ConcurrentDictionary<MeasurementKey, IMeasurement> m_map;

    /// <summary>
    /// Creates a new <see cref="MeasurementDictionary"/>.
    /// </summary>
    public MeasurementDictionary()
    {
        m_map = [];
    }

    /// <summary>
    /// Creates a new <see cref="MeasurementDictionary"/>.
    /// </summary>
    /// <param name="concurrencyLevel">The estimated number of threads that will update the <see cref="MeasurementDictionary"/> concurrently, or -1 to indicate a default value.</param>
    /// <param name="capacity">The initial number of elements that the <see cref="MeasurementDictionary"/> can contain.</param>
    public MeasurementDictionary(int concurrencyLevel, int capacity)
    {
        m_map = new ConcurrentDictionary<MeasurementKey, IMeasurement>(concurrencyLevel, capacity);
    }

    /// <summary>
    /// Creates a new <see cref="MeasurementDictionary"/>.
    /// </summary>
    /// <param name="measurements">The measurements that are copied to the new <see cref="MeasurementDictionary"/>.</param>
    public MeasurementDictionary(IEnumerable<KeyValuePair<MeasurementKey, IMeasurement>> measurements)
    {
        ArgumentNullException.ThrowIfNull(measurements);

    #if DEBUG
        foreach ((MeasurementKey key, var value) in measurements)
        {
            if (value is not null && key != value.Key)
                throw new ArgumentException("MeasurementKey mismatch");
        }
    #endif
        
        m_map = new ConcurrentDictionary<MeasurementKey, IMeasurement>(measurements);
    }

    private IDictionary<MeasurementKey, IMeasurement> Dictionary => m_map;

    /// <inheritdoc cref="IDictionary{TKey,TValue}" />
    public int Count => m_map.Count;

    /// <inheritdoc />
    public bool IsReadOnly => Dictionary.IsReadOnly;

    /// <inheritdoc />
    public ICollection<MeasurementKey> Keys => m_map.Keys;

    /// <inheritdoc />
    public ICollection<IMeasurement> Values => m_map.Values;

    IEnumerable<MeasurementKey> IReadOnlyDictionary<MeasurementKey, IMeasurement>.Keys => Keys;

    IEnumerable<IMeasurement> IReadOnlyDictionary<MeasurementKey, IMeasurement>.Values => Values;

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<MeasurementKey, IMeasurement>> GetEnumerator()
    {
        return m_map.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <inheritdoc />
    public void Add(KeyValuePair<MeasurementKey, IMeasurement> item)
    {
    #if DEBUG
        if (item.Value is not null && item.Key != item.Value.Key)
            throw new ArgumentException("MeasurementKey mismatch");
    #endif

        Dictionary.Add(item);
    }

    /// <inheritdoc />
    public void Clear()
    {
        m_map.Clear();
    }

    /// <inheritdoc />
    public bool Contains(KeyValuePair<MeasurementKey, IMeasurement> item)
    {
        return Dictionary.Contains(item);
    }

    /// <inheritdoc />
    public void CopyTo(KeyValuePair<MeasurementKey, IMeasurement>[] array, int arrayIndex)
    {
        Dictionary.CopyTo(array, arrayIndex);
    }

    /// <inheritdoc />
    public bool Remove(KeyValuePair<MeasurementKey, IMeasurement> item)
    {
        return Dictionary.Remove(item);
    }

    /// <inheritdoc />
    public void Add(MeasurementKey key, IMeasurement value)
    {
    #if DEBUG
        if (value is not null && key != value.Key)
            throw new ArgumentException("MeasurementKey mismatch");
    #endif

        Dictionary.Add(key, value!);
    }

    /// <inheritdoc cref="IDictionary{TKey,TValue}" />
    public bool ContainsKey(MeasurementKey key)
    {
        return m_map.ContainsKey(key);
    }

    /// <inheritdoc />
    public bool Remove(MeasurementKey key)
    {
        return Dictionary.Remove(key);
    }

    /// <inheritdoc cref="IDictionary{TKey,TValue}" />
    public bool TryGetValue(MeasurementKey key, [MaybeNullWhen(false)] out IMeasurement value)
    {
        return m_map.TryGetValue(key, out value);
    }

    /// <inheritdoc cref="IDictionary{TKey,TValue}" />
    public IMeasurement this[MeasurementKey key]
    {
        get => m_map[key];
        set
        {
        #if DEBUG
            if (value is not null && key != value.Key)
                throw new ArgumentException("MeasurementKey mismatch");
        #endif

            m_map[key] = value!;
        }
    }
}
