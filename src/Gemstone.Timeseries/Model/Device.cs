﻿//******************************************************************************************************
//  Device.cs - Gbtc
//
//  Copyright © 2020, Grid Protection Alliance.  All Rights Reserved.
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
//  01/21/2020 - J. Ritchie Carroll
//       Generated original version of source code.
//  11/09/2023 - Lillian Gensolin
//       Converted code to .NET core.
//
//******************************************************************************************************

#pragma warning disable 1591

using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Gemstone.ComponentModel.DataAnnotations;
using Gemstone.Data.Model;
using Gemstone.Expressions.Evaluator;
using Gemstone.Expressions.Model;

namespace Gemstone.Timeseries.Model;

[PrimaryLabel(nameof(Acronym))]
public class Device
{
    //[DefaultValueExpression("Global.NodeID")]
    //public Guid NodeID { get; set; }

    [Label("Local Device ID")]
    [PrimaryKey(true)]
    public int ID { get; set; }

    public int? ParentID { get; set; }

    [Label("Unique Device ID")]
    [DefaultValueExpression("Guid.NewGuid()")]
    public Guid UniqueID { get; set; }

    [Required]
    [StringLength(200)]
    [AcronymValidation]
    //[Searchable]
    public string Acronym { get; set; }

    [StringLength(200)]
    public string Name { get; set; }

    [Label("Folder Name")]
    [StringLength(20)]
    public string OriginalSource { get; set; }

    [Label("Is Concentrator")]
    public bool IsConcentrator { get; set; }

    [Required]
    [Label("Company")]
    [DefaultValueExpression("Connection.ExecuteScalar(typeof(int?), 0, \"SELECT ID FROM Company WHERE Acronym = {0}\", Global.CompanyAcronym)", Cached = true)]
    public int? CompanyID { get; set; }

    [Label(nameof(Historian))]
    public int? HistorianID { get; set; }

    [Label("Access ID")]
    public int AccessID { get; set; }

    [Label("Vendor Device")]
    public int? VendorDeviceID { get; set; }

    public decimal Longitude { get; set; }

    public decimal Latitude { get; set; }

    [Label("Interconnection")]
    [InitialValueScript("1")]
    public int? InterconnectionID { get; set; }

    [Label("Connection String")]
    [DefaultValue("")]
    public string ConnectionString { get; set; }

    [DefaultValue("")]
    public string Description { get; set; } = "";

    [StringLength(200)]
    [DefaultValue("UTC")]
    public string TimeZone { get; set; }

    public long TimeAdjustmentTicks { get; set; }

    [Label("Contacts")]
    [DefaultValue("")]
    public string ContactList { get; set; }

    public int LoadOrder { get; set; }

    public bool Subscribed { get; set; }

    [DefaultValue(true)]
    public bool Enabled { get; set; }

    [DefaultValueExpression("DateTime.UtcNow")]
    public DateTime CreatedOn { get; set; }

    [Required]
    [StringLength(50)]
    [DefaultValueExpression("UserInfo.CurrentUserID")]
    public string CreatedBy { get; set; }

    [DefaultValueExpression("this.CreatedOn", EvaluationOrder = 1)]
    [UpdateValueExpression("DateTime.UtcNow")]
    public DateTime UpdatedOn { get; set; }

    [Required]
    [StringLength(50)]
    [DefaultValueExpression("this.CreatedBy", EvaluationOrder = 1)]
    [UpdateValueExpression("UserInfo.CurrentUserID")]
    public string UpdatedBy { get; set; }

    static Device()
    {
        TypeRegistry registry = ValueExpressionParser.DefaultTypeRegistry;

        // Use a proxy global settings reference, as required by Device model,
        // if TSL host application has not already defined one. This is
        // commonly only defined when host has a self-hosted web interface.
        if (registry["Global"] is null)
            registry.RegisterSymbol("Global", GlobalSettings.Default);

    }
}
