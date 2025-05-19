//******************************************************************************************************
//  ConnectionStringParameterAttribute.cs - Gbtc
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
//  12/27/2010 - Stephen C. Wills
//       Generated original version of source code.
//  12/20/2012 - Starlynn Danyelle Gilliam
//       Modified Header.
//  11/09/2023 - Lillian Gensolin
//       Converted code to .NET core.
//
//******************************************************************************************************

using System;

namespace Gemstone.Timeseries.Adapters;

/// <summary>
/// Marks a parameter as being a connection string parameter used to configure an <see cref="IAdapter"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ConnectionStringParameterAttribute : Attribute
{
    /// <summary>
    /// Gets or sets whether this parameter should be shown in the UI.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool IsVisibleToUI { get; set; }

    public ConnectionStringParameterAttribute()
    {
        IsVisibleToUI = true;
    }

    public ConnectionStringParameterAttribute(bool isVisibleToUI)
    {
        IsVisibleToUI = isVisibleToUI;
    }
}
