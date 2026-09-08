using System;
using System.Collections.Generic;
using System.Linq;
using WaywardGamers.KParser.Interface;
using WaywardGamers.KParser.Parsing;

namespace WaywardGamers.KParser.Monitoring
{
    /// <summary>
    /// Class to handle directing requests to start and stop monitoring
    /// of FFXI data to the proper type of reader based on settings pref.
    /// </summary>
    public class Monitor
    {
        #region Singleton Construction
        /// <summary>
        /// Make the class a singleton
        /// </summary>
        private static readonly Monitor instance = new Monitoring.Monitor();

        /// <summary>
        /// Gets the singleton instance of the NewMessageManager class.
        /// </summary>
        public static Monitor Instance { get { return instance; } }
        
        /// <summary>
        /// Private constructor ensures singleton purity.
        /// </summary>
        private Monitor()
		{
            RamReader.Instance.ReaderDataChanged += ReaderDataListener;
            LogReader.Instance.ReaderDataChanged += ReaderDataListener;
            DatabaseReader.Instance.ReaderDataChanged += ReaderDataListener;

            RamReader.Instance.ReaderStatusChanged += ReaderStatusListener;
            LogReader.Instance.ReaderStatusChanged += ReaderStatusListener;
            DatabaseReader.Instance.ReaderStatusChanged += ReaderStatusListener;
        }
        #endregion

        #region Class members
        // Hold the current reader.  Set to a default value.
        IReader currentReader = RamReader.Instance;
        #endregion

        #region Current reader properties
        /// <summary>
        /// Gets whether the current reader is running.
        /// </summary>
        public bool IsRunning
        {
            get
            {
                return currentReader.IsRunning;
            }
        }

        /// <summary>
        /// Gets the DataSource type of the current reader.
        /// </summary>
        public DataSource ParseMode
        {
            get
            {
                return currentReader.ParseModeType;
            }
        }
        #endregion

        #region Functions for event handling
        /// <summary>
        /// Event to link to to receive the data the reader reads.
        /// </summary>
        public event ReaderDataHandler ReaderDataChanged;

        /// <summary>
        /// Event to link to to receive the reader's current progress/status.
        /// </summary>
        public event ReaderStatusHandler ReaderStatusChanged;

        /// <summary>
        /// This function listens to the current IReader and re-broadcasts any
        /// events that get sent out.
        /// </summary>
        /// <param name="sender">The IReader sender.</param>
        /// <param name="e">The data event data.</param>
        private void ReaderDataListener(object sender, ReaderDataEventArgs e)
        {
            if ((sender as IReader) == currentReader)
            {
                OnReaderDataChanged(sender, e);
            }
        }

        /// <summary>
        /// This function listens to the current IReader and re-broadcasts any
        /// events that get sent out.  If the parse is ending, also notify
        /// the MsgManager class to end the session.
        /// </summary>
        /// <param name="sender">The IReader sender.</param>
        /// <param name="e">The status event data.</param>
        private void ReaderStatusListener(object sender, ReaderStatusEventArgs e)
        {
            if ((sender as IReader) == currentReader)
            {
                OnReaderStatusChanged(sender, e);
            }
        }

        /// <summary>
        /// This function is called to notify any attached listeners about
        /// new reader data.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected void OnReaderDataChanged(object sender, ReaderDataEventArgs e)
        {
            ReaderDataHandler localDataChanged = ReaderDataChanged;
            if (localDataChanged != null)
            {
                localDataChanged(sender, e);
            }
        }

        /// <summary>
        /// This function is called to notify any attached listeners about
        /// the current reader status.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected void OnReaderStatusChanged(object sender, ReaderStatusEventArgs e)
        {
            ReaderStatusHandler localStatusChanged = ReaderStatusChanged;
            if (localStatusChanged != null)
            {
                localStatusChanged(sender, e);
            }
        }
        #endregion

        #region Functions to start and stop monitoring.
        // TODO: Move the logic for creating the database, etc, outside
        // of this class.

        /// <summary>
        /// Initiate monitoring of FFXI RAM/logs for data to be parsed.
        /// </summary>
        /// <param name="dataSourceType">The type of data source to monitor.</param>
        /// <param name="outputFileName">The name of the database file
        /// that the parsed data will be stored in.</param>
        public void Start(DataSource dataSourceType, string outputFileName)
        {
            if (currentReader.IsRunning == true)
                throw new InvalidOperationException(string.Format(
                    "{0} is already running", currentReader.GetType().Name));

            switch (dataSourceType)
            {
                case DataSource.Log:
                    currentReader = LogReader.Instance;
                    break;
                case DataSource.Ram:
                    currentReader = RamReader.Instance;
                    break;
                case DataSource.Packet:
                    currentReader = PacketReader.Instance;
                    break;
                case DataSource.Database:
                    currentReader = DatabaseReader.Instance;
                    break;
                default:
                    throw new ArgumentOutOfRangeException("dataSourceType",
                        string.Format("Unknown DataSource value: {0}", dataSourceType.ToString()));
            }

            // Create the output database in preperation for a new run.
            DatabaseManager.Instance.CreateDatabase(outputFileName);

            try
            {
                MsgManager.Instance.StartNewSession();
                currentReader.Start();
            }
            catch (Exception)
            {
                MsgManager.Instance.EndSession();
                throw;
            }
        }

        /// <summary>
        /// Start the RAM reader without creating a database or starting the
        /// parser session. Consumers can subscribe to ReaderDataChanged to
        /// capture the raw ChatLine stream without changing parser state.
        /// </summary>
        public void StartRawCapture()
        {
            if (currentReader.IsRunning == true)
                throw new InvalidOperationException(string.Format(
                    "{0} is already running", currentReader.GetType().Name));

            currentReader = RamReader.Instance;
            RamReader.Instance.StartRawCapture();
        }

        /// <summary>
        /// Continue parsing against an existing database.
        /// </summary>
        public void Continue(DataSource dataSourceType)
        {
            if (currentReader.IsRunning == true)
                throw new InvalidOperationException(string.Format(
                    "{0} is already running", currentReader.GetType().Name));

            if (DatabaseManager.Instance.IsDatabaseOpen == false)
            {
                throw new InvalidOperationException(
                    "You must have a database already open in order to continue parsing.");
            }

            switch (dataSourceType)
            {
                case DataSource.Log:
                    currentReader = LogReader.Instance;
                    break;
                case DataSource.Ram:
                    currentReader = RamReader.Instance;
                    break;
                case DataSource.Packet:
                    currentReader = PacketReader.Instance;
                    break;
                case DataSource.Database:
                    currentReader = DatabaseReader.Instance;
                    break;
                default:
                    throw new ArgumentOutOfRangeException("dataSourceType",
                        string.Format("Unknown DataSource value: {0}", dataSourceType.ToString()));
            }

            try
            {
                MsgManager.Instance.StartNewSession();
                currentReader.Start();
            }
            catch (Exception)
            {
                MsgManager.Instance.EndSession();
                throw;
            }
        }

        /// <summary>
        /// Import data from another database (reparse, DVS, DirectParse, etc)
        /// </summary>
        /// <param name="inFilename">The name of the database file to import.</param>
        /// <param name="outputFileName">The name of the new database.</param>
        /// <param name="importSource">The type of database to import.</param>
        public void Import(string inFilename, string outputFileName, ImportSourceType importSource)
        {
            if (currentReader.IsRunning == true)
                throw new InvalidOperationException(string.Format(
                    "{0} is already running", currentReader.GetType().Name));

            currentReader = DatabaseReader.Instance;

            DatabaseManager.Instance.CreateDatabase(outputFileName);
            System.Threading.Thread.Sleep(100);

            IDBReader dbReader;
            bool upgradeTimestamp = false;

            switch (importSource)
            {
                case ImportSourceType.KParser:
                    dbReader = KParserReadingManager.Instance;
                    break;
                case ImportSourceType.DirectParse:
                case ImportSourceType.DVSParse:
                    dbReader = DirectParseReadingManager.Instance;
                    break;
                default:
                    throw new InvalidOperationException();
            }

            dbReader.OpenDatabase(inFilename);

            if (dbReader is KParserReadingManager)
            {
                // Auto-detect files needing timestamp upgrades.
                if (KParserReadingManager.Instance.DatabaseParseVersion.CompareTo("1.3") < 0)
                    upgradeTimestamp = true;
            }

            try
            {
                MsgManager.Instance.StartNewSession();

                currentReader.Import(importSource, dbReader, upgradeTimestamp);
            }
            catch (Exception)
            {
                MsgManager.Instance.EndSession();
                throw;
            }
        }

        /// <summary>
        /// Import data (within a specific timestamp range) from
        /// another database (reparse, DVS, DirectParse, etc)
        /// </summary>
        /// <param name="inFilename">The name of the database file to import.</param>
        /// <param name="outputFileName">The name of the new database.</param>
        /// <param name="importSource">The type of database to import.</param>
        public void ImportRange(string inFilename, string outputFileName, ImportSourceType importSource,
            DateTime startOfRange, DateTime endOfRange)
        {
            if (currentReader.IsRunning == true)
                throw new InvalidOperationException(string.Format(
                    "{0} is already running", currentReader.GetType().Name));

            currentReader = DatabaseReader.Instance;

            DatabaseManager.Instance.CreateDatabase(outputFileName);
            System.Threading.Thread.Sleep(100);

            IDBReader dbReader;
            bool upgradeTimestamp = false;

            switch (importSource)
            {
                case ImportSourceType.KParser:
                    dbReader = KParserReadingManager.Instance;
                    break;
                case ImportSourceType.DirectParse:
                case ImportSourceType.DVSParse:
                    // Not supported
                default:
                    throw new InvalidOperationException();
            }

            dbReader.OpenDatabase(inFilename);

            if (dbReader == KParserReadingManager.Instance)
            {
                // Auto-detect files needing timestamp upgrades.
                if (KParserReadingManager.Instance.DatabaseParseVersion.CompareTo("1.3") < 0)
                    upgradeTimestamp = true;
            }

            try
            {
                MsgManager.Instance.StartNewSession();

                currentReader.ImportRange(importSource, dbReader, upgradeTimestamp, startOfRange, endOfRange);
            }
            catch (Exception)
            {
                MsgManager.Instance.EndSession();
                throw;
            }
        }

        /// <summary>
        /// Import data from another database (reparse, DVS, DirectParse, etc)
        /// </summary>
        /// <param name="inFilename1">The name of the first database file to import.</param>
        /// <param name="inFilename2">The name of the second database file to import.</param>
        /// <param name="outputFileName">The name of the new database.</param>
        /// <param name="importSource">The type of database to import.</param>
        public void Join(string inFilename1, string inFilename2, string outputFileName,
            ImportSourceType importSource)
        {
            try
            {
                if (currentReader.IsRunning == true)
                    throw new InvalidOperationException(string.Format(
                        "{0} is already running", currentReader.GetType().Name));

                currentReader = DatabaseReader.Instance;

                DatabaseManager.Instance.CreateDatabase(outputFileName);
                System.Threading.Thread.Sleep(100);

                IDBReader dbReader;
                IDBReader dbReader2;

                switch (importSource)
                {
                    case ImportSourceType.KParser:
                        dbReader = KParserReadingManager.Instance;
                        dbReader2 = new KParserReadingManager();
                        break;
                    case ImportSourceType.DirectParse:
                    case ImportSourceType.DVSParse:
                    // Not supported
                    default:
                        throw new InvalidOperationException();
                }

                dbReader.OpenDatabase(inFilename1);
                dbReader2.OpenDatabase(inFilename2);

                if (dbReader is KParserReadingManager)
                {
                    // Auto-detect files needing timestamp upgrades.
                    if (dbReader.DatabaseParseVersion.CompareTo("1.3") < 0)
                        throw new InvalidOperationException("Reparse to upgrade timestamps first.");
                    if (dbReader2.DatabaseParseVersion.CompareTo("1.3") < 0)
                        throw new InvalidOperationException("Reparse to upgrade timestamps first.");
                }

                try
                {
                    MsgManager.Instance.StartNewSession();

                    currentReader.Join(importSource, dbReader, dbReader2);
                }
                catch (Exception)
                {
                    MsgManager.Instance.EndSession();
                    throw;
                }
            }
            catch (Exception)
            {
                ReaderStatusListener(currentReader, new ReaderStatusEventArgs(0, 0, false, true));
                throw;
            }
        }

        /// <summary>
        /// Import data from another database (reparse, DVS, DirectParse, etc)
        /// </summary>
        /// <param name="inFilename1">The name of the first database file to import.</param>
        /// <param name="inFilename2">The name of the second database file to import.</param>
        /// <param name="outputFileName">The name of the new database.</param>
        /// <param name="importSource">The type of database to import.</param>
        public void JoinAll(List<string> inFilenames, string outputFileName, ImportSourceType importSource)
        {
            try
            {
                if (currentReader.IsRunning == true)
                    throw new InvalidOperationException(string.Format(
                        "{0} is already running", currentReader.GetType().Name));

                if (inFilenames.Count < 2)
                    throw new ArgumentOutOfRangeException("inFilenames",
                        "At least two files must be selected for joining.");

                if (importSource != ImportSourceType.KParser)
                    throw new ArgumentOutOfRangeException("importSource",
                        "Can only perform joins on KParser parses.");


                currentReader = DatabaseReader.Instance;

                DatabaseManager.Instance.CreateDatabase(outputFileName);
                System.Threading.Thread.Sleep(100);

                IDBReader dbReader = KParserReadingManager.Instance;
                IDBReader dbReader2 = new KParserReadingManager();

                var mainParse = inFilenames.First();
                var otherParses = inFilenames.Skip(1);

                dbReader.OpenDatabase(mainParse);

                if (dbReader.DatabaseParseVersion.CompareTo("1.3") < 0)
                    throw new InvalidOperationException("Reparse to upgrade timestamps first.");

                foreach (var parse in otherParses)
                {
                    try
                    {
                        dbReader2.OpenDatabase(parse);

                        try
                        {
                            MsgManager.Instance.StartNewSession();

                            currentReader.Join(importSource, dbReader, dbReader2);
                        }
                        catch (Exception)
                        {
                            MsgManager.Instance.EndSession();
                            throw;
                        }
                    }
                    finally
                    {
                        dbReader2.CloseDatabase();
                    }
                }
            }
            catch (Exception)
            {
                ReaderStatusListener(currentReader, new ReaderStatusEventArgs(0, 0, false, true));
                throw;
            }
        }

        /// <summary>
        /// Stop the current reader's monitoring.
        /// </summary>
        public void Stop()
        {
            currentReader.Stop();
            MsgManager.Instance.EndSession();
        }

        /// <summary>
        /// Stop a raw RAM capture without ending a parser session or
        /// touching the database.
        /// </summary>
        public void StopRawCapture()
        {
            if (currentReader == RamReader.Instance)
                RamReader.Instance.StopRawCapture();
            else
                currentReader.Stop();
        }
        #endregion

        #region Debugging functions
        /// <summary>
        /// Artificially set the current reader for specific parse modes.
        /// </summary>
        [System.Diagnostics.Conditional("DEBUG")]
        internal void SetParseMode(DataSource dataSource)
        {
            if (IsRunning == false)
            {
                switch (dataSource)
                {
                    case DataSource.Log:
                        currentReader = LogReader.Instance;
                        break;
                    case DataSource.Ram:
                        currentReader = RamReader.Instance;
                        break;
                    case DataSource.Database:
                        currentReader = DatabaseReader.Instance;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException("dataSourceType",
                            string.Format("Unknown DataSource value: {0}", dataSource.ToString()));
                }
            }
        }

        /// <summary>
        /// Initiate functions to analyze RAM when seeking for new Memloc.
        /// </summary>
        [System.Diagnostics.Conditional("DEBUG")]
        public void ScanRAM()
        {
            currentReader = RamReader.Instance;
            RamReader.Instance.ScanRAM();
        }
        #endregion

    }
}
