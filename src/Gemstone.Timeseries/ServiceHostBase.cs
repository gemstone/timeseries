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

using System;
using System.Data;
using Gemstone.Communication;
using Gemstone.IO;
using Gemstone.Threading.Collections;
using Gemstone.Threading.SynchronizedOperations;
using Gemstone.Timeseries.Adapters;
using Gemstone.Timeseries.Configuration;
using Gemstone.Timeseries.Reports;
using Microsoft.Extensions.Hosting;

namespace Gemstone.Timeseries;

/// <summary>
/// Represents the time-series framework service host.
/// </summary>
public abstract class ServiceHostBase : BackgroundService
{
    #region [ Members ]

    // Constants
    private const int DefaultMinThreadPoolSize = 25;
    private const int DefaultMaxThreadPoolSize = 100;
    private const int DefaultConfigurationBackups = 5;
    private const int DefaultMaxLogFiles = 300;

    internal event EventHandler<EventArgs<Guid, string, UpdateType>>? UpdatedStatus;
    internal event EventHandler<EventArgs<Exception>>? LoggedException;

    // Fields
    private IaonSession m_iaonSession;
    private IConfigurationLoader m_configurationLoader;
    private BinaryFileConfigurationLoader m_binaryCacheConfigurationLoader;
    private XMLConfigurationLoader m_xmlCacheConfigurationLoader;
    private string m_cachedXmlConfigurationFile;
    private string m_cachedBinaryConfigurationFile;
    private int m_configurationBackups;
    private bool m_uniqueAdapterIDs;
    private bool m_allowRemoteRestart;
    private bool m_preferCachedConfiguration;
    private MultipleDestinationExporter m_healthExporter;
    private MultipleDestinationExporter m_statusExporter;
    private ReportingProcessCollection m_reportingProcesses;
    private ProcessQueue<Tuple<string, Action<bool>>> m_reloadConfigQueue;
    private LongSynchronizedOperation m_configurationCacheOperation;
    private volatile DataSet m_latestConfiguration;
    private RunTimeLog m_runTimeLog;

    //private ServiceHelper m_serviceHelper;
    private ServerBase m_remotingServer;

    private bool m_disposed;

    #endregion

    public ServiceHostBase()
    {
        
    }
}
