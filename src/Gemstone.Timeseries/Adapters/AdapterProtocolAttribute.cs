//******************************************************************************************************
//  AdapterProtocolAttribute.cs - Gbtc
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
//  01/03/2025 - J. Ritchie Carroll
//       Generated original version of source code.
//
//******************************************************************************************************

using System;
using System.Text.Json.Serialization;

namespace Gemstone.Timeseries.Adapters
{
    /// <summary>
    /// Protocol type enumeration for adapter protocols.
    /// </summary>
    public enum ProtocolType
    {
        /// <summary>
        /// Indicates a frame-based protocol.
        /// </summary>
        Frame,
        /// <summary>
        /// Indicates a measurement-based protocol.
        /// </summary>
        Measurement
    }

    /// <summary>
    /// Marks a class as an adapter protocol.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class AdapterProtocolAttribute : Attribute
    {
        /// <summary>
        /// Gets the acronym for the adapter protocol.
        /// </summary>
        public string Acronym { get; }

        /// <summary>
        /// Gets the name of the adapter protocol.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the type of the adapter protocol.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ProtocolType Type { get; }

        /// <summary>
        /// Gets the category of the adapter protocol.
        /// </summary>
        /// <remarks>
        /// Common categories include "Phasor", "Gateway", "Device", etc.
        /// </remarks>
        public string Category { get; }

        /// <summary>
        /// Gets the load order of the adapter protocol.
        /// </summary>
        public int LoadOrder { get; }

        /// <summary>
        /// Creates a new instance of the <see cref="AdapterProtocolAttribute"/> class.
        /// </summary>
        /// <param name="acronym">Acronym for the adapter protocol.</param>
        /// <param name="name">Name of the adapter protocol.</param>
        /// <param name="type">Type of the adapter protocol.</param>
        /// <param name="category">Category of the adapter protocol.</param>
        /// <param name="loadOrder">Load order of the adapter protocol.</param>
        public AdapterProtocolAttribute(string acronym, string name, ProtocolType type, string category = "Device", int loadOrder = 0)
        {
        #if NET
            ArgumentException.ThrowIfNullOrWhiteSpace(acronym);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
        #endif

            Acronym = acronym;
            Name = name;
            Type = type;
            Category = category;
            LoadOrder = loadOrder;
        }
    }
}
