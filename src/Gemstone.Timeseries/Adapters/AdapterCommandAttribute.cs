//******************************************************************************************************
//  AdapterCommandAttribute.cs - Gbtc
//
//  Copyright © 2012, Grid Protection Alliance.  All Rights Reserved.
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
//  09/02/2010 - J. Ritchie Carroll
//       Generated original version of source code.
//  12/20/2012 - Starlynn Danyelle Gilliam
//       Modified Header.
//  11/09/2023 - Lillian Gensolin
//       Converted code to .NET core.
//
//******************************************************************************************************

using System;
using System.Collections.Generic;
using Gemstone.Security.AccessControl;

namespace Gemstone.Timeseries.Adapters;

/// <summary>
/// Represents an attribute that allows a method in an <see cref="IAdapter"/> class to be exposed as
/// an invokable command.
/// </summary>
/// <remarks>
/// Only public methods will be exposed as invokable.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AdapterCommandAttribute : Attribute
{
    #region [ Members ]

    // Fields
    private readonly ResourceAccessLevel[]? m_allowResources;

    #endregion

    #region [ Constructors ]

    /// <summary>
    /// Creates a new <see cref="AdapterCommandAttribute"/> with the specified <paramref name="description"/> value.
    /// </summary>
    /// <param name="description">Assigns the description for this adapter command.</param>
    public AdapterCommandAttribute(string description)
    {
        Description = description;
    }

    /// <summary>
    /// Creates a new <see cref="AdapterCommandAttribute"/> with the specified <paramref name="description"/> value.
    /// </summary>
    /// <param name="description">Assigns the description for this adapter command.</param>
    /// <param name="allowedResources">Assigns the resources which are allowed to invoke this adapter command.</param>
    public AdapterCommandAttribute(string description, params ResourceAccessLevel[] allowedResources) : this(description)
    {
        m_allowResources = allowedResources;
    }

    /// <summary>
    /// Creates a new <see cref="AdapterCommandAttribute"/> with the specified <paramref name="description"/> value.
    /// </summary>
    /// <param name="description">Assigns the description for this adapter command.</param>
    /// <param name="resourceNames">Assigns the resources, by string name, which are allowed to invoke this adapter command.</param>
    public AdapterCommandAttribute(string description, params string[] resourceNames) : this(description)
    {
        List<ResourceAccessLevel> allowedResources = [];

        foreach (string resourceName in resourceNames)
        {
            if (Enum.TryParse(resourceName, out ResourceAccessLevel resource))
                allowedResources.Add(resource);
            else if (resourceName.Equals("Administrator", StringComparison.OrdinalIgnoreCase))
                allowedResources.Add(ResourceAccessLevel.Admin);
            else
                throw new ArgumentException($"Invalid resource name '{resourceName}' specified for adapter command '{description}'.");
        }

        m_allowResources = allowedResources.ToArray();
    }

    /// <summary>
    /// Creates a new <see cref="AdapterCommandAttribute"/> with the specified <paramref name="description"/> value.
    /// </summary>
    /// <param name="description">Assigns the description for this adapter command.</param>
    /// <param name="includeUI">Assigns the UI accessible flag.</param>
    public AdapterCommandAttribute(string description, bool includeUI) : this(description)
    {
        UIAcessible = includeUI;   
    }

    /// <summary>
    /// Creates a new <see cref="AdapterCommandAttribute"/> with the specified <paramref name="description"/> value.
    /// </summary>
    /// <param name="description">Assigns the description for this adapter command.</param>
    /// <param name="includeUI">Assigns the UI accessible flag.</param>
    /// <param name="allowedResources">Assigns the resources which are allowed to invoke this adapter command.</param>
    public AdapterCommandAttribute(string description, bool includeUI, params ResourceAccessLevel[] allowedResources) : this(description, allowedResources)
    {
        UIAcessible = includeUI;
    }

    #endregion

    #region [ Properties ]

    /// <summary>
    /// Gets the description of this adapter command.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the resources which are allowed to invoke this adapter command.
    /// </summary>
    public ResourceAccessLevel[] AllowedResources => m_allowResources ?? [ResourceAccessLevel.Admin];

    /// <summary>
    /// Gets the flag that indicates if this should be included in the UI.
    /// </summary>
    public bool UIAcessible { get; } = true;
    #endregion
}
