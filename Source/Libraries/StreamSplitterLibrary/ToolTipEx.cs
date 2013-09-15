﻿//******************************************************************************************************
//  ToolTipEx.cs - Gbtc
//
//  Copyright © 2010, Grid Protection Alliance.  All Rights Reserved.
//
//  Licensed to the Grid Protection Alliance (GPA) under one or more contributor license agreements. See
//  the NOTICE file distributed with this work for additional information regarding copyright ownership.
//  The GPA licenses this file to you under the Eclipse Public License -v 1.0 (the "License"); you may
//  not use this file except in compliance with the License. You may obtain a copy of the License at:
//
//      http://www.opensource.org/licenses/eclipse-1.0.php
//
//  Unless agreed to in writing, the subject software distributed under the License is distributed on an
//  "AS-IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. Refer to the
//  License for the specific language governing permissions and limitations.
//
//  Code Modification History:
//  ----------------------------------------------------------------------------------------------------
//  09/13/2013 - J. Ritchie Carroll
//       Generated original version of source code.
//
//******************************************************************************************************

using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace StreamSplitter
{
    /// <summary>
    /// Represents a tool-tip provider with the ability to change the drawing font.
    /// </summary>
    public class ToolTipEx : ToolTip
    {
        #region [ Members ]

        // Fields
        private Font m_font;

        #endregion

        #region [ Constructors ]

        /// <summary>
        /// Creates a new <see cref="ToolTipEx"/>.
        /// </summary>
        public ToolTipEx()
        {
            Draw += OnDraw;
            Popup += OnPopup;
        }

        /// <summary>
        /// Creates a new <see cref="ToolTipEx"/>.
        /// </summary>
        public ToolTipEx(IContainer container)
            : this()
        {
        }

        #endregion

        #region [ Properties ]

        /// <summary>
        /// Gets or sets font to use with this tool tip.
        /// </summary>
        [Description("Defines font to use with this tool tip.")]
        public Font Font
        {
            get
            {
                return m_font;
            }
            set
            {
                m_font = value;
                OwnerDraw = ((object)m_font != null);
            }
        }

        #endregion

        #region [ Methods ]

        // Handle custom drawing of tool-tip when a new font is provided
        private void OnPopup(object sender, PopupEventArgs e)
        {
            if (OwnerDraw)
            {
                e.ToolTipSize = TextRenderer.MeasureText(GetToolTip(e.AssociatedControl), m_font);
                e.ToolTipSize = new Size(e.ToolTipSize.Width + 10, e.ToolTipSize.Height + 5);
            }
        }

        private void OnDraw(object sender, DrawToolTipEventArgs e)
        {
            if (OwnerDraw)
            {
                DrawToolTipEventArgs newArgs = new DrawToolTipEventArgs(e.Graphics,
                    e.AssociatedWindow, e.AssociatedControl, e.Bounds, e.ToolTipText,
                    BackColor, ForeColor, m_font);

                newArgs.DrawBackground();
                newArgs.DrawBorder();
                newArgs.DrawText(TextFormatFlags.TextBoxControl);
            }
        }

        #endregion
    }
}