//******************************************************************************************************
//  UIResourceAttribute.cs - Gbtc
//
//  Copyright © 2024, Grid Protection Alliance.  All Rights Reserved.
//
//  Licensed to the Grid Protection Alliance (GPA) under one or more contributor license agreements. See
//  the NOTICE file distributed with this work for additional information regarding copyright ownership.
//  The GPA licenses this file to you under the MIT License (MIT), the "License"; you may
//  not use this file except in compliance with the License. You may obtain a copy of the License at:
//
//      http://opensource.org/licenses/MIT
//
//  Unless agreed to in writing, the subject software distributed under the License is distributed on an
//  "AS-IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. Refer to the
//  License for the specific language governing permissions and limitations.
//
//  Code Modification History:
//  ----------------------------------------------------------------------------------------------------
//  10/31/2024 - C. Lackner
//       Migrated from GSF.
//  01/08/2025 - J. Ritchie Carroll
//       Restructured as a class target attribute.
//
//******************************************************************************************************

using System;

namespace Gemstone.Timeseries.Adapters
{
    /// <summary>
    /// Marks a class with user interface resources used to display or configure an <see cref="IAdapter"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class UIResourceAttribute : Attribute
    {
        /// <summary>
        /// Gets the assembly name where the UI resource is located.
        /// </summary>
        public string AssemblyName { get; }

        /// <summary>
        /// Gets the name of the UI resource to load.
        /// </summary>
        /// <remarks>
        /// Fully qualified name of the embedded resource, for example.
        /// If root namespace matches <see cref="AssemblyName"/>, can start with ".".
        /// </remarks>
        public string ResourceName { get; }

        /// <summary>
        /// Gets the usage target of the UI resource.
        /// </summary>
        /// <remarks>
        /// Examples include: "EntryFile", "BaseFile" and "ChunkFile". If more complex
        /// use cases exist, e.g., separating configuration from monitoring, prefix with
        /// a category, e.g., "Configuration:EntryFile".
        /// </remarks>
        public string UseTarget { get; }

        /// <summary>
        /// Creates a new <see cref="UIResourceAttribute"/>.
        /// </summary>
        /// <param name="assemblyName">Name of assembly where the UI resource is located.</param>
        /// <param name="resourceName">Name of the UI resource to load.</param>
        /// <param name="useTarget">Usage target of the UI resource.</param>
        public UIResourceAttribute(string assemblyName, string resourceName, string useTarget = "Default")
        {
        #if NET
            ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
            ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
            ArgumentException.ThrowIfNullOrWhiteSpace(useTarget);
        #endif

            if (resourceName.StartsWith('.'))
                resourceName = $"{assemblyName}{resourceName}";

            AssemblyName = assemblyName;
            ResourceName = resourceName;
            UseTarget = useTarget;
        }
    }
}
