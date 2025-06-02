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

/// <summary>
/// Defines global settings for the openHistorian system.
/// </summary>
public class GlobalSettings
{
    private static string? s_companyAcronym;
    private static string? s_systemName;
    private static double? s_nominalFrequency;

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

    private static string ReadSystemNameFromConfig()
    {
        try
        {
            dynamic section = ConfigSettings.Default[ConfigSettings.SystemSettingsCategory];
            string systemName = section["SystemName", "DEFAULT", "Name of openHistorian hosting system. Used to prefix to system level tags, when defined. Value should follow tag naming conventions, e.g., no spaces and all upper case."];

            if (string.IsNullOrWhiteSpace(systemName))
                systemName = "DEFAULT";
            
            return systemName;
        }
        catch (Exception ex)
        {
            Logger.SwallowException(ex, "Failed to load system name from settings");
            return "DEFAULT";
        }
    }

    private static double ReadNominalFrequencyFromConfig()
    {
        try
        {
            dynamic section = ConfigSettings.Default[ConfigSettings.SystemSettingsCategory];
            double nominalFrequency = section["NominalFrequency", 60.0D, "Nominal frequency of the system in Hertz."];
            
            if (nominalFrequency <= 0.0D)
                nominalFrequency = 60.0D;
            
            return nominalFrequency;
        }
        catch (Exception ex)
        {
            Logger.SwallowException(ex, "Failed to load nominal frequency from settings");
            return 60.0D;
        }
    }

    /// <summary>
    /// Gets the company acronym for the host system.
    /// </summary>
    public string CompanyAcronym => s_companyAcronym ??= ReadCompanyAcronymFromConfig();

    /// <summary>
    /// Gets the system name for the host system.
    /// </summary>
    public string SystemName => s_systemName ??= ReadSystemNameFromConfig();

    /// <summary>
    /// Gets the nominal frequency value used for system operations.
    /// </summary>
    public double NominalFrequency => s_nominalFrequency ??= ReadNominalFrequencyFromConfig();

    /// <summary>
    /// Defines default instance for global settings.
    /// </summary>
    public static readonly GlobalSettings Default = new();
}
