//******************************************************************************************************
//  UserInterfaceResourceAttribute.cs - Gbtc
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
//
//******************************************************************************************************

using System;

namespace Gemstone.Timeseries.Adapters
{
    /// <summary>
    /// Marks a method as being a user interface resource used to display or configure an <see cref="IAdapter"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class UserInterfaceResourceAttribute : Attribute
    {
        /// <summary>
        /// Gets the identifier used to find this resource.
        /// </summary>
        public string ResourceIdentifier { get; }

        public UserInterfaceResourceAttribute(string resourceIdentifier)
        {
            ResourceIdentifier = resourceIdentifier;
        }
    }
}
