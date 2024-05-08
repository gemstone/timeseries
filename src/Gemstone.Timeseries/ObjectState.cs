﻿//******************************************************************************************************
//  ObjectState.cs - Gbtc
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
//  05/23/2007 - Pinal C. Patel
//       Generated original version of source code.
//  09/09/2008 - J. Ritchie Carroll
//       Converted to C#.
//  11/05/2008 - Pinal C. Patel
//       Edited code comments.
//  09/14/2009 - Stephen C. Wills
//       Added new header and license agreement.
//  12/14/2012 - Starlynn Danyelle Gilliam
//       Modified Header.
//
//******************************************************************************************************

using System;

namespace Gemstone.Timeseries;

/// <summary>
/// A serializable class that can be used to track the current and previous state of an object.
/// </summary>
/// <typeparam name="TState">Type of the state to track.</typeparam>
[Serializable]
// TODO: Move out of Timeseries
public class ObjectState<TState>
{
    #region [ Members ]

    // Fields
    private string m_objectName;
    private TState m_currentState;
    private TState m_previousState;

    #endregion

    #region [ Constructors ]

    /// <summary>
    /// Initializes a new instance of the <see cref="ObjectState{TState}"/> class.
    /// </summary>
    /// <param name="objectName">The text label for the object whose state is being tracked.</param>
    public ObjectState(string objectName)
        : this(objectName, default(TState))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ObjectState{TState}"/> class.
    /// </summary>
    /// <param name="objectName">The text label for the object whose state is being tracked.</param>
    /// <param name="currentState">The current state of the object.</param>
    public ObjectState(string objectName, TState currentState)
        : this(objectName, currentState, default(TState))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ObjectState{TState}"/> class.
    /// </summary>
    /// <param name="objectName">The text label for the object whose state is being tracked.</param>
    /// <param name="currentState">The current state of the object.</param>
    /// <param name="previousState">The previous state of the object.</param>
    public ObjectState(string objectName, TState currentState, TState previousState)
    {
        this.ObjectName = objectName;
        this.CurrentState = currentState;
        this.PreviousState = previousState;
    }

    #endregion

    #region [ Properties ]

    /// <summary>
    /// Gets or sets a text label for the object whose state is being tracked.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value being assigned is a null or empty string.</exception>
    public string ObjectName
    {
        get
        {
            return m_objectName;
        }
        set
        {
            if (string.IsNullOrEmpty(value))
                throw new ArgumentNullException(nameof(value));

            m_objectName = value;
        }
    }

    /// <summary>
    /// Gets or sets the current state of the object.
    /// </summary>
    public TState CurrentState
    {
        get
        {
            return m_currentState;
        }
        set
        {
            m_currentState = value;
        }
    }

    /// <summary>
    /// Gets or sets the previous state of the object.
    /// </summary>
    public TState PreviousState
    {
        get
        {
            return m_previousState;
        }
        set
        {
            m_previousState = value;
        }
    }

    #endregion
}
