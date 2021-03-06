﻿//******************************************************************************************************
//  StreamSplitterManager.cs - Gbtc
//
//  Copyright © 2015, Grid Protection Alliance.  All Rights Reserved.
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
//  09/03/2013 - J. Ritchie Carroll
//       Generated original version of source code.
//
//******************************************************************************************************

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using GSF;
using GSF.Configuration;
using GSF.IO;
using GSF.Reflection;
using GSF.ServiceProcess;
using GSF.Windows.Forms;
using Timer = System.Timers.Timer;

namespace StreamSplitter
{
    #region [ Enumerations ]

    /// <summary>
    /// Defines possible states for a save.
    /// </summary>
    public enum SaveState
    {
        /// <summary>
        /// Save completed.
        /// </summary>
        Saved,
        /// <summary>
        /// Save skipped.
        /// </summary>
        Skipped,
        /// <summary>
        /// Save canceled.
        /// </summary>
        Canceled
    }

    #endregion

    /// <summary>
    /// Defines a form to manage proxy connections.
    /// </summary>
    public partial class StreamSplitterManager : Form
    {
        #region [ Members ]

        // Constants
        private const int MaximumToolTipSize = 1500;

        // Fields
        private string m_configurationFileName;
        private bool m_configurationSaved;
        private ProxyConnectionCollection m_proxyConnections;
        private ServiceConnection m_serviceConnection;
        private Timer m_refreshProxyStatusTimer;
        private bool m_lastConnectedState;
        private bool m_configRequested;
        private bool m_loaded;

        #endregion

        #region [ Constructors ]

        /// <summary>
        /// Creates a new <see cref="StreamSplitterManager"/> instance.
        /// </summary>
        public StreamSplitterManager()
        {
            InitializeComponent();

            // Set service connection
            m_serviceConnection = new ServiceConnection();
            m_serviceConnection.StatusMessage += m_serviceConnection_StatusMessage;
            m_serviceConnection.ServiceResponse += m_serviceConnection_ServiceResponse;
            m_serviceConnection.ConnectionState += m_serviceConnection_ConnectionState;

            // Setup status auto-refresh timer
            m_refreshProxyStatusTimer = new Timer();
            m_refreshProxyStatusTimer.Elapsed += m_refreshProxyStatusTimer_Elapsed;
            m_refreshProxyStatusTimer.Interval = 2000;
            m_refreshProxyStatusTimer.AutoReset = true;

            // Update version
            toolStripStatusLabelVersion.Text = AssemblyInfo.EntryAssembly.Version.ToString(3);

            // Set initial save state
            ConfigurationSaved = true;

            // Attempt to load last configuration
            string lastConfigurationFileName = ConfigurationFile.Current.Settings.General["LastConfiguration", true].ValueAs("");

            if (!string.IsNullOrEmpty(lastConfigurationFileName) && File.Exists(lastConfigurationFileName))
                LoadConfiguration(lastConfigurationFileName);

            // If no existing configuration was loaded, create a new one
            if (string.IsNullOrEmpty(m_configurationFileName))
                NewConfiguration();
        }

        #endregion

        #region [ Properties ]

        // Establishes a new proxy connection collection, detaching from an existing one if needed
        private ProxyConnectionCollection ProxyConnections
        {
            set
            {
                if ((object)m_proxyConnections != null)
                {
                    // Detach from events on existing proxy connection collection
                    m_proxyConnections.GotFocus -= m_proxyConnections_GotFocus;
                    m_proxyConnections.EnabledStateChanged -= m_proxyConnections_EnabledStateChanged;
                    m_proxyConnections.ApplyChanges -= m_proxyConnections_ApplyChanges;
                    m_proxyConnections.ConfigurationChanged -= m_proxyConnections_ConfigurationChanged;
                    m_proxyConnections.RemovingItem -= m_proxyConnections_RemovingItem;

                    // Remove controls for existing collection
                    foreach (ProxyConnection connection in m_proxyConnections)
                    {
                        RemoveProxyConnectionEditorControl(connection);
                    }
                }

                // Assign new proxy connection collection
                m_proxyConnections = value;

                if ((object)m_proxyConnections != null)
                {
                    // Attach to events on new proxy connection collection
                    m_proxyConnections.GotFocus += m_proxyConnections_GotFocus;
                    m_proxyConnections.EnabledStateChanged += m_proxyConnections_EnabledStateChanged;
                    m_proxyConnections.ApplyChanges += m_proxyConnections_ApplyChanges;
                    m_proxyConnections.ConfigurationChanged += m_proxyConnections_ConfigurationChanged;
                    m_proxyConnections.RemovingItem += m_proxyConnections_RemovingItem;

                    // Make sure new proxy connection collection gets assigned to binding source
                    bindingSource.DataSource = m_proxyConnections;
                }
            }
        }

        // Sets flag that establishes if configuration is saved
        private bool ConfigurationSaved
        {
            set
            {
                m_configurationSaved = value;
                toolStripButtonSaveConfig.Enabled = (!m_configurationSaved || ((object)m_proxyConnections != null && m_proxyConnections.Count > 0 && string.IsNullOrEmpty(m_configurationFileName)));
            }
        }

        #endregion

        #region [ Methods ]

        private void StreamSplitterManager_Load(object sender, EventArgs e)
        {
            this.RestoreLayout();

            // Start connection cycle
            if ((object)m_serviceConnection != null)
                m_serviceConnection.ConnectAsync();

            Show();
            ThreadPool.QueueUserWorkItem(PostProxyConnectionsLoad);

            m_loaded = true;
        }

        private void StreamSplitterManager_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveConfiguration(true);

            this.SaveLayout();

            // Save last configuration file name
            ConfigurationFile.Current.Settings.General["LastConfiguration", true].Value = m_configurationFileName.ToNonNullString();
            ConfigurationFile.Current.Save();
        }

        private void StreamSplitterManager_Activated(object sender, EventArgs e)
        {
            ProxyConnection currentItem = bindingSource.Current as ProxyConnection;

            if ((object)currentItem != null && (object)currentItem.ProxyConnectionEditor != null)
                SelectProxyConnectionEditorControl(currentItem.ProxyConnectionEditor);

            toolTipNewHelp.Hide(this);
        }

        private void ShowToolTipHelpForEmptyConfiguration()
        {
            if (m_proxyConnections.Count == 0)
                toolTipNewHelp.Show("Click the \"plus\" button to add a new configuration...", this, 425, -10, 10000);
        }

        // Create a new configuration
        private void NewConfiguration()
        {
            // Make sure to save any existing configuration before creating another
            if (SaveConfiguration(true) == SaveState.Canceled)
                return;

            m_configurationFileName = null;
            ProxyConnections = ProxyConnectionCollection.LoadConfiguration(null);
            ConfigurationSaved = true;

            // Change form title to include working file name
            UpdateFormTitle();

            ShowToolTipHelpForEmptyConfiguration();
        }

        // Load existing configuration
        private void LoadConfiguration(string configurationFileName = null)
        {
            // Make to save any existing configuration before loading another
            if (SaveConfiguration(true) == SaveState.Canceled)
                return;

            // Make sure a file name is defined for the configuration
            if (string.IsNullOrEmpty(configurationFileName))
            {
                openFileDialog.FileName = null;

                if (openFileDialog.ShowDialog(this) == DialogResult.OK)
                    m_configurationFileName = openFileDialog.FileName;
                else
                    return;
            }
            else
            {
                m_configurationFileName = configurationFileName;
            }

            try
            {
                Cursor = Cursors.WaitCursor;
                ProxyConnections = ProxyConnectionCollection.LoadConfiguration(m_configurationFileName);

                // Establish an editing user control for each proxy connection
                foreach (ProxyConnection connection in m_proxyConnections)
                {
                    if (!m_loaded)
                        SplashScreen.SetStatus("Loading " + connection.Name + "...", true);

                    // Editing-control creation happens after proxy connection exists and is in the binding list,
                    // so we depend on the PropertyChanged event to attach to needed control events
                    AddProxyConnectionEditorControl(connection);
                }

                ConfigurationSaved = true;

                if (m_loaded)
                    ThreadPool.QueueUserWorkItem(PostProxyConnectionsLoad);

                // Change form title to include working file name
                UpdateFormTitle();

                ShowToolTipHelpForEmptyConfiguration();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load configuration file: " + ex.Message, Tag.ToNonNullString(Text), MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private SaveState SaveConfiguration(bool verify = false, string verificationMessage = null)
        {
            if (m_configurationSaved)
                return SaveState.Saved;

            if (string.IsNullOrWhiteSpace(verificationMessage))
                verificationMessage = "Do you want to save the local configuration?";

            // Make sure configuration needs to be saved - user can choose to skip the save (i.e., discard changes)
            if (verify && MessageBox.Show(string.Format(verificationMessage), Tag.ToNonNullString(Text), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return SaveState.Skipped;

            // Make sure a file name is defined for the configuration
            if (string.IsNullOrEmpty(m_configurationFileName))
            {
                saveFileDialog.FileName = null;

                if (saveFileDialog.ShowDialog(this) == DialogResult.OK)
                    m_configurationFileName = saveFileDialog.FileName;
                else
                    return SaveState.Canceled;
            }

            try
            {
                Cursor = Cursors.WaitCursor;

                lock (m_proxyConnections)
                {
                    ProxyConnectionCollection.SaveConfiguration(m_proxyConnections, m_configurationFileName);
                }

                ConfigurationSaved = true;

                // Change form title to include working file name
                UpdateFormTitle();

                return SaveState.Saved;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save configuration file: " + ex.Message, Tag.ToNonNullString(Text), MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            return SaveState.Canceled;
        }

        private void AddProxyConnectionEditorControl(ProxyConnection connection)
        {
            toolTipNewHelp.Hide(this);

            // Create a new editing control for the proxy connection
            ProxyConnectionEditor editorControl = new ProxyConnectionEditor
            {
                ProxyConnection = connection
            };

            flowLayoutPanelProxyConnections.Controls.Add(editorControl);
            editorControl.buttonApply.Enabled = m_lastConnectedState;
            bindingSource.MoveLast();

            ConfigurationSaved = false;
            Application.DoEvents();
        }

        private void RemoveProxyConnectionEditorControl(ProxyConnection connection)
        {
            if ((object)connection != null)
            {
                ProxyConnectionEditor editorControl = connection.ProxyConnectionEditor;

                if ((object)editorControl != null)
                {
                    flowLayoutPanelProxyConnections.Controls.Remove(editorControl);
                    editorControl.Dispose();
                }
            }

            ConfigurationSaved = false;
            Application.DoEvents();
        }

        // Unselect all existing controls
        private void UnselectProxyConnectionEditorControls()
        {
            if (flowLayoutPanelProxyConnections.Controls.Count > 1)
            {
                foreach (ProxyConnectionEditor control in flowLayoutPanelProxyConnections.Controls)
                {
                    control.Selected = false;
                }
            }
        }

        // Select specified control
        private void SelectProxyConnectionEditorControl(ProxyConnectionEditor editorControl)
        {
            if ((object)editorControl == null || editorControl.Selected)
                return;

            // Unselect all existing controls
            UnselectProxyConnectionEditorControls();

            editorControl.Selected = true;
            flowLayoutPanelProxyConnections.ScrollControlIntoView(editorControl);

            flowLayoutPanelProxyConnections.Refresh();
        }

        private void UpdateToolTip(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return;

            try
            {
                string currentToolTip = Invoke((Func<string>)(() => toolTipEx.GetToolTip(statusStrip))) as string;

                if (string.IsNullOrWhiteSpace(currentToolTip))
                {
                    Invoke((Action)(() => toolTipEx.SetToolTip(statusStrip, ToolTipEx.WordWrapStatus(status))));
                    return;
                }

                currentToolTip += ToolTipEx.WordWrapStatus(status);

                // Truncate from the left to maintain maximum tool-tip size
                if (currentToolTip.Length > MaximumToolTipSize)
                    currentToolTip = currentToolTip.Substring(currentToolTip.Length - MaximumToolTipSize);

                Invoke((Action)(() => toolTipEx.SetToolTip(statusStrip, currentToolTip)));
            }
            catch (ObjectDisposedException)
            {
                // This can happen on shutdown :-p
            }
        }

        private void bindingSource_AddingNew(object sender, System.ComponentModel.AddingNewEventArgs e)
        {
            ProxyConnection connection = new ProxyConnection
            {
                ConnectionString = "name=New Proxy Connection " + (bindingSource.Count + 1)
            };

            e.NewObject = connection;

            // Adding editing control happens before item is added to binding list so
            // control will be available when connection is inserted
            AddProxyConnectionEditorControl(connection);
        }

        private void bindingSource_PositionChanged(object sender, EventArgs e)
        {
            ProxyConnection currentItem = bindingSource.Current as ProxyConnection;

            if ((object)currentItem != null)
                SelectProxyConnectionEditorControl(currentItem.ProxyConnectionEditor);
        }

        private void m_proxyConnections_GotFocus(object sender, EventArgs<ProxyConnection> e)
        {
            if ((object)e != null && (object)e.Argument != null)
            {
                int position = bindingSource.IndexOf(e.Argument);

                if (position > -1 && bindingSource.Position != position)
                    bindingSource.Position = position;
            }
        }

        private void m_proxyConnections_ConfigurationChanged(object sender, EventArgs<ProxyConnection> e)
        {
            ConfigurationSaved = false;
        }

        private void m_proxyConnections_ApplyChanges(object sender, EventArgs<ProxyConnection> e)
        {
            if ((object)m_serviceConnection != null && (object)e != null && (object)e.Argument != null)
                m_serviceConnection.SendCommand("UploadConnection", e.Argument);
        }

        private void m_proxyConnections_EnabledStateChanged(object sender, EventArgs<ProxyConnection, bool> e)
        {
            if ((object)m_serviceConnection != null && (object)e != null && (object)e.Argument1 != null)
                m_serviceConnection.SendCommand("UploadConnection", e.Argument1);
        }

        private void m_proxyConnections_RemovingItem(object sender, EventArgs<ProxyConnection, bool> e)
        {
            if ((object)e == null)
                return;

            string connectionName;

            if ((object)e.Argument1 == null || string.IsNullOrWhiteSpace(e.Argument1.Name))
                connectionName = "<unnamed proxy connection>";
            else
                connectionName = e.Argument1.Name;

            if (MessageBox.Show(string.Format("Are you sure you want to delete \"{0}\"?", connectionName), Tag.ToNonNullString(Text), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                RemoveProxyConnectionEditorControl(e.Argument1);
            else
                e.Argument2 = false;
        }

        private void toolStripButtonNewConfig_Click(object sender, EventArgs e)
        {
            NewConfiguration();
        }

        private void toolStripButtonLoadConfig_Click(object sender, EventArgs e)
        {
            LoadConfiguration();
        }

        private void toolStripButtonSaveConfig_Click(object sender, EventArgs e)
        {
            SaveConfiguration();
        }

        private void toolStripButtonSaveConfigAs_Click(object sender, EventArgs e)
        {
            saveFileDialog.FileName = null;

            if (saveFileDialog.ShowDialog(this) == DialogResult.OK)
            {
                m_configurationFileName = saveFileDialog.FileName;
                m_configurationSaved = false;
                SaveConfiguration();
            }
        }

        private void toolStripButtonDownloadConfig_Click(object sender, EventArgs e)
        {
            if (SaveConfiguration(true, "Current configuration is not saved. Do you want to save changes to the current configuration before downloading the running service configuration?") != SaveState.Canceled)
            {
                if ((object)m_serviceConnection != null)
                    m_serviceConnection.SendCommand("DownloadConfig");
            }
            else
            {
                MessageBox.Show("Configuration download canceled.", Tag.ToNonNullString(Text), MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            }
        }

        private void toolStripButtonUploadConfig_Click(object sender, EventArgs e)
        {
            // Null proxy connections is not valid, however, a count of zero might be
            if ((object)m_proxyConnections == null)
            {
                MessageBox.Show("Cannot upload configuration, no proxy connections are currently defined.", Tag.ToNonNullString(Text), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (m_proxyConnections.Count == 0)
            {
                if ((object)m_serviceConnection != null && MessageBox.Show("WARNING: You are about to upload an empty configuration as the running service configuration.\r\n\r\nAre you sure you want to upload an empty configuration and clear the running service configuration?", Tag.ToNonNullString(Text), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    m_serviceConnection.SendCommand("UploadConfig", ProxyConnectionCollection.SerializeConfiguration(m_proxyConnections));
            }
            else if (SaveConfiguration(true, "Current configuration must be saved before you upload it. Do you want to save changes to the current configuration?") == SaveState.Saved)
            {
                if ((object)m_serviceConnection != null && MessageBox.Show("Are you sure you want to upload the current local configuration and make it the running service configuration?", Tag.ToNonNullString(Text), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    m_serviceConnection.SendCommand("UploadConfig", ProxyConnectionCollection.SerializeConfiguration(m_proxyConnections));
            }
            else
            {
                MessageBox.Show("Configuration upload canceled.", Tag.ToNonNullString(Text), MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            }
        }

        private void toolStripButtonConnectTo_Click(object sender, EventArgs e)
        {
            using (ConnectTo dialog = new ConnectTo())
            {
                Dictionary<string, string> settings = m_serviceConnection.ConnectionString.ToNonNullString().ParseKeyValuePairs();
                string serverAddressPort, serverInterface;

                if (settings.TryGetValue("server", out serverAddressPort) && !string.IsNullOrWhiteSpace(serverAddressPort))
                    dialog.textBoxServerConnection.Text = serverAddressPort;

                if (!settings.TryGetValue("interface", out serverInterface) || string.IsNullOrWhiteSpace(serverInterface))
                    serverInterface = "0.0.0.0";

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    if ((object)m_serviceConnection != null)
                    {
                        // Apply updated service connection string
                        if (!string.IsNullOrWhiteSpace(dialog.textBoxServerConnection.Text))
                        {
                            string connectionString = "server=" + dialog.textBoxServerConnection.Text + "; interface=" + serverInterface;
                            ThreadPool.QueueUserWorkItem(ConnectToService, connectionString);
                        }
                    }
                    else
                    {
                        MessageBox.Show("Reconnect canceled - service connection is unavailable.", Tag.ToNonNullString(Text), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
        }

        private void toolStripButtonRestartService_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Are you sure you want to restart the service?", Tag.ToNonNullString(Text), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                if ((object)m_serviceConnection != null)
                    m_serviceConnection.SendCommand("Restart");
            }
        }

        private void toolStripButtonGPA_Click(object sender, EventArgs e)
        {
            Process process = new Process
            {
                StartInfo =
                {
                    UseShellExecute = true,
                    FileName = "http://www.gridprotectionalliance.org/"
                }
            };

            process.Start();
        }

        private void bindingNavigatorDeleteItem_MouseEnter(object sender, EventArgs e)
        {
            // Make sure it is obvious which item is currently selected when hovering over the Delete button
            if ((object)m_proxyConnections != null)
            {
                m_proxyConnections.TransparentPanelEnabled = true;
                flowLayoutPanelProxyConnections.Refresh();
            }
        }

        private void bindingNavigatorDeleteItem_MouseLeave(object sender, EventArgs e)
        {
            if ((object)m_proxyConnections != null)
            {
                m_proxyConnections.TransparentPanelEnabled = false;
                flowLayoutPanelProxyConnections.Refresh();
            }
        }

        private void m_serviceConnection_ConnectionState(object sender, EventArgs<bool> e)
        {
            bool connected = e.Argument;

            // Only refresh controls if state has actually changed
            if (connected != m_lastConnectedState)
            {
                m_lastConnectedState = connected;

                BeginInvoke((Action)(() => toolStripStatusLabelState.Text = connected ? "Connected to Service" : "Disconnected from Service"));
                BeginInvoke((Action)(() => toolStripStatusLabelState.Image = imageList.Images[connected ? 0 : 1]));

                if ((object)m_refreshProxyStatusTimer != null)
                    m_refreshProxyStatusTimer.Enabled = connected;

                BeginInvoke((Action)(() => toolStripButtonDownloadConfig.Enabled = connected));
                BeginInvoke((Action)(() => toolStripButtonUploadConfig.Enabled = connected));
                BeginInvoke((Action)(() => toolStripButtonRestartService.Enabled = connected));

                // Apply changes to proxy connection editors based on service connectivity state
                if ((object)m_proxyConnections != null)
                {
                    lock (m_proxyConnections)
                    {
                        foreach (ProxyConnection proxyConnection in m_proxyConnections)
                        {
                            ProxyConnectionEditor editorControl = proxyConnection.ProxyConnectionEditor;

                            if ((object)editorControl != null)
                            {
                                // Reset all connection indication bubbles to gray when disconnected
                                if (!connected)
                                    BeginInvoke((Action)(() => editorControl.ConnectionState = ConnectionState.Disabled));

                                // The apply button should only be enabled when service is connected
                                BeginInvoke((Action)(() => editorControl.buttonApply.Enabled = connected));
                            }
                        }
                    }
                }

                // If we are now connected and no configuration is loaded - we download config from the server
                if (connected)
                {
                    if (!m_configRequested && ((object)m_proxyConnections == null || m_proxyConnections.Count == 0))
                    {
                        m_configRequested = true;

                        // Kick-off request for configuration download from another thread to let event
                        // handler the state change on the service connection complete gracefully
                        ThreadPool.QueueUserWorkItem(state =>
                        {
                            if ((object)m_serviceConnection != null)
                            {
                                if (MessageBox.Show("Connected to service. Would you like to download the current running configuration?", Tag.ToNonNullString(Text), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                                    m_serviceConnection.SendCommand("DownloadConfig");
                            }
                        });
                    }
                }
                else
                {
                    m_configRequested = false;
                }

                BeginInvoke((Action)Refresh);
            }
        }

        private void m_serviceConnection_StatusMessage(object sender, EventArgs<UpdateType, string> e)
        {
            switch (e.Argument1)
            {
                case UpdateType.Warning:
                    BeginInvoke((Action)(() => toolStripStatusLabelStatus.ForeColor = Color.Yellow));
                    break;
                case UpdateType.Alarm:
                    BeginInvoke((Action)(() => toolStripStatusLabelStatus.ForeColor = Color.Red));
                    break;
                default:
                    BeginInvoke((Action)(() => toolStripStatusLabelStatus.ForeColor = SystemColors.ControlText));
                    break;
            }

            BeginInvoke((Action)(() => toolStripStatusLabelStatus.Text = e.Argument2.Replace("\r\n", "  ")));
            UpdateToolTip(e.Argument2);
        }

        private void m_refreshProxyStatusTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if ((object)m_serviceConnection != null && (object)m_proxyConnections != null && m_proxyConnections.Count > 0)
                m_serviceConnection.SendCommand("GetStreamProxyStatus");
        }

        private void m_serviceConnection_ServiceResponse(object sender, EventArgs<ServiceResponse, string, bool> e)
        {
            if ((object)e == null)
                return;

            // Handle service responses
            ServiceResponse response = e.Argument1;
            string sourceCommand = e.Argument2.ToNonNullString().Trim();
            bool responseSuccess = e.Argument3;

            if ((object)response == null || string.IsNullOrWhiteSpace(sourceCommand))
                return;

            // If command has attachments, they will be first followed by an attachment with the original command arguments
            List<object> attachments = response.Attachments;
            bool attachmentsExist = ((object)attachments != null && attachments.Count > 1);

            // Handle command responses
            if (string.Compare(sourceCommand, "GetStreamProxyStatus", true) == 0)
            {
                if (responseSuccess && attachmentsExist)
                    ThreadPool.QueueUserWorkItem(ApplyStreamProxyStatusUpdates, attachments[0] as StreamProxyStatus[]);
            }
            else if (string.Compare(sourceCommand, "DownloadConfig", true) == 0)
            {
                if (responseSuccess && attachmentsExist)
                    ThreadPool.QueueUserWorkItem(ApplyDownloadedProxyConnections, ProxyConnectionCollection.DeserializeConfiguration(attachments[0] as byte[]));
            }
        }

        private void ApplyStreamProxyStatusUpdates(object state)
        {
            StreamProxyStatus[] streamProxies = state as StreamProxyStatus[];
            ProxyConnection proxyConnection;
            ProxyConnectionEditor editorControl;

            if ((object)streamProxies == null || (object)m_proxyConnections == null || m_proxyConnections.Count == 0)
                return;

            // Apply updates for each stream proxy status
            foreach (StreamProxyStatus proxyStatus in streamProxies)
            {
                lock (m_proxyConnections)
                {
                    // Attempt to find associated proxy connection
                    proxyConnection = m_proxyConnections.FirstOrDefault(connection => connection.ID == proxyStatus.ID);
                }

                if ((object)proxyConnection != null)
                {
                    // Get associated editor control for proxy connection
                    editorControl = proxyConnection.ProxyConnectionEditor;

                    if ((object)editorControl != null)
                        BeginInvoke((Action<ProxyConnectionEditor, StreamProxyStatus>)ApplyStreamProxyStatusUpdate, editorControl, proxyStatus);
                }
            }
        }

        private void ApplyStreamProxyStatusUpdate(ProxyConnectionEditor editorControl, StreamProxyStatus proxyStatus)
        {
            editorControl.ConnectionState = proxyStatus.ConnectionState;
            editorControl.ConnectionStatus = proxyStatus.RecentStatusMessages;
        }

        private void ApplyDownloadedProxyConnections(object state)
        {
            Invoke((Action)(() =>
            {
                ProxyConnections = state as ProxyConnectionCollection;

                if ((object)m_proxyConnections != null)
                {
                    if (m_proxyConnections.Count > 0)
                    {
                        // Establish an editing user control for each proxy connection
                        foreach (ProxyConnection connection in m_proxyConnections)
                        {
                            // Editing-control creation happens after proxy connection exists and is in the binding list,
                            // so we depend on the PropertyChanged event to attach to needed control events
                            AddProxyConnectionEditorControl(connection);
                        }

                        ConfigurationSaved = false;

                        ThreadPool.QueueUserWorkItem(PostProxyConnectionsLoad);
                    }
                    else
                    {
                        MessageBox.Show("The service has no running configuration, click the \"+\" tool bar button to create a new proxy connection.", Tag.ToNonNullString(Text), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }

                // Mark new configuration as unsaved
                m_configurationFileName = null;
                UpdateFormTitle();
            }));
        }

        private void PostProxyConnectionsLoad(object state)
        {
            Invoke((Action)bindingSource.MoveFirst);

            ProxyConnection proxyConnection = bindingSource.Current as ProxyConnection;

            if ((object)proxyConnection != null)
                Invoke((Action<ProxyConnectionEditor>)SelectProxyConnectionEditorControl, proxyConnection.ProxyConnectionEditor);

            Invoke((Action)Activate);
            Invoke((Action)ShowToolTipHelpForEmptyConfiguration);
        }

        private void ConnectToService(object state)
        {
            string connectionString = state as string;

            if (!string.IsNullOrWhiteSpace(connectionString) && (object)m_serviceConnection != null)
            {
                // Update connection string
                m_serviceConnection.ConnectionString = connectionString;

                // Restart connection cycle
                m_serviceConnection.ConnectAsync();
            }
        }

        private void UpdateFormTitle()
        {
            if (InvokeRequired)
                Invoke((Action)UpdateFormTitle);

            // Change form title to include working file name
            if (string.IsNullOrEmpty(m_configurationFileName))
                Text = Tag.ToString();
            else
                Text = Tag + " - " + FilePath.GetFileName(m_configurationFileName);
        }

        #endregion

    }
}
