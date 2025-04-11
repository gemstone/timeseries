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
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Gemstone.Timeseries.Adapters
{
    /// <summary>
    /// Marks a class with user interface resources used to display or configure an <see cref="IAdapter"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class UIResourceAttribute : Attribute
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
        /// <para>
        /// Defaults to last segment of <see cref="ResourceName"/> if not specified.
        /// For example, for a resource name of "AdapterUI.CSVAdapters.main.js",
        /// the default resource ID would be "main.js".
        /// </para>
        /// <para>
        /// Examples might include: "EntryFile", "BaseFile" and "ChunkFile". If more
        /// complex use cases exist, e.g., separating configuration from monitoring,
        /// prefix with a category, e.g., "Configuration:EntryFile".
        /// </para>
        /// </remarks>
        public string ResourceID { get; }

        /// <summary>
        /// Creates a new <see cref="UIResourceAttribute"/>.
        /// </summary>
        /// <param name="assemblyName">Name of assembly where the UI resource is located.</param>
        /// <param name="resourceName">Name of the UI resource to load.</param>
        /// <param name="resourceID">Usage target of the UI resource.</param>
        public UIResourceAttribute(string assemblyName, string resourceName, string? resourceID = null)
        {
        #if NET
            ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
            ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        #endif

            AssemblyName = assemblyName.Trim();

            resourceName = resourceName.Trim();

            if (resourceName.StartsWith("."))
                resourceName = $"{AssemblyName}{resourceName}";

            ResourceName = resourceName;

            if (string.IsNullOrWhiteSpace(resourceID))
            {
                // Resource name is expected to end in a file extension,
                // so last segment of resource name is next to last dot
                string[] segments = resourceName.Split('.');

                resourceID = segments.Length > 1 ? string.Join(".", segments[^2..]) : resourceName;
            }
            else
            {
                resourceID = resourceID.Trim();
            }

            ResourceID = resourceID;
        }

        /// <summary>
        /// Gets the stream for the resource.
        /// </summary>
        /// <returns>Stream for the resource.</returns>
        public Stream? GetResourceStream()
        {
            return s_assemblyCache.GetOrAdd(AssemblyName, getAssembly)
                .GetManifestResourceStream(ResourceName);

            Assembly getAssembly(string assemblyName)
            {
                // Try to get the already loaded assembly
                Assembly? assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(isMatchingAssembly);

                // If the assembly is not already loaded, load it
                if (assembly == null)
                    assembly = Assembly.Load(assemblyName);

                return assembly;
            }

            bool isMatchingAssembly(Assembly assembly)
            {
                return assembly.GetName().Name?.Equals(AssemblyName, StringComparison.OrdinalIgnoreCase) ?? false;
            }
        }

        private static readonly ConcurrentDictionary<string, Assembly> s_assemblyCache = [];
    }
}
