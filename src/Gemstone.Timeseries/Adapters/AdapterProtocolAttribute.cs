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
    /// Allows filtering to specific Applications.
    /// </summary>
    public enum Application
    {
        OpenHistorian,
        OpenPDC,
        WaveApps
    }

    /// <summary>
    /// Marks a class as an adapter protocol.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public class AdapterProtocolAttribute : Attribute
    {
        private static readonly Application[] DefaultApplications = new Application[] { Application.OpenHistorian, Application.OpenPDC };

        /// <summary>a 
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

        public Application[] Applications { get; }

        /// <summary>
        /// Device fields locked when the device is external. Empty = nothing locked.
        /// </summary>
        public string[] LockedDeviceFields { get; }

        /// <summary>
        /// Measurement fields locked when the measurement is external. Empty = nothing locked.
        /// </summary>
        public string[] LockedMeasurementFields { get; }

        /// <summary>
        /// Phasor fields locked when the phasor is external. Empty = nothing locked.
        /// </summary>
        public string[] LockedPhasorFields { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="AdapterProtocolAttribute"/> class with the specified parameters.
        /// </summary>
        /// <param name="acronym">The unique acronym representing the adapter protocol.</param>
        /// <param name="name">The display name of the adapter protocol.</param>
        /// <param name="type">The type of the adapter protocol, indicating its purpose (e.g., <see cref="ProtocolType.Frame"/> or <see cref="ProtocolType.Measurement"/>).</param>
        /// <param name="visibility">The visibility of the protocol in the user interface, defined by <see cref="UIVisibility"/>.</param>
        /// <param name="supportsConnectionTest">A value indicating whether the adapter protocol supports connection testing. Defaults to <c>true</c>.</param>
        /// <param name="loadOrder">The load order of the adapter protocol, used to determine initialization sequence. Defaults to <c>0</c>.</param>
        /// <param name="applications">An array of <see cref="Application"/> values specifying the applications that support this protocol in the user interface. Defaults to a predefined set of applications.</param>
        /// <param name="lockedDeviceFields">An array of field names that are locked for device configuration. Defaults to an empty array if not specified.</param>
        /// <param name="lockedMeasurementFields">An array of field names that are locked for measurement configuration. Defaults to an empty array if not specified.</param>
        /// <param name="lockedPhasorFields">An array of field names that are locked for phasor configuration. Defaults to an empty array if not specified.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="acronym"/> or <paramref name="name"/> is <c>null</c>, empty, or consists only of white-space characters.</exception>
        public AdapterProtocolAttribute(
            string acronym,
            string name,
            ProtocolType type,
            UIVisibility visibility,
            bool supportsConnectionTest = true,
            int loadOrder = 0,
            Application[]? applications = null,
            string[]? lockedDeviceFields = null,
            string[]? lockedMeasurementFields = null,
            string[]? lockedPhasorFields = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(acronym);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            Acronym = acronym;
            Name = name;
            Type = type;
            Visibility = visibility;
            SupportsConnectionTest = supportsConnectionTest;
            LoadOrder = loadOrder;
            Applications = applications ?? DefaultApplications;
            LockedDeviceFields = lockedDeviceFields ?? Array.Empty<string>();
            LockedMeasurementFields = lockedMeasurementFields ?? Array.Empty<string>();
            LockedPhasorFields = lockedPhasorFields ?? Array.Empty<string>();
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
