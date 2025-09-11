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

    public enum UIVisibility
    {
        /// <summary>
        /// Indicates that this protocol should be hidden from UI
        /// </summary>
        Hidden,
        /// <summary>
        /// Indicates that this protocol is for inputs.
        /// </summary>
        Input,
        /// <summary>
        /// Indicates that this protocol is for outputs.
        /// </summary>
        Output
    }

    /// <summary>
    /// Marks a class as an adapter protocol.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
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
        /// Gets flag that determines if the adapter protocol supports a connection test.
        /// </summary>
        public bool SupportsConnectionTest { get; }

        /// <summary>
        /// Gets the load order of the adapter protocol.
        /// </summary>
        public int LoadOrder { get; }

        public UIVisibility Visibility { get; }

        /// <summary>
        /// Creates a new instance of the <see cref="AdapterProtocolAttribute"/> class.
        /// </summary>
        /// <param name="acronym">Acronym for the adapter protocol.</param>
        /// <param name="name">Name of the adapter protocol.</param>
        /// <param name="type">Type of the adapter protocol.</param>
        /// <param name="visibility">UI Visibility of the protocol.</param>
        /// <param name="supportsConnectionTest">Determines if the adapter protocol supports a connection test.</param>
        /// <param name="loadOrder">Load order of the adapter protocol.</param>
        public AdapterProtocolAttribute(string acronym, string name, ProtocolType type, UIVisibility visibility, bool supportsConnectionTest = true, int loadOrder = 0)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(acronym);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            Acronym = acronym;
            Name = name;
            Type = type;
            Visibility = visibility;
            SupportsConnectionTest = supportsConnectionTest;
            LoadOrder = loadOrder;
        }
    }

    /// <summary>
    /// UI-enabled variant of the adapter protocol attribute.
    /// This attribute inherits from UIResourceAttribute so that it carries UI resource information 
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class UIAdapterProtocolAttribute : UIResourceAttribute
    {
        /// <summary>
        /// Gets the acronym for the adapter protocol.
        /// </summary>
        public string Acronym { get; }

        /// <summary>
        /// Creates a new instance of the <see cref="UIAdapterProtocolAttribute"/> class.
        /// </summary>
        /// <param name="acronym">Acronym for the adapter protocol.</param>
        /// <param name="assemblyName">Name of the assembly where the UI resource is located.</param>
        /// <param name="resourceName">Name of the UI resource (fully qualified embedded resource name).</param>
        public UIAdapterProtocolAttribute(string acronym, string assemblyName, string resourceName) : base(assemblyName, resourceName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(acronym);

            Acronym = acronym;
        }
    }

}
