//******************************************************************************************************
//  Common.cs - Gbtc
//
//  Copyright © 2016, Grid Protection Alliance.  All Rights Reserved.
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
//  11/15/2016 - Ritchie Carroll
//       Generated original version of source code.
//  11/09/2023 - Lillian Gensolin
//       Added UpdateType enum / changed Common class from internal to public (temporary).
//******************************************************************************************************

//using Gemstone.Diagnostics;
using Gemstone.Threading;

namespace Gemstone.Timeseries;

/// <summary>
/// Defines common properties and functions for the time-series library.
/// </summary>
// TODO: Change to internal once UpdateType is moved
public class Common
{
    /// <summary>
    /// Folder name for dynamically compiled assemblies.
    /// </summary>
    public const string DynamicAssembliesFolderName = "DynamicAssemblies";

    // Common use static timer for the Time-Series Library
    public static readonly SharedTimerScheduler TimerScheduler;

    //Static Constructor
    static Common()
    {
        //using Logger.AppendStackMessages("Owner", "Timeseries.Common");
        TimerScheduler = new SharedTimerScheduler();
    }
    /// <summary>
    /// Indicates the type of update.
    /// </summary>
    // TODO: This needs to be moved into Gemstone Common.
    public enum UpdateType
    {
        /// <summary>
        /// Update is informational.
        /// </summary>
        Information,
        /// <summary>
        /// Update is a warning.
        /// </summary>
        Warning,
        /// <summary>
        /// Update is an alarm.
        /// </summary>
        Alarm
    }
}
