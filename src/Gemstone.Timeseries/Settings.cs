//******************************************************************************************************
//  Settings.cs - Gbtc
//
//  Copyright © 2024, Grid Protection Alliance.  All Rights Reserved.
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
//  03/07/2024 - J. Ritchie Carroll
//       Generated original version of source code.
//
//******************************************************************************************************

using System;
using System.Globalization;
using Gemstone.Configuration.AppSettings;
using Gemstone.StringExtensions;
using Microsoft.Extensions.Configuration;

namespace Gemstone.Timeseries;

///// <summary>
///// Defines time-series specific system settings for an application.
///// </summary>
//public class Settings : Gemstone.Data.Settings
//{
//    /// <summary>
//    /// Defines the configuration section name for threshold settings.
//    /// </summary>
//    public const string ThresholdSettings = nameof(ThresholdSettings);

//    /// <summary>
//    /// Default value for <see cref="NodeID"/>.
//    /// </summary>
//    public const string DefaultNodeID = "00000000-0000-0000-0000-000000000000";

//    /// <summary>
//    /// Default value for <see cref="CompanyName"/>.
//    /// </summary>
//    public const string DefaultCompanyName = "Grid Protection Alliance";

//    /// <summary>
//    /// Default value for <see cref="CompanyAcronym"/>.
//    /// </summary>
//    public const string DefaultCompanyAcronym = "GPA";

//    /// <summary>
//    /// Default value for <see cref="ProcessStatistics"/>.
//    /// </summary>
//    public const bool DefaultProcessStatistics = true;

//    /// <summary>
//    /// Default value for <see cref="ForwardStatisticsToSnmp"/>.
//    /// </summary>
//    public const bool DefaultForwardStatisticsToSnmp = false;

//    /// <summary>
//    /// Default value for <see cref="MeasurementWarningThreshold"/>.
//    /// </summary>
//    public const int DefaultMeasurementWarningThreshold = 100000;

//    /// <summary>
//    /// Default value for <see cref="MeasurementDumpingThreshold"/>.
//    /// </summary>
//    public const int DefaultMeasurementDumpingThreshold = 500000;

//    /// <summary>
//    /// Default value for <see cref="SampleSizeWarningThreshold"/>.
//    /// </summary>
//    public const int DefaultSampleSizeWarningThreshold = 10;

//    /// <summary>
//    /// Default value for <see cref="MedianTimestampDeviation"/>.
//    /// </summary>
//    public const double DefaultMedianTimestampDeviation = 30.0D;

//    /// <summary>
//    /// Default value for <see cref="OptimizationsConnectionString"/>.
//    /// </summary>
//    public const string DefaultOptimizationsConnectionString = "";

//    /// <summary>
//    /// Gets the configured NodeID for the system.
//    /// </summary>
//    public Guid NodeID { get; set; } = Guid.Parse(DefaultNodeID);

//    /// <summary>
//    /// Gets or sets the company name for the system.
//    /// </summary>
//    public string CompanyName { get; set; } = DefaultCompanyName;

//    /// <summary>
//    /// Gets or sets the company acronym for the system.
//    /// </summary>
//    public string CompanyAcronym { get; set; } = DefaultCompanyAcronym;

//    /// <summary>
//    /// Gets or sets flag that determines if statistics should be processed during operation.
//    /// </summary>
//    public bool ProcessStatistics { get; set; } = DefaultProcessStatistics;

//    /// <summary>
//    /// Gets or sets flag that determines if statistics should be published as SNMP trap messages.
//    /// </summary>
//    public bool ForwardStatisticsToSnmp { get; set; } = DefaultForwardStatisticsToSnmp;

//    /// <summary>
//    /// Gets or sets the number of unarchived measurements allowed in any output adapter queue before displaying a warning message.
//    /// </summary>
//    public int MeasurementWarningThreshold { get; set; } = DefaultMeasurementWarningThreshold;

//    /// <summary>
//    /// Gets or sets the number of unarchived measurements allowed in any output adapter queue before taking evasive action and dumping data.
//    /// </summary>
//    public int MeasurementDumpingThreshold { get; set; } = DefaultMeasurementDumpingThreshold;

//    /// <summary>
//    /// Gets or sets the default number of unpublished samples (in seconds) allowed in any action adapter queue before displaying a warning message.
//    /// </summary>
//    public int SampleSizeWarningThreshold { get; set; } = DefaultSampleSizeWarningThreshold;

//    /// <summary>
//    /// Gets or sets the maximum allowed deviation from median timestamp, in seconds, for consideration in average timestamp calculation.
//    /// </summary>
//    public double MedianTimestampDeviation { get; set; } = DefaultMedianTimestampDeviation;

//    /// <summary>
//    /// Gets the connection string for the various system optimizations.
//    /// </summary>
//    public string OptimizationsConnectionString { get; set; } = DefaultOptimizationsConnectionString;

//    /// <summary>
//    /// Creates a new <see cref="Settings"/> instance.
//    /// </summary>
//    public Settings()
//    {
//    }

//    /// <inheritdoc/>
//    public override void Initialize(IConfiguration configuration)
//    {
//        base.Initialize(configuration);

//        IConfigurationSection systemSettings = Configuration.GetSection(SystemSettings);
//        IConfigurationSection thresholdSettings = Configuration.GetSection(ThresholdSettings);

//        NodeID = Guid.Parse(systemSettings[nameof(NodeID)].ToNonNullNorWhiteSpace(DefaultNodeID));
//        CompanyName = systemSettings[nameof(CompanyName)].ToNonNullNorWhiteSpace(DefaultCompanyName);
//        CompanyAcronym = systemSettings[nameof(CompanyAcronym)].ToNonNullNorWhiteSpace(DefaultCompanyAcronym);
//        ProcessStatistics = systemSettings[nameof(ProcessStatistics)].ParseBoolean();
//        ForwardStatisticsToSnmp = systemSettings[nameof(ForwardStatisticsToSnmp)].ParseBoolean();

//        MeasurementWarningThreshold = int.Parse(thresholdSettings[nameof(MeasurementWarningThreshold)].ToNonNullString(DefaultMeasurementWarningThreshold.ToString()));
//        MeasurementDumpingThreshold = int.Parse(thresholdSettings[nameof(MeasurementDumpingThreshold)].ToNonNullString(DefaultMeasurementDumpingThreshold.ToString()));
//        SampleSizeWarningThreshold = int.Parse(thresholdSettings[nameof(SampleSizeWarningThreshold)].ToNonNullString(DefaultSampleSizeWarningThreshold.ToString()));
//        MedianTimestampDeviation = double.Parse(thresholdSettings[nameof(MedianTimestampDeviation)].ToNonNullString(DefaultMedianTimestampDeviation.ToString(CultureInfo.InvariantCulture)));

//        OptimizationsConnectionString = systemSettings[nameof(OptimizationsConnectionString)] ?? DefaultOptimizationsConnectionString;
//    }

//    /// <inheritdoc/>
//    public override void ConfigureAppSettings(IAppSettingsBuilder builder)
//    {
//        base.ConfigureAppSettings(builder);

//        builder.Add($"{SystemSettings}:{nameof(NodeID)}", DefaultNodeID, "Defines the configured NodeID for the system.");
//        builder.Add($"{SystemSettings}:{nameof(CompanyName)}", DefaultCompanyName, "Defines the company name for the system.");
//        builder.Add($"{SystemSettings}:{nameof(CompanyAcronym)}", DefaultCompanyAcronym, "Defines the company acronym for the system.");
//        builder.Add($"{SystemSettings}:{nameof(ProcessStatistics)}", $"{DefaultProcessStatistics}", "Defines flag that determines if statistics should be processed during operation.");
//        builder.Add($"{SystemSettings}:{nameof(ForwardStatisticsToSnmp)}", $"{DefaultForwardStatisticsToSnmp}", "Defines flag that determines if statistics should be published as SNMP trap messages.");

//        SwitchMappings[$"--{nameof(NodeID)}"] = $"{SystemSettings}:{nameof(NodeID)}";
//        SwitchMappings["-n"] = $"{SystemSettings}:{nameof(NodeID)}";
//        SwitchMappings[$"--{nameof(ProcessStatistics)}"] = $"{SystemSettings}:{nameof(ProcessStatistics)}";
//        SwitchMappings[$"--{nameof(ForwardStatisticsToSnmp)}"] = $"{SystemSettings}:{nameof(ForwardStatisticsToSnmp)}";

//        builder.Add($"{ThresholdSettings}:{nameof(MeasurementWarningThreshold)}", $"{DefaultMeasurementWarningThreshold}", "Defines the number of unarchived measurements allowed in any output adapter queue before displaying a warning message.");
//        builder.Add($"{ThresholdSettings}:{nameof(MeasurementDumpingThreshold)}", $"{DefaultMeasurementDumpingThreshold}", "Defines the number of unarchived measurements allowed in any output adapter queue before taking evasive action and dumping data.");
//        builder.Add($"{ThresholdSettings}:{nameof(SampleSizeWarningThreshold)}", $"{DefaultSampleSizeWarningThreshold}", "Defines the default number of unpublished samples (in seconds) allowed in any action adapter queue before displaying a warning message.");
//        builder.Add($"{ThresholdSettings}:{nameof(MedianTimestampDeviation)}", $"{DefaultMedianTimestampDeviation}", "Defines the maximum allowed deviation from median timestamp, in seconds, for consideration in average timestamp calculation.");

//        SwitchMappings[$"--{nameof(MeasurementWarningThreshold)}"] = $"{ThresholdSettings}:{nameof(MeasurementWarningThreshold)}";
//        SwitchMappings[$"--{nameof(MeasurementDumpingThreshold)}"] = $"{ThresholdSettings}:{nameof(MeasurementDumpingThreshold)}";
//        SwitchMappings[$"--{nameof(SampleSizeWarningThreshold)}"] = $"{ThresholdSettings}:{nameof(SampleSizeWarningThreshold)}";
//        SwitchMappings[$"--{nameof(MedianTimestampDeviation)}"] = $"{ThresholdSettings}:{nameof(MedianTimestampDeviation)}";

//        builder.Add($"{SystemSettings}:{nameof(OptimizationsConnectionString)}", DefaultOptimizationsConnectionString, "Defines connection string for the various system optimizations.");

//        SwitchMappings[$"--{nameof(OptimizationsConnectionString)}"] = $"{SystemSettings}:{nameof(OptimizationsConnectionString)}";
//        SwitchMappings["-opt"] = $"{SystemSettings}:{nameof(OptimizationsConnectionString)}";
//    }

//    /// <summary>
//    /// Gets the default instance of <see cref="Settings"/>.
//    /// </summary>
//    public new static Settings Instance => (Settings)Gemstone.Data.Settings.Instance;
//}
