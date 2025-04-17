//******************************************************************************************************
//  GlobalSettings.cs - Gbtc
//
//  Copyright © 2021, Grid Protection Alliance.  All Rights Reserved.
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
//  12/01/2021 - J. Ritchie Carroll
//       Generated original version of source code.
//  11/09/2023 - Lillian Gensolin
//       Converted code to .NET core.
//
//******************************************************************************************************

using System;
using Gemstone.Diagnostics;
using ConfigSettings = Gemstone.Configuration.Settings;

namespace Gemstone.Timeseries.Model;

public class GlobalSettings
{
    //public Guid NodeID => Settings.Default.System.NodeID;

    private static string? s_companyAcronym;

    private static string ReadCompanyAcronymFromConfig()
    {
        try
        {
            dynamic section = ConfigSettings.Default[ConfigSettings.SystemSettingsCategory];

            string companyAcronym = section["CompanyAcronym", "GPA", "The acronym representing the company who owns the host system."];

            if (string.IsNullOrWhiteSpace(companyAcronym))
                companyAcronym = "GPA";

            return companyAcronym;
        }
        catch (Exception ex)
        {
            Logger.SwallowException(ex, "Failed to load company acronym from settings");
            return "GPA";
        }
    }


    /// <summary>
    /// Gets the company acronym for the host system.
    /// </summary>
    public string CompanyAcronym => s_companyAcronym ??= ReadCompanyAcronymFromConfig();
}
