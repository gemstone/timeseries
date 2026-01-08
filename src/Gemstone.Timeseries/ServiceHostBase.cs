//******************************************************************************************************
//  ServiceHostBase.cs - Gbtc
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
//  03/12/2024 - J. Ritchie Carroll
//       Generated original version of source code.
//
//******************************************************************************************************
// ReSharper disable GrammarMistakeInComment
// ReSharper disable CommentTypo

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Gemstone.Caching;
using Gemstone.Configuration;
using Gemstone.Data;
using Gemstone.Data.DataSetExtensions;
using Gemstone.Diagnostics;
using Gemstone.EventHandlerExtensions;
using Gemstone.IO;
using Gemstone.Net.Security;
using Gemstone.Security.Cryptography;
using Gemstone.StringExtensions;
using Gemstone.StringExtensions.StringMatcher;
using Gemstone.Threading;
using Gemstone.Threading.Collections;
using Gemstone.Threading.SynchronizedOperations;
using Gemstone.Timeseries.Adapters;
using Gemstone.Timeseries.Configuration;
using Gemstone.Timeseries.Model;
using Gemstone.Timeseries.Reports;
using Gemstone.Timeseries.Statistics;
using Gemstone.Units;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using IAdapter = Gemstone.Timeseries.Adapters.IAdapter;

namespace Gemstone.Timeseries;

#region [ Enumerations ]

/// <summary>
/// Configuration data source type enumeration.
/// </summary>
public enum ConfigurationType
{
    /// <summary>
    /// Configuration source is a database.
    /// </summary>
    Database,
    /// <summary>
    /// Configuration source is a web-service.
    /// </summary>
    WebService,
    /// <summary>
    /// Configuration source is a binary file.
    /// </summary>
    BinaryFile,
    /// <summary>
    /// Configuration source is a XML file.
    /// </summary>
    XmlFile
}

#endregion

/// <summary>
/// Represents the time-series framework service host.
/// </summary>
public abstract class ServiceHostBase : BackgroundService, IDefineSettings
{
    #region [ Events ]

    /// <summary>
    /// Provides status messages to consumer.
    /// </summary>
    /// <remarks>
    /// <see cref="EventArgs{T}.Argument"/> is new status message.
    /// </remarks>
    public event EventHandler<EventArgs<UILogMessage>>? StatusMessage;

    #endregion

    #region [ Members ]

    // Constants
    private const int DefaultConfigurationBackups = 5;
    private const int MaxLogMessageQueueSize = 1000;

    // Fields
    private IaonSession? m_iaonSession;
    private IConfigurationLoader m_configurationLoader = default!;
    private BinaryFileConfigurationLoader m_binaryCacheConfigurationLoader = default!;
    private XMLConfigurationLoader m_xmlCacheConfigurationLoader = default!;
    private string m_cachedXmlConfigurationFile = default!;
    private string m_cachedBinaryConfigurationFile = default!;
    private int m_configurationBackups;
    //private bool m_uniqueAdapterIDs;
    //private bool m_allowRemoteRestart;
    private bool m_preferCachedConfiguration;
    private MultipleDestinationExporter? m_healthExporter;
    private MultipleDestinationExporter? m_statusExporter;
    private ReportingProcessCollection? m_reportingProcesses;
    private ProcessQueue<Tuple<string, Action<bool>>>? m_reloadConfigQueue;
    private LongSynchronizedOperation m_configurationCacheOperation = default!;
    private volatile DataSet? m_latestConfiguration;
    private RunTimeLog? m_runTimeLog;
    private readonly ILogger m_logger;

    private double? m_maxFilteredStatusMessageCacheRange;
    private readonly ConcurrentDictionary<string, List<UILogMessage>> m_filteredStatusMessages;
    private readonly Queue<UILogMessage> m_statusLog;
    private readonly Regex m_logSourceMatcher;
    private readonly object m_logLock;
    private uint m_logMessageIndex;

    //private ServiceHelper m_serviceHelper;
    //private ServerBase m_remotingServer;

    private bool m_disposed;

    #endregion

    #region [ Constructors ]

    /// <summary>
    /// Creates a new <see cref="ServiceHostBase"/>.
    /// </summary>
    protected ServiceHostBase(ILogger logger)
    {
        m_logger = logger;
        m_filteredStatusMessages = new();
        m_statusLog = new(MaxLogMessageQueueSize);
        m_logLock = new object();
        m_logMessageIndex = 0;

        m_logSourceMatcher = new Regex(@"^\[(?<Source>[^\]]+)\]\s*(?:\[(?<SecondSource>[^\]]+)\])?.+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        //s_serviceHost = new WeakReference<ServiceHostBase>(this);
    }

    #endregion

    #region [ Properties ]

    /// <summary>
    /// Gets the related remote console application name.
    /// </summary>
    public virtual string ConsoleApplicationName => $"{ServiceName}Console.exe";

    ///// <summary>
    ///// Gets access to the <see cref="GSF.ServiceProcess.ServiceHelper"/>.
    ///// </summary>
    //public ServiceHelper ServiceHelper => m_serviceHelper;

    ///// <summary>
    ///// Gets reference to the <see cref="TcpServer"/> based remoting server.
    ///// </summary>
    //public ServerBase RemotingServer => m_remotingServer;

    /// <summary>
    /// Gets reference to the <see cref="AllAdaptersCollection"/>.
    /// </summary>
    public AllAdaptersCollection? AllAdapters => m_iaonSession?.AllAdapters;

    /// <summary>
    /// Gets reference to the <see cref="InputAdapterCollection"/>.
    /// </summary>
    public InputAdapterCollection? InputAdapters => m_iaonSession?.InputAdapters;

    /// <summary>
    /// Gets reference to the <see cref="ActionAdapterCollection"/>.
    /// </summary>
    public ActionAdapterCollection? ActionAdapters => m_iaonSession?.ActionAdapters;

    /// <summary>
    /// Gets reference to the <see cref="OutputAdapterCollection"/>.
    /// </summary>
    public OutputAdapterCollection? OutputAdapters => m_iaonSession?.OutputAdapters;

    /// <summary>
    /// Gets reference to the <see cref="FilterAdapterCollection"/>.
    /// </summary>
    public FilterAdapterCollection? FilterAdapters => m_iaonSession?.FilterAdapters;

    /// <summary>
    /// Gets the currently loaded system configuration <see cref="DataSet"/>.
    /// </summary>
    public DataSet? DataSource => m_iaonSession?.DataSource;

    /// <summary>
    /// Gets the defined system <see cref="Timeseries.ConfigurationType"/>.
    /// </summary>
    public ConfigurationType ConfigurationType { get; private set; }

    #endregion

    #region [ Methods ]

    #region [ Service Event Handlers ]

    /// <inheritdoc />
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await base.StartAsync(cancellationToken);

            GenerateLocalCertificate();

            SetupTempPath();

            InitializeServiceHelper();

            //// Register service level event handlers
            //m_serviceHelper.ServiceStarting += ServiceStartingHandler;
            //m_serviceHelper.ServiceStarted += ServiceStartedHandler;
            //m_serviceHelper.ServiceStopping += ServiceStoppingHandler;
            //m_serviceHelper.UpdatedStatus += UpdatedStatusHandler;
            //m_serviceHelper.LoggedException += LoggedExceptionHandler;

            //if (m_serviceHelper.StatusLog is not null)
            //    m_serviceHelper.StatusLog.LogException += StatusLogExceptionHandler;

            //if (m_serviceHelper.ErrorLogger?.ErrorLog is not null)
            //    m_serviceHelper.ErrorLogger.ErrorLog.LogException += ErrorLogExceptionHandler;

            //if (m_serviceHelper.ConnectionErrorLogger?.ErrorLog is not null)
            //    m_serviceHelper.ConnectionErrorLogger.ErrorLog.LogException += ConnectionErrorLogExceptionHandler;

            //m_serviceHelper.OnStart(args);
        }
        catch (Exception ex)
        {
            LogException(ex);
        }

        ShutdownHandler.Initialize();

        // Define a line of asterisks for emphasis
        string stars = new('*', 79);

        // On Windows platforms, get product name from registry
        string productName = Environment.OSVersion.Platform == PlatformID.Win32NT ?
            $"    Product Name: {Registry.GetValue("HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion", "ProductName", null).ToNonNullString("<Unavailable>")}{Environment.NewLine}" : "";

        string servicePack = string.IsNullOrWhiteSpace(Environment.OSVersion.ServicePack) ? "" :
            $"    Service Pack: {Environment.OSVersion.ServicePack}{Environment.NewLine}";

        // Log startup information
        m_logger.LogInformation(
              $"{Environment.NewLine}{stars}{Environment.NewLine}" +
              $"     System Time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC{Environment.NewLine}" +
              $"    Current Path: {FilePath.TrimFileName(FilePath.RemovePathSuffix(FilePath.GetAbsolutePath("")), 61)}{Environment.NewLine}" +
              $"    Machine Name: {Environment.MachineName}{Environment.NewLine}" +
              $"      OS Version: {Environment.OSVersion.VersionString}{Environment.NewLine}" +
              productName +
              servicePack +
              $"  Working Memory: {SI2.ToScaledString(Environment.WorkingSet, 3, "B", SI2.IECSymbols)}{Environment.NewLine}" +
              $"  Execution Mode: {IntPtr.Size * 8}-bit{Environment.NewLine}" +
              $"      Processors: {Environment.ProcessorCount}{Environment.NewLine}" +
              $"  GC Server Mode: {GCSettings.IsServerGC}{Environment.NewLine}" +
              $" GC Latency Mode: {GCSettings.LatencyMode}{Environment.NewLine}" +
              $" Process Account: {Environment.UserDomainName}\\{Environment.UserName}{Environment.NewLine}" +
              $"{stars}{Environment.NewLine}");

        // Define a run-time log
        m_runTimeLog = new RunTimeLog();
        m_runTimeLog.FileName = "RunTimeLog.txt";
        m_runTimeLog.ProcessException += ProcessExceptionHandler;
        m_runTimeLog.Initialize();

        // Use this run-time log for system up-time calculations
        PerformanceStatistics.SystemRunTimeLog = m_runTimeLog;

        dynamic section = Settings.Default[Settings.SystemSettingsCategory];

        // Initialize Iaon session
        m_iaonSession = new IaonSession();
        m_iaonSession.StatusMessage += StatusMessageHandler;
        m_iaonSession.ProcessException += ProcessExceptionHandler;
        m_iaonSession.ConfigurationChanged += ConfigurationChangedHandler;

        // Attempt to set default culture
        try
        {
            string defaultCulture = section.DefaultCulture;
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CreateSpecificCulture(defaultCulture);     // Defaults for date formatting, etc.
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CreateSpecificCulture(defaultCulture);   // Culture for resource strings, etc.
        }
        catch (Exception ex)
        {
            DisplayStatusMessage("Failed to set default culture due to exception, defaulting to \"{1}\": {0}", MessageLevel.Error, ex.Message, CultureInfo.CurrentCulture.Name.ToNonNullNorEmptyString("Undetermined"));
            LogException(ex);
        }

        // Retrieve application paths as defined in the config file
        string servicePath = FilePath.GetAbsolutePath("");

        // Retrieve configuration cache directory as defined in the config file
        string cachePath = FilePath.GetAbsolutePath(section.ConfigurationCachePath);

        // Make sure configuration cache directory exists
        try
        {
            if (!Directory.Exists(cachePath))
                Directory.CreateDirectory(cachePath);
        }
        catch (Exception ex)
        {
            DisplayStatusMessage("Failed to create configuration cache directory \"{0}\" due to exception: {1}", MessageLevel.Error, cachePath, ex.Message);
            LogException(ex);
        }

        try
        {
            Directory.SetCurrentDirectory(servicePath);
        }
        catch (Exception ex)
        {
            DisplayStatusMessage("Failed to set current directory to execution path \"{0}\" due to exception: {1}", MessageLevel.Error, servicePath, ex.Message);
            LogException(ex);
        }

        // Initialize system settings
        ConfigurationType = section.ConfigurationType;
        m_cachedXmlConfigurationFile = FilePath.AddPathSuffix(cachePath) + section.CachedConfigurationFile;
        m_cachedBinaryConfigurationFile = $"{FilePath.AddPathSuffix(cachePath)}{FilePath.GetFileNameWithoutExtension(m_cachedXmlConfigurationFile)}.bin";
        m_configurationBackups = section.ConfigurationBackups;
        //m_uniqueAdapterIDs = section.UniqueAdaptersIDs;
        //m_allowRemoteRestart = section.AllowRemoteRestart;
        m_preferCachedConfiguration = section.PreferCachedConfiguration;

        m_reloadConfigQueue = ProcessQueue<Tuple<string, Action<bool>>>.CreateSynchronousQueue(ExecuteReloadConfig, 500.0D, Timeout.Infinite, false, false);
        m_reloadConfigQueue.ProcessException += m_iaonSession.ProcessExceptionHandler;

        m_configurationCacheOperation = new LongSynchronizedOperation(ExecuteConfigurationCache)
        {
            IsBackground = true
        };

        // Get configuration settings for thread pool size
        int targetMinThreadPoolSize = section.MinThreadPoolWorkerThreads; //Math.Max(section.MinThreadPoolWorkerThreads, short.MaxValue);
        int targetMaxThreadPoolSize = section.MaxThreadPoolWorkerThreads; //Math.Max(section.MaxThreadPoolWorkerThreads, short.MaxValue * 20);
        int targetMinIOPortThreads = section.MinThreadPoolIOPortThreads;  //Math.Max(section.MinThreadPoolIOPortThreads, Environment.ProcessorCount);
        int targetMaxIOPortThreads = section.MaxThreadPoolIOPortThreads;  //Math.Max(section.MaxThreadPoolIOPortThreads, Environment.ProcessorCount * 20);

        // If all thread pool sizes are set to -1, then do not change system defaults
        if (targetMinThreadPoolSize > -1 || targetMaxThreadPoolSize > -1 || targetMinIOPortThreads > -1 || targetMaxIOPortThreads > -1)
        {
            ThreadPool.GetMinThreads(out int currentMinThreadPoolSize, out int currentMinIOPortThreads);
            ThreadPool.GetMaxThreads(out int currentMaxThreadPoolSize, out int currentMaxIOPortThreads);

            // Use current thread pool sizes for target sizes that are set to -1
            if (targetMinThreadPoolSize < 0)
                targetMinThreadPoolSize = currentMinThreadPoolSize;

            if (targetMinIOPortThreads < 0)
                targetMinIOPortThreads = currentMinIOPortThreads;

            if (targetMaxThreadPoolSize < 0)
                targetMaxThreadPoolSize = currentMaxThreadPoolSize;

            if (targetMaxIOPortThreads < 0)
                targetMaxIOPortThreads = currentMaxIOPortThreads;

            // Min threads needs to be between 0 and maximum threads
            // Max threads needs to be at least as large as both min threads and Environment.ProcessorCount
            if (targetMaxThreadPoolSize < Environment.ProcessorCount)
                targetMaxThreadPoolSize = Environment.ProcessorCount;

            if (targetMaxIOPortThreads < Environment.ProcessorCount)
                targetMaxIOPortThreads = Environment.ProcessorCount;

            if (targetMinThreadPoolSize > targetMaxThreadPoolSize)
                targetMinThreadPoolSize = targetMaxThreadPoolSize;

            if (targetMinIOPortThreads > targetMaxIOPortThreads)
                targetMinIOPortThreads = targetMaxIOPortThreads;

            // Update max before min to avoid constraint violations
            bool success = ThreadPool.SetMaxThreads(targetMaxThreadPoolSize, targetMaxIOPortThreads);

            if (!success)
            {
                string message = $"Failed to set maximum thread pool size to {targetMaxThreadPoolSize} worker threads and {targetMaxIOPortThreads} I/O port threads.";
                DisplayStatusMessage(message, MessageLevel.Error);
                LogException(new InvalidOperationException(message));
            }

            success = ThreadPool.SetMinThreads(targetMinThreadPoolSize, targetMinIOPortThreads);

            if (!success)
            {
                string message = $"Failed to set minimum thread pool size to {targetMinThreadPoolSize} worker threads and {targetMinIOPortThreads} I/O port threads.";
                DisplayStatusMessage(message, MessageLevel.Error);
                LogException(new InvalidOperationException(message));
            }
        }

        // Set up the configuration loader
        m_configurationLoader = ConfigurationType switch
        {
            ConfigurationType.Database => new DatabaseConfigurationLoader { ConnectionString = section.ConnectionString, DataProviderString = section.DataProviderString },
            ConfigurationType.WebService => new WebServiceConfigurationLoader { URI = section.ConnectionString },
            ConfigurationType.BinaryFile => new BinaryFileConfigurationLoader { FilePath = section.ConnectionString },
            ConfigurationType.XmlFile => new XMLConfigurationLoader { FilePath = section.ConnectionString },
            _ => m_configurationLoader
        };

        m_binaryCacheConfigurationLoader = new BinaryFileConfigurationLoader
        {
            FilePath = m_cachedBinaryConfigurationFile
        };

        m_xmlCacheConfigurationLoader = new XMLConfigurationLoader
        {
            FilePath = m_cachedXmlConfigurationFile
        };

        m_configurationLoader.StatusMessage += (_, args) => DisplayStatusMessage(args.Argument, MessageLevel.Info);
        m_binaryCacheConfigurationLoader.StatusMessage += (_, args) => DisplayStatusMessage(args.Argument, MessageLevel.Info);
        m_xmlCacheConfigurationLoader.StatusMessage += (_, args) => DisplayStatusMessage(args.Argument, MessageLevel.Info);

        m_configurationLoader.ProcessException += ConfigurationLoader_ProcessException;
        m_binaryCacheConfigurationLoader.ProcessException += ConfigurationLoader_ProcessException;
        m_xmlCacheConfigurationLoader.ProcessException += ConfigurationLoader_ProcessException;

        m_reloadConfigQueue.Start();

        try
        {
            // Attempt to assign desired process priority. Note that process will require SeIncreaseBasePriorityPrivilege or 
            // Administrative privileges to make this change
            Process.GetCurrentProcess().PriorityClass = section.ProcessPriority;
        }
        catch (Exception ex)
        {
            LogException(ex);
        }

        m_logger.LogInformation("Service started.");

        //// Add run-time log as a service component
        //m_serviceHelper.ServiceComponents.Add(m_runTimeLog);

        // Create health exporter
        m_healthExporter = new MultipleDestinationExporter("HealthExporter");
        m_healthExporter.Initialize([new ExportDestination(FilePath.GetAbsolutePath("Health.txt"), false)]);
        m_healthExporter.StatusMessage += m_iaonSession!.StatusMessageHandler;
        m_healthExporter.ProcessException += m_iaonSession.ProcessExceptionHandler;
        //m_serviceHelper.ServiceComponents.Add(m_healthExporter);

        // Create status exporter
        m_statusExporter = new MultipleDestinationExporter("StatusExporter");
        m_statusExporter.Initialize([new ExportDestination(FilePath.GetAbsolutePath("Status.txt"), false)]);
        m_statusExporter.StatusMessage += m_iaonSession.StatusMessageHandler;
        m_statusExporter.ProcessException += m_iaonSession.ProcessExceptionHandler;
        //m_serviceHelper.ServiceComponents.Add(m_statusExporter);

        // Create reporting processes
        m_reportingProcesses = [];
        //m_serviceHelper.ServiceComponents.Add(m_reportingProcesses);

        m_reportingProcesses.LoadImplementations(_ =>
        {
            try
            {
                //if (m_serviceHelper.IsDisposed)
                //    return;

                // Define processes for each report (initially unscheduled)
                //m_serviceHelper.AddProcess(ReportingProcessHandler, $"{reportingProcess.ReportType}Reporting");
            }
            catch (Exception ex)
            {
                LogException(ex);
            }
        },
        ex =>
        {
            DisplayStatusMessage("Failed to load reporting process: {0}", MessageLevel.Warning, ex.Message);
            LogException(ex);
        });

        //// Define scheduled service processes
        //m_serviceHelper.AddScheduledProcess(HealthMonitorProcessHandler, "HealthMonitor", "* * * * *");    // Every minute
        //m_serviceHelper.AddScheduledProcess(StatusExportProcessHandler, "StatusExport", "*/30 * * * *");   // Every 30 minutes

        //// Add key Iaon collections as service components
        //m_serviceHelper.ServiceComponents.Add(m_iaonSession.InputAdapters);
        //m_serviceHelper.ServiceComponents.Add(m_iaonSession.ActionAdapters);
        //m_serviceHelper.ServiceComponents.Add(m_iaonSession.OutputAdapters);
        //m_serviceHelper.ServiceComponents.Add(m_iaonSession.FilterAdapters);

        //// Define remote client requests (i.e., console commands)
        //m_serviceHelper.ClientRequestHandlers.Add(new ClientRequestHandler("List", "Displays status for specified adapter or collection", ListRequestHandler, new[] { "ls", "dir" }));
        //m_serviceHelper.ClientRequestHandlers.Add(new ClientRequestHandler("Connect", "Connects (or starts) specified adapter", StartRequestHandler));
        //m_serviceHelper.ClientRequestHandlers.Add(new ClientRequestHandler("Disconnect", "Disconnects (or stops) specified adapter", StopRequestHandler));
        //m_serviceHelper.ClientRequestHandlers.Add(new ClientRequestHandler("Invoke", "Invokes a command for specified adapter", InvokeRequestHandler));
        //m_serviceHelper.ClientRequestHandlers.Add(new ClientRequestHandler("ListCommands", "Displays possible commands for specified adapter", ListCommandsRequestHandler));
        //m_serviceHelper.ClientRequestHandlers.Add(new ClientRequestHandler("Initialize", "Initializes specified adapter or collection", InitializeRequestHandler, new[] { "init" }));
        //m_serviceHelper.ClientRequestHandlers.Add(new ClientRequestHandler("ReloadConfig", "Manually reloads the system configuration", ReloadConfigRequestHandler));
        //m_serviceHelper.ClientRequestHandlers.Add(new ClientRequestHandler("UpdateConfigFile", "Updates an option in the configuration file", UpdateConfigFileRequestHandler));
        //m_serviceHelper.ClientRequestHandlers.Add(new ClientRequestHandler("ManageCertificate", "Manages the certificate used by the service", ManageCertificateHandler));
        //m_serviceHelper.ClientRequestHandlers.Add(new ClientRequestHandler("Authenticate", "Authenticates network shares for health and status exports", AuthenticateRequestHandler, new[] { "auth" }));
        //m_serviceHelper.ClientRequestHandlers.Add(new ClientRequestHandler("Restart", "Attempts to restart the host service", RestartServiceHandler));
        //m_serviceHelper.ClientRequestHandlers.Add(new ClientRequestHandler("RefreshRoutes", "Spawns request to recalculate routing tables", RefreshRoutesRequestHandler));
        //m_serviceHelper.ClientRequestHandlers.Add(new ClientRequestHandler("TemporalSupport", "Determines if any adapters support temporal processing", TemporalSupportRequestHandler));
        //m_serviceHelper.ClientRequestHandlers.Add(new ClientRequestHandler("ListReports", "Lists the available reports that have been generated", ListReportsRequestHandler));
        //m_serviceHelper.ClientRequestHandlers.Add(new ClientRequestHandler("GetReport", "Gets a previously generated report for the specified date", GetReportRequestHandler));
        //m_serviceHelper.ClientRequestHandlers.Add(new ClientRequestHandler("GetReportStatus", "Gets a reporting process status", GetReportStatusRequestHandler));
        //m_serviceHelper.ClientRequestHandlers.Add(new ClientRequestHandler("GenerateReport", "Generates a report for the specified date", GenerateReportRequestHandler));
        //m_serviceHelper.ClientRequestHandlers.Add(new ClientRequestHandler("ReportingConfig", "Displays or modifies the configuration of the reporting process", ReportingConfigRequestHandler, false));
        //m_serviceHelper.ClientRequestHandlers.Add(new ClientRequestHandler("LogEvent", "Logs remote event log entries.", LogEventRequestHandler, false));
        //m_serviceHelper.ClientRequestHandlers.Add(new ClientRequestHandler("Ping", "Shells out a process to perform a ping on the device.", PingRequestHandler));
        //m_serviceHelper.ClientRequestHandlers.Add(new ClientRequestHandler("GetInputMeasurements", "Gets adapter input measurements.", GetInputMeasurementsRequestHandler, false));
        //m_serviceHelper.ClientRequestHandlers.Add(new ClientRequestHandler("GetOutputMeasurements", "Gets adapter output measurements.", GetOutputMeasurementsRequestHandler, false));

        // Start system initialization on an independent thread so that service responds in a timely fashion...
        InitializeSystem();
    }

    /// <summary>
    /// Service stopping event handler.
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            m_logger.LogInformation("Service stopped.");
        }
        finally
        {
            await base.StopAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Releases all the resources used by the <see cref="ServiceHostBase"/> object.
    /// </summary>
    public sealed override void Dispose()
    {
        try
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        finally
        {
            base.Dispose();
        }
    }

    /// <summary>
    /// Releases the unmanaged resources used by the <see cref="ServiceHostBase"/> object and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (m_disposed)
            return;

        try
        {
            if (!disposing)
                return;

            // Dispose system health exporter
            if (m_healthExporter is not null)
            {
                m_healthExporter.Enabled = false;
                //m_serviceHelper.ServiceComponents.Remove(m_healthExporter);
                m_healthExporter.Dispose();

                if (m_iaonSession is not null)
                {
                    m_healthExporter.StatusMessage -= m_iaonSession.StatusMessageHandler;
                    m_healthExporter.ProcessException -= m_iaonSession.ProcessExceptionHandler;
                }
                m_healthExporter = null;
            }

            // Dispose system status exporter
            if (m_statusExporter is not null)
            {
                m_statusExporter.Enabled = false;
                //m_serviceHelper.ServiceComponents.Remove(m_statusExporter);
                m_statusExporter.Dispose();

                if (m_iaonSession is not null)
                {
                    m_statusExporter.StatusMessage -= m_iaonSession.StatusMessageHandler;
                    m_statusExporter.ProcessException -= m_iaonSession.ProcessExceptionHandler;
                }
                m_statusExporter = null;
            }

            // Dispose reload config queue
            if (m_reloadConfigQueue is not null)
            {
                if (m_iaonSession is not null)
                {
                    m_reloadConfigQueue.ProcessException -= m_iaonSession.ProcessExceptionHandler;
                }
                m_reloadConfigQueue.Dispose();
                m_reloadConfigQueue = null;
            }

            // Dispose reporting processes
            if (m_reportingProcesses is not null)
            {
                //m_serviceHelper.ServiceComponents.Remove(m_reportingProcesses);
                m_reportingProcesses = null;
            }

            // Dispose Iaon session
            if (m_iaonSession is not null)
            {
                m_iaonSession.Dispose();
                m_iaonSession.StatusMessage -= StatusMessageHandler;
                m_iaonSession.ProcessException -= ProcessExceptionHandler;
                m_iaonSession.ConfigurationChanged -= ConfigurationChangedHandler;
                m_iaonSession = null;
            }

            // Dispose of run-time log
            if (m_runTimeLog is not null)
            {
                //m_serviceHelper.ServiceComponents.Remove(m_runTimeLog);
                m_runTimeLog.ProcessException -= ProcessExceptionHandler;
                m_runTimeLog.Dispose();
                m_runTimeLog = null;
            }

            // Dispose Filtered Status Messages
            m_filteredStatusMessages.Clear();

            //m_serviceHelper.ServiceStarting -= ServiceStartingHandler;
            //m_serviceHelper.ServiceStarted -= ServiceStartedHandler;
            //m_serviceHelper.ServiceStopping -= ServiceStoppingHandler;
            //m_serviceHelper.UpdatedStatus -= UpdatedStatusHandler;
            //m_serviceHelper.LoggedException -= LoggedExceptionHandler;

            //if (m_serviceHelper.StatusLog is not null)
            //{
            //    m_serviceHelper.StatusLog.Flush();
            //    m_serviceHelper.StatusLog.LogException -= StatusLogExceptionHandler;
            //}

            //if (m_serviceHelper.ErrorLogger?.ErrorLog is not null)
            //{
            //    m_serviceHelper.ErrorLogger.ErrorLog.Flush();
            //    m_serviceHelper.ErrorLogger.ErrorLog.LogException -= ErrorLogExceptionHandler;
            //}

            //if (m_serviceHelper.ConnectionErrorLogger?.ErrorLog is not null)
            //{
            //    m_serviceHelper.ConnectionErrorLogger.ErrorLog.Flush();
            //    m_serviceHelper.ConnectionErrorLogger.ErrorLog.LogException -= ConnectionErrorLogExceptionHandler;
            //}

            //if (components is not null)
            //{
            //    components.Dispose();
            //    components = null;
            //}

            //if (m_serviceHelper is not null)
            //{
            //    m_serviceHelper.Dispose();
            //    m_serviceHelper = null;
            //}

            //if (m_remotingServer is not null)
            //{
            //    m_remotingServer.Dispose();
            //    m_remotingServer = null;
            //}
        }
        finally
        {
            m_disposed = true;  // Prevent duplicate dispose.
        }
    }

    #endregion

    #region [ System Initialization ]

    private void GenerateLocalCertificate()
    {
        CertificateGenerator? certificateGenerator = null;

        if (string.IsNullOrWhiteSpace(ServiceName))
            throw new InvalidOperationException("EstablishServiceProperties must be overridden and ServiceName must be set");

        try
        {
            X509Certificate2? certificate = null;
            dynamic section = Settings.Default[Settings.SystemSettingsCategory];
            string certificatePath = FilePath.GetAbsolutePath(section.LocalCertificate);

            certificateGenerator = new CertificateGenerator()
            {
                Issuer = ServiceName,
                CertificatePath = certificatePath,
                ValidYears = 20
            };

            if (File.Exists(certificatePath))
                certificate = new X509Certificate2(certificatePath);

            if (Equals(certificate, certificateGenerator.GenerateCertificate()))
                return;

            string message = new StringBuilder()
                .AppendLine($"Created self-signed certificate for service: \"{certificatePath}\"")
                .AppendLine()
                .AppendLine("Certificate Generation Log:")
                .AppendLine(string.Join(Environment.NewLine, certificateGenerator.DebugLog))
                .ToString();

            TryWriteToEventLog(message, EventLogEntryType.Information);
        }
        catch (Exception ex)
        {
            string message = new StringBuilder()
                .AppendLine(ex.ToString())
                .AppendLine()
                .AppendLine("Certificate Generation Log:")
                .AppendLine(string.Join(Environment.NewLine, certificateGenerator?.DebugLog ?? []))
                .ToString();

            TryWriteToEventLog(message, EventLogEntryType.Error);
        }
    }

    private void SetupTempPath()
    {
        string? assemblyDirectory = null;

        try
        {
            // Setup custom temp folder for services so that dynamically compiled razor assemblies can be easily managed
            assemblyDirectory = FilePath.GetAbsolutePath(Common.DynamicAssembliesFolderName);

            if (!Directory.Exists(assemblyDirectory))
                Directory.CreateDirectory(assemblyDirectory);

            Environment.SetEnvironmentVariable("TEMP", assemblyDirectory);
            Environment.SetEnvironmentVariable("TMP", assemblyDirectory);
        }
        catch (Exception ex)
        {
            // This is not catastrophic
            Logger.SwallowException(ex, $"Failed to assign temp folder location for \"{ServiceName}\" service to: {assemblyDirectory}");
        }
    }

    /// <summary>
    /// Initializes the service helper.
    /// </summary>
    public void InitializeServiceHelper()
    {
        //CategorizedSettingsElementCollection remotingServerSettings = ConfigurationFile.Current.Settings["remotingServer"];

        //m_serviceHelper = new ServiceHelper();

        //if (remotingServerSettings.Cast<CategorizedSettingsElement>().Any(element => element.Name.Equals("EnabledSslProtocols", StringComparison.OrdinalIgnoreCase) && !element.Value.Equals("None", StringComparison.OrdinalIgnoreCase)))
        //    m_remotingServer = InitializeTlsServer();
        //else
        //    m_remotingServer = InitializeTcpServer();

        //m_serviceHelper.ErrorLogger.ErrorLog.FileName = "ErrorLog.txt";
        //m_serviceHelper.ErrorLogger.ErrorLog.PersistSettings = true;
        //m_serviceHelper.ErrorLogger.ErrorLog.SettingsCategory = "ErrorLog";
        //m_serviceHelper.ErrorLogger.ErrorLog.Initialize();

        //m_serviceHelper.ErrorLogger.LogToEventLog = false;
        //m_serviceHelper.ErrorLogger.PersistSettings = true;
        //m_serviceHelper.ErrorLogger.SmtpServer = "";
        //m_serviceHelper.ErrorLogger.Initialize();

        //m_serviceHelper.ConnectionErrorLogger.ErrorLog.FileName = "ConnectionErrorLog.txt";
        //m_serviceHelper.ConnectionErrorLogger.ErrorLog.PersistSettings = true;
        //m_serviceHelper.ConnectionErrorLogger.ErrorLog.SettingsCategory = "ConnectionErrorLog";
        //m_serviceHelper.ConnectionErrorLogger.ErrorLog.Initialize();

        //m_serviceHelper.ConnectionErrorLogger.LogToEventLog = false;
        //m_serviceHelper.ConnectionErrorLogger.PersistSettings = true;
        //m_serviceHelper.ConnectionErrorLogger.SmtpServer = "";
        //m_serviceHelper.ConnectionErrorLogger.Initialize();

        //m_serviceHelper.ProcessScheduler.PersistSettings = true;
        //m_serviceHelper.ProcessScheduler.SettingsCategory = "ProcessScheduler";
        //m_serviceHelper.ProcessScheduler.Initialize();

        //m_serviceHelper.StatusLog.FileName = "StatusLog.txt";
        //m_serviceHelper.StatusLog.PersistSettings = true;
        //m_serviceHelper.StatusLog.SettingsCategory = "StatusLog";
        //m_serviceHelper.StatusLog.Initialize();

        //m_serviceHelper.ParentService = this;
        //m_serviceHelper.PersistSettings = true;
        //m_serviceHelper.RemotingServer = m_remotingServer;
        //m_serviceHelper.Initialize();
    }

    //private TcpServer InitializeTcpServer()
    //{
    //    TcpServer remotingServer = new();
    //    remotingServer.ConfigurationString = "Port=8500";
    //    remotingServer.IgnoreInvalidCredentials = true;
    //    remotingServer.PayloadAware = true;
    //    remotingServer.PersistSettings = true;
    //    remotingServer.SettingsCategory = nameof(RemotingServer);
    //    remotingServer.Initialize();

    //    return remotingServer;
    //}

    //private TlsServer InitializeTlsServer()
    //{
    //    TlsServer remotingServer = new();
    //    remotingServer.CertificateFile = "Internal.cer";
    //    remotingServer.ConfigurationString = "Port=8500";
    //    remotingServer.IgnoreInvalidCredentials = true;
    //    remotingServer.PayloadAware = true;
    //    remotingServer.PersistSettings = true;
    //    remotingServer.SettingsCategory = nameof(RemotingServer);
    //    remotingServer.TrustedCertificatesPath = $"Certs{Path.DirectorySeparatorChar}Remotes";
    //    remotingServer.Initialize();

    //    remotingServer.RemoteCertificateValidationCallback = (_, _, _, _) => true;

    //    return remotingServer;
    //}

    // Perform system initialization

    private void InitializeSystem()
    {
        m_reloadConfigQueue?.Add(Tuple.Create("Startup", new Action<bool>(success =>
        {
            // Attempt to load system configuration
            if (success)
            {
                // Initialize and start all session adapters
                m_iaonSession?.Initialize();

                DisplayStatusMessage("System initialization complete.", MessageLevel.Info);

                try
                {
                    // TODO: If any settings have been added to configuration file, we go ahead and save them now
                    //m_serviceHelper.SaveSettings(true);
                    //ConfigurationFile.Current.Save();
                }
                catch (Exception ex)
                {
                    LogException(new InvalidOperationException($"Failed to save current service helper settings: {ex.Message}", ex));
                }
            }
            else
            {
                DisplayStatusMessage("System initialization failed due to unavailable configuration.", MessageLevel.Error);
            }

            // Log current thread pool size
            ThreadPool.GetMinThreads(out int minWorkerThreads, out int minIOThreads);
            ThreadPool.GetMaxThreads(out int maxWorkerThreads, out int maxIOThreads);

            DisplayStatusMessage("Thread pool size: minimum {0} worker {1} I/O, maximum {2} worker {3} I/O", MessageLevel.Info, minWorkerThreads, minIOThreads, maxWorkerThreads, maxIOThreads);
        })));
    }

    private void ExecuteReloadConfig(Tuple<string, Action<bool>>[] items)
    {
        DataSet? configuration = null;
        bool systemConfigurationLoaded = false;

        List<string> requestTypes = items
            .Select(tuple => tuple.Item1)
            .ToList();

        List<string> distinctRequestsTypes = requestTypes
            .Distinct()
            .OrderBy(type => requestTypes.LastIndexOf(type))
            .ToList();

        foreach (string type in distinctRequestsTypes)
        {
            string typeInner = type;

            if (type == ConfigurationType.ToString())
            {
                DisplayStatusMessage("Loading system configuration...", MessageLevel.Info);
                configuration = GetConfigurationDataSet();

                // Update data source on all adapters in all collections
                if (configuration is not null)
                    PropagateDataSource(configuration);
            }
            else
                switch (type)
                {
                    case "Augmented":
                    {
                        DisplayStatusMessage("Augmenting current system configuration...", MessageLevel.Info);
                        configuration = AugmentConfigurationDataSet(DataSource);

                        // Update data source on all adapters in all collections
                        if (configuration is not null)
                            PropagateDataSource(configuration);

                        break;
                    }
                    case "BinaryCache":
                    {
                        DisplayStatusMessage("Loading binary cached configuration...", MessageLevel.Info);
                        configuration = GetBinaryCachedConfigurationDataSet();

                        // Update data source on all adapters in all collections
                        if (configuration is not null)
                            PropagateDataSource(configuration);

                        break;
                    }
                    case "XmlCache":
                    {
                        DisplayStatusMessage("Loading XML cached configuration...", MessageLevel.Info);
                        configuration = GetXMLCachedConfigurationDataSet();

                        // Update data source on all adapters in all collections
                        if (configuration is not null)
                            PropagateDataSource(configuration);

                        break;
                    }
                    case "Startup":
                        systemConfigurationLoaded = LoadStartupConfiguration();

                        break;
                    default:
                        // No specific reload command was issued;
                        // load system configuration as normal
                        systemConfigurationLoaded = LoadSystemConfiguration();

                        break;
                }

            foreach (Action<bool> callback in items.Where(tuple => tuple.Item1 == typeInner).Select(tuple => tuple.Item2))
            {
                try
                {
                    callback(systemConfigurationLoaded || configuration is not null);
                }
                catch (Exception ex)
                {
                    DisplayStatusMessage("Failed to execute callback for ReloadConfig request of type {0}: {1}", MessageLevel.Error, type, ex.Message);
                    LogException(ex);
                }
            }

            // Spawn routing table calculation updates
            m_iaonSession?.RecalculateRoutingTables();
        }
    }

    // Load the system configuration data set
    private bool LoadStartupConfiguration()
    {
        // If cached configuration is not preferred,
        // load system configuration normally
        if (!m_preferCachedConfiguration)
            return LoadSystemConfiguration();

        // Cached configuration is preferred, so start by loading cached configuration
        bool loadedFromCache = true;
        DataSet? configuration = GetBinaryCachedConfigurationDataSet() ?? GetXMLCachedConfigurationDataSet();

        // If cached configuration failed to load,
        // load from regular configuration data source after all
        if (configuration is null)
        {
            loadedFromCache = false;
            configuration = GetConfigurationDataSet();
        }

        // Update data source on all adapters in all collections
        if (configuration is null || !PropagateDataSource(configuration))
            return false;

        // Cache the configuration if it wasn't already loaded from a cache
        if (!loadedFromCache)
            CacheCurrentConfiguration(configuration);

        return true;

    }

    // Load the system configuration data set
    private bool LoadSystemConfiguration()
    {
        DatabaseConfigurationLoader? dbConfigurationLoader = m_configurationLoader as DatabaseConfigurationLoader;

        DataSet? configuration = null;
        DataSet? binaryCache = null;
        DataSet? xmlCache = null;

        bool binaryCacheLoadAttempted = false;
        bool xmlCacheLoadAttempted = false;
        bool loadedFromCache = false;

        try
        {
            // If the configuration is a database configuration, make sure
            // to open the database before we start, so we're not constantly
            // opening and closing connections to the database
            dbConfigurationLoader?.Open();

            if (m_configurationLoader.CanAugment)
            {
                // Get the current running configuration
                configuration = DataSource;

                if (configuration is not null)
                {
                    // Existing configuration is available so we attempt to augment that
                    DisplayStatusMessage("Attempting to augment existing configuration data set...", MessageLevel.Info);
                    configuration = AugmentConfigurationDataSet(configuration.Copy());
                }
                else
                {
                    // Attempt to load the binary cached configuration
                    binaryCache = GetBinaryCachedConfigurationDataSet();
                    binaryCacheLoadAttempted = true;

                    if (binaryCache is not null)
                    {
                        // Binary cached configuration is available, so we attempt to augment that
                        DisplayStatusMessage("Attempting to augment binary cached configuration data set...", MessageLevel.Info);
                        configuration = AugmentConfigurationDataSet(binaryCache.Copy());
                    }
                    else
                    {
                        // Attempt to load the XML cached configuration
                        xmlCache = GetXMLCachedConfigurationDataSet();
                        xmlCacheLoadAttempted = true;

                        if (xmlCache is not null)
                        {
                            // XML cached configuration is available, so we attempt to augment that
                            DisplayStatusMessage("Attempting to augment XML cached configuration data set...", MessageLevel.Info);
                            configuration = AugmentConfigurationDataSet(xmlCache.Copy());
                        }
                    }
                }
            }

            // Either we cannot augment configuration or augmentation failed,
            // so we attempt to load the whole configuration from the source
            configuration ??= GetConfigurationDataSet();

            if (configuration is null)
            {
                // If all else fails, load configuration from the binary cache
                loadedFromCache = true;
                configuration = binaryCacheLoadAttempted ? binaryCache : GetBinaryCachedConfigurationDataSet();
            }

            // If we even failed to load from the
            // binary cache, load from the XML cache
            configuration ??= xmlCacheLoadAttempted ? xmlCache : GetXMLCachedConfigurationDataSet();

            // Attempt to add primary key to ActiveMeasurements table
            try
            {
                DataTable? activeMeasurements = configuration?.Tables["ActiveMeasurements"];

                if (activeMeasurements is { PrimaryKey.Length: 0 })
                    activeMeasurements.PrimaryKey = [activeMeasurements.Columns["SignalID"]!];
            }
            catch (Exception ex)
            {
                DisplayStatusMessage($"Failed to add primary key to ActiveMeasurements: {ex.Message}", MessageLevel.Warning);
                LogException(ex);
            }

            // Attempt to update data source on all adapters in all collections
            if (configuration is null || !PropagateDataSource(configuration))
                return false;

            // Cache the configuration if it wasn't already loaded from a cache
            if (!loadedFromCache)
                CacheCurrentConfiguration(configuration);

            return true;
        }
        catch (Exception ex)
        {
            DisplayStatusMessage(ex.Message, MessageLevel.Error);
            LogException(ex);

            // Most of the methods used above have a try-catch implemented to display helpful error messages
            // and prevent this code from executing. This should only execute when using a database configuration
            // as the primary configuration source and only if the system is unable to open a connection to the
            // database. In that case, we simply attempt to load the cached configuration and use that instead
            DisplayStatusMessage("Failed to open a connection to the database. Falling back on cached configuration...", MessageLevel.Warning);

            configuration ??= GetBinaryCachedConfigurationDataSet() ?? GetXMLCachedConfigurationDataSet();

            return configuration is not null && PropagateDataSource(configuration);
        }
        finally
        {
            dbConfigurationLoader?.Close();
        }
    }

    private DataSet? GetConfigurationDataSet()
    {
        try
        {
            return m_configurationLoader.Load();
        }
        catch (Exception ex)
        {
            DisplayStatusMessage("Failed to load {0} configuration due to exception: {1}", MessageLevel.Warning, ConfigurationType, ex.Message);
            LogException(ex);
            return null;
        }
    }

    private DataSet? GetBinaryCachedConfigurationDataSet()
    {
        try
        {
            return File.Exists(m_binaryCacheConfigurationLoader.FilePath) ? m_binaryCacheConfigurationLoader.Load() : null;
        }
        catch (Exception ex)
        {
            DisplayStatusMessage("Failed to load cached binary configuration due to exception: {0}", MessageLevel.Error, ex.Message);
            LogException(ex);
            return null;
        }
    }

    private DataSet? GetXMLCachedConfigurationDataSet()
    {
        try
        {
            return File.Exists(m_xmlCacheConfigurationLoader.FilePath) ? m_xmlCacheConfigurationLoader.Load() : null;
        }
        catch (Exception ex)
        {
            DisplayStatusMessage("Failed to load cached XML configuration due to exception: {0}", MessageLevel.Error, ex.Message);
            LogException(ex);
            return null;
        }
    }

    private DataSet? AugmentConfigurationDataSet(DataSet? configuration)
    {
        if (configuration is null)
            return null;

        try
        {
            m_configurationLoader.Augment(configuration);
            return configuration;
        }
        catch (Exception ex)
        {
            DisplayStatusMessage("Failed to augment configuration due to exception: {0}", MessageLevel.Warning, ex.Message);
            LogException(ex);
            return null;
        }
    }

    /// <summary>
    /// Handle assignment of data source to Iaon session.
    /// </summary>
    /// <param name="dataSource">New data source to assign.</param>
    /// <returns><c>true</c> if data source was assigned; otherwise, <c>false</c>.</returns>
    protected virtual bool PropagateDataSource(DataSet dataSource)
    {
        try
        {
            if (m_iaonSession is not null) 
                m_iaonSession.DataSource = dataSource;

            return true;
        }
        catch (Exception ex)
        {
            DisplayStatusMessage("Unable to propagate configuration data set to adapters due to exception: {0}", MessageLevel.Error, ex.Message);
            LogException(ex);
            return false;
        }
    }

    /// <summary>
    /// Caches the current system configuration.
    /// </summary>
    /// <param name="configuration">Configuration <see cref="DataSet"/>.</param>
    /// <remarks>
    /// This method allows caching of the current system configuration, so it can be used if primary configuration source is unavailable.
    /// </remarks>
    protected virtual void CacheCurrentConfiguration(DataSet configuration)
    {
        m_latestConfiguration = configuration;
        m_configurationCacheOperation.RunAsync();
    }

    // Executes actual serialization of current configuration
    private void ExecuteConfigurationCache()
    {
        DataSet? configuration = m_latestConfiguration;

        if (configuration is null)
            return;

        // Create backups of binary configurations
        BackupConfiguration(ConfigurationType.BinaryFile, m_cachedBinaryConfigurationFile);

        // Create backups of XML configurations
        BackupConfiguration(ConfigurationType.XmlFile, m_cachedXmlConfigurationFile);

        try
        {
            // Wait a moment for write lock in case binary file is open by another process
            if (File.Exists(m_cachedBinaryConfigurationFile))
                FilePath.WaitForWriteLock(m_cachedBinaryConfigurationFile);

            // Cache binary serialized version of data set
            using (FileStream configurationFileStream = File.OpenWrite(m_cachedBinaryConfigurationFile))
                configuration.SerializeToStream(configurationFileStream);

            DisplayStatusMessage("Successfully cached current configuration to binary.", MessageLevel.Info);
        }
        catch (Exception ex)
        {
            DisplayStatusMessage("Failed to cache last known configuration due to exception: {0}", MessageLevel.Error, ex.Message);
            LogException(ex);
        }

        // Serialize current data set to configuration files
        try
        {

            // Wait a moment for write lock in case XML file is open by another process
            if (File.Exists(m_cachedXmlConfigurationFile))
                FilePath.WaitForWriteLock(m_cachedXmlConfigurationFile);

            // Cache XML serialized version of data set
            configuration.WriteXml(m_cachedXmlConfigurationFile, XmlWriteMode.WriteSchema);

            DisplayStatusMessage("Successfully cached current configuration to XML.", MessageLevel.Info);
        }
        catch (Exception ex)
        {
            DisplayStatusMessage("Failed to cache last known configuration due to exception: {0}", MessageLevel.Error, ex.Message);
            LogException(ex);
        }
    }

    private void BackupConfiguration(ConfigurationType configType, string configurationFile)
    {
        try
        {
            // Create multiple backup configurations, if requested
            for (int i = m_configurationBackups; i > 0; i--)
            {
                string origConfigFile = $"{configurationFile}.backup{(i == 1 ? "" : (i - 1).ToString())}";

                if (!File.Exists(origConfigFile))
                    continue;

                string nextConfigFile = $"{configurationFile}.backup{i}";

                if (File.Exists(nextConfigFile))
                    File.Delete(nextConfigFile);

                File.Move(origConfigFile, nextConfigFile);
            }
        }
        catch (Exception ex)
        {
            DisplayStatusMessage("Failed to create extra backup {0} configurations due to exception: {1}", MessageLevel.Warning, configType, ex.Message);
            LogException(ex);
        }

        try
        {
            if (m_configurationBackups < 1 || !File.Exists(configurationFile))
                return;

            // Back up current configuration file
            string backupConfigFile = $"{configurationFile}.backup";

            if (File.Exists(backupConfigFile))
                File.Delete(backupConfigFile);

            File.Move(configurationFile, backupConfigFile);
        }
        catch (Exception ex)
        {
            DisplayStatusMessage("Failed to backup last known cached {0} configuration due to exception: {1}", MessageLevel.Warning, configType, ex.Message);
            LogException(ex);
        }
    }

    // Attempt to log event to Windows event log during system initialization.
    // On failure, ignore errors and continue system initialization.
    // Eventually GSF logger will be initialized and can handle exceptions that occur when writing to the event log.
    private void TryWriteToEventLog(string message, EventLogEntryType entryType)
    {
        try
        {
            EventLog.WriteEntry(ServiceName, message, entryType, 0);
        }
        catch
        {
            // ignored
        }
    }

    #endregion

    #region [ Service Binding ]

    //internal void SendRequest(IPrincipal principal, Guid clientID, string userInput)
    //{
    //    ClientRequest request = ClientRequest.Parse(userInput);

    //    if (request is null)
    //        return;

    //    ClientRequestHandler requestHandler = m_serviceHelper.FindClientRequestHandler(request.Command);

    //    if (requestHandler is null)
    //    {
    //        DisplayStatusMessage($"Command \"{request.Command}\" is not supported\r\n\r\n", MessageLevel.Error);
    //    }
    //    else
    //    {
    //        ClientInfo clientInfo = new() { ClientID = clientID };
    //        ClientRequestInfo clientRequestInfo = new(clientInfo, request);
    //        clientInfo.SetClientUser(principal);
    //        requestHandler.HandlerMethod(clientRequestInfo);
    //    }
    //}

    #endregion

    #region [ Primary Adapter Event Handlers ]

    /// <summary>
    /// Event handler for reporting status messages.
    /// </summary>
    /// <param name="sender">Event source of the status message.</param>
    /// <param name="e">Event arguments containing the status message and its type to report.</param>
    /// <remarks>
    /// The time-series framework <see cref="IaonSession"/> uses this event to report adapter status messages (e.g., to a log file or console window).
    /// </remarks>
    private void StatusMessageHandler(object? sender, EventArgs<string, MessageLevel> e)
    {
        DisplayStatusMessage(e.Argument1, e.Argument2);
    }

    /// <summary>
    /// Event handler for processing reported exceptions.
    /// </summary>
    /// <param name="sender">Event source of the exception.</param>
    /// <param name="e">Event arguments containing the exception to report.</param>
    /// <remarks>
    /// The time-series framework <see cref="IaonSession"/> uses this event to report exceptions.
    /// </remarks>
    private void ProcessExceptionHandler(object? sender, EventArgs<Exception> e)
    {
        LogException(e.Argument);
    }

    /// <summary>
    /// Event handler for processing notifications from adapters that configuration has changed.
    /// </summary>
    /// <param name="sender">Event source of the notification.</param>
    /// <param name="e">Event arguments, if any.</param>
    /// <remarks>
    /// The time-series framework <see cref="IaonSession"/> uses this event to report configuration changes.
    /// </remarks>
    private void ConfigurationChangedHandler(object? sender, EventArgs e)
    {
        m_reloadConfigQueue?.Add(Tuple.Create(nameof(System), (Action<bool>)(_ => { })));
    }

    /// <summary>
    /// Reloads the system configuration.
    /// </summary>
    public void ReloadConfig()
    {
        m_reloadConfigQueue?.Add(Tuple.Create("User", (Action<bool>)(_ => { })));
    }

    /// <summary>
    /// Initializes the adapter with the specified acronym.
    /// </summary>
    /// <param name="acronym">Acronym to initialize.</param>
    public void Initialize(string acronym)
    {
        if (m_reloadConfigQueue is null)
            throw new NullReferenceException("System is not initialized");

        m_reloadConfigQueue.Add(Tuple.Create(nameof(System), new Action<bool>(success =>
        {
            if (!success)
            {
                LogException(new InvalidOperationException("Failed to load system configuration."));
                return;
            }

            AllAdaptersCollection? adapters = AllAdapters;

            if (adapters is null)
            {
                LogException(new NullReferenceException("No adapters are currently defined"));
                return;
            }

            if (!adapters.TryGetAnyAdapterByName(acronym, out IAdapter? adapter, out IAdapterCollection? collection))
            {
                LogException(new InvalidOperationException($"Failed to find adapter with acronym \"{acronym}\""));
                return;
            }

            if (!collection.TryInitializeAdapterByID(adapter.ID))
                LogException(new InvalidOperationException($"Failed to initialize adapter with acronym \"{acronym}\""));
        })));
    }

    ///// <summary>
    ///// Event handler for processing exceptions encountered while writing entries to any log file.
    ///// </summary>
    ///// <param name="sender">Event source of the exception.</param>
    ///// <param name="e">Event arguments containing the exception to report.</param>
    ///// <remarks>
    ///// This function is for derived classes wanting access to any logged exception without needing
    ///// to override all specific log exception handlers.
    ///// </remarks>
    //protected virtual void LogExceptionHandler(object sender, EventArgs<Exception> e)
    //{
    //    LogException(e.Argument);
    //}

    ///// <summary>
    ///// Event handler for processing exceptions encountered while writing entries to the status log file.
    ///// </summary>
    ///// <param name="sender">Event source of the exception.</param>
    ///// <param name="e">Event arguments containing the exception to report.</param>
    //protected virtual void StatusLogExceptionHandler(object sender, EventArgs<Exception> e)
    //{
    //    DisplayStatusMessage($"Status log file exception: {e.Argument.Message}", MessageLevel.Error);
    //    LogExceptionHandler(sender, e);
    //}

    ///// <summary>
    ///// Event handler for processing exceptions encountered while writing entries to the error log file.
    ///// </summary>
    ///// <param name="sender">Event source of the exception.</param>
    ///// <param name="e">Event arguments containing the exception to report.</param>
    //protected virtual void ErrorLogExceptionHandler(object sender, EventArgs<Exception> e)
    //{
    //    DisplayStatusMessage($"Error log file exception: {e.Argument.Message}", MessageLevel.Error);
    //    LogExceptionHandler(sender, e);
    //}

    ///// <summary>
    ///// Event handler for processing exceptions encountered while writing entries to the connection log file.
    ///// </summary>
    ///// <param name="sender">Event source of the exception.</param>
    ///// <param name="e">Event arguments containing the exception to report.</param>
    //protected virtual void ConnectionErrorLogExceptionHandler(object sender, EventArgs<Exception> e)
    //{
    //    DisplayStatusMessage($"Connection log file exception: {e.Argument.Message}", MessageLevel.Error);
    //    LogExceptionHandler(sender, e);
    //}

    ///// <summary>
    ///// Event handler for scheduled health monitor display.
    ///// </summary>
    ///// <param name="name">Scheduled event name.</param>
    ///// <param name="parameters">Scheduled event parameters.</param>
    //protected virtual void HealthMonitorProcessHandler(string name, object[] parameters)
    //{
    //    //const string RequestCommand = "Health";
    //    //ClientRequestHandler requestHandler = m_serviceHelper.FindClientRequestHandler(RequestCommand);

    //    //if (requestHandler is null)
    //    //    return;

    //    //// We pretend to be a client and send a "Health" command to ourselves...
    //    //requestHandler.HandlerMethod(ClientHelper.PretendRequest(RequestCommand));

    //    //// We also export human readable health information to a text file for external display
    //    //m_healthExporter.ExportData(m_serviceHelper.PerformanceMonitor.Status);
    //}

    ///// <summary>
    ///// Event handler for scheduled adapter status export.
    ///// </summary>
    ///// <param name="name">Scheduled event name.</param>
    ///// <param name="parameters">Scheduled event parameters.</param>
    //protected virtual void StatusExportProcessHandler(string name, object[] parameters)
    //{
    //    // Every thirty minutes we export a human readable service status to a text file for external display
    //    // m_statusExporter.ExportData(m_serviceHelper.Status);
    //}

    ///// <summary>
    ///// Event handler for scheduled reporting services.
    ///// </summary>
    ///// <param name="name">Scheduled event name.</param>
    ///// <param name="parameters">Scheduled event parameters.</param>
    //protected virtual void ReportingProcessHandler(string name, object[] parameters)
    //{
    //    // Report processing schedule names end with "Reporting", remove suffix
    //    //                 123456789
    //    if (name.EndsWith("Reporting", StringComparison.OrdinalIgnoreCase))
    //        name = name.Substring(0, name.Length - 9);

    //    // Lookup reporting process
    //    IReportingProcess? reportingProcess = m_reportingProcesses?.FindReportType(name);

    //    if (reportingProcess is null)
    //        return;

    //    // Reporting process schedule is due, generate a new report
    //    reportingProcess.CleanReportLocation();
    //    reportingProcess.GenerateReport(DateTime.UtcNow - TimeSpan.FromDays(1), true);
    //}

    #endregion

    #region [ Remote Client Request Handlers ]

    ///// <summary>
    ///// Gets requested <see cref="IAdapterCollection"/>.
    ///// </summary>
    ///// <param name="requestInfo"><see cref="ClientRequestInfo"/> instance containing the client request.</param>
    ///// <returns>Requested <see cref="IAdapterCollection"/>.</returns>
    //protected virtual IAdapterCollection GetRequestedCollection(ClientRequestInfo requestInfo)
    //{
    //    if (requestInfo.Request.Arguments.Exists("A"))
    //        return m_iaonSession.ActionAdapters;

    //    if (requestInfo.Request.Arguments.Exists("O"))
    //        return m_iaonSession.OutputAdapters;

    //    if (requestInfo.Request.Arguments.Exists("F"))
    //        return m_iaonSession.FilterAdapters;

    //    return m_iaonSession.InputAdapters;
    //}

    ///// <summary>
    ///// Gets requested <see cref="IAdapter"/>.
    ///// </summary>
    ///// <param name="requestInfo"><see cref="ClientRequestInfo"/> instance containing the client request.</param>
    ///// <returns>Requested <see cref="IAdapter"/>.</returns>
    //protected virtual IAdapter GetRequestedAdapter(ClientRequestInfo requestInfo) =>
    //    GetRequestedAdapter(requestInfo, out IAdapterCollection _);

    ///// <summary>
    ///// Gets requested <see cref="IAdapter"/> and its containing <see cref="IAdapterCollection"/>.
    ///// </summary>
    ///// <param name="requestInfo"><see cref="ClientRequestInfo"/> instance containing the client request.</param>
    ///// <param name="collection">Containing <see cref="IAdapterCollection"/> for <see cref="IAdapter"/>.</param>
    ///// <returns>Requested <see cref="IAdapter"/>.</returns>
    //protected virtual IAdapter GetRequestedAdapter(ClientRequestInfo requestInfo, out IAdapterCollection collection)
    //{
    //    string adapterID = requestInfo.Request.Arguments["OrderedArg1"];
    //    collection = GetRequestedCollection(requestInfo);

    //    if (string.IsNullOrWhiteSpace(adapterID))
    //        return null;

    //    IAdapter adapter;

    //    adapterID = adapterID.Trim();

    //    if (adapterID.IsAllNumbers() && uint.TryParse(adapterID, out uint id))
    //    {
    //        // Adapter ID is numeric, try numeric lookup by adapter ID in requested collection
    //        if (collection.TryGetAdapterByID(id, out adapter))
    //            return adapter;

    //        // Try looking for ID in any collection if all runtime ID's are unique
    //        if (m_uniqueAdapterIDs && m_iaonSession.AllAdapters.TryGetAnyAdapterByID(id, out adapter, out collection))
    //            return adapter;

    //        collection = GetRequestedCollection(requestInfo);
    //        SendResponse(requestInfo, false, "Failed to find adapter with ID \"{0}\".", id);
    //    }
    //    else
    //    {
    //        // Adapter ID is alpha-numeric, try text-based lookup by adapter name in requested collection
    //        if (collection.TryGetAdapterByName(adapterID, out adapter))
    //            return adapter;

    //        // Try looking for adapter name in any collection
    //        if (m_iaonSession.AllAdapters.TryGetAnyAdapterByName(adapterID, out adapter, out collection))
    //            return adapter;

    //        collection = GetRequestedCollection(requestInfo);
    //        SendResponse(requestInfo, false, "Failed to find adapter named \"{0}\".", adapterID);
    //    }

    //    return null;
    //}

    ///// <summary>
    ///// Displays status of specified adapter or collection.
    ///// </summary>
    ///// <param name="requestInfo"><see cref="ClientRequestInfo"/> instance containing the client request.</param>
    //protected virtual void ListRequestHandler(ClientRequestInfo requestInfo)
    //{
    //    if (requestInfo.Request.Arguments.ContainsHelpRequest)
    //    {
    //        StringBuilder helpMessage = new();

    //        helpMessage.Append("Displays status of specified adapter or collection.");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Usage:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       List [ID] [Options]");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   ID:".PadRight(20));
    //        helpMessage.Append("ID of the adapter to display, or all adapters if not specified");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Options:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -?".PadRight(20));
    //        helpMessage.Append("Displays this help message");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -I".PadRight(20));
    //        helpMessage.Append("Enumerate input adapters (default)");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -A".PadRight(20));
    //        helpMessage.Append("Enumerate action adapters");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -O".PadRight(20));
    //        helpMessage.Append("Enumerate output adapters");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -actionable".PadRight(20));
    //        helpMessage.Append("Returns results via an actionable event");
    //        helpMessage.AppendLine();

    //        DisplayResponseMessage(requestInfo, helpMessage.ToString());
    //    }
    //    else
    //    {
    //        StringBuilder adapterList = new();
    //        IAdapterCollection collection = GetRequestedCollection(requestInfo);
    //        IEnumerable<IAdapter> listItems = collection;
    //        bool idArgExists = requestInfo.Request.Arguments.Exists("OrderedArg1");
    //        int enumeratedItems = 0;

    //        adapterList.AppendFormat("System Uptime: {0}", m_serviceHelper.RemotingServer.RunTime.ToString(3));
    //        adapterList.AppendLine();
    //        adapterList.AppendLine();

    //        if (idArgExists)
    //            adapterList.AppendFormat(">> Selected adapter from {0}", collection.Name);
    //        else
    //            adapterList.AppendFormat(">> All defined adapters in {0} ({1} total)", collection.Name, collection.Count);

    //        // Make a collection of one item for individual adapters
    //        if (idArgExists)
    //        {
    //            IAdapter adapter = GetRequestedAdapter(requestInfo);
    //            List<IAdapter> singleItemList = new();

    //            if (adapter is not null)
    //                singleItemList.Add(adapter);

    //            listItems = singleItemList;
    //        }

    //        adapterList.AppendLine();
    //        adapterList.AppendLine();
    //        adapterList.Append("    ID     Name");
    //        adapterList.AppendLine();
    //        //                  12345678901234567890123456789012345678901234567890123456789012345678901234567890
    //        //                           1         2         3         4         5         6         7         8
    //        adapterList.Append("---------- --------------------------------------------------------------------");
    //        //                             123456789012345678901234567890123456789012345678901234567890123456789
    //        //                                      1         2         3         4         5         6
    //        adapterList.AppendLine();

    //        foreach (IAdapter adapter in listItems)
    //        {
    //            adapterList.AppendFormat("{0} {1}", adapter.ID.ToString().CenterText(10), adapter.Name.TruncateRight(66));
    //            adapterList.AppendLine();
    //            adapterList.Append("           ");
    //            adapterList.Append(adapter.GetShortStatus(68).TruncateRight(68));

    //            // If a request was made to list a specific item, we request full status
    //            if (idArgExists)
    //            {
    //                adapterList.AppendLine();
    //                adapterList.AppendLine();
    //                adapterList.Append(adapter.Status);
    //            }
    //            adapterList.AppendLine();

    //            enumeratedItems++;
    //        }

    //        if (requestInfo.Request.Arguments.Exists("actionable"))
    //        {
    //            if (enumeratedItems > 0)
    //                SendResponseWithAttachment(requestInfo, true, adapterList.ToString(), null);
    //            else
    //                SendResponseWithAttachment(requestInfo, false, "No items were available enumerate.", null);
    //        }
    //        else
    //        {
    //            if (enumeratedItems > 0)
    //                SendResponse(requestInfo, true, adapterList.ToString());
    //            else
    //                SendResponse(requestInfo, false, "No items were available enumerate.");
    //        }
    //    }
    //}

    ///// <summary>
    ///// Starts specified adapter.
    ///// </summary>
    ///// <param name="requestInfo"><see cref="ClientRequestInfo"/> instance containing the client request.</param>
    //protected virtual void StartRequestHandler(ClientRequestInfo requestInfo) =>
    //    ActionRequestHandler(requestInfo, adapter => adapter.Start());

    ///// <summary>
    ///// Stops specified adapter.
    ///// </summary>
    ///// <param name="requestInfo"><see cref="ClientRequestInfo"/> instance containing the client request.</param>
    //protected virtual void StopRequestHandler(ClientRequestInfo requestInfo) =>
    //    ActionRequestHandler(requestInfo, adapter => adapter.Stop());

    ///// <summary>
    ///// Generic adapter request handler.
    ///// </summary>
    ///// <param name="requestInfo"><see cref="ClientRequestInfo"/> instance containing the client request.</param>
    ///// <param name="adapterAction">Action to perform on <see cref="IAdapter"/>.</param>
    //protected virtual void ActionRequestHandler(ClientRequestInfo requestInfo, Action<IAdapter> adapterAction)
    //{
    //    string actionName = requestInfo.Request.Command.ToTitleCase();

    //    if (requestInfo.Request.Arguments.ContainsHelpRequest)
    //    {
    //        StringBuilder helpMessage = new();

    //        helpMessage.AppendFormat("Handles {0} command for specified adapter.", actionName);
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Usage:");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendFormat("       {0} ID [Options]", actionName);
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   ID:".PadRight(20));
    //        helpMessage.Append("ID of the adapter to execute action on");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Options:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -?".PadRight(20));
    //        helpMessage.Append("Displays this help message");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -I".PadRight(20));
    //        helpMessage.AppendFormat("Perform {0} command on input adapters (default)", actionName);
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -A".PadRight(20));
    //        helpMessage.AppendFormat("Perform {0} command on action adapters", actionName);
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -O".PadRight(20));
    //        helpMessage.AppendFormat("Perform {0} command on output adapters", actionName);
    //        helpMessage.AppendLine();

    //        DisplayResponseMessage(requestInfo, helpMessage.ToString());
    //    }
    //    else
    //    {
    //        if (requestInfo.Request.Arguments.Exists("OrderedArg1"))
    //        {
    //            IAdapter adapter = GetRequestedAdapter(requestInfo);

    //            if (adapter is null)
    //                return;

    //            if (adapter.Initialized)
    //            {
    //                adapterAction(adapter);
    //                SendResponse(requestInfo, true);
    //            }
    //            else
    //            {
    //                SendResponse(requestInfo, false, "Unable to {0} -- adapter is not initialized.", actionName);
    //            }
    //        }
    //        else
    //        {
    //            SendResponse(requestInfo, false, "No ID was specified for \"{0}\" command.", actionName);
    //        }
    //    }
    //}

    ///// <summary>
    ///// Invokes specified adapter command.
    ///// </summary>
    ///// <param name="requestInfo"><see cref="ClientRequestInfo"/> instance containing the client request.</param>
    //protected virtual void InvokeRequestHandler(ClientRequestInfo requestInfo)
    //{
    //    if (requestInfo.Request.Arguments.ContainsHelpRequest)
    //    {
    //        StringBuilder helpMessage = new();

    //        helpMessage.Append("Invokes specified adapter command.");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Usage:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       Invoke [ID] Command [Params] [Options]");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   ID:".PadRight(20));
    //        helpMessage.Append("ID of the adapter to execute command on");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine("   If ID is not specified, action will be on collection itself.");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Command:".PadRight(20));
    //        helpMessage.Append("Name of the adapter command (i.e., method) to invoke");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Params:".PadRight(20));
    //        helpMessage.Append("Command parameters, if any, separated by spaces");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Options:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -?".PadRight(20));
    //        helpMessage.Append("Displays this help message");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -I".PadRight(20));
    //        helpMessage.Append("Invoke specified command on input adapter (default)");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -A".PadRight(20));
    //        helpMessage.Append("Invoke specified command on action adapter");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -O".PadRight(20));
    //        helpMessage.Append("Invoke specified command on output adapter");
    //        helpMessage.AppendLine();

    //        DisplayResponseMessage(requestInfo, helpMessage.ToString());
    //    }
    //    else
    //    {
    //        Thread invocationThread = new(() =>
    //        {
    //            IAdapter adapter = null;
    //            string command = null;

    //            // See if specific ID for an adapter was requested
    //            if (requestInfo.Request.Arguments.Exists("OrderedArg1"))
    //            {
    //                if (requestInfo.Request.Arguments.Exists("OrderedArg2"))
    //                {
    //                    adapter = GetRequestedAdapter(requestInfo);
    //                    command = requestInfo.Request.Arguments["OrderedArg2"];
    //                }
    //                else
    //                {
    //                    adapter = GetRequestedCollection(requestInfo);
    //                    command = requestInfo.Request.Arguments["OrderedArg1"];
    //                }
    //            }
    //            else
    //            {
    //                SendResponse(requestInfo, false, "No command was specified to invoke.");
    //            }

    //            if (adapter is null)
    //                return;

    //            try
    //            {
    //                // See if method exists with specified name using reflection
    //                MethodInfo method = adapter.GetType().GetMethod(command, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.IgnoreCase);

    //                // Invoke method
    //                if (method is not null)
    //                {
    //                    // Make sure method is marked as invokable (i.e., AdapterCommandAttribute exists on method)
    //                    if (method.TryGetAttribute(out AdapterCommandAttribute commandAttribute) && (!m_serviceHelper.SecureRemoteInteractions || commandAttribute.AllowedRoles.Any(role => requestInfo.Sender.ClientUser.IsInRole(role))))
    //                    {
    //                        ParameterInfo[] parameters = method.GetParameters();
    //                        object returnValue = null;
    //                        bool success = true;

    //                        if (parameters.Length == 0)
    //                        {
    //                            // Invoke parameterless adapter command
    //                            returnValue = method.Invoke(adapter, null);
    //                        }
    //                        else
    //                        {
    //                            int argCount = requestInfo.Request.Arguments.Count - 2;
    //                            int attachmentCount = requestInfo.Request.Attachments.Count;
    //                            int expectedParameterCount = parameters.Length - parameters.Count(p => p.HasDefaultValue);

    //                            // Create typed parameters for method and invoke
    //                            if (argCount + attachmentCount >= expectedParameterCount)
    //                            {
    //                                static object convertToType(string value, Type type)
    //                                {
    //                                    if (string.IsNullOrEmpty(value))
    //                                        return null;

    //                                    if (type == typeof(bool))
    //                                        return value.ParseBoolean();

    //                                    // This handle objects that have type converters (e.g., Enum, Color, Point, etc.)
    //                                    TypeConverter converter = TypeDescriptor.GetConverter(type);

    //                                    return converter.ConvertFromString(null!, CultureInfo.InvariantCulture, value);
    //                                }

    //                                // Attempt to convert command parameters to the method parameter types
    //                                object[] providedArgs = requestInfo.Request.Arguments.Skip(2)
    //                                    .Select((arg, i) => convertToType(arg.Value, parameters[i].ParameterType))
    //                                    .Concat(requestInfo.Request.Attachments)
    //                                    .ToArray();

    //                                object[] args = new object[parameters.Length];

    //                                for (int i = 0; i < args.Length; i++)
    //                                {
    //                                    if (i < providedArgs.Length)
    //                                    {
    //                                        args[i] = providedArgs[i];
    //                                    }
    //                                    else if (parameters[i].HasDefaultValue)
    //                                    {
    //                                        args[i] = parameters[i].DefaultValue;
    //                                    }
    //                                    else
    //                                    {
    //                                        SendResponse(requestInfo, false, "Parameter count mismatch, \"{0}\" command expects at least {1} parameters.", command, expectedParameterCount);
    //                                        return;
    //                                    }
    //                                }

    //                                // Invoke adapter command with specified parameters
    //                                returnValue = method.Invoke(adapter, args);
    //                            }
    //                            else
    //                            {
    //                                success = false;
    //                                SendResponse(requestInfo, false, "Parameter count mismatch, \"{0}\" command expects at least {1} parameters.", command, expectedParameterCount);
    //                            }
    //                        }

    //                        // If invoke was successful, return actionable response
    //                        if (!success)
    //                            return;

    //                        // Return value, if any, will be returned to requesting client as a response attachment
    //                        if (returnValue is null)
    //                            SendResponse(requestInfo, true, "Command \"{0}\" successfully invoked.", command);
    //                        else
    //                            SendResponseWithAttachment(requestInfo, true, returnValue, "Command \"{0}\" successfully invoked, return value = {1}", command, returnValue.ToNonNullString("null"));
    //                    }
    //                    else
    //                    {
    //                        SendResponse(requestInfo, false, "Specified command \"{0}\" is not marked as invokable for adapter \"{1}\" [Type = {2}].", command, adapter.Name, adapter.GetType().Name);
    //                    }
    //                }
    //                else
    //                {
    //                    SendResponse(requestInfo, false, "Specified command \"{0}\" does not exist for adapter \"{1}\" [Type = {2}].", command, adapter.Name, adapter.GetType().Name);
    //                }
    //            }
    //            catch (TargetInvocationException ex)
    //            {
    //                SendResponse(requestInfo, false, "Failed to invoke command: {0}", ex.InnerException?.Message ?? ex.Message);
    //                LogException(ex);
    //            }
    //            catch (Exception ex)
    //            {
    //                SendResponse(requestInfo, false, "Failed to invoke command: {0}", ex.Message);
    //                LogException(ex);
    //            }
    //        });

    //        invocationThread.IsBackground = true;
    //        invocationThread.Start();
    //    }
    //}

    ///// <summary>
    ///// Lists possible commands of specified adapter.
    ///// </summary>
    ///// <param name="requestInfo"><see cref="ClientRequestInfo"/> instance containing the client request.</param>
    //protected virtual void ListCommandsRequestHandler(ClientRequestInfo requestInfo)
    //{
    //    if (requestInfo.Request.Arguments.ContainsHelpRequest)
    //    {
    //        StringBuilder helpMessage = new();

    //        helpMessage.Append("Lists possible commands of specified adapter.");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Usage:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       ListCommands [ID] [Options]");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   ID:".PadRight(20));
    //        helpMessage.Append("ID of the adapter to execute command on");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine("   If ID is not specified, action will be on collection itself.");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Options:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -?".PadRight(20));
    //        helpMessage.Append("Displays this help message");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -I".PadRight(20));
    //        helpMessage.Append("Lists commands on input adapter (default)");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -A".PadRight(20));
    //        helpMessage.Append("Lists commands on action adapter");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -O".PadRight(20));
    //        helpMessage.Append("Lists commands on output adapter");
    //        helpMessage.AppendLine();

    //        DisplayResponseMessage(requestInfo, helpMessage.ToString());
    //    }
    //    else
    //    {
    //        // See if specific ID for an adapter was requested
    //        IAdapter adapter =
    //            requestInfo.Request.Arguments.Exists("OrderedArg1") ?
    //            GetRequestedAdapter(requestInfo) :
    //            GetRequestedCollection(requestInfo);

    //        if (adapter is null)
    //            return;

    //        try
    //        {
    //            // Get public command methods of specified adapter using reflection
    //            MethodInfo[] methods = adapter.GetType().GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.IgnoreCase);

    //            // Invoke method
    //            StringBuilder methodList = new();

    //            methodList.AppendFormat("Adapter \"{0}\" [Type = {1}] Command List:", adapter.Name, adapter.GetType().Name);
    //            methodList.AppendLine();
    //            methodList.AppendLine();

    //            // Enumerate each public method
    //            foreach (MethodInfo method in methods)
    //            {
    //                // Only display methods marked as invokable (i.e., AdapterCommandAttribute exists on method)
    //                if (!method.TryGetAttribute(out AdapterCommandAttribute commandAttribute))
    //                    continue;

    //                // Don't display methods marked as hidden - allows derived classes to hide methods
    //                if (method.TryGetAttribute(out EditorBrowsableAttribute editorBrowsableAttribute) && editorBrowsableAttribute.State == EditorBrowsableState.Never)
    //                    continue;

    //                // Don't bother displaying commands to users who cannot invoke them
    //                if (m_serviceHelper.SecureRemoteInteractions && !commandAttribute.AllowedRoles.Any(role => requestInfo.Sender.ClientUser.IsInRole(role)))
    //                    continue;

    //                bool firstParameter = true;

    //                methodList.Append("    ");
    //                methodList.Append(method.Name);
    //                methodList.Append('(');

    //                // Enumerate each method parameter
    //                foreach (ParameterInfo parameter in method.GetParameters())
    //                {
    //                    if (!firstParameter)
    //                        methodList.Append(", ");

    //                    methodList.Append(parameter.ParameterType.GetReflectedTypeName(false));
    //                    methodList.Append(' ');
    //                    methodList.Append(parameter.Name);

    //                    if (parameter.HasDefaultValue)
    //                    {
    //                        methodList.Append(" = ");

    //                        switch (parameter.DefaultValue)
    //                        {
    //                            case null:
    //                                methodList.Append("null");
    //                                break;
    //                            case string:
    //                                methodList.Append('\'');
    //                                methodList.Append(parameter.DefaultValue.ToNonNullString());
    //                                methodList.Append('\'');
    //                                break;
    //                            default:
    //                                methodList.Append(parameter.DefaultValue.ToNonNullString());
    //                                break;
    //                        }
    //                    }

    //                    firstParameter = false;
    //                }

    //                methodList.Append(')');
    //                methodList.AppendLine();

    //                if (!string.IsNullOrWhiteSpace(commandAttribute.Description))
    //                {
    //                    methodList.Append("        ");
    //                    methodList.Append(commandAttribute.Description);
    //                }

    //                methodList.AppendLine();
    //            }

    //            methodList.AppendLine();

    //            SendResponse(requestInfo, true, methodList.ToString());
    //        }
    //        catch (Exception ex)
    //        {
    //            SendResponse(requestInfo, false, "Failed to list commands: {0}", ex.Message);
    //            LogException(ex);
    //        }
    //    }
    //}

    ///// <summary>
    ///// Performs initialization or reinitialization of specified adapter or collection.
    ///// </summary>
    ///// <param name="requestInfo"><see cref="ClientRequestInfo"/> instance containing the client request.</param>
    //protected virtual void InitializeRequestHandler(ClientRequestInfo requestInfo)
    //{
    //    if (requestInfo.Request.Arguments.ContainsHelpRequest)
    //    {
    //        StringBuilder helpMessage = new();

    //        helpMessage.Append("Performs (re)initialization of specified adapter or collection.");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Usage:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       Initialize [ID] [Options]");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   ID:".PadRight(20));
    //        helpMessage.Append("ID of the adapter to initialize, or all adapters if not specified");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Options:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -?".PadRight(20));
    //        helpMessage.Append("Displays this help message");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -I".PadRight(20));
    //        helpMessage.Append("Initialize input adapters (default)");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -A".PadRight(20));
    //        helpMessage.Append("Initialize action adapters");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -O".PadRight(20));
    //        helpMessage.Append("Initialize output adapters");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -SkipReloadConfig".PadRight(20));
    //        helpMessage.Append("Skips configuration reload before initialize");
    //        helpMessage.AppendLine();

    //        DisplayResponseMessage(requestInfo, helpMessage.ToString());
    //    }
    //    else
    //    {
    //        bool requestedAdapterExists = RequestedAdapterExists(requestInfo);

    //        if (requestInfo.Request.Arguments.Exists("SkipReloadConfig"))
    //        {
    //            if (!requestedAdapterExists)
    //            {
    //                SendResponse(requestInfo, false, "Unable to initialize disabled or nonexistent adapter with ID.");
    //                return;
    //            }

    //            HandleInitializeRequest(requestInfo);

    //            // Spawn routing table calculation updates
    //            m_iaonSession.RecalculateRoutingTables();
    //        }
    //        else
    //        {
    //            m_reloadConfigQueue.Add(Tuple.Create(nameof(System), new Action<bool>(success =>
    //            {
    //                if (!success)
    //                {
    //                    SendResponse(requestInfo, false, "Failed to load system configuration.");
    //                    return;
    //                }

    //                if (requestedAdapterExists)
    //                    HandleInitializeRequest(requestInfo);
    //            })));
    //        }
    //    }
    //}

    //private void HandleInitializeRequest(ClientRequestInfo requestInfo)
    //{
    //    IAdapterCollection collection;

    //    // See if specific ID for an adapter was requested
    //    if (requestInfo.Request.Arguments.Exists("OrderedArg1"))
    //    {
    //        string adapterID = requestInfo.Request.Arguments["OrderedArg1"];

    //        // Try initializing new adapter by ID searching in any collection if all runtime ID's are unique
    //        IAdapter adapter;

    //        if (m_uniqueAdapterIDs && uint.TryParse(adapterID, out uint id) && m_iaonSession.AllAdapters.TryInitializeAdapterByID(id))
    //        {
    //            if (m_iaonSession.AllAdapters.TryGetAnyAdapterByID(id, out adapter, out collection))
    //                SendResponse(requestInfo, true, "Adapter \"{0}\" ({1}) was successfully initialized...", adapter.Name, adapter.ID);
    //            else
    //                SendResponse(requestInfo, true, "Adapter ({1}) was successfully initialized...", id);
    //        }
    //        else
    //        {
    //            adapter = GetRequestedAdapter(requestInfo, out collection);

    //            // Initialize specified adapter
    //            if (adapter is null || collection is null)
    //            {
    //                SendResponse(requestInfo, false, "Requested adapter was not found.");
    //            }
    //            else
    //            {
    //                if (collection.TryInitializeAdapterByID(adapter.ID))
    //                    SendResponse(requestInfo, true, "Adapter \"{0}\" ({1}) was successfully initialized...", adapter.Name, adapter.ID);
    //                else
    //                    SendResponse(requestInfo, false, "Adapter \"{0}\" ({1}) failed to initialize.", adapter.Name, adapter.ID);
    //            }
    //        }
    //    }
    //    else
    //    {
    //        // Get specified adapter collection
    //        collection = GetRequestedCollection(requestInfo);

    //        if (collection is null)
    //        {
    //            SendResponse(requestInfo, false, "Requested collection was unavailable.");
    //        }
    //        else
    //        {
    //            DisplayStatusMessage("Initializing all adapters in {0}...", MessageLevel.Info, collection.Name);
    //            collection.Initialize();
    //            DisplayStatusMessage("{0} initialization complete.", MessageLevel.Info, collection.Name);
    //            SendResponse(requestInfo, true);
    //        }
    //    }
    //}

    //private void ListReportsRequestHandler(ClientRequestInfo requestInfo)
    //{
    //    if (requestInfo.Request.Arguments.ContainsHelpRequest)
    //    {
    //        StringBuilder helpMessage = new();

    //        helpMessage.Append("Returns a list of reports that are available from the report location.");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Usage:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       ListReports ReportType [Options]");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   ReportType:".PadRight(20));
    //        helpMessage.Append("Type of the report to process (e.g., Completeness)");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Options:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -?".PadRight(20));
    //        helpMessage.Append("Displays this help message");
    //        helpMessage.AppendLine();

    //        DisplayResponseMessage(requestInfo, helpMessage.ToString());
    //    }
    //    else
    //    {
    //        IReportingProcess reportingProcess;

    //        if (requestInfo.Request.Arguments.Exists("OrderedArg1"))
    //            reportingProcess = m_reportingProcesses.FindReportType(requestInfo.Request.Arguments["OrderedArg1"]);
    //        else
    //            throw new ArgumentException("ReportType not specified.");

    //        if (reportingProcess is null)
    //            throw new ArgumentException($"ReportType \"{requestInfo.Request.Arguments["OrderedArg1"]}\" undefined.");

    //        reportingProcess.CleanReportLocation();

    //        List<string> reportsList = reportingProcess.GetReportsList();
    //        List<string> pendingReportsList = reportingProcess.GetPendingReportsList();
    //        int fileColumnWidth = Math.Max(6, reportsList.Concat(pendingReportsList).Select(report => report.Length).DefaultIfEmpty(0).Max()) / 4 * 4 + 4;

    //        StringBuilder listBuilder = new();
    //        listBuilder.Append("Report".PadRight(fileColumnWidth));
    //        listBuilder.AppendLine("Status");
    //        listBuilder.Append("------".PadRight(fileColumnWidth));
    //        listBuilder.AppendLine("------");

    //        foreach (string report in reportsList)
    //        {
    //            string reportPath = FilePath.GetAbsolutePath(Path.Combine(reportingProcess.ReportLocation, report));
    //            listBuilder.Append(report.PadRight(fileColumnWidth));

    //            FileInfo info = new(reportPath);
    //            TimeSpan expiration = TimeSpan.FromDays(reportingProcess.IdleReportLifetime) - (DateTime.UtcNow - info.LastAccessTimeUtc);

    //            if (expiration.TotalSeconds <= 0.0D)
    //                listBuilder.Append("Expired");
    //            if (expiration.TotalSeconds < 60.0D)
    //                listBuilder.AppendFormat("Expires in {0:0} seconds", expiration.TotalSeconds);
    //            else if (expiration.TotalMinutes < 60.0D)
    //                listBuilder.AppendFormat("Expires in {0:0} minutes", expiration.TotalMinutes);
    //            else if (expiration.TotalHours < 24.0D)
    //                listBuilder.AppendFormat("Expires in {0:0} hours", expiration.TotalHours);
    //            else
    //                listBuilder.AppendFormat("Expires in {0:0} days", expiration.TotalDays);

    //            listBuilder.AppendLine();
    //        }

    //        foreach (string report in pendingReportsList)
    //        {
    //            listBuilder.Append(report.PadRight(fileColumnWidth));
    //            listBuilder.AppendLine("Pending");
    //        }

    //        SendResponse(requestInfo, true, listBuilder.ToString());
    //    }
    //}

    //private void GetReportRequestHandler(ClientRequestInfo requestInfo)
    //{
    //    if (requestInfo.Request.Arguments.ContainsHelpRequest || requestInfo.Request.Arguments.OrderedArgCount == 0)
    //    {
    //        StringBuilder helpMessage = new();

    //        helpMessage.Append("Gets a report for the specified date.");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Usage:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       GetReport ReportType [ReportDate] [Options]");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   ReportType:".PadRight(20));
    //        helpMessage.Append("Type of the report to process (e.g., Completeness)");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   ReportDate:".PadRight(20));
    //        helpMessage.Append("Date of the report to be generated (yyyy-MM-dd), or yesterday if not specified");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Options:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -?".PadRight(20));
    //        helpMessage.Append("Displays this help message");
    //        helpMessage.AppendLine();

    //        DisplayResponseMessage(requestInfo, helpMessage.ToString());
    //    }
    //    else
    //    {
    //        IReportingProcess reportingProcess;

    //        if (requestInfo.Request.Arguments.Exists("OrderedArg1"))
    //            reportingProcess = m_reportingProcesses.FindReportType(requestInfo.Request.Arguments["OrderedArg1"]);
    //        else
    //            throw new ArgumentException("ReportType not specified.");

    //        if (reportingProcess is null)
    //            throw new ArgumentException($"ReportType \"{requestInfo.Request.Arguments["OrderedArg1"]}\" undefined.");

    //        DateTime now = DateTime.UtcNow;
    //        DateTime today = DateTime.Parse(now.ToString("yyyy-MM-dd"));

    //        try
    //        {
    //            if (!requestInfo.Request.Arguments.Exists("OrderedArg2") || !DateTime.TryParse(requestInfo.Request.Arguments["OrderedArg2"], out DateTime reportDate))
    //                reportDate = today - TimeSpan.FromDays(1);

    //            if (reportDate < today)
    //            {
    //                string reportPath = FilePath.GetAbsolutePath(Path.Combine(reportingProcess.ReportLocation, $"{reportingProcess.Title} {reportDate:yyyy-MM-dd}.pdf"));

    //                if (File.Exists(reportPath))
    //                {
    //                    File.SetLastAccessTimeUtc(reportPath, DateTime.UtcNow);
    //                    SendResponseWithAttachment(requestInfo, true, File.ReadAllBytes(reportPath), $"Found and returned report for {reportDate:yyyy-MM-dd}.");
    //                }
    //                else
    //                {
    //                    SendResponse(requestInfo, false, $"Unable to find report for {reportDate:yyyy-MM-dd}.");
    //                }
    //            }
    //            else
    //            {
    //                SendResponse(requestInfo, false, $"[{now:yyyy-MM-dd HH:mm:ss}] Unable to generate report for {reportDate:yyyy-MM-dd} until {reportDate + TimeSpan.FromDays(1):yyyy-MM-dd}. The statistics archive is not fully populated for that date.");
    //            }
    //        }
    //        catch (Exception ex)
    //        {
    //            SendResponse(requestInfo, false, "Unable to generate report due to exception: {0}", ex.Message);
    //        }
    //    }
    //}

    //private void GetReportStatusRequestHandler(ClientRequestInfo requestInfo)
    //{
    //    if (requestInfo.Request.Arguments.ContainsHelpRequest || requestInfo.Request.Arguments.OrderedArgCount == 0)
    //    {
    //        StringBuilder helpMessage = new();

    //        helpMessage.Append("Gets a reporting process status.");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Usage:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       GetReportStatus ReportType [Options]");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   ReportType:".PadRight(20));
    //        helpMessage.Append("Type of the report to process (e.g., Completeness)");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Options:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -?".PadRight(20));
    //        helpMessage.Append("Displays this help message");
    //        helpMessage.AppendLine();

    //        DisplayResponseMessage(requestInfo, helpMessage.ToString());
    //    }
    //    else
    //    {
    //        IReportingProcess reportingProcess;

    //        if (requestInfo.Request.Arguments.Exists("OrderedArg1"))
    //            reportingProcess = m_reportingProcesses.FindReportType(requestInfo.Request.Arguments["OrderedArg1"]);
    //        else
    //            throw new ArgumentException("ReportType not specified.");

    //        if (reportingProcess is null)
    //            throw new ArgumentException($"ReportType \"{requestInfo.Request.Arguments["OrderedArg1"]}\" undefined.");

    //        try
    //        {
    //            SendResponse(requestInfo, true, reportingProcess.Status);
    //        }
    //        catch (Exception ex)
    //        {
    //            SendResponse(requestInfo, false, "Unable to get report status due to exception: {0}", ex.Message);
    //        }
    //    }
    //}

    //private void GenerateReportRequestHandler(ClientRequestInfo requestInfo)
    //{
    //    if (requestInfo.Request.Arguments.ContainsHelpRequest || requestInfo.Request.Arguments.OrderedArgCount == 0)
    //    {
    //        StringBuilder helpMessage = new();

    //        helpMessage.Append("Generates a report for the specified date.");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Usage:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       GenerateReport ReportType [ReportDate] [Options]");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   ReportType:".PadRight(20));
    //        helpMessage.Append("Type of the report to process (e.g., Completeness)");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   ReportDate:".PadRight(20));
    //        helpMessage.Append("Date of the report to be generated (yyyy-MM-dd), or yesterday if not specified");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Options:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -email".PadRight(20));
    //        helpMessage.Append("E-mail generated report, if enabled");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -?".PadRight(20));
    //        helpMessage.Append("Displays this help message");
    //        helpMessage.AppendLine();

    //        DisplayResponseMessage(requestInfo, helpMessage.ToString());
    //    }
    //    else
    //    {
    //        IReportingProcess reportingProcess;

    //        if (requestInfo.Request.Arguments.Exists("OrderedArg1"))
    //            reportingProcess = m_reportingProcesses.FindReportType(requestInfo.Request.Arguments["OrderedArg1"]);
    //        else
    //            throw new ArgumentException("ReportType not specified.");

    //        if (reportingProcess is null)
    //            throw new ArgumentException($"ReportType \"{requestInfo.Request.Arguments["OrderedArg1"]}\" undefined.");

    //        DateTime today = DateTime.UtcNow;
    //        bool emailReport = requestInfo.Request.Arguments.Exists("email");

    //        try
    //        {
    //            if (!requestInfo.Request.Arguments.Exists("OrderedArg2") || !DateTime.TryParse(requestInfo.Request.Arguments["OrderedArg2"], out DateTime reportDate))
    //                reportDate = today - TimeSpan.FromDays(1);

    //            if (reportDate < today)
    //            {
    //                reportingProcess.GenerateReport(reportDate, emailReport);
    //                SendResponse(requestInfo, true, "Report was successfully queued for generation.");
    //            }
    //            else
    //            {
    //                SendResponse(requestInfo, false, $"[{today:yyyy-MM-dd HH:mm:ss}] Unable to generate report for {reportDate:yyyy-MM-dd} until {reportDate + TimeSpan.FromDays(1):yyyy-MM-dd}. The statistics archive is not fully populated for that date.");
    //            }
    //        }
    //        catch (Exception ex)
    //        {
    //            SendResponse(requestInfo, false, "Unable to generate report due to exception: {0}", ex.Message);
    //        }
    //    }
    //}

    //private void ReportingConfigRequestHandler(ClientRequestInfo requestInfo)
    //{
    //    if (requestInfo.Request.Arguments.ContainsHelpRequest)
    //    {
    //        StringBuilder helpMessage = new();

    //        helpMessage.Append("Displays or modifies the configuration of the reporting process.");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Usage:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       ReportingConfig ReportType [Options]");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   ReportType:".PadRight(20));
    //        helpMessage.Append("Type of the report to process (e.g., Completeness)");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Options:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -get".PadRight(20));
    //        helpMessage.Append("Gets the configuration of the reporting process as command-line arguments");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -set Args".PadRight(20));
    //        helpMessage.Append("Sets the configuration of the reporting process using the given command-line arguments");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -?".PadRight(20));
    //        helpMessage.Append("Displays this help message");
    //        helpMessage.AppendLine();

    //        DisplayResponseMessage(requestInfo, helpMessage.ToString());
    //    }
    //    else
    //    {
    //        IReportingProcess reportingProcess;

    //        if (requestInfo.Request.Arguments.Exists("OrderedArg1"))
    //            reportingProcess = m_reportingProcesses.FindReportType(requestInfo.Request.Arguments["OrderedArg1"]);
    //        else
    //            throw new ArgumentException("ReportType not specified.");

    //        if (reportingProcess is null)
    //            throw new ArgumentException($"ReportType \"{requestInfo.Request.Arguments["OrderedArg1"]}\" undefined.");

    //        if (requestInfo.Request.Arguments.Exists("get") || !requestInfo.Request.Arguments.Exists("set"))
    //        {
    //            try
    //            {
    //                string configuration = $"{reportingProcess.GetArguments()} --idleReportLifetime=\" {reportingProcess.IdleReportLifetime} \"";

    //                SendResponse(requestInfo, true, configuration);
    //            }
    //            catch (Exception ex)
    //            {
    //                SendResponse(requestInfo, false, "Unable to send reporting configuration due to exception: {0}", ex.Message);
    //            }
    //        }
    //        else
    //        {
    //            try
    //            {
    //                lock (reportingProcess)
    //                {
    //                    reportingProcess.SetArguments(requestInfo.Request.Arguments);
    //                    reportingProcess.SaveSettings();
    //                    SendResponse(requestInfo, true, "Reporting configuration saved successfully.");
    //                }
    //            }
    //            catch (Exception ex)
    //            {
    //                SendResponse(requestInfo, false, "Unable to save settings reporting configuration due to exception: {0}", ex.Message);
    //            }
    //        }
    //    }
    //}

    ///// <summary>
    ///// Sends remote entry for logging.
    ///// </summary>
    ///// <param name="requestInfo"></param>
    //protected virtual void LogEventRequestHandler(ClientRequestInfo requestInfo)
    //{
    //    if (requestInfo.Request.Arguments.ContainsHelpRequest)
    //    {
    //        StringBuilder helpMessage = new();

    //        helpMessage.Append("Logs remote entry to event log.");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Usage:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       LogEvent [Options]");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Options:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -?".PadRight(20));
    //        helpMessage.Append("Displays this help message");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -Message=\"Event Message\"".PadRight(20));
    //        helpMessage.Append("Specifies message for event log entry (required)");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -Type=[Error|Warning|Information|...]".PadRight(20));
    //        helpMessage.Append("Specifies EventLogEntryType setting (optional)");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -ID=0".PadRight(20));
    //        helpMessage.Append("Specifies application event log ID (optional)");
    //        helpMessage.AppendLine();

    //        DisplayResponseMessage(requestInfo, helpMessage.ToString());
    //    }
    //    else
    //    {
    //        if (requestInfo.Request.Arguments.Exists("Message"))
    //        {
    //            try
    //            {
    //                string message = requestInfo.Request.Arguments["Message"];

    //                if (!(requestInfo.Request.Arguments.TryGetValue(nameof(Type), out string type) && Enum.TryParse(type, out EventLogEntryType entryType)))
    //                    entryType = EventLogEntryType.Information;

    //                if (!(requestInfo.Request.Arguments.TryGetValue("ID", out string id) && ushort.TryParse(id, out ushort eventID)))
    //                    eventID = 0;

    //                EventLog.WriteEntry(ServiceName, message, entryType, eventID);
    //                SendResponse(requestInfo, true, "Successfully wrote event log entry.");
    //            }
    //            catch (Exception ex)
    //            {
    //                SendResponse(requestInfo, false, "Failed to write event log entry: {0}", ex.Message);
    //            }
    //        }
    //        else
    //        {
    //            SendResponse(requestInfo, false, "Failed to write event log entry: required \"message\" parameter was not specified.");
    //        }
    //    }
    //}

    ///// <summary>
    ///// Gets adapter input measurements.
    ///// </summary>
    ///// <param name="requestInfo"></param>
    //protected virtual void GetInputMeasurementsRequestHandler(ClientRequestInfo requestInfo)
    //{
    //    if (requestInfo.Request.Arguments.ContainsHelpRequest)
    //    {
    //        StringBuilder helpMessage = new();

    //        helpMessage.Append("Gets adapter input measurements.");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Usage:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       GetInputMeasurements AdapterName [Options]");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Options:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -?".PadRight(20));
    //        helpMessage.Append("Displays this help message");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -actionable".PadRight(20));
    //        helpMessage.Append("Returns results via an actionable event");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();

    //        DisplayResponseMessage(requestInfo, helpMessage.ToString());
    //    }
    //    else
    //    {
    //        if (requestInfo.Request.Arguments.Exists("OrderedArg1"))
    //        {
    //            IAdapter adapter = GetRequestedAdapter(requestInfo);
    //            IEnumerable<Guid> signalIDs = adapter.InputMeasurementKeys?.Select(key => key.SignalID) ?? Enumerable.Empty<Guid>();

    //            if (requestInfo.Request.Arguments.Exists("actionable"))
    //                SendResponseWithAttachment(requestInfo, true, signalIDs.Select(id => id.ToByteArray()).ToArray(), null);
    //            else
    //                SendResponse(requestInfo, true, string.Join(";", signalIDs.Select(id => id.ToString())));
    //        }
    //        else
    //        {
    //            SendResponse(requestInfo, false, "No target adapter was specified.");
    //        }
    //    }
    //}

    ///// <summary>
    ///// Gets adapter output measurements.
    ///// </summary>
    ///// <param name="requestInfo"></param>
    //protected virtual void GetOutputMeasurementsRequestHandler(ClientRequestInfo requestInfo)
    //{
    //    if (requestInfo.Request.Arguments.ContainsHelpRequest)
    //    {
    //        StringBuilder helpMessage = new();

    //        helpMessage.Append("Gets adapter output measurements.");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Usage:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       GetOutputMeasurements AdapterName [Options]");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Options:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -?".PadRight(20));
    //        helpMessage.Append("Displays this help message");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -actionable".PadRight(20));
    //        helpMessage.Append("Returns results via an actionable event");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();

    //        DisplayResponseMessage(requestInfo, helpMessage.ToString());
    //    }
    //    else
    //    {
    //        if (requestInfo.Request.Arguments.Exists("OrderedArg1"))
    //        {
    //            IAdapter adapter = GetRequestedAdapter(requestInfo);
    //            IEnumerable<Guid> signalIDs = adapter.OutputMeasurements?.Select(m => m.Key.SignalID) ?? Enumerable.Empty<Guid>();

    //            if (requestInfo.Request.Arguments.Exists("actionable"))
    //                SendResponseWithAttachment(requestInfo, true, signalIDs.Select(id => id.ToByteArray()).ToArray(), null);
    //            else
    //                SendResponse(requestInfo, true, string.Join(";", signalIDs.Select(id => id.ToString())));
    //        }
    //        else
    //        {
    //            SendResponse(requestInfo, false, "No target adapter was specified.");
    //        }
    //    }
    //}

    ///// <summary>
    ///// Determines support for temporal processing from existing adapters.
    ///// </summary>
    ///// <param name="requestInfo"><see cref="ClientRequestInfo"/> instance containing the client request.</param>
    //protected virtual void TemporalSupportRequestHandler(ClientRequestInfo requestInfo)
    //{
    //    if (requestInfo.Request.Arguments.ContainsHelpRequest)
    //    {
    //        StringBuilder helpMessage = new();

    //        helpMessage.Append("Checks support for temporal processing.");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Usage:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       TemporalSupport [Options]");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Options:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -?".PadRight(20));
    //        helpMessage.Append("Displays this help message");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -I".PadRight(20));
    //        helpMessage.Append("Checks support for input adapters (default)");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -A".PadRight(20));
    //        helpMessage.Append("Checks support for action adapters");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -O".PadRight(20));
    //        helpMessage.Append("Checks support for output adapters");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -System".PadRight(20));
    //        helpMessage.Append("Checks support for any adapter");
    //        helpMessage.AppendLine();

    //        DisplayResponseMessage(requestInfo, helpMessage.ToString());
    //    }
    //    else
    //    {
    //        if (requestInfo.Request.Arguments.Exists(nameof(System)))
    //        {
    //            bool supported = m_iaonSession.TemporalProcessingSupportExists();
    //            SendResponseWithAttachment(requestInfo, true, supported, "Temporal processing support {0}", supported ? "exists" : "does not exist");
    //        }
    //        else
    //        {
    //            string collectionName = GetRequestedCollection(requestInfo).DataMember;
    //            bool supported = m_iaonSession.TemporalProcessingSupportExists(collectionName);
    //            SendResponseWithAttachment(requestInfo, true, supported, "Temporal processing support {0} for {1}", supported ? "exists" : "does not exist", collectionName);
    //        }
    //    }
    //}

    ///// <summary>
    ///// Recalculates routing tables.
    ///// </summary>
    ///// <param name="requestInfo"><see cref="ClientRequestInfo"/> instance containing the client request.</param>
    //protected virtual void RefreshRoutesRequestHandler(ClientRequestInfo requestInfo)
    //{
    //    if (requestInfo.Request.Arguments.ContainsHelpRequest)
    //    {
    //        StringBuilder helpMessage = new();

    //        helpMessage.Append("Recalculates routing tables.");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Usage:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       RefreshRoutes [Options]");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Options:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -?".PadRight(20));
    //        helpMessage.Append("Displays this help message");

    //        DisplayResponseMessage(requestInfo, helpMessage.ToString());
    //    }
    //    else
    //    {
    //        // Spawn routing table calculation updates
    //        m_iaonSession.RecalculateRoutingTables();
    //        SendResponse(requestInfo, true, "Spawned request to refresh routing tables.");
    //    }
    //}

    ///// <summary>
    ///// Manually reloads system configuration.
    ///// </summary>
    ///// <param name="requestInfo"><see cref="ClientRequestInfo"/> instance containing the client request.</param>
    //protected virtual void ReloadConfigRequestHandler(ClientRequestInfo requestInfo)
    //{
    //    Arguments arguments = requestInfo.Request.Arguments;

    //    ClientRequestInfo invokeInfo = null;
    //    ClientRequestHandler invokeHandler = null;
    //    bool invoking;

    //    if (arguments.ContainsHelpRequest)
    //    {
    //        StringBuilder helpMessage = new();

    //        helpMessage.Append("Manually reloads system configuration.");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Usage:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       ReloadConfig [Options]");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Options:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -?".PadRight(20));
    //        helpMessage.Append("Displays this help message");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -Invoke=\"Command [Args]\"".PadRight(20));
    //        helpMessage.AppendFormat("Invokes another command once the reload config is complete");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -Augmented".PadRight(20));
    //        helpMessage.AppendFormat("Augments the current running configuration from the {0}, if supported", ConfigurationType);
    //        helpMessage.AppendLine();
    //        helpMessage.Append($"       -{ConfigurationType}".PadRight(20));
    //        helpMessage.AppendFormat("Loads configuration from the {0}", ConfigurationType);
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -BinaryCache".PadRight(20));
    //        helpMessage.Append("Loads configuration from the latest cached binary file");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -XmlCache".PadRight(20));
    //        helpMessage.Append("Loads configuration from the latest cached XML file");
    //        helpMessage.AppendLine();

    //        DisplayResponseMessage(requestInfo, helpMessage.ToString());
    //    }
    //    else
    //    {
    //        invoking = arguments.Exists("Invoke");

    //        if (invoking)
    //        {
    //            invokeInfo = new ClientRequestInfo(requestInfo.Sender, ClientRequest.Parse(arguments["Invoke"]));
    //            string resource = invokeInfo.Request.Command;

    //            // Check if remote client has permission to invoke the requested command.
    //            if (m_serviceHelper.SecureRemoteInteractions)
    //            {
    //                // Validate current client principal
    //                if (SecurityProviderUtility.IsResourceSecurable(resource) && !SecurityProviderUtility.IsResourceAccessible(resource, requestInfo.Sender.ClientUser))
    //                    throw new SecurityException($"Access to '{resource}' is denied");
    //            }

    //            invokeHandler = m_serviceHelper.FindClientRequestHandler(resource);
    //        }

    //        if (arguments.Count == (invoking ? 2 : 1))
    //        {
    //            if (!arguments.Exists(ConfigurationType.ToString()) && !arguments.Exists("Augmented") && !arguments.Exists("BinaryCache") && !arguments.Exists("XmlCache"))
    //            {
    //                SendResponse(requestInfo, false, "Invalid argument supplied to ReloadConfig command.");
    //                return;
    //            }
    //        }
    //        else if (arguments.Count > (invoking ? 2 : 1))
    //        {
    //            SendResponse(requestInfo, false, "Invalid arguments supplied to ReloadConfig command.");
    //            return;
    //        }

    //        m_reloadConfigQueue.Add(Tuple.Create(GetReloadConfigType(requestInfo), new Action<bool>(success =>
    //        {
    //            if (success)
    //            {
    //                if (!invoking || invokeHandler is null)
    //                    SendResponse(requestInfo, true, "System configuration was successfully reloaded.");
    //                else
    //                    invokeHandler.HandlerMethod(invokeInfo);
    //            }
    //            else
    //            {
    //                SendResponse(requestInfo, false, "System configuration failed to reload.");
    //            }
    //        })));
    //    }
    //}

    //private string GetReloadConfigType(ClientRequestInfo requestInfo)
    //{
    //    Arguments arguments = requestInfo.Request.Arguments;

    //    if (arguments.Exists("Augmented"))
    //        return "Augmented";

    //    if (arguments.Exists(ConfigurationType.ToString()))
    //        return ConfigurationType.ToString();

    //    if (arguments.Exists("BinaryCache"))
    //        return "BinaryCache";

    //    if (arguments.Exists("XmlCache"))
    //        return "XmlCache";

    //    return nameof(System);
    //}

    ///// <summary>
    ///// Updates an option in the configuration file.
    ///// </summary>
    ///// <param name="requestInfo"><see cref="ClientRequestInfo"/> instance containing the client request.</param>
    //protected virtual void UpdateConfigFileRequestHandler(ClientRequestInfo requestInfo)
    //{
    //    int orderedArgCount = requestInfo.Request.Arguments.OrderedArgCount;
    //    bool listSetting = requestInfo.Request.Arguments.Exists("list");
    //    bool deleteSetting = requestInfo.Request.Arguments.Exists("delete");
    //    bool addSetting = requestInfo.Request.Arguments.Exists("add");

    //    if (requestInfo.Request.Arguments.ContainsHelpRequest || (!listSetting && orderedArgCount < 2) || (!listSetting && !deleteSetting && orderedArgCount < 3))
    //    {
    //        StringBuilder helpMessage = new();

    //        helpMessage.Append("Updates an option in the configuration file.");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Usage:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       UpdateConfigFile [Category Name] -list");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       UpdateConfigFile \"Category Name\" \"Setting Name\" -delete");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       UpdateConfigFile \"Category Name\" \"Setting Name\" \"Setting Value\" [-add]");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Options:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -?".PadRight(20));
    //        helpMessage.Append("Displays this help message");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -add".PadRight(20));
    //        helpMessage.Append("Adds specified setting to the specified category");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -delete".PadRight(20));
    //        helpMessage.Append("Deletes specified setting from the specified category");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -list".PadRight(20));
    //        helpMessage.Append("Lists categories or settings under a specified category");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();

    //        DisplayResponseMessage(requestInfo, helpMessage.ToString());
    //    }
    //    else
    //    {
    //        string categoryName = requestInfo.Request.Arguments["OrderedArg1"];
    //        string settingName = requestInfo.Request.Arguments["OrderedArg2"];
    //        string settingValue = requestInfo.Request.Arguments["OrderedArg3"];

    //        ConfigurationFile config = ConfigurationFile.Current;

    //        if (listSetting)
    //        {
    //            if (orderedArgCount == 0)
    //            {
    //                StringBuilder categoryList = new();
    //                categoryList.Append("List of categories in the configuration file:");
    //                categoryList.AppendLine();
    //                categoryList.AppendLine();

    //                string xml = config.Settings.SectionInformation.GetRawXml();
    //                XmlDocument xmlDoc = new();
    //                xmlDoc.LoadXml(xml);

    //                // List settings categories.
    //                if (xmlDoc.DocumentElement is not null)
    //                {
    //                    foreach (XmlNode node in xmlDoc.DocumentElement)
    //                    {
    //                        categoryList.Append("   ");
    //                        categoryList.Append(node.Name);
    //                        categoryList.AppendLine();
    //                    }
    //                }

    //                categoryList.AppendLine();
    //                DisplayResponseMessage(requestInfo, categoryList.ToString());
    //            }
    //            else
    //            {
    //                CategorizedSettingsElementCollection settings = config.Settings[categoryName];
    //                StringBuilder settingsList = new();
    //                settingsList.Append($"List of settings under the category {categoryName}:");
    //                settingsList.AppendLine();
    //                settingsList.AppendLine();

    //                // List settings under specified category.
    //                foreach (CategorizedSettingsElement settingsElement in settings)
    //                {
    //                    // Skip encrypted settings for security purpose.
    //                    if (settingsElement.Encrypted)
    //                        continue;

    //                    settingsList.Append("   Name:        ");
    //                    settingsList.Append(settingsElement.Name);
    //                    settingsList.AppendLine();
    //                    settingsList.Append("   Value:       ");
    //                    settingsList.Append(settingsElement.Value);
    //                    settingsList.AppendLine();
    //                    settingsList.Append("   Description: ");
    //                    settingsList.Append(settingsElement.Description);
    //                    settingsList.AppendLine();
    //                    settingsList.AppendLine();
    //                }

    //                settingsList.Replace("{", "{{{{");
    //                settingsList.Replace("}", "}}}}");

    //                DisplayResponseMessage(requestInfo, settingsList.ToString());
    //            }
    //        }
    //        else
    //        {
    //            CategorizedSettingsElementCollection settings = config.Settings[categoryName];
    //            CategorizedSettingsElement setting = settings[settingName];

    //            if (deleteSetting)
    //            {
    //                // Delete existing setting.
    //                if (setting is null)
    //                {
    //                    SendResponse(requestInfo, false, "Failed to delete setting \"{0}\" under category \"{1}\". Setting does not exist.\r\n\r\n", settingName, categoryName);
    //                }
    //                else
    //                {
    //                    settings.Remove(setting);
    //                    config.Save();
    //                    SendResponse(requestInfo, true, "Successfully deleted setting \"{0}\" under category \"{1}\".\r\n\r\n", settingName, categoryName);
    //                }
    //            }
    //            else if (addSetting)
    //            {
    //                // Add new setting.
    //                if (setting is null)
    //                {
    //                    settings.Add(settingName, settingValue);
    //                    config.Save();
    //                    SendResponse(requestInfo, true, "Successfully added setting \"{0}\" under category \"{1}\".\r\n\r\n", settingName, categoryName);
    //                }
    //                else
    //                {
    //                    SendResponse(requestInfo, false, "Failed to add setting \"{0}\" under category \"{1}\". Setting already exists.\r\n\r\n", settingName, categoryName);
    //                }
    //            }
    //            else
    //            {
    //                // Update existing setting.
    //                if (setting is null)
    //                {
    //                    SendResponse(requestInfo, false, "Failed to update value of setting \"{0}\" under category \"{1}\" . Setting does not exist.\r\n\r\n", settingName, categoryName);
    //                }
    //                else
    //                {
    //                    setting.Value = settingValue;
    //                    config.Save();
    //                    SendResponse(requestInfo, true, "Successfully updated setting \"{0}\" under category \"{1}\".\r\n\r\n", settingName, categoryName);
    //                }
    //            }
    //        }
    //    }
    //}

    ///// <summary>
    ///// Manages the certificate used by the service.
    ///// </summary>
    ///// <param name="requestInfo"><see cref="ClientRequestInfo"/> instance containing the client request.</param>
    //protected virtual void ManageCertificateHandler(ClientRequestInfo requestInfo)
    //{
    //    bool import = requestInfo.Request.Arguments.Exists(nameof(import));
    //    bool export = requestInfo.Request.Arguments.Exists(nameof(export));
    //    string certificateFile = requestInfo.Request.Arguments["OrderedArg1"];
    //    SecureString password = requestInfo.Request.Arguments["OrderedArg2"].ToSecureString();

    //    if (requestInfo.Request.Arguments.ContainsHelpRequest || !(import ^ export) || string.IsNullOrEmpty(certificateFile) || (password?.Length ?? 0) == 0)
    //    {
    //        StringBuilder helpMessage = new();

    //        helpMessage.Append("Updates an option in the configuration file.");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Usage:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       ManageCertificate { -import | -export } \"Certificate File\" \"Password\"");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Options:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -?".PadRight(20));
    //        helpMessage.Append("Displays this help message");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -import".PadRight(20));
    //        helpMessage.Append("Imports the given certificate into the current user store");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -export".PadRight(20));
    //        helpMessage.Append("Exports the current certificate from the user store");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();

    //        DisplayResponseMessage(requestInfo, "{0}", helpMessage);
    //    }
    //    else
    //    {
    //        ConfigurationFile configurationFile = ConfigurationFile.Current;
    //        CategorizedSettingsElementCollection remotingServer = configurationFile.Settings[nameof(remotingServer)];
    //        remotingServer.Add("CertificateFile", $"{ServiceName}.cer", "Path to the local certificate used by this server for authentication.");

    //        string certificatePath = FilePath.GetAbsolutePath(remotingServer["CertificateFile"].Value);

    //        CertificateGenerator certificateGenerator = new()
    //        {
    //            Issuer = ServiceName,
    //            CertificatePath = certificatePath
    //        };

    //        if (import)
    //            certificateGenerator.ImportCertificateWithPrivateKey(certificateFile, password);

    //        if (export)
    //            certificateGenerator.ExportCertificateWithPrivateKey(certificateFile, password);

    //        SendResponse(requestInfo, true);
    //    }
    //}

    ///// <summary>
    ///// Attempts to authenticate or re-authenticate to network shares.
    ///// </summary>
    ///// <param name="requestInfo"><see cref="ClientRequestInfo"/> instance containing the client request.</param>
    //protected virtual void AuthenticateRequestHandler(ClientRequestInfo requestInfo)
    //{
    //    if (requestInfo.Request.Arguments.ContainsHelpRequest)
    //    {
    //        StringBuilder helpMessage = new();

    //        helpMessage.Append("Attempts to (re)authenticate to network shares.");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Usage:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       Authenticate [Options]");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Options:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -?".PadRight(20));
    //        helpMessage.Append("Displays this help message");

    //        DisplayResponseMessage(requestInfo, helpMessage.ToString());
    //    }
    //    else
    //    {
    //        DisplayStatusMessage("Attempting to re-authenticate network shares for health and status exports...", MessageLevel.Info);

    //        try
    //        {
    //            m_healthExporter.Initialize();
    //            m_statusExporter.Initialize();
    //            SendResponse(requestInfo, true);
    //        }
    //        catch (Exception ex)
    //        {
    //            SendResponse(requestInfo, false, "Failed to re-authenticate network shares: {0}", ex.Message);
    //            LogException(ex);
    //        }
    //    }
    //}

    ///// <summary>
    ///// Attempts to restart the hose service.
    ///// </summary>
    ///// <param name="requestInfo"><see cref="ClientRequestInfo"/> instance containing the client request.</param>
    //protected virtual void RestartServiceHandler(ClientRequestInfo requestInfo)
    //{
    //    if (requestInfo.Request.Arguments.ContainsHelpRequest)
    //    {
    //        StringBuilder helpMessage = new();

    //        helpMessage.Append("Attempts to restart the host service.");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Usage:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       Restart [Options]");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Options:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -?".PadRight(20));
    //        helpMessage.Append("Displays this help message");

    //        DisplayResponseMessage(requestInfo, helpMessage.ToString());
    //    }
    //    else
    //    {
    //        if (m_allowRemoteRestart)
    //        {
    //            DisplayStatusMessage("Attempting to restart host service...", MessageLevel.Info);

    //            try
    //            {
    //                if (GSF.Common.IsPosixEnvironment)
    //                {
    //                    using Process shell = new();

    //                    shell.StartInfo = new ProcessStartInfo(ConsoleApplicationName)
    //                    {
    //                        CreateNoWindow = true,
    //                        Arguments = $"{ServiceName} -restart"
    //                    };

    //                    shell.Start();
    //                }
    //                else
    //                {
    //                    string batchFileName = FilePath.GetAbsolutePath("RestartService.cmd");
    //                    File.WriteAllText(batchFileName, $@"@ECHO OFF{Environment.NewLine}{ConsoleApplicationName} {ServiceName} -restart");

    //                    // This is the only .NET call that will spawn an independent, non-child, process on Windows
    //                    Process.Start(batchFileName);
    //                }

    //                SendResponse(requestInfo, true);
    //            }
    //            catch (Exception ex)
    //            {
    //                SendResponse(requestInfo, false, "Failed to restart host service: {0}", ex.Message);
    //                LogException(ex);
    //            }
    //        }
    //        else
    //        {
    //            DisplayStatusMessage("Remote restart request denied, this is currently disallowed in the system configuration.", MessageLevel.Warning);
    //        }
    //    }
    //}

    ///// <summary>
    ///// Determines whether the requested adapter exists.
    ///// </summary>
    ///// <param name="requestInfo"><see cref="ClientRequestInfo"/> instance containing the client request.</param>
    ///// <returns>True if the requested adapter exists; false otherwise.</returns>
    //private bool RequestedAdapterExists(ClientRequestInfo requestInfo)
    //{
    //    string adapterID = requestInfo.Request.Arguments["OrderedArg1"];
    //    IAdapterCollection collection = GetRequestedCollection(requestInfo);

    //    if (string.IsNullOrWhiteSpace(adapterID))
    //        return false;

    //    adapterID = adapterID.Trim();

    //    if (adapterID.IsAllNumbers() && uint.TryParse(adapterID, out uint id))
    //    {
    //        // Adapter ID is numeric, try numeric lookup by adapter ID in requested collection
    //        if (collection.TryGetAdapterByID(id, out _))
    //            return true;

    //        // Try looking for ID in any collection if all runtime ID's are unique
    //        if (m_uniqueAdapterIDs && m_iaonSession.AllAdapters.TryGetAnyAdapterByID(id, out _, out _))
    //            return true;
    //    }
    //    else
    //    {
    //        // Adapter ID is alpha-numeric, try text-based lookup by adapter name in requested collection
    //        if (collection.TryGetAdapterByName(adapterID, out _))
    //            return true;

    //        // Try looking for adapter name in any collection
    //        if (m_iaonSession.AllAdapters.TryGetAnyAdapterByName(adapterID, out _, out _))
    //            return true;
    //    }

    //    return false;
    //}

    ///// <summary>
    ///// Used to ping a device.
    ///// </summary>
    ///// <param name="requestInfo"><see cref="ClientRequestInfo"/> instance containing the client request.</param>
    ///// <returns>True if the requested adapter exists; false otherwise.</returns>
    //protected virtual void PingRequestHandler(ClientRequestInfo requestInfo)
    //{
    //    if (requestInfo.Request.Arguments.ContainsHelpRequest)
    //    {
    //        StringBuilder helpMessage = new();

    //        helpMessage.Append("Attempts to ping a device.");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Usage:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       Ping [Options]");
    //        helpMessage.AppendLine();
    //        helpMessage.AppendLine();
    //        helpMessage.Append("   Options:");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("       -?".PadRight(20));
    //        helpMessage.Append("Displays this help message");
    //        helpMessage.AppendLine();
    //        helpMessage.Append("        [ID] ".PadRight(20));
    //        helpMessage.Append("Attempts to ping the specified device");

    //        SendResponse(requestInfo, true, helpMessage.ToString());
    //    }
    //    else
    //    {
    //        IAdapter adapter = GetRequestedAdapter(requestInfo);

    //        if (adapter.Settings.TryGetValue("server", out string server))
    //        {
    //            string hostName = server.Split(':')[0];

    //            SendResponse(requestInfo, true, $"Attempting to ping {adapter.Name} @ {hostName}...");

    //            try
    //            {
    //                ProcessStartInfo psi = new(ConsoleApplicationName)
    //                {
    //                    CreateNoWindow = true,
    //                    FileName = "ping",
    //                    Arguments = hostName,
    //                    RedirectStandardOutput = true,
    //                    RedirectStandardError = true
    //                };

    //                Task.Run(() =>
    //                {
    //                    using (Process shell = new())
    //                    {
    //                        shell.StartInfo = psi;

    //                        shell.OutputDataReceived += (_, processArgs) =>
    //                        {
    //                            if (string.IsNullOrWhiteSpace(processArgs.Data))
    //                                return;

    //                            SendResponse(requestInfo, true, processArgs.Data);
    //                        };

    //                        shell.ErrorDataReceived += (_, processArgs) =>
    //                        {
    //                            if (string.IsNullOrWhiteSpace(processArgs.Data))
    //                                return;

    //                            SendResponse(requestInfo, false, processArgs.Data);
    //                        };


    //                        shell.Start();
    //                        shell.BeginOutputReadLine();
    //                        shell.BeginErrorReadLine();

    //                        if (!shell.WaitForExit(30000))
    //                            shell.Kill();
    //                    }

    //                    SendResponse(requestInfo, true);
    //                });
    //            }
    //            catch (Exception ex)
    //            {
    //                SendResponse(requestInfo, false, $"Failed to ping {adapter.Name} @ {hostName}: {ex.Message}");
    //                LogException(ex);
    //            }
    //        }
    //        else
    //        {
    //            SendResponse(requestInfo, false, "Unable to ping this adapter because it does not have a host name or IP address defined by the 'server' connection string parameter.");
    //        }
    //    }
    //}

    #endregion

    #region [ Broadcast Message Handling ]

    ///// <summary>
    ///// Sends an actionable response to client.
    ///// </summary>
    ///// <param name="requestInfo"><see cref="ClientRequestInfo"/> instance containing the client request.</param>
    ///// <param name="success">Flag that determines if this response to client request was a success.</param>
    //protected virtual void SendResponse(ClientRequestInfo requestInfo, bool success) =>
    //    SendResponseWithAttachment(requestInfo, success, null, null);

    ///// <summary>
    ///// Sends an actionable response to client with a formatted message.
    ///// </summary>
    ///// <param name="requestInfo"><see cref="ClientRequestInfo"/> instance containing the client request.</param>
    ///// <param name="success">Flag that determines if this response to client request was a success.</param>
    ///// <param name="status">Formatted status message to send with response.</param>
    ///// <param name="args">Arguments of the formatted status message.</param>
    //protected virtual void SendResponse(ClientRequestInfo requestInfo, bool success, string status, params object[] args) =>
    //    SendResponseWithAttachment(requestInfo, success, null, status, args);

    ///// <summary>
    ///// Sends an actionable response to client with a formatted message and attachment.
    ///// </summary>
    ///// <param name="requestInfo"><see cref="ClientRequestInfo"/> instance containing the client request.</param>
    ///// <param name="success">Flag that determines if this response to client request was a success.</param>
    ///// <param name="attachment">Attachment to send with response.</param>
    ///// <param name="status">Formatted status message to send with response.</param>
    ///// <param name="args">Arguments of the formatted status message.</param>
    //protected virtual void SendResponseWithAttachment(ClientRequestInfo requestInfo, bool success, object attachment, string status, params object[] args)
    //{
    //    try
    //    {
    //        // Send actionable response
    //        m_serviceHelper.SendActionableResponse(requestInfo, success, attachment, status, args);

    //        // Log details of client request as well as response
    //        if (!m_serviceHelper.LogStatusUpdates || !m_serviceHelper.StatusLog.IsOpen)
    //            return;

    //        string responseType = $"{requestInfo.Request.Command}:{(success ? "Success" : "Failure")}";
    //        string arguments = requestInfo.Request.Arguments.ToString();
    //        string message = $"{responseType}{(string.IsNullOrWhiteSpace(arguments) ? "" : $"({arguments})")}";

    //        if (status is not null)
    //            message = $"{message} - {(args.Length == 0 ? status : string.Format(status, args))}";

    //        m_serviceHelper.StatusLog.WriteTimestampedLine(message);
    //    }
    //    catch (Exception ex)
    //    {
    //        LogException(ex);
    //        m_serviceHelper.UpdateStatus(requestInfo.Sender.ClientID, MessageLevel.Error, $"Failed to send client response due to an exception: {ex.Message}\r\n\r\n");
    //    }
    //}

    ///// <summary>
    ///// Displays a response message to client requester.
    ///// </summary>
    ///// <param name="requestInfo"><see cref="ClientRequestInfo"/> instance containing the client request.</param>
    ///// <param name="status">Formatted status message to send to client.</param>
    ///// <param name="args">Arguments of the formatted status message.</param>
    //protected virtual void DisplayResponseMessage(ClientRequestInfo requestInfo, string status, params object[] args)
    //{
    //    try
    //    {
    //        m_serviceHelper.UpdateStatus(requestInfo.Sender.ClientID, MessageLevel.Info, $"{status}\r\n\r\n", args);
    //    }
    //    catch (Exception ex)
    //    {
    //        LogException(ex);
    //        m_serviceHelper.UpdateStatus(requestInfo.Sender.ClientID, MessageLevel.Error, $"Failed to update client status \"{status.ToNonNullString()}\" due to an exception: {ex.Message}\r\n\r\n");
    //    }
    //}

    /// <summary>
    /// Displays a broadcast message to all subscribed clients.
    /// </summary>
    /// <param name="status">Status message to send to all clients.</param>
    /// <param name="type"><see cref="UpdateType"/> of message to send.</param>
    protected virtual void DisplayStatusMessage(string status, MessageLevel type)
    {
        DisplayStatusMessage(status, type, true);
    }

    /// <summary>
    /// Displays a broadcast message to all subscribed clients.
    /// </summary>
    /// <param name="status">Status message to send to all clients.</param>
    /// <param name="type"><see cref="UpdateType"/> of message to send.</param>
    /// <param name="publishToLog">Determines if messages should be sent logging engine.</param>
    protected virtual void DisplayStatusMessage(string status, MessageLevel type, bool publishToLog)
    {
        try
        {
            status = status.Replace("{", "{{").Replace("}", "}}");

            // Parse out source if available

            Match sourceMatch = m_logSourceMatcher.Match(status);
            UILogMessage message = new()
            {
                TimeStamp = DateTime.UtcNow,
                Message = status,
                Level = type,
                Source = ""
            };

            if (sourceMatch.Success)
            {
                if (!string.IsNullOrEmpty(sourceMatch.Groups["Source"].Value))
                    message.Source = $"[{sourceMatch.Groups["Source"].Value}]";

                if (!string.IsNullOrEmpty(sourceMatch.Groups["SecondSource"].Value))
                    message.Source += $" [{sourceMatch.Groups["SecondSource"].Value}]";
    
                message.Message = message.Message.Replace(message.Source, "").Trim();
            }

            StatusMessage?.SafeInvoke(this, new EventArgs<UILogMessage>(message));

            if (!publishToLog)
                return;

            switch (type)
            {
#if DEBUG
                case MessageLevel.Debug:
#endif
                case MessageLevel.Info:
                    m_logger.LogInformation(status);
                    break;
                case MessageLevel.Warning:
                    m_logger.LogWarning(status);
                    break;
                case MessageLevel.Error:
                case MessageLevel.Critical:
                    m_logger.LogError(status);
                    break;
            }
        }
        catch (Exception ex)
        {
            LogException(ex);
            //m_serviceHelper.UpdateStatus(MessageLevel.Error, $"Failed to update client status \"{status.ToNonNullString()}\" due to an exception: {ex.Message}\r\n\r\n");
        }
    }

    /// <summary>
    /// Displays a broadcast message to all subscribed clients.
    /// </summary>
    /// <param name="status">Formatted status message to send to all clients.</param>
    /// <param name="type"><see cref="UpdateType"/> of message to send.</param>
    /// <param name="args">Arguments of the formatted status message.</param>
    protected virtual void DisplayStatusMessage(string status, MessageLevel type, params object[] args)
    {
        DisplayStatusMessage(status, type, true, args);
    }

    /// <summary>
    /// Displays a broadcast message to all subscribed clients.
    /// </summary>
    /// <param name="status">Formatted status message to send to all clients.</param>
    /// <param name="type"><see cref="UpdateType"/> of message to send.</param>
    /// <param name="publishToLog">Determines if messages should be sent logging engine.</param>
    /// <param name="args">Arguments of the formatted status message.</param>
    protected virtual void DisplayStatusMessage(string status, MessageLevel type, bool publishToLog, params object[] args)
    {
        try
        {
            DisplayStatusMessage(string.Format(status, args), type, publishToLog);
        }
        catch (Exception ex)
        {
            LogException(ex);
            //m_serviceHelper.UpdateStatus(MessageLevel.Error, $"Failed to update client status \"{status.ToNonNullString()}\" due to an exception: {ex.Message}\r\n\r\n");
        }
    }

    /// <summary>
    /// Logs an exception.
    /// </summary>
    /// <param name="ex"><see cref="Exception"/> to log.</param>
    protected virtual void LogException(Exception ex)
    {
        try
        {
            //    // Connection exceptions are logged separately since these types of exceptions
            //    // can be so frequent when a device is offline it makes looking for specific,
            //    // non-connection related, exceptions more difficult.
            //    if (ex is ConnectionException connectionException)
            //        m_serviceHelper.LogConnectionException(connectionException);
            //    else
            //        m_serviceHelper.LogException(ex);

            m_logger.LogError(ex, ex.Message);

            //LoggedException?.SafeInvoke(this, new EventArgs<Exception>(ex));
        }
        catch (Exception logex)
        {
            Logger.CreatePublisher(typeof(ServiceHostBase), MessageClass.Framework)
                .Publish(MessageLevel.Critical, "Log Failure", nameof(LogException), exception: logex);
        }
    }

    // Processes exceptions coming from the configuration loaders.
    private void ConfigurationLoader_ProcessException(object? sender, EventArgs<Exception> args)
    {
        DisplayStatusMessage(args.Argument.Message, MessageLevel.Warning);
        LogException(args.Argument);
    }

    /// <inheritdoc />
    public UILogMessage[] GetFilteredStatusMessages(StringMatchingMode matchMode, string filter, bool caseSensitive = false, double range = 15.0D, MessageLevel minLevel = MessageLevel.Info)
    {
        if (range <= 0.0D)
            throw new ArgumentOutOfRangeException(nameof(range), "Range must be greater than zero minutes.");

        if (range > (m_maxFilteredStatusMessageCacheRange ??= GetMaxFilteredStatusMessageCacheRange()))
            throw new ArgumentOutOfRangeException(nameof(range), $"Range must be less than or equal to {m_maxFilteredStatusMessageCacheRange:N2} minutes.");

        string cacheName = $"{matchMode}:{filter}:{(caseSensitive ? 1 : 0)}:{(int)minLevel}-{range}";
        bool addedNewCache = false;
        
        // Memory ca`che expires after one minute if not accessed
        StringMatcher messageMatcher = MemoryCache<StringMatcher>.GetOrAdd(cacheName, () =>
        {
            addedNewCache = true;
            return new StringMatcher(matchMode, filter, caseSensitive);
        });

        // Synchronize calls for the same cache, first call will initialize status messages from log
        lock (messageMatcher)
        {
            List<UILogMessage> filteredMessages;

            if (!addedNewCache)
                return m_filteredStatusMessages.TryGetValue(cacheName, out filteredMessages) ?
                    filteredMessages.ToArray() :
                    [];

            filteredMessages = new List<UILogMessage>();
            DateTime minTimestamp = DateTime.MaxValue;
            DateTime maxTimestamp = DateTime.MinValue;

            List<UILogMessage> statusLog = m_statusLog.ToList();
            // Process status log in reverse order to get most recent messages first
            statusLog.Reverse();

            // Load initial set of status messages from cached status log
            foreach (UILogMessage message in statusLog)
            {
                // Parse timestamp (local time) in brackets from beginning of log record
                if (string.IsNullOrWhiteSpace(message.Message) || !messageMatcher.IsMatch(message.Source + " " + message.Message) || message.Level < minLevel)
                    continue;

                if (message.TimeStamp < minTimestamp)
                    minTimestamp = message.TimeStamp;

                if (message.TimeStamp > maxTimestamp)
                    maxTimestamp = message.TimeStamp;

                if ((maxTimestamp - minTimestamp).TotalMinutes > range)
                    break;

                filteredMessages.Insert(0, message);
            }

            m_filteredStatusMessages[cacheName] = filteredMessages;

            return filteredMessages.ToArray();
            
        }
    }

    #endregion

    #endregion

    #region [ Static ]

    // Nested Types

    //// Measurement monitor adapter used to expose latest values of specific measurements.
    //// Adapter is added to the internal data publisher to allow for measurements to be
    //// directly routed and exposed as needed.
    //private sealed class MeasurementMonitor : FacileActionAdapterBase
    //{
    //    // ReSharper disable once MemberHidesStaticFromOuterClass
    //    public event EventHandler<EventArgs<IEnumerable<IMeasurement>>> SubscribedMeasurements;

    //    public override string Name
    //    {
    //        get
    //        {
    //            return nameof(MeasurementMonitor);
    //        }
    //        set
    //        {
    //            base.Name = value;
    //        }
    //    }

    //    public override void QueueMeasurementsForProcessing(IEnumerable<IMeasurement> measurements)
    //    {
    //        SubscribedMeasurements?.Invoke(this, new EventArgs<IEnumerable<IMeasurement>>(measurements));
    //    }

    //    public override bool SupportsTemporalProcessing
    //    {
    //        get
    //        {
    //            return false;
    //        }
    //    }

    //    public override string GetShortStatus(int maxLength)
    //    {
    //        return $"{nameof(MeasurementMonitor)} watching {InputMeasurementKeys.Length:N0} signals...";
    //    }
    //}

    // Static Fields
    //private static WeakReference<ServiceHostBase> s_serviceHost;

    // Static Properties

    /// <summary>
    /// Gets or sets the name of the service.
    /// </summary>
    public static string ServiceName { get; set; } = "IaonHost";

    // Gets the current status log lines (as read from file) of the service host.
    //internal static List<string> StatusLog
    //{
    //    get
    //    {
    //        try
    //        {
    //            if (s_serviceHost?.TryGetTarget(out ServiceHostBase reference) ?? false)
    //                return reference.m_serviceHelper.StatusLog.ReadLines(true);
    //        }
    //        catch (Exception ex)
    //        {
    //            Logger.SwallowException(ex);
    //        }

    //        return [];
    //    }
    //}

    // Static Methods

    // Attempts to get the current IaonSession from the service host.
    //private static bool TryGetIaonSession(out IaonSession? session)
    //{
    //    if (s_serviceHost.TryGetTarget(out ServiceHostBase reference) ?? false)
    //    {
    //        if (reference.m_iaonSession is not null)
    //        {
    //            session = reference.m_iaonSession;
    //            return true;
    //        }
    //    }

    //    session = null;
    //    return false;
    //}

    // Attempts to get internal data publisher from the service host.
    //private static bool TryGetDataPublisher(out DataPublisher dataPublisher)
    //{
    //    dataPublisher = null;

    //    if (!TryGetIaonSession(out IaonSession session))
    //        return false;

    //    IEnumerable<IActionAdapter> actionAdapters = session.ActionAdapters;

    //    if (actionAdapters.FirstOrDefault(adapter => adapter.Name.Equals("INTERNAL!DATAPUBLISHER")) is not DataPublisher publisherAdapter)
    //        return false;

    //    dataPublisher = publisherAdapter;
    //    return true;
    //}

    // Attempts to get the measurement monitor from the service host via internal data publisher.
    //private static bool TryGetMeasurementMonitor(out MeasurementMonitor? monitor)
    //{
    //    if (TryGetDataPublisher(out DataPublisher dataPublisher))
    //    {
    //        monitor = GetMeasurementMonitor(dataPublisher);
    //        return true;
    //    }

    //    monitor = null;
    //    return false;
    //}

    // Get the measurement monitor from the provided internal data publisher, or creates it if it doesn't exist.
    //private static MeasurementMonitor GetMeasurementMonitor(DataPublisher dataPublisher)
    //{
    //    if (dataPublisher.TryGetAdapterByName(nameof(MeasurementMonitor), out IActionAdapter? actionAdapter))
    //        return actionAdapter as MeasurementMonitor;

    //    MeasurementMonitor monitor = new();

    //    monitor.DataSource = dataPublisher.DataSource;
    //    dataPublisher.Add(monitor);

    //    monitor.Initialize();
    //    monitor.Initialized = true;
    //    monitor.MeasurementReportingInterval = dataPublisher.MeasurementReportingInterval;

    //    // Start tracking cached measurements
    //    monitor.Start();

    //    return monitor;
    //}

    // Attempts to get any cached measurement value from the service host.
    //internal static bool TryGetCachedValue(MeasurementKey key, out double value)
    //{
    //    value = double.NaN;

    //    if (!TryGetDataPublisher(out DataPublisher dataPublisher))
    //        return false;

    //    MeasurementMonitor monitor = GetMeasurementMonitor(dataPublisher);

    //    // Add measurement key to monitor if it's not already being tracked
    //    if (!monitor.InputMeasurementKeys.Contains(key))
    //    {
    //        monitor.InputMeasurementKeys = monitor.InputMeasurementKeys.Concat(new[] { key }).ToArray();
    //        dataPublisher.RecalculateRoutingTables();
    //    }

    //    // Get the data publisher latest measurement cache, if available - statistics
    //    // will typically be pre-cached. In this case, we can use the cache to quickly
    //    // get the latest value for the measurement key instead of waiting for the next
    //    // measurement to be published:
    //    if (!dataPublisher.TryGetAdapterByName("LatestMeasurementCache", out IActionAdapter actionAdapter))
    //        return false;

    //    if (actionAdapter is not FacileActionAdapterBase cacheAdapter)
    //        return false;

    //    IMeasurement cachedMeasurement = cacheAdapter.LatestMeasurements.FirstOrDefault(measurement => measurement.Key == key);

    //    if (cachedMeasurement is null)
    //        return false;

    //    value = cachedMeasurement.AdjustedValue;
    //    return true;
    //}

    // Remove measurement key from monitoring
    //internal static void RemoveCachedValue(MeasurementKey key)
    //{
    //    if (!TryGetDataPublisher(out DataPublisher dataPublisher))
    //        return;

    //    MeasurementMonitor monitor = GetMeasurementMonitor(dataPublisher);

    //    if (!monitor.InputMeasurementKeys.Contains(key))
    //        return;

    //    // Remove measurement key from monitor
    //    monitor.InputMeasurementKeys = monitor.InputMeasurementKeys.Where(measurementKey => measurementKey != key).ToArray();
    //    dataPublisher.RecalculateRoutingTables();
    //}

    // Static Events

    //// Exposes the status message events from the service host.
    //internal static event EventHandler<EventArgs<string, UpdateType>>? StatusMessage;

    //internal static event EventHandler<EventArgs<Exception>>? LoggedException;

    // Exposes the subscribed measurements event of the MeasurementMonitor in the service host via internal data publisher.
    //internal static event EventHandler<EventArgs<IEnumerable<IMeasurement>>> SubscribedMeasurements
    //{
    //    add
    //    {
    //        if (!TryGetMeasurementMonitor(out MeasurementMonitor monitor))
    //            return;

    //        lock (typeof(ServiceHostBase))
    //            monitor.SubscribedMeasurements += value;
    //    }
    //    remove
    //    {
    //        if (!TryGetMeasurementMonitor(out MeasurementMonitor monitor))
    //            return;

    //        lock (typeof(ServiceHostBase))
    //            monitor.SubscribedMeasurements -= value;
    //    }
    //}

    /// <inheritdoc cref="IDefineSettings.DefineSettings" />
    public static void DefineSettings(Settings settings, string settingsCategory = Settings.SystemSettingsCategory)
    {
        AdoDataConnection.DefineSettings(settings);
        DataProtection.DefineSettings(settings);
        IaonSession.DefineSettings(settings);

        dynamic section = settings[settingsCategory];

        string servicePath = FilePath.GetAbsolutePath("");
        string cachePath = string.Format("{0}{1}ConfigurationCache{1}", servicePath, Path.DirectorySeparatorChar);

        // System settings
        section.SystemName = ("", "Defines the system name for the application instance");
        section.ConfigurationType = (ConfigurationType.Database, "Specifies type of configuration: Database, WebService, BinaryFile or XmlFile");
        section.ConnectionString = ($"Data Source=localhost\\SQLEXPRESS; Initial Catalog={ServiceName}; Integrated Security=SSPI; Connect Timeout=5", "Configuration database connection string");
        section.DataProviderString = ("AssemblyName={System.Data, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089}; ConnectionType=System.Data.SqlClient.SqlConnection; AdapterType=System.Data.SqlClient.SqlDataAdapter", "Configuration database ADO.NET data provider assembly type creation string");
        section.ConfigurationCachePath = (cachePath, "Defines the path used to cache serialized configurations");
        section.CachedConfigurationFile = ("SystemConfiguration.xml", "File name for last known good system configuration (only cached for a Database or WebService connection)");
        section.UniqueAdaptersIDs = ("True", "Set to true if all runtime adapter ID's will be unique to allow for easier adapter specification");
        section.ProcessPriority = (ProcessPriorityClass.High, "Sets desired process priority: Normal, AboveNormal, High, RealTime");
        section.AllowRemoteRestart = ("True", "Controls ability to remotely restart the host service");
        section.DefaultTheme = ("bootstrap.min.css", "The bootstrap theme file to be used if no theme is specified by the user");
        section.SystemFrequency = (60.0D, "Defines the System frequency in Hz");
        section.DefaultFrameRate = (30, "Defines the default frame rate used for adapters in frames per second.");
        section.DefaultLeadTime = (10.0D, "Defines the default lead time used for adapters in seconds.");
        section.DefaultLagTime = (10.0D, "Defines the default lag time used for adapters in seconds.");

        // Classic with condensed phase information
        //private const string DefaultPointTagNameExpression = "{Company.Acronym}_{DeviceAcronym}[?{SignalType.Source}=Phasor[-{SignalType.Suffix}{SignalIndex}]]:[?{Vendor.Acronym}!=[{VendorAcronym}_]][?{Phase}=A[APH]][?{Phase}=B[BPH]][?{Phase}=C[CPH]][?{Phase}=+[PSQ]][?{Phase}=-[NSQ]][?{Phase}=0[ZSQ]]{SignalType.Abbreviation}[?{SignalType.Source}!=Phasor[?{SignalIndex}!=-1[{SignalIndex}]]]";

        // Verbose
        //private const string DefaultPointTagNameExpression = "{Company.Acronym}_{DeviceAcronym}[?{SignalType.Source}=Phasor[-{SignalType.Suffix}{SignalIndex}]]:[?{Vendor.Acronym}!=[{Vendor.Acronym}_]][?{Phase}=A[PhaseA_]][?{Phase}=B[PhaseB_]][?{Phase}=C[PhaseC_]][?{Phase}=+[PosSeq_]][?{Phase}=-[NegSeq_]][?{Phase}=0[ZeroSeq_]][?{SignalType.Acronym}!=STAT[{SignalType.LongAcronym}]][?{SignalType.Acronym}=STAT[{SignalType.Suffix}]][?{SignalType.Source}!=Phasor[?{SignalIndex}!=-1[{SignalIndex}]]]";

        // Verbose without vendor information
        //private const string DefaultPointTagNameExpression = "{Company.Acronym}_{DeviceAcronym}[?{SignalType.Source}=Phasor[-{SignalType.Suffix}{SignalIndex}]]:[?{Phase}=A[PhaseA_]][?{Phase}=B[PhaseB_]][?{Phase}=C[PhaseC_]][?{Phase}=+[PosSeq_]][?{Phase}=-[NegSeq_]][?{Phase}=0[ZeroSeq_]][?{SignalType.Acronym}!=STAT[{SignalType.LongAcronym}]][?{SignalType.Acronym}=STAT[{SignalType.Suffix}]][?{SignalType.Source}!=Phasor[?{SignalIndex}!=-1[{SignalIndex}]]]";

        section.PointTagNameExpression = ("{Company.Acronym}_{DeviceAcronym}[?{SignalType.Source}=Phasor[-{SignalType.Suffix}{SignalIndex}]]:{Vendor.Acronym}{SignalType.Abbreviation}[?{SignalType.Source}!=Phasor[?{SignalIndex}!=-1[{SignalIndex}]]]", "Defines the expression used for point tag naming.");
        section.ExportProfilesDirectory = ("C:\\ProgramData\\openHistorian\\ExportProfiles", "Defines the directory where Export Profile records will be stored.");

        ThreadPool.GetMinThreads(out int minWorkerThreads, out int minIOPortThreads);
        section.MinThreadPoolWorkerThreads = (minWorkerThreads, "Defines the minimum number of allowed thread pool worker threads");
        section.MinThreadPoolIOPortThreads = (minIOPortThreads, "Defines the minimum number of allowed thread pool I/O completion port threads (used by socket layer)");

        ThreadPool.GetMaxThreads(out int maxWorkerThreads, out int maxIOPortThreads);
        section.MaxThreadPoolWorkerThreads = (maxWorkerThreads, "Defines the maximum number of allowed thread pool worker threads");
        section.MaxThreadPoolIOPortThreads = (maxIOPortThreads, "Defines the maximum number of allowed thread pool I/O completion port threads (used by socket layer)");

        section.ConfigurationBackups = (DefaultConfigurationBackups, "Defines the total number of older backup configurations to maintain");
        section.PreferCachedConfiguration = (false, "Set to true to try the cached configuration first, before loading database configuration - typically used when cache is updated by external process");
        section.LocalCertificate = ($"{ServiceName}.cer", "Path to the local certificate used by this server for authentication");
        section.RemoteCertificatesPath = (@"Certs\Remotes", "Path to the directory where remote certificates are stored");
        section.DefaultCulture = ("en-US", "Default culture to use for language, country/region and calendar formats");
        section.CompanyName = ("Grid Protection Alliance", "Defines the company name for the system");
        section.CompanyAcronym = ("GPA", "Defines the company acronym for the system");

        MultipleDestinationExporter.DefineSettings(settings, "HealthExporter");
        MultipleDestinationExporter.DefineSettings(settings, "StatusExporter");
    }

    private static double GetMaxFilteredStatusMessageCacheRange()
    {
        SettingsSection section = Settings.Instance[Settings.SystemSettingsCategory];
        return (double?)section["MaxFilteredStatusMessageCacheRange"] ?? 1000.0D;
    }
    #endregion
}
