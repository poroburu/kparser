using System;
using System.Globalization;
using System.Resources;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Security.Principal;
using System.IO;
using System.Data;
using System.Text;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using System.Diagnostics;
using System.Text.RegularExpressions;
using WaywardGamers.KParser.Plugin;
using WaywardGamers.KParser.Database;
using WaywardGamers.KParser.Forms;
using WaywardGamers.KParser.Monitoring;
using System.Runtime.InteropServices;

namespace WaywardGamers.KParser
{
    public partial class ParserWindow : Form
    {
        #region Main Entry Point
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            try
            {
                Application.EnableVisualStyles();
                Application.Run(new ParserWindow());
            }
            catch (Exception e)
            {
                Logger.Instance.FatalLog(e);
            }
        }
        #endregion


        [DllImport("shell32.dll", CharSet=CharSet.Auto)]
        internal static extern void SHAddToRecentDocs(uint uFlags, string pathAndFileName);

        #region Member Variables
        string applicationDirectory;
        string defaultSaveDirectory;
        const uint HorizonMemoryOffset = 0x0062D8F0;
        UiControlServer uiControlServer;
        int uiGeneration;
        DateTime uiLastResetUtc;
        string uiLastError = String.Empty;
        string uiBoundaryMode = "none";
        string uiBoundaryQuality = "unavailable";
        string uiBoundaryReason = "no reset boundary";

        Properties.WindowSettings windowSettings = new WaywardGamers.KParser.Properties.WindowSettings();
        Properties.Settings appSettings = new WaywardGamers.KParser.Properties.Settings();

        List<IPlugin> pluginList = new List<IPlugin>();
        List<TabPage> tabList = new List<TabPage>();
        List<ToolStripMenuItem> tabMenuList = new List<ToolStripMenuItem>();

        List<string> recentFilesList;
        ToolStripMenuItem noRecentFiles = new ToolStripMenuItem();

        TabPage currentTab = null;

        ImportMode ReparseMode;
        bool isReparsing;
        bool reparseComplete;
        string revertToThisDatabaseFile = string.Empty;

        readonly CultureInfo englishCulture = new CultureInfo("en-US");
        readonly CultureInfo frenchCulture = new CultureInfo("fr-FR");
        readonly CultureInfo germanCulture = new CultureInfo("de-DE");
        readonly CultureInfo japaneseCulture = new CultureInfo("ja-JP");

        Font japaneseFont = new Font("Arial Unicode MS", 8.25f);
        Font defaultTabStripFont = new Font("Microsoft Sans Serif", 8.25f);
        Font defaultMenuFont = new Font("Tahoma", 8.25f);

        #endregion

        #region Constructor
        public ParserWindow()
        {
            InitializeComponent();

            applicationDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        }
        #endregion

        #region Load/Close Event handlers for saving program state
        private void ParserWindow_Load(object sender, EventArgs e)
        {
            LoadUpgrade();

            LoadRestoreWindowPosition();

            LoadCleanupPluginList();

            LoadRecentFiles();

            LoadMiscSettings();

            LoadCultureSettings();

            LoadParseCultureSettings();

            // Load plugins on startup and add them to the Windows menu
            FindAndLoadPlugins();
            PopulateTabsMenu();

            LoadWatchMonitors();

            StartUiControlServer();
            LoadCommandLine();
        }

        private void LoadUpgrade()
        {
            // Upgrade settings files if necessary
            try
            {
                if (windowSettings.UpgradeRequired)
                {
                    windowSettings.Upgrade();
                    windowSettings.UpgradeRequired = false;
                    windowSettings.Save();
                }

                if (appSettings.UpgradeRequired)
                {
                    appSettings.Upgrade();
                    appSettings.UpgradeRequired = false;
                    appSettings.Save();
                }
            }
            catch (Exception e)
            {
                Logger.Instance.Log(e);
            }
        }

        private void LoadRestoreWindowPosition()
        {
            // Restore main window position and size.

            this.Size = windowSettings.mainWindowSize;
            this.Location = windowSettings.mainWindowPosition;
            if (windowSettings.mainWindowMaximized == true)
                this.WindowState = FormWindowState.Maximized;
            else
                this.WindowState = FormWindowState.Normal;

            // Verify that the window hasn't been pushed out of visibility.

            Screen workingScreen = Screen.FromControl(this);

            if ((this.Location.X > workingScreen.Bounds.Right) ||
                (this.Location.Y > workingScreen.Bounds.Bottom) ||
                ((this.Size.Width + this.Location.X) < workingScreen.Bounds.Left) ||
                ((this.Size.Height + this.Location.Y) < workingScreen.Bounds.Top))
            {
                this.Location = new Point(0, 0);
            }
        }

        private void LoadCleanupPluginList()
        {
            // Cleanup in case of corruption:

            if (windowSettings.activePluginList == null)
            {
                windowSettings.activePluginList = new StringCollection();
            }
            else
            {
                HashSet<string> tmpHash = new HashSet<string>();
                foreach (string str in windowSettings.activePluginList)
                {
                    tmpHash.Add(str);
                }

                windowSettings.activePluginList.Clear();
                foreach (string str in tmpHash)
                    windowSettings.activePluginList.Add(str);
            }
        }

        private void LoadRecentFiles()
        {
            // Get recent files listing and populate menu
            if (windowSettings.RecentFiles == null)
                windowSettings.RecentFiles = new StringCollection();

            recentFilesList = new List<string>(10);
            foreach (var recentFile in windowSettings.RecentFiles)
            {
                recentFilesList.Add(recentFile);
            }

            UpdateRecentFilesMenu();
        }

        private void LoadMiscSettings()
        {
            // Set default save directory
            if (appSettings.DefaultParseSaveDirectory == string.Empty)
            {
                appSettings.DefaultParseSaveDirectory = Application.CommonAppDataPath;
            }

            defaultSaveDirectory = appSettings.DefaultParseSaveDirectory;

            // Set what types of names are shown.
            showJobInsteadOfNameToolStripMenuItem.Checked = appSettings.ShowCombatantJobNameIfPresent;
        }

        private void LoadCultureSettings()
        {
            // Set the culture up
            try
            {
                if (appSettings.InterfaceCulture != string.Empty)
                {
                    CultureInfo savedCulture = new CultureInfo(appSettings.InterfaceCulture);
                    if (savedCulture != null)
                    {
                        SetCurrentCulture(savedCulture);
                    }
                }
            }
            catch
            {
            }

            // Mark which language we're currently running as on the language menu
            UncheckLanguages();
            switch (System.Threading.Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName)
            {
                case "fr":
                    frenchToolStripMenuItem.Checked = true;
                    break;
                case "de":
                    germanToolStripMenuItem.Checked = true;
                    break;
                case "ja":
                    japaneseToolStripMenuItem.Checked = true;
                    break;
                default:
                    englishToolStripMenuItem.Checked = true;
                    break;
            }
        }

        private void LoadParseCultureSettings()
        {
            UncheckParsingLanguages();
            switch (appSettings.ParsingCulture)
            {
                case "fr-FR":
                    frenchParsingToolStripMenuItem.Checked = true;
                    break;
                case "de-DE":
                    germanParsingToolStripMenuItem.Checked = true;
                    break;
                case "ja-JP":
                    japaneseParsingToolStripMenuItem.Checked = true;
                    break;
                default:
                    englishParsingToolStripMenuItem.Checked = true;
                    break;
            }
        }

        private void LoadWatchMonitors()
        {
            Monitoring.Monitor.Instance.ReaderStatusChanged += WatchParserStatus;
            DatabaseManager.Instance.ReparseProgressChanged += WatchDatabaseStatus;
        }

        private void LoadCommandLine()
        {
            string[] cla = Environment.GetCommandLineArgs();

            if (cla.Skip(1).Any(argument =>
                string.Equals(argument, "--parity-ui", StringComparison.OrdinalIgnoreCase)))
            {
                StartParityUi();
                return;
            }

            // Handle any command line arguments, to allow us to open files
            // directed at us.
            if (cla.Length > 1)
                OpenFile(cla[1]);
        }

        private void StartParityUi()
        {
            // The parity UI is an explicit opt-in startup mode. It keeps the
            // normal kparser startup behavior unchanged while making the
            // Horizon live comparison surface immediately useful.
            ActivateParityTabs();
            if (StartParsing(string.Empty, HorizonMemoryOffset, false))
            {
                uiGeneration++;
                uiLastResetUtc = DateTime.UtcNow;
                uiLastError = String.Empty;
            }
        }

        private void StartUiControlServer()
        {
            try
            {
                uiControlServer = new UiControlServer(
                    "kparser",
                    GetUiControlDescriptorPath(),
                    GetUiControlStatus,
                    ResetUiSession);
                uiControlServer.Start();
            }
            catch (Exception ex)
            {
                uiLastError = ex.Message;
                Logger.Instance.Log(ex, "UI control server");
            }
        }

        private string ResetUiSession(
            string resetId,
            string sessionUuid,
            string afterMessageId,
            string boundaryMode,
            string boundaryQuality,
            string boundaryReason)
        {
            if (InvokeRequired)
            {
                return (string)Invoke(
                    new UiResetHandler(ResetUiSession),
                    resetId,
                    sessionUuid,
                    afterMessageId,
                    boundaryMode,
                    boundaryQuality,
                    boundaryReason);
            }

            if (boundaryMode != "exact" && boundaryMode != "degraded")
            {
                uiLastError = "boundary_mode must be exact or degraded";
                return GetUiControlStatus(
                    resetId,
                    false,
                    sessionUuid,
                    afterMessageId,
                    boundaryMode,
                    boundaryQuality,
                    boundaryReason);
            }

            if (boundaryMode == "exact" &&
                (boundaryQuality != "exact" || String.IsNullOrEmpty(sessionUuid)))
            {
                uiLastError = "exact boundaries require exact quality and session_uuid";
                return GetUiControlStatus(
                    resetId,
                    false,
                    sessionUuid,
                    afterMessageId,
                    boundaryMode,
                    boundaryQuality,
                    boundaryReason);
            }

            StopParsing();
            ActivateParityTabs();

            if (!StartParsing(String.Empty, HorizonMemoryOffset, false))
            {
                return GetUiControlStatus(
                    resetId,
                    false,
                    sessionUuid,
                    afterMessageId,
                    boundaryMode,
                    boundaryQuality,
                    boundaryReason);
            }

            uiGeneration++;
            uiLastResetUtc = DateTime.UtcNow;
            uiLastError = String.Empty;
            uiBoundaryMode = boundaryMode;
            uiBoundaryQuality = boundaryQuality;
            uiBoundaryReason = boundaryReason;
            return GetUiControlStatus(
                resetId,
                true,
                sessionUuid,
                afterMessageId,
                boundaryMode,
                boundaryQuality,
                boundaryReason);
        }

        private string GetUiControlStatus()
        {
            return GetUiControlStatus(
                String.Empty,
                Monitor.Instance.IsRunning,
                String.Empty,
                "0",
                uiBoundaryMode,
                uiBoundaryQuality,
                uiBoundaryReason);
        }

        private string GetUiControlStatus(
            string resetId,
            bool ok,
            string sessionUuid,
            string boundaryMessageId,
            string boundaryMode,
            string boundaryQuality,
            string boundaryReason)
        {
            return String.Format(
                "{{\"ok\":{0},\"service\":\"kparser\",\"pid\":{1},\"running\":{2},\"generation\":{3},\"reset_id\":\"{4}\",\"session_uuid\":\"{5}\",\"boundary_message_id\":\"{6}\",\"boundary_mode\":\"{7}\",\"boundary_quality\":\"{8}\",\"boundary_reason\":\"{9}\",\"last_reset_utc\":\"{10}\",\"error\":\"{11}\"}}",
                ok ? "true" : "false",
                Process.GetCurrentProcess().Id,
                Monitor.Instance.IsRunning ? "true" : "false",
                uiGeneration,
                JsonEscape(resetId),
                JsonEscape(sessionUuid),
                JsonEscape(boundaryMessageId),
                JsonEscape(boundaryMode),
                JsonEscape(boundaryQuality),
                JsonEscape(boundaryReason),
                uiLastResetUtc == DateTime.MinValue ? String.Empty : uiLastResetUtc.ToString("O"),
                JsonEscape(uiLastError));
        }

        private static string GetUiControlDescriptorPath()
        {
            string root = Environment.GetEnvironmentVariable("KDEV_ROOT");
            DirectoryInfo directory = new DirectoryInfo(
                String.IsNullOrEmpty(root)
                    ? AppDomain.CurrentDomain.BaseDirectory
                    : root);

            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "ffxi-captures")))
                {
                    string captureDirectory = Path.Combine(
                        Path.Combine(directory.FullName, "ffxi-captures"),
                        "ndjson");
                    Directory.CreateDirectory(captureDirectory);
                    return Path.Combine(captureDirectory, "ui-control-kparser.json");
                }

                directory = directory.Parent;
            }

            string fallback = Path.Combine(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "kdev"),
                "ui-control-kparser.json");
            Directory.CreateDirectory(Path.GetDirectoryName(fallback));
            return fallback;
        }

        private static string JsonEscape(string value)
        {
            return (value ?? String.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }

        private void ParserWindow_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (uiControlServer != null)
            {
                uiControlServer.Dispose();
                uiControlServer = null;
            }

            SaveWindowState();
            SavePluginList();
            SaveRecentFilesList();
            SaveCulture();

            SaveSettings();
        }

        private void SaveWindowState()
        {
            try
            {
                if (this.WindowState == FormWindowState.Maximized)
                {
                    windowSettings.mainWindowMaximized = true;
                    windowSettings.mainWindowPosition = this.RestoreBounds.Location;
                    windowSettings.mainWindowSize = this.RestoreBounds.Size;
                }
                else
                {
                    windowSettings.mainWindowMaximized = false;
                    windowSettings.mainWindowPosition = this.Location;
                    windowSettings.mainWindowSize = this.Size;
                }

                if (windowSettings.activePluginList == null)
                    windowSettings.activePluginList = new StringCollection();

            }
            catch (Exception ex)
            {
                Logger.Instance.Log(ex, "window state");
            }

        }

        private void SavePluginList()
        {
            try
            {
                windowSettings.activePluginList.Clear();

                foreach (IPlugin plugin in pluginList)
                {
                    if (plugin.IsActive)
                        windowSettings.activePluginList.Add(plugin.TabName);
                }

            }
            catch (Exception ex)
            {
                Logger.Instance.Log(ex, "active plugins");
            }
        }

        private void SaveRecentFilesList()
        {
            try
            {
                if (recentFilesList != null)
                {
                    if (windowSettings.RecentFiles == null)
                        windowSettings.RecentFiles = new StringCollection();

                    windowSettings.RecentFiles.Clear();

                    foreach (var recentFile in recentFilesList.Take<string>(10))
                    {
                        windowSettings.RecentFiles.Add(recentFile);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(ex, "recent files");
            }
        }

        private void SaveCulture()
        {
            try
            {
                appSettings.InterfaceCulture = System.Threading.Thread.CurrentThread.CurrentUICulture.Name;
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(ex, "culture");
            }
        }

        private void SaveSettings()
        {
            try
            {
                windowSettings.Save();
                appSettings.Save();
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(ex, "saving");
            }
        }

        #endregion

        #region Menu Popup Handlers
        private void fileMenu_Popup(object sender, EventArgs e)
        {
            bool monitorRunning = Monitor.Instance.IsRunning;
            bool databaseOpen = DatabaseManager.Instance.IsDatabaseOpen;

            // Can't start a parse if one is running.
            beginDefaultParseMenuItem.Enabled = !monitorRunning;
            beginParseAndSaveDataMenuItem.Enabled = !monitorRunning;
            openSavedDataMenuItem.Enabled = !monitorRunning;
            recentFilesToolStripMenuItem.Enabled = !monitorRunning;

            // Can only stop a parse if one is running.
            quitParsingMenuItem.Enabled = monitorRunning;

            // Can only continue or save if none running, and database is opened
            continueParsingMenuItem.Enabled = (!monitorRunning) && (databaseOpen);
            saveCurrentDataAsMenuItem.Enabled = (!monitorRunning) && (databaseOpen);
            saveReportMenuItem.Enabled = (!monitorRunning) && (databaseOpen);

            splitParseToolStripMenuItem.Enabled = (!monitorRunning) && (databaseOpen);
            joinParsesToolStripMenuItem.Enabled = (!monitorRunning);

            // Can't import if a parse is running.
            importToolStripMenuItem.Enabled = !monitorRunning;
        }

        private void editToolStripMenuItem_DropDownOpening(object sender, EventArgs e)
        {
            showJobInsteadOfNameToolStripMenuItem.Enabled = DatabaseManager.Instance.IsDatabaseOpen;
        }

        private void fFXILanguageToolStripMenuItem_DropDownOpening(object sender, EventArgs e)
        {
            if (Monitor.Instance.IsRunning)
            {
                englishParsingToolStripMenuItem.Enabled = false;
                frenchParsingToolStripMenuItem.Enabled = false;
                germanParsingToolStripMenuItem.Enabled = false;
                japaneseParsingToolStripMenuItem.Enabled = false;
            }
            else
            {
                englishParsingToolStripMenuItem.Enabled = true;
                frenchParsingToolStripMenuItem.Enabled = false;
                germanParsingToolStripMenuItem.Enabled = false;
                japaneseParsingToolStripMenuItem.Enabled = false;
            }
        }

        private void toolsMenu_Popup(object sender, EventArgs e)
        {
            toolsReparseMenuItem.Enabled = (Monitor.Instance.IsRunning == false);

#if DEBUG
            toolsTestFunctionMenuItem.Visible = true;
#else
            toolsTestFunctionMenuItem.Visible = false;
#endif
        }

        private void windowsMenu_Popup(object sender, EventArgs e)
        {
            appSettings.Reload();

            bool inDebugMode = appSettings.DebugMode;

#if DEBUG
            inDebugMode = true;
#endif

            // If any tabs open, enable menu item to close all tabs
            closeAllTabsToolStripMenuItem.Enabled = pluginList.Any(p => p.IsActive == true);


            try
            {
                // Scan through the menu list and enable menu items based on debug mode.

                for (int i = 0; i < tabMenuList.Count; i++)
                {
                    if (inDebugMode)
                    {
                        tabMenuList[i].Enabled = true;
                    }
                    else
                    {
                        if (pluginList[i].IsDebug)
                        {
                            tabMenuList[i].Enabled = false;
                            tabMenuList[i].Checked = false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(ex);
            }
        }

        #endregion

        #region Menu Event Handlers
        /// <summary>
        /// Get the requested filename to save the database as and start
        /// parsing to that output file.
        /// </summary>
        private void menuBeginParseWithSave_Click(object sender, EventArgs e)
        {
            string outFilename;
            if (GetParseFileName(out outFilename) == true)
            {
                StartParsing(outFilename);
            }
        }

        /// <summary>
        /// Initiate parsing with no output file provided.
        /// </summary>
        private void menuBeginDefaultParse_Click(object sender, EventArgs e)
        {
            StartParsing("");
        }

        /// <summary>
        /// Stop any active parsing.
        /// </summary>
        private void menuStopParse_Click(object sender, EventArgs e)
        {
            StopParsing();
        }

        private void menuContinueParse_Click(object sender, EventArgs e)
        {
            // Let the database notify us of changes, and we'll notify the active plugins.
            DatabaseManager.Instance.DatabaseChanging += MonitorDatabaseChanging;
            DatabaseManager.Instance.DatabaseChanged += MonitorDatabaseChanged;

            Monitor.Instance.Continue(appSettings.ParseMode);

            programStatusLabel.Text = "Continue Parsing...";
        }

        private void menuOpenSavedData_Click(object sender, EventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.InitialDirectory = defaultSaveDirectory;
            ofd.Multiselect = false;
            ofd.DefaultExt = "sdf";
            ofd.Filter = "KParser Parses|*.sdf;*.kps|All Files|*.*";
            ofd.Title = "Select parse...";

            if (ofd.ShowDialog() == DialogResult.OK)
            {
                StopParsing();

                OpenFile(ofd.FileName);

                programStatusLabel.Text = string.Empty;
            }
        }

        private void menuSaveDataAs_Click(object sender, EventArgs e)
        {
            SaveFileDialog sfd = new SaveFileDialog();
            sfd.InitialDirectory = defaultSaveDirectory;
            sfd.DefaultExt = "sdf";
            sfd.Title = "Select file to save parse data to...";
            sfd.Filter = "Complete copy (*.sdf)|*.sdf|Copy without Chat Logs (*.sdf)|*.sdf||";
            sfd.FilterIndex = 1;

            if (sfd.ShowDialog() == DialogResult.OK)
            {
                if (sfd.FileName == DatabaseManager.Instance.DatabaseFilename)
                {
                    MessageBox.Show("Can't save the database file back onto itself.", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                try
                {
                    Cursor.Current = Cursors.WaitCursor;

                    switch (sfd.FilterIndex)
                    {
                        case 1:
                            File.Copy(DatabaseManager.Instance.DatabaseFilename, sfd.FileName, true);
                            OpenFile(sfd.FileName);
                            break;
                        case 2:
                            File.Copy(DatabaseManager.Instance.DatabaseFilename, sfd.FileName, true);
                            OpenFile(sfd.FileName);
                            DatabaseManager.Instance.PurgeChatInfo();
                            OpenFile(sfd.FileName);
                            break;
                        default:
                            string errmsg = string.Format("Unknown save format (index {0})", sfd.FilterIndex);
                            throw new InvalidOperationException(errmsg);
                    }

                    programStatusLabel.Text = string.Empty;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                    Logger.Instance.Log(ex);
                }
                finally
                {
                    Cursor.Current = Cursors.Default;
                }
            }
        }

        private void closeParseFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CloseFile();
        }

        private void splitParseToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SplitParsesDlg splitParse = new SplitParsesDlg();

            if (splitParse.ShowDialog() == DialogResult.OK)
            {
                string inFilename = DatabaseManager.Instance.DatabaseFilename;
                string outFilename = string.Empty;

                if (GetParseFileName(out outFilename) == true)
                {
                    if (outFilename == inFilename)
                    {
                        MessageBox.Show("Cannot save to the same file you want to reparse.", "Error",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    Cursor.Current = Cursors.WaitCursor;
                    ReparseMode = ImportMode.Reparse;

                    try
                    {
                        try
                        {
                            PrepareToImportOrReparse();

                            Monitor.Instance.ImportRange(inFilename, outFilename, ImportSourceType.KParser,
                                splitParse.StartBoundary, splitParse.EndBoundary);
                        }
                        catch (Exception ex)
                        {
                            CancelImportOrReparse();

                            StopParsing();
                            Logger.Instance.Log(ex);
                            MessageBox.Show(ex.Message, "Error while attempting to reparse.",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);

                            return;
                        }

                    }
                    catch (Exception ex)
                    {
                        Logger.Instance.Log(ex);
                    }
                }
            }
        }

        private void joinParsesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (Monitor.Instance.IsRunning == true)
            {
                MessageBox.Show("Cannot reparse while another parse is running.", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string inFilename1 = string.Empty;
            string inFilename2 = string.Empty;
            string outFilename = string.Empty;
            revertToThisDatabaseFile = string.Empty;

            if (DatabaseManager.Instance.IsDatabaseOpen)
            {
                DialogResult includeCurrent = MessageBox.Show("Do you want to include the current data?", "Join current data?",
                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);

                if (includeCurrent == DialogResult.Cancel)
                    return;

                if (includeCurrent == DialogResult.Yes)
                {
                    inFilename1 = DatabaseManager.Instance.DatabaseFilename;
                }

                revertToThisDatabaseFile = DatabaseManager.Instance.DatabaseFilename;
            }

            if (inFilename1 == string.Empty)
            {
                OpenFileDialog ofd = new OpenFileDialog();
                ofd.InitialDirectory = defaultSaveDirectory;
                ofd.Multiselect = false;
                ofd.DefaultExt = "sdf";
                ofd.Title = "Select first file to join...";

                if (ofd.ShowDialog() == DialogResult.Cancel)
                {
                    return;
                }
                else
                {
                    inFilename1 = ofd.FileName;
                }
            }

            if (inFilename2 == string.Empty)
            {
                OpenFileDialog ofd = new OpenFileDialog();
                ofd.InitialDirectory = defaultSaveDirectory;
                ofd.Multiselect = false;
                ofd.DefaultExt = "sdf";
                ofd.Title = "Select additional file to join...";

                if (ofd.ShowDialog() == DialogResult.Cancel)
                {
                    return;
                }
                else
                {
                    inFilename2 = ofd.FileName;
                }
            }

            if (GetParseFileName(out outFilename) == true)
            {
                if ((outFilename == inFilename1) || (outFilename == inFilename2))
                {
                    MessageBox.Show("Cannot save to the same file you want to join.", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                Cursor.Current = Cursors.WaitCursor;
                ReparseMode = ImportMode.Reparse;

                try
                {
                    try
                    {
                        PrepareToImportOrReparse();

                        Monitor.Instance.Join(inFilename1, inFilename2, outFilename, ImportSourceType.KParser);
                    }
                    catch (Exception ex)
                    {
                        CancelImportOrReparse();

                        StopParsing();
                        Logger.Instance.Log(ex);
                        MessageBox.Show(ex.Message, "Error while attempting to reparse.",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);

                        return;
                    }

                }
                catch (Exception ex)
                {
                    Logger.Instance.Log(ex);
                }
            }
        }

        private void menuSaveReport_Click(object sender, EventArgs e)
        {
            // Save report generated by the current plugin
            SaveFileDialog sfd = new SaveFileDialog();
            sfd.InitialDirectory = defaultSaveDirectory;
            sfd.DefaultExt = "sdf";
            sfd.Title = "Select file to save parse data to...";
            //sfd.Filter = "Plain text (Current tab) (*.txt)|*.txt|Plain text (All tabs) (*.txt)|*.txt|Excel Spreadsheet (Current tab) (*.xls)|*.xls||";
            sfd.Filter = "Plain text (Current tab) (*.txt)|*.txt|Plain text (All tabs) (*.txt)|*.txt||";
            sfd.FilterIndex = 1;

            if (sfd.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    Cursor.Current = Cursors.WaitCursor;
                    IPlugin tabPlugin;

                    switch (sfd.FilterIndex)
                    {
                        case 1:
                            // Save as raw text
                            tabPlugin = pluginTabs.SelectedTab.Controls.OfType<IPlugin>().FirstOrDefault();

                            if (tabPlugin != null)
                            {
                                string textContents = tabPlugin.TextContents;

                                using (StreamWriter sw = new StreamWriter(sfd.FileName))
                                {
                                    sw.Write(textContents);
                                }
                            }
                            break;
                        case 2:
                            // Save raw text of all tabs into one text file.
                            using (StreamWriter sw = new StreamWriter(sfd.FileName))
                            {
                                foreach (IPlugin tab in pluginTabs.TabPages.OfType<IPlugin>())
                                {
                                    sw.WriteLine(tab.TabName);
                                    sw.WriteLine();

                                    string textContents = tab.TextContents;
                                    sw.Write(textContents);

                                    sw.WriteLine();
                                    sw.WriteLine();
                                }
                            }
                            break;
                        case 3:
                            // Save generated output from the tab into an excel spreadsheet.
                            tabPlugin = pluginTabs.SelectedTab.Controls.OfType<IPlugin>().FirstOrDefault();
                            
                            if (tabPlugin != null)
                            {
                                if (tabPlugin.GeneratedDataTableForExcel != null)
                                {
                                    using (TextWriter tx = new StreamWriter(sfd.FileName))
                                    {
                                        System.Web.HttpResponse response = new System.Web.HttpResponse(tx);
                                        ExcelExport.Convert(tabPlugin.GeneratedDataTableForExcel, response);
                                    }
                                }
                                else
                                {
                                    MessageBox.Show(string.Format("The {0} tab is not set up to export data to Excel at this time.",
                                        tabPlugin.TabName), "Cannot export", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                                }
                            }
                            break;
                        default:
                            string errmsg = string.Format("Unknown save format (index {0})", sfd.FilterIndex);
                            throw new InvalidOperationException(errmsg);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                    Logger.Instance.Log(ex);
                }
                finally
                {
                    Cursor.Current = Cursors.Default;
                }
            }
        }

        private void menuExit_Click(object sender, EventArgs e)
        {
            this.Shutdown();
            Application.Exit();
        }

        private void menuOptions_Click(object sender, EventArgs e)
        {
            try
            {
                Options optionsForm = new Options(Monitor.Instance.IsRunning);
                if (optionsForm.ShowDialog(this) == DialogResult.OK)
                {
                    windowSettings.Reload();

                    windowsMenu_Popup(windowsMenu, null);
                    UpdateRecentFilesMenu();

                    // Reload possibly changed save directory.
                    defaultSaveDirectory = appSettings.DefaultParseSaveDirectory;
                }

            }
            catch (Exception ex)
            {
                Logger.Instance.Log(ex);
            }
        }

        private void menuAbout_Click(object sender, EventArgs e)
        {
            AboutBox aboutForm = new AboutBox();
            aboutForm.ShowDialog();
        }

        private void copyTabInfoAsTextToolStripMenuItem_Click(object sender, EventArgs e)
        {
            IPlugin tabPlugin = pluginTabs.SelectedTab.Controls.OfType<IPlugin>().FirstOrDefault();

            if (tabPlugin != null)
            {
                string tabContents = tabPlugin.TextContents;
                if (tabContents != string.Empty)
                    Clipboard.SetText(tabContents);
            }
        }

        private void copyTabInfoAsHTMLToolStripMenuItem_Click(object sender, EventArgs e)
        {
            IPlugin tabPlugin = pluginTabs.SelectedTab.Controls.OfType<IPlugin>().FirstOrDefault();

            Utility.RTFConverter rtfConverter = new WaywardGamers.KParser.Utility.RTFConverter();

            try
            {
                if (tabPlugin != null)
                {
                    string tabContentsAsRTF = tabPlugin.TextContentsAsRTF;
                    string tabContentsAsHTML = rtfConverter.ConvertRTFToHTML(tabContentsAsRTF);
                    if (tabContentsAsHTML != string.Empty)
                        Clipboard.SetText(tabContentsAsHTML);
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(ex);
            }
        }

        private void copyTabInfoAsBBCodeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            IPlugin tabPlugin = pluginTabs.SelectedTab.Controls.OfType<IPlugin>().FirstOrDefault();

            Utility.RTFConverter rtfConverter = new WaywardGamers.KParser.Utility.RTFConverter();

            try
            {
                if (tabPlugin != null)
                {
                    string tabContentsAsRTF = tabPlugin.TextContentsAsRTF;
                    string tabContentsAsBBCode = rtfConverter.ConvertRTFToBBCode(tabContentsAsRTF);
                    if (tabContentsAsBBCode != string.Empty)
                        Clipboard.SetText(tabContentsAsBBCode);
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(ex);
            }
        }

        private void copyTabInfoAsRTFToolStripMenuItem_Click(object sender, EventArgs e)
        {
            IPlugin tabPlugin = pluginTabs.SelectedTab.Controls.OfType<IPlugin>().FirstOrDefault();

            if (tabPlugin != null)
            {
                string tabContents = tabPlugin.TextContentsAsRTF;
                //Clipboard.SetText(tabContents, TextDataFormat.Rtf);
                if (tabContents != string.Empty)
                    Clipboard.SetText(tabContents);
            }
        }

        private void playerInformationToolStripMenuItem_Click(object sender, EventArgs e)
        {
            bool databaseOpen = DatabaseManager.Instance.IsDatabaseOpen;

            if (databaseOpen == false)
            {
                MessageBox.Show("You must open or start a parse first.", "No parse file.",
                    MessageBoxButtons.OK, MessageBoxIcon.Stop);
                return;
            }

            AddMonitorChanging();
            PlayerInfoDlg infoForm = new PlayerInfoDlg(this);
            infoForm.Show(this);
        }

        private void showJobInsteadOfNameToolStripMenuItem_CheckedChanged(object sender, EventArgs e)
        {
            appSettings.ShowCombatantJobNameIfPresent = showJobInsteadOfNameToolStripMenuItem.Checked;
            DatabaseManager.Instance.ShowJobInsteadOfName = showJobInsteadOfNameToolStripMenuItem.Checked;

            NotifyPlugins();
        }

        private void closeAllTabsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                foreach (var tabMenu in tabMenuList)
                {
                    tabMenu.Checked = false;
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(ex);
            }
        }

        #endregion

        #region Language switching/localization
        private void englishToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetCurrentCulture(englishCulture);
            UncheckLanguages();
            englishToolStripMenuItem.Checked = true;
        }

        private void frenchToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetCurrentCulture(frenchCulture);
            UncheckLanguages();
            frenchToolStripMenuItem.Checked = true;
        }

        private void germanToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetCurrentCulture(germanCulture);
            UncheckLanguages();
            germanToolStripMenuItem.Checked = true;
        }

        private void japaneseToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetCurrentCulture(japaneseCulture);
            UncheckLanguages();
            japaneseToolStripMenuItem.Checked = true;
        }

        private void UncheckLanguages()
        {
            englishToolStripMenuItem.Checked = false;
            frenchToolStripMenuItem.Checked = false;
            germanToolStripMenuItem.Checked = false;
            japaneseToolStripMenuItem.Checked = false;
        }

        private void SetCurrentCulture(CultureInfo ci)
        {
            // Culture for formatting
            System.Threading.Thread.CurrentThread.CurrentCulture = ci;
            // Culture for resources
            System.Threading.Thread.CurrentThread.CurrentUICulture = ci;

            UpdateUI();

            NotifyTabsOfCultureChange();

            RenameTabs();
        }

        private void RenameTabs()
        {
            if (tabList.Count != pluginList.Count)
                throw new InvalidOperationException();

            for (int i = 0; i < pluginList.Count; i++)
            {
                tabList[i].Text = pluginList[i].TabName;
                tabMenuList[i].Text = pluginList[i].TabName;
            }

            // Adjust font if using Japanese text.
            if (System.Threading.Thread.CurrentThread.CurrentUICulture == japaneseCulture)
            {
                pluginTabs.Font = japaneseFont;
                //mainMenuStrip.Font = japaneseFont;
                tabContextMenuStrip.Font = japaneseFont;
            }
            else
            {
                pluginTabs.Font = defaultTabStripFont;
                //mainMenuStrip.Font = defaultMenuFont;
                tabContextMenuStrip.Font = defaultMenuFont;
            }
        }

        private void UpdateUI()
        {
            System.ComponentModel.ComponentResourceManager resources =
                new System.ComponentModel.ComponentResourceManager(typeof(ParserWindow));

            resources.ApplyResources(this.statusStrip, "statusStrip");
            resources.ApplyResources(this.programStatusLabel, "toolStripStatusLabel");
            resources.ApplyResources(this.tabContextMenuStrip, "tabContextMenuStrip");
            resources.ApplyResources(this.closeTabToolStripMenuItem, "closeTabToolStripMenuItem");
            resources.ApplyResources(this.closeOtherTabsToolStripMenuItem, "closeOtherTabsToolStripMenuItem");
            resources.ApplyResources(this.mainMenuStrip, "mainMenuStrip");
            resources.ApplyResources(this.fileMenu, "fileMenu");
            resources.ApplyResources(this.beginParseAndSaveDataMenuItem, "beginParseAndSaveDataMenuItem");
            resources.ApplyResources(this.beginDefaultParseMenuItem, "beginDefaultParseMenuItem");
            resources.ApplyResources(this.quitParsingMenuItem, "quitParsingMenuItem");
            resources.ApplyResources(this.toolStripSeparator1, "toolStripSeparator1");
            resources.ApplyResources(this.openSavedDataMenuItem, "openSavedDataMenuItem");
            resources.ApplyResources(this.continueParsingMenuItem, "continueParsingMenuItem");
            resources.ApplyResources(this.saveCurrentDataAsMenuItem, "saveCurrentDataAsMenuItem");
            resources.ApplyResources(this.saveReportMenuItem, "saveReportMenuItem");
            resources.ApplyResources(this.toolStripSeparator2, "toolStripSeparator2");
            resources.ApplyResources(this.importToolStripMenuItem, "importToolStripMenuItem");
            resources.ApplyResources(this.toolStripSeparator4, "toolStripSeparator4");
            resources.ApplyResources(this.exitMenuItem, "exitMenuItem");
            resources.ApplyResources(this.editToolStripMenuItem, "editToolStripMenuItem");
            resources.ApplyResources(this.copyasTextToolStripMenuItem, "copyasTextToolStripMenuItem");
            resources.ApplyResources(this.copyasHTMLToolStripMenuItem, "copyasHTMLToolStripMenuItem");
            resources.ApplyResources(this.copyTabInfoAsBBCodeToolStripMenuItem, "copyTabInfoAsBBCodeToolStripMenuItem");
            resources.ApplyResources(this.copyTabasRTFToolStripMenuItem, "copyTabasRTFToolStripMenuItem");
            resources.ApplyResources(this.toolStripSeparator5, "toolStripSeparator5");
            resources.ApplyResources(this.playerInformationToolStripMenuItem, "playerInformationToolStripMenuItem");
            resources.ApplyResources(this.toolsMenu, "toolsMenu");
            resources.ApplyResources(this.toolsTestFunctionMenuItem, "toolsTestFunctionMenuItem");
            resources.ApplyResources(this.toolsReparseMenuItem, "toolsReparseMenuItem");
            resources.ApplyResources(this.toolStripSeparator3, "toolStripSeparator3");
            resources.ApplyResources(this.optionsToolStripMenuItem, "optionsToolStripMenuItem");
            resources.ApplyResources(this.windowsMenu, "windowsMenu");
            resources.ApplyResources(this.aboutToolStripMenuItem, "aboutToolStripMenuItem");
            resources.ApplyResources(this.windowsToolStripSeparator, "windowsToolStripSeparator");
            resources.ApplyResources(this.closeAllTabsToolStripMenuItem, "closeAllTabsToolStripMenuItem");
            resources.ApplyResources(this.languageToolStripMenuItem, "languageToolStripMenuItem");
            resources.ApplyResources(this.toolStripSeparator6, "toolStripSeparator6");
            resources.ApplyResources(this.englishToolStripMenuItem, "englishToolStripMenuItem");
            resources.ApplyResources(this.frenchToolStripMenuItem, "frenchToolStripMenuItem");
            resources.ApplyResources(this.germanToolStripMenuItem, "germanToolStripMenuItem");
            resources.ApplyResources(this.japaneseToolStripMenuItem, "japaneseToolStripMenuItem");
            resources.ApplyResources(this.englishParsingToolStripMenuItem, "englishParsingToolStripMenuItem");
            resources.ApplyResources(this.frenchParsingToolStripMenuItem, "frenchParsingToolStripMenuItem");
            resources.ApplyResources(this.germanParsingToolStripMenuItem, "germanParsingToolStripMenuItem");
            resources.ApplyResources(this.japaneseParsingToolStripMenuItem, "japaneseParsingToolStripMenuItem");
            resources.ApplyResources(this.recentFilesToolStripMenuItem, "recentFilesToolStripMenuItem");
            resources.ApplyResources(this.noneToolStripMenuItem, "noneToolStripMenuItem");
            resources.ApplyResources(this.showJobInsteadOfNameToolStripMenuItem, "showJobInsteadOfNameToolStripMenuItem");

            //Size preserveWindowSize = this.Size;
            //Point preserveWindowLocation = this.Location;

            //resources.ApplyResources(this, "$this");
            //resources.ApplyResources(this.pluginTabs, "pluginTabs");

            //this.Location = preserveWindowLocation;
            //this.Size = preserveWindowSize;
        }

        private void NotifyTabsOfCultureChange()
        {
            // Notify the plugins
            foreach (IPlugin plugin in pluginList)
            {
                plugin.NotifyOfCultureChange();
            }

            // Notify the MobXPHandler singleton class so that
            // it can notify its CustomMobSelectionDlg.
            MobXPHandler.Instance.NotifyOfCultureChange();
        }

        private void englishParsingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            UncheckLanguages();
            englishParsingToolStripMenuItem.Checked = true;
            SetParsingCulture("");
        }

        private void frenchParsingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            UncheckLanguages();
            frenchParsingToolStripMenuItem.Checked = true;
            SetParsingCulture("fr-FR");
        }

        private void germanParsingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            UncheckLanguages();
            germanParsingToolStripMenuItem.Checked = true;
            SetParsingCulture("de-DE");
        }

        private void japaneseParsingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            UncheckLanguages();
            japaneseParsingToolStripMenuItem.Checked = true;
            SetParsingCulture("ja-JP");
        }

        private void UncheckParsingLanguages()
        {
            englishParsingToolStripMenuItem.Checked = false;
            frenchParsingToolStripMenuItem.Checked = false;
            germanParsingToolStripMenuItem.Checked = false;
            japaneseParsingToolStripMenuItem.Checked = false;
        }

        private void SetParsingCulture(string parseCulture)
        {
            appSettings.ParsingCulture = parseCulture;
            appSettings.Save();
        }
        #endregion

        #region Status monitoring
        private void PrepareToImportOrReparse()
        {
            programStatusLabel.Text =
                (ReparseMode == ImportMode.Reparse) ? "Reparsing..." : "Importing...";

            isReparsing = true;
            reparseComplete = false;
            ShowProgressBars();
        }

        private void CancelImportOrReparse()
        {
            isReparsing = false;
            HideProgressBars();
        }

        private void ShowProgressBars()
        {
            reparseProgressBar.Value = 0;
            savingProgressBar.Value = 0;
            reparseProgressBar.Visible = true;
            savingProgressBar.Visible = true;
        }

        private void HideProgressBars()
        {
            reparseProgressBar.Visible = false;
            savingProgressBar.Visible = false;
        }

        private void WatchParserStatus(object sender, ReaderStatusEventArgs e)
        {
            if (this.InvokeRequired)
            {
                Action<object, ReaderStatusEventArgs> thisFunc = WatchParserStatus;
                Invoke(thisFunc, new object[] { sender, e });
                return;
            }

            if (sender is Interface.IReader)
            {
                if (e.Active)
                {
                    string mainStatus = programStatusLabel.Text;

                    switch (e.DataSourceType)
                    {
                        case DataSource.Log:
                            mainStatus = "Parsing Log Files";
                            break;
                        case DataSource.Ram:
                            mainStatus = "Parsing RAM";
                            break;
                        case DataSource.Database:
                            break;
                    }

                    programStatusLabel.Text = mainStatus;
                }
                else
                {
                    programStatusLabel.Text = "Stopped";
                }

                if (string.IsNullOrEmpty(e.StatusMessage) == false)
                {
                    if (appSettings.DebugMode)
                        secondaryStatusLabel.Text = e.StatusMessage;
                    else
                        secondaryStatusLabel.Text = string.Empty;
                }
                else
                {
                    if (e.DataSourceType == DataSource.Database)
                    {
                        if (e.Completed)
                        {
                            programStatusLabel.Text = "Completed";
                            secondaryStatusLabel.Text = string.Empty;

                            reparseComplete = true;
                        }
                        else if (e.Failed)
                        {
                            programStatusLabel.Text = "Aborted";
                            secondaryStatusLabel.Text = string.Empty;

                            // reload original database file, or the file we were reparsing if no original
                            if (string.IsNullOrEmpty(revertToThisDatabaseFile) == false)
                                OpenFile(revertToThisDatabaseFile);
                            else
                                OpenFile(KParserReadingManager.Instance.DatabaseFilename);
                        }
                        else if (e.TotalItems > 0)
                        {
                            secondaryStatusLabel.Text = string.Format("{0}/{1}", e.ProcessedItems, e.TotalItems);
                            reparseProgressBar.Maximum = e.TotalItems;
                            reparseProgressBar.Value = e.ProcessedItems;
                        }
                    }
                }
            }
        }

        private void WatchDatabaseStatus(object sender, ReaderStatusEventArgs e)
        {
            if (this.InvokeRequired)
            {
                Action<object, ReaderStatusEventArgs> thisFunc = WatchDatabaseStatus;
                Invoke(thisFunc, new object[] { sender, e });
                return;
            }

            if ((sender is DatabaseManager) && (isReparsing == true))
            {
                if (e.Completed == true)
                {
                    if (reparseComplete == true)
                    {
                        programStatusLabel.Text =
                            ((ReparseMode == ImportMode.Reparse) ? "Reparse " : "Import ") + "complete.";

                        secondaryStatusLabel.Text = string.Empty;
                        isReparsing = false;
                        HideProgressBars();

                        OpenFile(DatabaseManager.Instance.DatabaseFilename);
                    }
                }
                else if (e.Failed == true)
                {
                    programStatusLabel.Text = "Aborted.";

                    secondaryStatusLabel.Text = string.Empty;
                    isReparsing = false;
                    HideProgressBars();

                    // reload original database file, or the file we were reparsing if no original
                    if (revertToThisDatabaseFile != string.Empty)
                        OpenFile(revertToThisDatabaseFile);
                    else
                        OpenFile(KParserReadingManager.Instance.DatabaseFilename);
                }
                else
                {
                    savingProgressBar.Maximum = e.TotalItems;
                    savingProgressBar.Value = e.ProcessedItems;

                    if (reparseComplete)
                    {
                        programStatusLabel.Text = "Saving...";
                        secondaryStatusLabel.Text = string.Format("{0}/{1}", e.ProcessedItems, e.TotalItems);
                    }
                }
            }
        }
        #endregion

        #region Reparse/Import functions
        // Menu event handlers
        private void reparseDatabaseToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ReparseDatabase();
        }

        private void importDatabaseToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ImportDatabase();
        }

        // Implementation code

        private void ReparseDatabase()
        {
            if (Monitor.Instance.IsRunning == true)
            {
                MessageBox.Show("Cannot reparse while another parse is running.", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string inFilename = string.Empty;
            string outFilename = string.Empty;
            revertToThisDatabaseFile = string.Empty;

            if (DatabaseManager.Instance.IsDatabaseOpen)
            {
                DialogResult reparse = MessageBox.Show("Do you want to reparse the current data?", "Reparse current data?",
                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);

                if (reparse == DialogResult.Cancel)
                    return;

                if (reparse == DialogResult.Yes)
                {
                    inFilename = DatabaseManager.Instance.DatabaseFilename;
                }

                revertToThisDatabaseFile = DatabaseManager.Instance.DatabaseFilename;
            }

            if (inFilename == string.Empty)
            {
                OpenFileDialog ofd = new OpenFileDialog();
                ofd.InitialDirectory = defaultSaveDirectory;
                ofd.Multiselect = false;
                ofd.DefaultExt = "sdf";
                ofd.Title = "Select file to reparse...";

                if (ofd.ShowDialog() == DialogResult.Cancel)
                {
                    return;
                }
                else
                {
                    inFilename = ofd.FileName;
                }
            }

            if (GetParseFileName(out outFilename) == true)
            {
                if (outFilename == inFilename)
                {
                    MessageBox.Show("Cannot save to the same file you want to reparse.", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                Cursor.Current = Cursors.WaitCursor;
                ReparseMode = ImportMode.Reparse;

                try
                {
                    try
                    {
                        PrepareToImportOrReparse();

                        Monitor.Instance.Import(inFilename, outFilename, ImportSourceType.KParser);
                    }
                    catch (Exception ex)
                    {
                        CancelImportOrReparse();

                        StopParsing();
                        Logger.Instance.Log(ex);
                        MessageBox.Show(ex.Message, "Error while attempting to reparse.",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);

                        return;
                    }

                }
                catch (Exception ex)
                {
                    Logger.Instance.Log(ex);
                }
            }
        }

        /// <summary>
        /// Initiate importing a DirectParse/DVSParse database file.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ImportDatabase()
        {
            if (Monitor.Instance.IsRunning == true)
            {
                MessageBox.Show("Cannot import while another parse is running.", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            ImportType importTypeForm = new ImportType();
            if (importTypeForm.ShowDialog(this) == DialogResult.Cancel)
                return;

            ImportSourceType importSource = importTypeForm.ImportSource;

            string inFilename = string.Empty;
            string outFilename = string.Empty;

            if (inFilename == string.Empty)
            {
                OpenFileDialog ofd = new OpenFileDialog();
                ofd.InitialDirectory = defaultSaveDirectory;
                ofd.Multiselect = false;
                ofd.Filter = "Direct Parse Files (*.dpd)|*.dpd|DVS/Direct Parse Files (*.dvsd)|*.dvsd|Database Files (*.sdf)|*.sdf|All Files (*.*)|*.*";
                ofd.FilterIndex = 0;
                ofd.Title = "Select file to import...";

                if (ofd.ShowDialog() == DialogResult.Cancel)
                {
                    return;
                }
                else
                {
                    inFilename = ofd.FileName;
                }
            }

            outFilename = Path.Combine(defaultSaveDirectory, Path.GetFileNameWithoutExtension(inFilename));
            outFilename = Path.ChangeExtension(outFilename, "sdf");

            SaveFileDialog sfd = new SaveFileDialog();
            sfd.InitialDirectory = defaultSaveDirectory;
            sfd.Filter = "Database Files (*.sdf)|*.sdf|All Files (*.*)|*.*";
            sfd.FilterIndex = 0;
            sfd.DefaultExt = "sdf";
            sfd.FileName = outFilename;
            sfd.Title = "Select database file to save parse to...";

            if (sfd.ShowDialog() == DialogResult.OK)
            {
                outFilename = sfd.FileName;

                if (outFilename == inFilename)
                {
                    MessageBox.Show("Cannot save to the same file you want to import.", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                Cursor.Current = Cursors.WaitCursor;
                ReparseMode = ImportMode.Import;

                try
                {
                    try
                    {
                        PrepareToImportOrReparse();

                        Monitor.Instance.Import(inFilename, outFilename, importSource);
                    }
                    catch (Exception ex)
                    {
                        CancelImportOrReparse();

                        StopParsing();
                        Logger.Instance.Log(ex);
                        MessageBox.Show(ex.Message, "Error while attempting to import.",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);

                        return;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Instance.Log(ex);
                }
            }
        }
        #endregion

        #region Menu Support Functions
        /// <summary>
        /// Gets the filename to save the parse output to.  By default it uses
        /// the current date and a numeric progression.
        /// </summary>
        /// <param name="fileName">The name of the file to save the parse to.</param>
        /// <returns>True if the user ok'd the filename, false if it was cancelled.</returns>
        private bool GetParseFileName(out string fileName)
        {
            string baseDateName = string.Format("{0:D2}-{1:D2}-{2:D2}", DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day);

            string dateNameFilter = baseDateName + "_???.sdf";

            string[] files = Directory.GetFiles(defaultSaveDirectory, dateNameFilter);

            int index = 1;

            try
            {
                if (files.Length > 0)
                {
                    Array.Sort(files);

                    string lastFullFileName = files[files.Length - 1];

                    FileInfo fi = new FileInfo(lastFullFileName);

                    string lastFileName = fi.Name;

                    Regex rx = new Regex(@"\d{2}-\d{2}-\d{2}_(\d{3}).sdf");

                    Match match = rx.Match(lastFileName);

                    if (match.Success == true)
                    {
                        if (Int32.TryParse(match.Groups[1].Value, out index) == false)
                        {
                            index = files.Length;
                        }

                        index++;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(string.Format(ex.Message + "\nUsing date index 1.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error));
            }

            string dateName = Path.Combine(defaultSaveDirectory, string.Format("{0}_{1:D3}.sdf", baseDateName, index));

            SaveFileDialog sfd = new SaveFileDialog();
            sfd.InitialDirectory = defaultSaveDirectory;
            sfd.DefaultExt = "sdf";
            sfd.FileName = dateName;
            sfd.Title = "Select database file to save parse to...";

            if (sfd.ShowDialog() == DialogResult.OK)
            {
                fileName = sfd.FileName;
                return true;
            }

            fileName = "";
            return false;
        }

        private void OpenFile(string fileName)
        {
            if (File.Exists(fileName) == false)
                return;

            try
            {
                DatabaseManager.Instance.OpenDatabase(fileName);

                //SHAddToRecentDocs(0, fileName);
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(ex);
                MessageBox.Show("Unable to open database.  You may need to reparse or upgrade the database file.",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                Cursor.Current = Cursors.WaitCursor;

                // Update recent files
                if (recentFilesList.Contains(fileName))
                    recentFilesList.Remove(fileName);

                recentFilesList.Insert(0, fileName);

                UpdateRecentFilesMenu();

                // Notify all plugins
                MobXPHandler.Instance.Reset();

                NotifyPlugins(ProfilingFlag.RunProfiling);

                // Change title of window
                string parseFileName = (new FileInfo(fileName)).Name;

                this.Text = string.Format("{0} - {1}", parseFileName, Application.ProductName);
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(ex);
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }

        private void CloseFile()
        {
            try
            {
                Cursor.Current = Cursors.WaitCursor;

                StopParsing();

                DatabaseManager.Instance.CloseDatabase();

                // Notify all plugins
                MobXPHandler.Instance.Reset();

                NotifyPlugins();

                // Change title of window
                this.Text = string.Format("{0}", Application.ProductName);
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(ex);
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }

        private void UpdateRecentFilesMenu()
        {
            windowSettings.Reload();

            recentFilesToolStripMenuItem.DropDownItems.Clear();
            recentFilesToolStripMenuItem.DropDownItems.Add(noneToolStripMenuItem);

            ToolStripMenuItem tsmi;
            // Max width of name (in px) after shortening the name.
            int maxWidth = 400;

            if (recentFilesList.Count > 0)
            {
                for (int i = 0; i < recentFilesList.Count && i < windowSettings.NumberOfRecentFilesToDisplay; i++)
                {
                    tsmi = new ToolStripMenuItem(recentFilesList[i], null, recentFile_Click, recentFilesList[i]);
                    tsmi.ToolTipText = tsmi.Name;
                    tsmi.Text = ShortenedRecentFileName(tsmi.Name, maxWidth, tsmi.Height);
                    recentFilesToolStripMenuItem.DropDownItems.Add(tsmi);
                }
            }
        }

        private string ShortenedRecentFileName(string fullPathName, int width, int height)
        {
            string modifiedPathName = String.Copy(fullPathName);
            Size boxSize = new Size(width, height);

            // This function modifies the string in-place when using the ModifyString flag.
            TextRenderer.MeasureText(modifiedPathName, defaultMenuFont, boxSize,
                TextFormatFlags.ModifyString | TextFormatFlags.PathEllipsis);

            string shortPathName;
            int index = modifiedPathName.IndexOf('\0');

            if (index > 0)
                shortPathName = modifiedPathName.Substring(0, index);
            else
                shortPathName = modifiedPathName;

            return shortPathName;
        }

        private void recentFilesToolStripMenuItem_DropDownOpening(object sender, EventArgs e)
        {
            if (recentFilesList.Count == 0)
                noneToolStripMenuItem.Visible = true;
            else
                noneToolStripMenuItem.Visible = false;

        }

        private void recentFile_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem tsmi = sender as ToolStripMenuItem;

            if (tsmi == noneToolStripMenuItem)
                return;

            if (tsmi != null)
            {
                FileInfo fi = new FileInfo(tsmi.Name);

                programStatusLabel.Text = string.Empty;

                if (fi.Exists)
                {
                    OpenFile(tsmi.Name);
                }
                else
                {
                    recentFilesList.Remove(tsmi.Name);
                    UpdateRecentFilesMenu();
                    MessageBox.Show(Resources.PublicResources.FileDoesNotExist, Resources.PublicResources.Error);
                }
            }
        }

        #endregion

        #region Plugin Tab/Window Management
        /// <summary>
        /// Search all DLLs in the application directory for classes derived from the
        /// abstract plugin class.  If one exists, create an instance of that class
        /// and add it to the list of available plugins.
        /// </summary>
        private void FindAndLoadPlugins()
        {
            // Get the DLLs in the application directory
            string dllFilter = "*.dll";
            string[] files = Directory.GetFiles(applicationDirectory, dllFilter);

            Assembly a;
            Type pluginInterfaceType = typeof(WaywardGamers.KParser.Plugin.IPlugin);
            Type userControlType = typeof(UserControl);

            foreach (string file in files)
            {
                try
                {
                    a = Assembly.LoadFrom(file);
                }
                catch (BadImageFormatException)
                {
                    continue;
                }

                // Don't look in the core for plugins [change this to plugin base dll later]
                if (a.ManifestModule.Name != "WaywardGamers.KParser.ParserCore.dll")
                {
                    Type[] exportedTypes = a.GetExportedTypes();

                    // Check the types in each one
                    foreach (Type t in exportedTypes)
                    {
                        // If they're of type PluginBase, and aren't the abstract parent type,
                        // add them to our list of valid plugins.
                        if ((t.IsPublic == true) &&
                            (t.IsSubclassOf(userControlType) == true) &&
                            (pluginInterfaceType.IsAssignableFrom(t) == true))
                        {
                            try
                            {
                                pluginList.Add((IPlugin)Activator.CreateInstance(t));

                            }
                            catch (Exception ex)
                            {
                                Logger.Instance.Log(ex);
                            }
                        }
                    }
                }
            }

            if (windowSettings.activePluginList != null)
            {
                foreach (var plug in pluginList)
                {
                    if (windowSettings.activePluginList.Contains(plug.TabName) == true)
                    {
                        plug.IsActive = true;
                    }
                }
            }
        }

        /// <summary>
        /// Called on startup, this adds the names of the plugins to the Window
        /// menu so that the user can enable/disable individual plugins.
        /// </summary>
        private void PopulateTabsMenu()
        {
            // This is only run once.

            // Add a separator under the About menu item if we have
            // any plugins available.
            if (pluginList.Count > 0)
            {
                windowsMenu.DropDownItems.Add(windowsToolStripSeparator);
            }
            else
            {
                return;
            }

            pluginList = pluginList.OrderBy(p => p.TabName).ToList();

            // Create a menu item and tab page for each plugin, with synced indexes.
            for (int i = 0; i < pluginList.Count; i++)
            {
                ToolStripMenuItem tsmi = new ToolStripMenuItem(pluginList[i].TabName);
                tsmi.Name = pluginList[i].TabName;
                tsmi.CheckOnClick = true;
                tsmi.CheckedChanged += new EventHandler(tabMenuItem_CheckedChanged);
                windowsMenu.DropDownItems.Add(tsmi);
                // Keep track of it for changing the name during re-localization.
                tabMenuList.Add(tsmi);

                TabPage tp = new TabPage(pluginList[i].TabName);
                tabList.Add(tp);

                BuildTab(tp, pluginList[i]);

                tsmi.Checked = pluginList[i].IsActive;
            }

            if (pluginTabs.TabCount > 0)
                pluginTabs.SelectedIndex = 0;
        }

        private void ActivateParityTabs()
        {
            for (int index = 0; index < pluginList.Count; index++)
            {
                IPlugin plugin = pluginList[index];
                if (plugin.IsDebug)
                    continue;

                if (!tabMenuList[index].Checked)
                    tabMenuList[index].Checked = true;
            }

            if (pluginTabs.TabCount > 0)
                pluginTabs.SelectedIndex = 0;
        }

        /// <summary>
        /// Configure the tab the will contain the specified plugin control.
        /// </summary>
        /// <param name="tp">The tab that gets the plugin.</param>
        /// <param name="iPlugin">The plugin that goes in the tab.</param>
        private void BuildTab(TabPage tp, IPlugin iPlugin)
        {
            iPlugin.Reset();
            UserControl control = iPlugin.Control;

            control.Anchor = (AnchorStyles)(AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top);
            tp.Controls.Add(control);
            control.Location = new System.Drawing.Point(2, 2);
            control.Size = new Size(tp.Width - 4, tp.Height - 4);
        }

        /// <summary>
        /// When a plugin is checked/unchecked from the Window menu, add or
        /// remove it from active plugin list, then update the visible tabs.
        /// </summary>
        void tabMenuItem_CheckedChanged(object sender, EventArgs e)
        {
            ToolStripMenuItem tsmi = sender as ToolStripMenuItem;

            if (tsmi == null)
                return;

            TabPage tabFromMenu = tabList.FirstOrDefault(t => t.Text == tsmi.Text);

            // If we can't find the associated TabPage, make sure the menu item is unchecked and just return.
            if (tabFromMenu == null)
            {
                tsmi.Checked = false;
                return;
            }

            // Are we turning the tab on or off?
            if (tsmi.Checked == false)
            {
                var plugin = pluginList.FirstOrDefault(p => p.TabName == tsmi.Text);

                if (plugin != null)
                {
                    plugin.IsActive = false;
                }

                pluginTabs.TabPages.Remove(tabFromMenu);
            }
            else
            {
                var plugin = pluginList.FirstOrDefault(p => p.TabName == tsmi.Text);

                if (plugin != null)
                {
                    pluginTabs.TabPages.Add(tabFromMenu);
                    plugin.IsActive = true;

                    if (DatabaseManager.Instance.IsDatabaseOpen)
                    {
                        try
                        {
                            Cursor.Current = Cursors.WaitCursor;

                            plugin.NotifyOfUpdate();
                        }
                        finally
                        {
                            Cursor.Current = Cursors.Default;
                        }
                    }

                    pluginTabs.SelectedTab = tabFromMenu;
                }
            }
        }

        /// <summary>
        /// Close the tab currently under the cursor.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void closeTabToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                if (currentTab != null)
                {
                    int currentTabIndex = tabList.IndexOf(currentTab);

                    ToolStripMenuItem tabOnMenu = tabMenuList[currentTabIndex];

                    if (tabOnMenu != null)
                        tabOnMenu.Checked = false;
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(ex);
            }
        }

        /// <summary>
        /// Close all tabs other than the one currently under the cursor.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void closeOtherTabsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                if (currentTab != null)
                {
                    int currentTabIndex = tabList.IndexOf(currentTab);

                    ToolStripMenuItem tabOnMenu = tabMenuList[currentTabIndex];

                    foreach (var menuItem in tabMenuList)
                    {
                        if (menuItem != tabOnMenu)
                            menuItem.Checked = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(ex);
            }
        }

        /// <summary>
        /// Determine which tab is currently under the cursor.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void pluginTabs_MouseMove(object sender, MouseEventArgs e)
        {
            if (pluginTabs == sender)
            {
                // tab height
                int height = pluginTabs.ItemSize.Height;

                if (e.Y > height)
                    return;

                currentTab = null;

                for (int index = 0; index < pluginTabs.TabCount; index++)
                {
                    if (pluginTabs.GetTabRect(index).Contains(e.X, e.Y))
                    {
                        currentTab = pluginTabs.TabPages[index];
                        break;
                    }
                }
            }
        }
        #endregion

        #region Parsing Control Methods
        private bool StartParsing(
            string outputFileName,
            uint? memoryOffsetOverride = null,
            bool showErrors = true)
        {
            appSettings.Reload();
            if (memoryOffsetOverride.HasValue)
            {
                appSettings.ParseMode = DataSource.Ram;
                appSettings.MemoryOffset = memoryOffsetOverride.Value;
            }

            if (!VerifyAdminAccess())
            {
                uiLastError = Resources.PublicResources.AdminPrivilegeNeeded;
                if (showErrors)
                {
                    MessageBox.Show(Resources.PublicResources.AdminPrivilegeNeeded,
                        Resources.PublicResources.Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                return false;
            }

            programStatusLabel.Text = string.Empty;

            // Let the database notify us of changes, and we'll notify the active plugins.
            DatabaseManager.Instance.DatabaseChanging += MonitorDatabaseChanging;
            DatabaseManager.Instance.DatabaseChanged += MonitorDatabaseChanged;

            bool reopeningFile = (outputFileName != string.Empty) && File.Exists(outputFileName);

            // Reset all plugins
            foreach (IPlugin plugin in pluginList)
            {
                if (plugin.IsActive)
                    plugin.Reset();
            }

            // Reset the xp handler
            MobXPHandler.Instance.Reset();

            try
            {
                Monitor.Instance.Start(appSettings.ParseMode, outputFileName);

                if (reopeningFile == true)
                {
                    NotifyPlugins();
                }

                // Get the database file name (either specified or default)
                FileInfo parseFile = new FileInfo(DatabaseManager.Instance.DatabaseFilename);


                // Update recent files
                if (recentFilesList.Contains(parseFile.FullName))
                    recentFilesList.Remove(parseFile.FullName);

                recentFilesList.Insert(0, parseFile.FullName);

                UpdateRecentFilesMenu();


                // Update window title
                string parseFileName = parseFile.Name;

                this.Text = string.Format("{0} - {1}", parseFileName, Application.ProductName);

                programStatusLabel.Text = "Parsing...";
                return true;
            }
            catch (Exception e)
            {
                StopParsing();
                programStatusLabel.Text = "Error.  Parsing stopped.";
                Logger.Instance.Log(e);
                uiLastError = e.Message;
                if (showErrors)
                {
                    MessageBox.Show(e.Message, "Error while attempting to initiate parse.",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return false;
            }
        }

        /// <summary>
        /// Check if the user/program has admin privileges (and thus can read
        /// other process's memory).  Return true if it's safe to parse (including
        /// if we're parsing from logs, which don't need the access), otherwise
        /// return false.
        /// </summary>
        /// <returns>True if we have necessary privileges to parse.</returns>
        private bool VerifyAdminAccess()
        {
            if ((appSettings.ParseMode == DataSource.Ram) || (appSettings.ParseMode == DataSource.Packet))
            {
                //Get Operating system information.
                OperatingSystem os = Environment.OSVersion;

                //Get version information about the os.
                Version vs = os.Version;

                if (os.Platform == PlatformID.Win32NT)
                {
                    // Check if we're running Vista or Windows 7
                    // Those two default to running a restricted user
                    // account, only elevating when needed.  As such
                    // we need to ensure we have admin privileges
                    // in order to read the FFXI process memory.
                    if (vs.Major == 6)
                    {
                        bool isElevated;
                        WindowsIdentity identity = WindowsIdentity.GetCurrent();
                        WindowsPrincipal principal = new WindowsPrincipal(identity);
                        isElevated = principal.IsInRole(WindowsBuiltInRole.Administrator);

                        return isElevated;
                    }
                }
            }

            // Everything's clear for continuing.
            return true;
        }

        private void StopParsing()
        {
            Cursor.Current = Cursors.Default;

            if (Monitor.Instance.IsRunning == true)
            {
                Monitor.Instance.Stop();
                programStatusLabel.Text = "Stopped";

                DatabaseManager.Instance.DatabaseChanging -= MonitorDatabaseChanging;
                DatabaseManager.Instance.DatabaseChanged -= MonitorDatabaseChanged;

                NotifyPlugins();
            }
        }

        private void NotifyPlugins()
        {
            NotifyPlugins(ProfilingFlag.NoProfiling);
        }

        /// <summary>
        /// Notify all plugins to update their data.  This is called when opening,
        /// reopening or ending a parse.
        /// </summary>
        /// <param name="profile">Indicate whether to do profile timing per plugin.</param>
        private void NotifyPlugins(ProfilingFlag profileFlag)
        {
            try
            {
                if (profileFlag == ProfilingFlag.RunProfiling)
                {
                    foreach (IPlugin plugin in pluginList)
                    {
                        if (plugin.IsActive)
                        {
                            using (new RegionProfiler("Opening " + plugin.TabName))
                            {
                                plugin.NotifyOfUpdate();
                            }
                        }
                    }
                }
                else
                {
                    foreach (IPlugin plugin in pluginList)
                    {
                        if (plugin.IsActive)
                            plugin.NotifyOfUpdate();
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Instance.Log(e);
                MessageBox.Show(e.Message, "Error while attempting to stop parse.",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Shutdown()
        {
            Monitor.Instance.Stop();
            programStatusLabel.Text = "Stopped.";
        }

        private void MonitorDatabaseChanging(object sender, DatabaseWatchEventArgs e)
        {
            foreach (IPlugin plugin in pluginList)
            {
                if (plugin.IsActive)
                    plugin.WatchDatabaseChanging(sender, e);
            }
        }

        private void MonitorDatabaseChanged(object sender, DatabaseWatchEventArgs e)
        {
            foreach (IPlugin plugin in pluginList)
            {
                if (plugin.IsActive)
                    plugin.WatchDatabaseChanged(sender, e);
            }
        }

        private void AddMonitorChanging()
        {
            DatabaseManager.Instance.DatabaseChanging += MonitorDatabaseChanging;
        }

        public void RemoveMonitorChanging()
        {
            DatabaseManager.Instance.DatabaseChanging -= MonitorDatabaseChanging;

            NotifyPlugins();
        }
        #endregion

        #region Code for testing stuff
        private void menuTestItem_Click(object sender, EventArgs e)
        {
            //Application.CommonAppDataPath;
            //Application.UserAppDataPath;

            ScanMemoryForUpdatedMemloc();

            //TestArrayCopySpeed();

            //RunATest();

            //TestImpetus();

            //TestMonad();
        }

        private void TestImpetus()
        {
            int sum = 0;
            int bonus = 0;
            int countHits = 0;
            int sequenceLimit = 10000000;
            int acc = 950;
            int currentSequenceLength = 0;
            int highestSequenceLength = 0;
            int sumSequenceLength = 0;
            int countSequenceLength = 0;

            Random swing = new Random(DateTime.Now.Millisecond);

            for (int i = 1; i < sequenceLimit; i++)
            {
                int hit = swing.Next(1000);

                if (currentSequenceLength > 63)
                {
                    sumSequenceLength += currentSequenceLength;
                    currentSequenceLength = 0;
                    bonus = 0;
                }

                if (hit < acc)
                {
                    currentSequenceLength++;
                    countHits++;
                    sum += bonus;
                    bonus += 2;
                    if (bonus > 100)
                        bonus = 100;
                }
                else
                {
                    sumSequenceLength += currentSequenceLength;
                    if (currentSequenceLength > 0)
                        countSequenceLength++;

                    currentSequenceLength = 0;
                    bonus = 0;
                }

                if (currentSequenceLength > highestSequenceLength)
                    highestSequenceLength = currentSequenceLength;
            }

            double avgBonus = 0;
            double avgSequenceLength = 0;

            if (countHits > 0)
                avgBonus = (double)sum / countHits;

            if (countSequenceLength > 0)
                avgSequenceLength = (double)sumSequenceLength / countSequenceLength;

            Trace.WriteLine(string.Format("{0} hits, {1} avg bonus, {2} longest sequence, {3} avg sequence length",
                countHits, avgBonus, highestSequenceLength, avgSequenceLength));

        }

        private static void RunATest()
        {
            Parsing.InternalTesting testCode = new Parsing.InternalTesting();
            //testCode.RunA1LineTest();
            testCode.RunA2LineTest();
        }

        private static void TestMonad()
        {
            try
            {
                Monad.MonadParser monad = new WaywardGamers.KParser.Monad.MonadParser();
                monad.RunTests();
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(ex);
            }
        }

        private static void ScanMemoryForUpdatedMemloc()
        {
            Monitor.Instance.ScanRAM();
        }

        private static void TestArrayCopySpeed()
        {

            int arraySize = 5000000;
            byte[] baseArray = new byte[arraySize];
            byte[] copyToArray = new byte[arraySize];

            // fill starting array
            for (int i = 0; i < arraySize; i++)
            {
                baseArray[i] = (byte)(i % 256);
            }

            using (new RegionProfiler("array copy x100"))
            {
                for (int i = 0; i < 100; i++)
                {
                    for (int j = 0; j < arraySize; j++)
                    {
                        copyToArray[i] = baseArray[i];
                    }
                }
            }


            using (new RegionProfiler("blockcopy x100"))
            {
                for (int i = 0; i < 100; i++)
                {
                    Buffer.BlockCopy(baseArray, 0, copyToArray, 0, arraySize);
                }
            }
        }
        #endregion

    }
}