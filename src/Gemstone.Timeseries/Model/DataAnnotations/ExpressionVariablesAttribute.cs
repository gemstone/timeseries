//******************************************************************************************************
//  FilterMeasurementAttribute.cs - Gbtc
//
//  Copyright © 2025, Grid Protection Alliance.  All Rights Reserved.
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
//  10/24/2025 - Preston Crawford
//       Generated original version of source code
//
//******************************************************************************************************


using System;

namespace Gemstone.Timeseries.Model.DataAnnotations;

/// <summary>
/// Specifies the available variables that can be used for an expression-based property.
/// </summary>
/// <remarks>
/// This attribute is used to define which variables are available for substitution
/// when parsing expressions. Variables are typically referenced in expressions
/// using a placeholder syntax such as <c>{VariableName}</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class ExpressionVariablesAttribute : Attribute
{
    /// <summary>
    /// Gets the array of variable names available for use in the expression.
    /// </summary>
    public string[] Variables { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExpressionVariablesAttribute"/> class.
    /// </summary>
    /// <param name="variables">The variable names available for substitution in the expression.</param>
    public ExpressionVariablesAttribute(params string[] variables)
    {
        Variables = variables;
    }
}
