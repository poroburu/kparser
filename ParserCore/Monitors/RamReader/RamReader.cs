using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using WaywardGamers.KParser.Monitoring.Memory;
using WaywardGamers.KParser.Interface;

namespace WaywardGamers.KParser.Monitoring
{
    /// <summary>
    /// This class is created and has its monitor function run as a thread.
    /// It checks for changes in the POL chat log, and when detected reads
    /// that log data as chat data lines.  That collection of lines is stored
    /// in the event data of RamWatchEventArgs, and then any watchers of
    /// this class (ie: RamReader) are notified by firing the event delegates.
    /// The watching class then has the duty of dissecting the chat lines and
    /// sending them to be parsed.
    /// </summary>

    /// <summary>
    /// Class to handle interfacing with system RAM reading
    /// in order to monitor the FFXI process space and read
    /// log info to be parsed.
    /// </summary>
    internal class RamReader : AbstractReader
    {
        #region Singleton Constructor
        // Make the class a singleton
        private static readonly RamReader instance = new RamReader();

        /// <summary>
        /// Gets the singleton instance of the LogParser class.
        /// This is the only internal way to access the singleton.
        /// </summary>
        internal static RamReader Instance { get { return instance; } }

        /// <summary>
        /// Private constructor ensures singleton purity.
        /// </summary>
        private RamReader()
		{
            // Layout of total structure:
            // 50 offsets to new log records (short): 100 bytes
            // 50 offsets to old log offsets (short): 100 bytes
            // ChatLogInfoStruct block
            sizeOfChatLogInfoStruct = (uint)(Marshal.SizeOf(typeof(ChatLogInfoStruct)));

            sizeOfChatLogControlStruct = (uint)(Marshal.SizeOf(typeof(ChatLogControlStruct)));
        }
		#endregion

        #region Member Variables
        Properties.Settings appSettings = new WaywardGamers.KParser.Properties.Settings();

        Thread readerThread;

        int polPID = 0;
        bool rawCaptureMode;
        const uint HorizonMemoryOffset = 0x0062D8F0;
        POL pol;
        uint initialMemoryOffset;
        ChatLogLocationInfo chatLogLocation;
        ChatLogControlStruct chatLogControl;

        uint sizeOfChatLogInfoStruct;
        uint sizeOfChatLogControlStruct;

        ManualResetEvent abortMonitorThread = new ManualResetEvent(false);
        #endregion

        #region Interface Control Methods and Properties

        /// <summary>
        /// Return type of DataSource this reader works on.
        /// </summary>
        public override DataSource ParseModeType { get { return DataSource.Ram; } }

        /// <summary>
        /// Start a thread that reads log files for parsing.
        /// </summary>
        public override void Start()
        {
            StartInternal(false);
        }

        /// <summary>
        /// Start a reader for the headless ChatLine export path.
        /// </summary>
        internal void StartRawCapture()
        {
            StartInternal(true);
        }

        private void StartInternal(bool rawCapture)
        {
            rawCaptureMode = rawCapture;
            IsRunning = true;

            try
            {
                // Reset the thread
                if ((readerThread != null) && 
                    ((readerThread.ThreadState == System.Threading.ThreadState.Running) ||
                     (readerThread.ThreadState == System.Threading.ThreadState.Background)))
                {
                    readerThread.Abort();
                }

                // Make sure we have the latest version of the app settings data.
                appSettings.Reload();

                // Update the memory offset of the thread class before starting.
                // The headless oracle targets the HorizonXI client.  Its
                // chat-log root is different from the legacy Eden/retail
                // default stored in the shared settings file.
                initialMemoryOffset = rawCaptureMode
                    ? HorizonMemoryOffset
                    : appSettings.MemoryOffset;

                // If the user requests that they be allowed to specify the particular
                // POL process, bring up a form to determine that value.  If not found
                // or not requested, set the polPID to 0 to signal that the later
                // functions should search for it normally.
                if (appSettings.SpecifyPID == true)
                {
                    SelectPOLProcess selectPID = new SelectPOLProcess();
                    if (selectPID.ShowDialog() == DialogResult.OK)
                    {
                        polPID = selectPID.SelectedPID;
                    }
                    else
                    {
                        polPID = 0;
                    }
                }
                else
                {
                    polPID = 0;
                }

                // Begin the thread
                readerThread = new Thread(Monitor);
                readerThread.IsBackground = true;
                readerThread.Name = "Memory Monitor Thread";
                readerThread.Start();
            }
            catch (Exception)
            {
                IsRunning = false;
                throw;
            }
        }

        /// <summary>
        /// Stop the active reader thread.
        /// </summary>
        public override void Stop()
        {
            if (IsRunning == false)
                return;

            Abort();

            IsRunning = false;
        }

        /// <summary>
        /// Stop a headless capture and wait for the monitor thread to finish
        /// publishing its final status.
        /// </summary>
        internal void StopRawCapture()
        {
            Stop();

            if ((readerThread != null) && readerThread.IsAlive)
                readerThread.Join(6000);
        }

        /// <summary>
        /// Call this function rather than aborting the thread directly.
        /// </summary>
        internal void Abort()
        {
            abortMonitorThread.Set();
        }

        /// <summary>
        /// This is an event handler for if/when FFXI exits while we're still running
        /// so that we can clean up properly.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        internal void PolExited(object sender, EventArgs e)
        {
            // Halt monitoring
            Stop();
            pol = null;
        }

        #endregion

        #region Monitor RAM
        /// <summary>
        /// This function is run as a thread to read ram and raise events when new
        /// data shows up.
        /// </summary>
        internal void Monitor()
        {
            try
            {
                abortMonitorThread.Reset();
                bool needToLogin = true;

                pol = rawCaptureMode
                    ? ProcessAccess.GetFFXIProcessForCapture(polPID, abortMonitorThread)
                    : ProcessAccess.GetFFXIProcess(polPID, abortMonitorThread);
                if (pol == null)
                {
                    string statusMessage = rawCaptureMode
                        ? ProcessAccess.LastCaptureFailure
                        : null;

                    if (String.IsNullOrEmpty(statusMessage))
                        statusMessage = "Failed to find FFXI";

                    OnReaderStatusChanged(new ReaderStatusEventArgs()
                    {
                        Active = true,
                        DataSourceType = this.ParseModeType,
                        StatusMessage = statusMessage
                    });

                    return;
                }

                OnReaderStatusChanged(new ReaderStatusEventArgs()
                {
                    Active = true,
                    DataSourceType = DataSource.Ram,
                    StatusMessage = "Found FFXI"
                });

                ChatLogDetails previousDetails = null;
                ChatLogDetails currentDetails;
                int highestLineProcessed = 0;
                
                int numberOfNewLines;
                int numberOfLinesMissed;

                byte[][] newChatLinesBytes;
                byte[][] missedChatLinesBytes;
                byte[][] linesToProcessBytes;

                // Loop until notified to stop or the FFXI process exits.
                while (!abortMonitorThread.WaitOne(0))
                {
                    // If polProcess is ever lost (player disconnects), block on trying to reacquire it.
                    if (pol == null)
                    {
                        pol = rawCaptureMode
                            ? ProcessAccess.GetFFXIProcessForCapture(polPID, abortMonitorThread)
                            : ProcessAccess.GetFFXIProcess(polPID, abortMonitorThread);
                        if (pol == null)
                        {
                            // End here if the FindFFXIProcess returns false,
                            // as that means the monitor thread has been aborted.
                            return;
                        }

                        needToLogin = true;
                    }

                    try
                    {
                        if (needToLogin)
                        {
                            try
                            {
                                chatLogLocation = LocateChatLog();
                            }
                            catch (Exception e)
                            {
                                try
                                {
                                    chatLogLocation = LocateChatLogViaSignature();
                                }
                                catch (Exception e2)
                                {
                                    Logger.Instance.Log(e);
                                    chatLogLocation = null;
                                    throw e2;
                                }

                                if (chatLogLocation == null)
                                {
                                    throw;
                                }
                            }


                            //chatLogLocation = LocateChatLog();
                            needToLogin = false;
                        }
                        
                        //Fetch details such as how many lines are in the chat log, pointers to
                        //the memory containing the actual text, etc.
                        currentDetails = ReadChatLogDetails(chatLogLocation.ChatLogInfoAddress);

                        // If read failed, it will return null.
                        if (currentDetails == null)
                        {
                            OnReaderStatusChanged(new ReaderStatusEventArgs()
                            {
                                Active = true,
                                DataSourceType = DataSource.Ram,
                                StatusMessage = "No details available."
                            });
                            continue;
                        }

                        // If there are zero lines in the NEW chat log, they are not logged into
                        // the game (e.g. at the character selection screen).  Loop in a holding
                        // pattern.
                        if ((currentDetails.ChatLogInfo.NumberOfLines <= 0) || (currentDetails.ChatLogInfo.NumberOfLines > 50))
                        {
                            OnReaderStatusChanged(new ReaderStatusEventArgs()
                            {
                                Active = true,
                                DataSourceType = DataSource.Ram,
                                StatusMessage = string.Format("Not logged in [0x{0:X8}/{1}].",
                                    initialMemoryOffset, currentDetails.ChatLogInfo.NumberOfLines)
                            });

                            needToLogin = true;
                            highestLineProcessed = 0;
                            previousDetails = null;
                            continue;
                        }

                        // If every single field is the same as it was the last time we checked,
                        // assume that the chat log hasn't changed and continue on.
                        if (currentDetails.Equals(previousDetails))
                        {
                            OnReaderStatusChanged(new ReaderStatusEventArgs()
                            {
                                Active = true,
                                DataSourceType = DataSource.Ram,
                                StatusMessage = "No new chat data."
                            });

                            continue;
                        }


                        // Read the chat log strings as byte arrays
                        newChatLinesBytes = ReadChatLinesAsByteArrays(currentDetails.ChatLogInfo.PtrToCurrentChatLog,
                            currentDetails.ChatLogInfo.FinalOffset, currentDetails.ChatLogInfo.NumberOfLines);

                        // Extract the unique line number from the first line so we know where
                        // we are in the chat sequence.
                        int firstLineNumber = GetChatLineLineNumber(newChatLinesBytes[0]);

                        if (previousDetails == null)
                        {
                            //This is our first pass through the loop (i.e. parser just started)
                            //Set our counter to the last line currently in memory, so that next
                            //time through the loop, we start with the first line that wasn't there
                            //when the parser started.

                            previousDetails = currentDetails;
                            highestLineProcessed = firstLineNumber + currentDetails.ChatLogInfo.NumberOfLines - 1;

                            continue;
                        }


                        //It's not our first pass through the loop.  Check if lines were missed
                        numberOfLinesMissed = firstLineNumber - highestLineProcessed - 1;

                        if (numberOfLinesMissed > 0)
                        {
                            OnReaderStatusChanged(new ReaderStatusEventArgs()
                            {
                                Active = true,
                                DataSourceType = DataSource.Ram,
                                StatusMessage = string.Format("Missed {0} lines.", numberOfLinesMissed)
                            });

                            // However we can't deal with more than 50 missed lines. That's as
                            // many as the old log holds.
                            if (numberOfLinesMissed > 50)
                                numberOfLinesMissed = 50;

                            // Find the first index we want to read by counting back from 50 (the
                            // maximum array size) by the number of lines missed.
                            int indexOfFirstMissedLine = 50 - numberOfLinesMissed;

                            IntPtr startOfFirstMissedLine = Pointers.IncrementPointer(currentDetails.ChatLogInfo.PtrToPreviousChatLog,
                                (uint)currentDetails.ChatLogInfo.prevLogOffsets[indexOfFirstMissedLine]);

                            uint numberOfBytesUntilStartOfLastMissedLine = (uint)(currentDetails.ChatLogInfo.prevLogOffsets[49]
                                - currentDetails.ChatLogInfo.prevLogOffsets[indexOfFirstMissedLine]);

                            // There is a field "FinalOffset" in the main Chat log meta struct that tells
                            // us where the last byte of actual chat log text is for the NEW log.  Unfortunately
                            // there is no analogous field for the OLD log.  Because of this, the best
                            // we can do is overestimate.  This has the nasty side effect of leading to 
                            // rare ReadProcessMemory() errors where we attempt to read past the end
                            // of a memory region, and into a block with different protection settings.
                            // Fortunately, even though ReadProcessMemory() returns an error in this case
                            // we can sleep well knowing we got everything we needed.
                            short totalBytesToRead = (short)(numberOfBytesUntilStartOfLastMissedLine + 310);

                            // Read lines from the old chat log.
                            missedChatLinesBytes = ReadChatLinesAsByteArrays(startOfFirstMissedLine, totalBytesToRead, numberOfLinesMissed);

                            if (missedChatLinesBytes.Length == 0)
                            {
                                // We know we missed lines or we wouldn't be here.  On the other hand, when 
                                // we tried to read them we got back nothing.  Log a warning and continue.

                                Trace.WriteLine(String.Format(Thread.CurrentThread.Name + ": Chat monitor missed line(s) [{0}, {1}]",
                                    indexOfFirstMissedLine, indexOfFirstMissedLine + numberOfLinesMissed));
                            }

                            // Make a single array containing all missed lines plus all new lines.
                            linesToProcessBytes = new byte[missedChatLinesBytes.GetLength(0) + newChatLinesBytes.GetLength(0)][];

                            Array.ConstrainedCopy(missedChatLinesBytes, 0, linesToProcessBytes, 0, missedChatLinesBytes.GetLength(0));
                            Array.ConstrainedCopy(newChatLinesBytes, 0, linesToProcessBytes, missedChatLinesBytes.GetLength(0), newChatLinesBytes.GetLength(0));
                        }
                        else
                        {
                            // We have probably already processed some of the lines in the current array.
                            // So the only ones we want to copy to the array of items to process are ones
                            // with a line number higher that the highest line number we've seen.
                            int indexOfFirstLineToProcess = (highestLineProcessed + 1) - firstLineNumber;
                            numberOfNewLines = currentDetails.ChatLogInfo.NumberOfLines - indexOfFirstLineToProcess;

                            OnReaderStatusChanged(new ReaderStatusEventArgs()
                            {
                                Active = true,
                                DataSourceType = DataSource.Ram,
                                StatusMessage = string.Format("Reading {0} line{1}.", numberOfNewLines,
                                    (numberOfNewLines == 1 ? "" : "s"))
                            });

                            linesToProcessBytes = new byte[numberOfNewLines][];
                            Array.ConstrainedCopy(newChatLinesBytes, (int)indexOfFirstLineToProcess, linesToProcessBytes, 0, (int)numberOfNewLines);
                        }



                        if ((linesToProcessBytes != null) && (linesToProcessBytes.GetLength(0) > 0))
                        {
                            highestLineProcessed = (int)GetChatLineLineNumber(linesToProcessBytes[linesToProcessBytes.GetLength(0) - 1]);

                            // Add chat data to eventArgs array for watchers to process
                            List<ChatLine> chatData = new List<ChatLine>(linesToProcessBytes.GetLength(0));

                            foreach (var newLine in linesToProcessBytes)
                            {
                                if (newLine != null)
                                    chatData.Add(new ChatLine(newLine));
                            }

                            // Notify watchers
                            this.OnReaderDataChanged(new ReaderDataEventArgs(chatData));
                        }

                        // Set for the next time through the loop
                        previousDetails = currentDetails;
                        
                    }
                    catch (Exception e)
                    {
                        Logger.Instance.Log(e);
                    }
                    finally
                    {
                        // Make sure the thread doesn't hammer the system with constant
                        // monitoring, but often enough to get good event time resolution.
                        // Ram contents seem to be updated once per second, to set the
                        // monitor delay to half that for best update time.
                        Thread.Sleep(500);
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Instance.Log(e);
            }
        }

        #endregion

        #region Utility functions for reading memory data
        /// <summary>
        /// Fetch details such as how many lines are in the chat log, pointers to
        /// the memory containing the actual text, etc.
        /// </summary>
        /// <param name="chatLogLocation"></param>
        /// <returns>Returns a completed ChatLogDetails object if successful.
        /// If unsuccessful, returns null.</returns>
        private ChatLogDetails ReadChatLogDetails(IntPtr chatLogInfoAddress)
        {
            IntPtr lineOffsetsBuffer = IntPtr.Zero;

            using (ProcessMemoryReading pmr = new ProcessMemoryReading(pol.Process.Handle, chatLogInfoAddress, sizeOfChatLogInfoStruct))
            {
                ChatLogInfoStruct chatLogInfoStruct;

                if (pmr.ReadBufferPtr != null)
                    chatLogInfoStruct = (ChatLogInfoStruct)Marshal.PtrToStructure(pmr.ReadBufferPtr, typeof(ChatLogInfoStruct));
                else
                    throw new ArgumentNullException("pmr.ReadBufferPtr");

                return new ChatLogDetails(chatLogInfoStruct);
            }
        }

        /// <summary>
        /// Read memory starting at the provided buffer point and break
        /// it out into an array of strings.
        /// </summary>
        /// <param name="bufferStart">Where to start reading.</param>
        /// <param name="bufferSize">The size of the buffer being read.</param>
        /// <param name="maxLinesToRead">Maximum number of lines to read.</param>
        /// <returns>Returns an array of strings from the buffer.</returns>
        private string[] ReadChatLinesAsStrings(IntPtr bufferStartAddress, short bufferSize, int maxLinesToRead)
        {
            if (maxLinesToRead == 0)
                return new string[0];

            // Don't try to read more than 50 lines
            if (maxLinesToRead > 50)
                maxLinesToRead = 50;

            string nullDelimitedChatLines = string.Empty;

            // Read the raw databuffer from the process space
            using (ProcessMemoryReading pmr = new ProcessMemoryReading(pol.Process.Handle, bufferStartAddress, (uint)bufferSize))
            {
                if (pmr.ReadBufferPtr == IntPtr.Zero)
                    return new string[0];

                // Marshall the entire databuffer into a string with embedded nulls.
                nullDelimitedChatLines = Marshal.PtrToStringAnsi(pmr.ReadBufferPtr, (int)bufferSize);
            }

            // Split the marshalled string on the null delimiter, but allow for an extra
            // element in case of trailing data (otherwise it gets embedded in the last
            // entry).
            string[] splitStringArray = nullDelimitedChatLines.Split(
                new char[] { '\0' }, maxLinesToRead + 1, StringSplitOptions.RemoveEmptyEntries);

            int numberOfLinesToCopy = splitStringArray.Length;

            // Check the last entry for bogus data from reading past the end in the OldChatLog
            string lastLine = splitStringArray[splitStringArray.Length - 1];

            // Sample line: 01,00,00,80808080,00000019,00000017,0010,00,01,01,00,.. etc

            // Check length of the header values of the message line, not counting the line itself.
            if (lastLine.Length < 50)
            {
                // If it fails, don't copy it
                numberOfLinesToCopy--;
            }
            else
            {
                if (lastLine.Substring(2, 1) != ",")
                {
                    // If the third character isn't a comma, don't copy it
                    numberOfLinesToCopy--;
                }
                else
                {
                    // If the first two characters aren't hex digits, don't copy it
                    uint firstNumber = 0;
                    if (uint.TryParse(lastLine.Substring(0, 2), System.Globalization.NumberStyles.AllowHexSpecifier, null, out firstNumber) == false)
                        numberOfLinesToCopy--;
                }
            }

            // Make sure we're under our max lines limit
            if (numberOfLinesToCopy > maxLinesToRead)
                numberOfLinesToCopy = maxLinesToRead;

            string[] chatLineArray = new string[numberOfLinesToCopy];

            // Copy the split array into the array we're returning while removing any
            // potential extraneous data from the last array slot.
            Array.ConstrainedCopy(splitStringArray, 0, chatLineArray, 0, numberOfLinesToCopy);

            return chatLineArray;
        }

        /// <summary>
        /// Read memory starting at the provided buffer point and break
        /// it out into an array of strings.
        /// </summary>
        /// <param name="bufferStart">Where to start reading.</param>
        /// <param name="bufferSize">The size of the buffer being read.</param>
        /// <param name="maxLinesToRead">Maximum number of lines to read.</param>
        /// <returns>Returns an array of strings from the buffer.</returns>
        private byte[][] ReadChatLinesAsByteArrays(IntPtr bufferStartAddress, short bufferSize, int maxLinesToRead)
        {
            if (maxLinesToRead == 0)
                return new byte[0][];

            // Don't try to read more than 50 lines
            if (maxLinesToRead > 50)
                maxLinesToRead = 50;

            byte[] byteArrayOfAllChatLines = new byte[bufferSize];
            byte[][] arrayOfArraysOfChatLineBytes = new byte[maxLinesToRead][];

            // Read the raw databuffer from the process space
            using (ProcessMemoryReading pmr = new ProcessMemoryReading(pol.Process.Handle, bufferStartAddress, (uint)bufferSize))
            {
                if (pmr.ReadBufferPtr == IntPtr.Zero)
                    return new byte[0][];

                // Marshal the buffer data as a byte array.
                Marshal.Copy(pmr.ReadBufferPtr, byteArrayOfAllChatLines, 0, bufferSize);
            }

            // If nothing got marshalled, return empty string array.
            if (byteArrayOfAllChatLines.Length == 0)
                return new byte[0][];

            // Extract out the null-delimited string elements from the full byte array.  No encoding is done at this point.

            int fullByteArrayStartIndex = 0;
            int fullByteArray0Index = 0;
            int subarrayLength = 0;

            for (int i = 0; i < maxLinesToRead; i++)
            {
                if (fullByteArrayStartIndex < byteArrayOfAllChatLines.Length)
                {
                    fullByteArray0Index = Array.IndexOf<byte>(byteArrayOfAllChatLines, 0, fullByteArrayStartIndex);

                    if (fullByteArray0Index > fullByteArrayStartIndex)
                    {
                        subarrayLength = fullByteArray0Index - fullByteArrayStartIndex;
                        arrayOfArraysOfChatLineBytes[i] = new byte[subarrayLength];
                        Array.ConstrainedCopy(byteArrayOfAllChatLines, fullByteArrayStartIndex, arrayOfArraysOfChatLineBytes[i], 0, subarrayLength);

                        fullByteArrayStartIndex = fullByteArray0Index + 1;
                    }
                }
            }

            // Sample line: 01,00,00,80808080,00000019,00000017,0010,00,01,01,00,.. etc

            // Ok, all substrings should be in their own byte arrays in arrayOfArraysOfChatLineBytes.
            // We now need to convert them to strings with appropriate encoding.

            // --
            // All character encoding adjustments are now done at the ChatLine level
            // --
            //string[] chatLineArray = new string[maxLinesToRead];
            //int chatLineArrayIndex = 0;

            //for (int i = 0; i < maxLinesToRead; i++)
            //{
            //    if (arrayOfArraysOfChatLineBytes[i] != null)
            //    {
            //        chatLineArray[chatLineArrayIndex++] =
            //            System.Text.Encoding.GetEncoding("Shift-JIS").GetString(arrayOfArraysOfChatLineBytes[i]);
            //    }
            //}

            return arrayOfArraysOfChatLineBytes;
        }

        /// <summary>
        /// Extract the line number from the provided chat line for use
        /// in tracking line position while monitoring RAM.
        /// </summary>
        /// <param name="chatLine">The chat line string.</param>
        /// <returns>The parsed out line number.  Returns -1 if unable to get valid data parsed.</returns>
        private int GetChatLineLineNumber(string chatLine)
        {
            if (chatLine == null)
                throw new ArgumentNullException();

            if (chatLine.Length < 35)
                throw new ArgumentOutOfRangeException("chatLine", chatLine, "Invalid chat line length.");

            // Parse it.  If it fails, it will throw an exception.
            return int.Parse(chatLine.Substring(27, 8), System.Globalization.NumberStyles.AllowHexSpecifier);
        }

        private int GetChatLineLineNumber(byte[] chatLine)
        {
            if (chatLine == null)
                throw new ArgumentNullException();

            if (chatLine.Length < 50)
                throw new ArgumentOutOfRangeException("chatLine", chatLine, "Invalid chat line length.");

            string str = System.Text.Encoding.ASCII.GetString(chatLine, 27, 8);

            return int.Parse(str, System.Globalization.NumberStyles.AllowHexSpecifier);
        }

        #endregion

        #region Process Information Location Methods
        /// <summary>
        /// This function digs into the FFXI address space to locate the
        /// address of where the chat log data is stored.  This info is
        /// saved in the class variable chatLogLocation.
        /// </summary>
        private ChatLogLocationInfo LocateChatLog()
        {
            // initialMemoryOffset is the offset to the address that contains the
            // first pointer in the hierarchy of FFXI's chat log data structures.
            // It is an offset from the base address that FFXIMain.dll is loaded at.
            // This is a pointer to a data structure we want to read.
            IntPtr rootAddress = Pointers.IncrementPointer(pol.FFXIBaseAddress, initialMemoryOffset);

            return LocateChatLog(rootAddress);
        }

        private ChatLogLocationInfo LocateChatLog(IntPtr rootAddress)
        {
            //Dereference that pointer to get our next address.
            IntPtr dataStructurePointer = Pointers.FollowPointer(pol.Process.Handle, rootAddress);

            if (dataStructurePointer == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    string.Format("Error dereferencing memloc pointer 0x{0:X8} (memloc: 0x{1:X8}).",
                    rootAddress, initialMemoryOffset));
            }

            chatLogControl = ReadControlStructure(dataStructurePointer);

            if (chatLogControl.ChatLogInfoPtr == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    string.Format("Control structure chat info pointer is zero.  dataStructurePointer: 0x{0:X8}", dataStructurePointer));
            }

            return new ChatLogLocationInfo(dataStructurePointer, chatLogControl.ChatLogInfoPtr);
        }

        /// <summary>
        /// Read the ChatLogControlStruct from the memory location specified in the IntPtr parameter.
        /// </summary>
        /// <param name="dataStructurePointer">Where in POL process memory to look for the
        /// ChatLogControlStruct.</param>
        /// <returns>Returns the extracted ChatLogControlStruct.  Throws an exception on
        /// an invalid read (null address pointer provided in the parameter).</returns>
        internal ChatLogControlStruct ReadControlStructure(IntPtr dataStructurePointer)
        {
            // The general memloc points to a control structure that handles some metadata on
            // the chat log.  This includes the unique line IDs and the pointer to more chat
            // log metadata that describes where all the chat log data actually is.

            using (ProcessMemoryReading pmr = new ProcessMemoryReading(pol.Process.Handle, dataStructurePointer, sizeOfChatLogControlStruct))
            {
                if (pmr.ReadBufferPtr == IntPtr.Zero)
                    throw new InvalidOperationException("Error reading chat log control structure.");

                // Copy the structure from memory buffer to managed class.
                return (ChatLogControlStruct)Marshal.PtrToStructure(pmr.ReadBufferPtr, typeof(ChatLogControlStruct));
            }
        }
        #endregion

        #region Utility functions for examining RAM to determine new base address
        [Conditional("DEBUG")]
        internal void ScanRAM()
        {
            try
            {
                IsRunning = true;
                initialMemoryOffset = appSettings.MemoryOffset;

                // Note: Automatically disables LocateChatLog in FindFFXIProcess().

                pol = ProcessAccess.GetFFXIProcess(0, null);
                if (pol == null)
                    return;

                // Section 1

                // Locate a known string in memory space.  Adjust the string until you can locate
                // the start of the chat log messages within the byteString variable in the
                // FindString function.
                // From there, determine the start of the array of chat log messages.
                // Use that address in Section 2.
                //FindString("c8,00,00,ff5020ff");
                // EG: memory offset of 0x02a8fcc0 + index of 0x2d0 == 
                // chat log start address of 0x02A8FF90


                // Section 2

                // Locate a pointer to the start of the chat log messages. (0x02A8FF90)
                // The location of that pointer is used in Section 3.
                //FindAddress(0x054DB678);
                // Use calculated pointerLocation.
                // EG: Scan Address 0x0488f000 + Index j (989) * 4 -
                //     Base address 0x01e00000 = Pointer location 0x02a8ff74


                // Section 3

                // From that, determine the start of the ChatLogInfoStruct.
                // The start of ChatLogInfoStruct is (4 bytes + 50 shorts + 50 shorts =
                // 204 bytes (0xCC) before the located pointer.
                // Result: 0x02a8ff74 - 0xCC = 0x02A8FEA8


                // Section 4

                // Examine the ChatLogInfoStruct from the previous address
                // to make sure things match up.
                //CheckStructureAtAddress(0x613CC50);

                // Section 5

                // Since we know where the structure lives, find the address
                // that points to that.  Use the same address as that used when
                // checking the ChatLog structure.
                //FindAddress(0x613CC50);
                // Use calculated pointerLocation.
                // EG: Scan Address 0x0488f000 + Index j (929) * 4 - 
                //     Base address 0x01e00000 = Pointer location 0x02a8fe84

                // Section 6

                // That pointer is the second in a structure that is pointed
                // to by an initial address point.  Locate the address of our
                // (pointer from section 5) - 4.
                // EG: 0x02a8fe84 - 4 = 0x02a8fe80
                //FindAddress(0x0605d560);
                // Use calculated pointerLocation.
                // EG: Scan Address 0x0237d000 + Index j (474) * 4 - 
                //     Base address 0x01e00000 = Pointer location 0x0057d768


                // History Section

                // Base address before patch on 2008-03-10:  0x0056A788
                // Base address after patch  on 2008-03-10:  0x0056DA48
                // Base address after update on 2008-06-09:  0x00575968
                // Base address after update on 2008-09-08:  0x00576D58
                // Base address after update on 2008-12-08:  0x0057A2C8
                // Base address after update on 2009-04-08:  0x0057d768
                // Base address after patch  on 2009-04-22:  0x0057d7e8
                // Base address after update  on 2009-07-20: 0x0057da98
                // Base address after update  on 2009-11-09: 0x005801d8
                // Base address after update  on 2010-03-22: 0x00581518
                // Base address after update  on 2010-06-21: 0x005827d8
                // Base address after update  on 2010-09-08: 0x00582958
                // Base address after update  on 2010-12-06: 0x00583948
                // Base address after update  on 2011-02-14: 0x005839C8
                // Base address after update  on 2011-07-12: 0x005856D8
                // Base address after update  on 2011-09-19: 0x005858D8
                

                // An easier way to find the new memloc.  Start at the
                // most recently known memloc and proceed forward.
                CycleStartPoint(0x005858D8);
            }
            finally
            {
                Debug.WriteLine("Done with ScanRAM function.");
                IsRunning = false;
            }
        }

        [Conditional("DEBUG")]
        private void FindString(string stringToFind)
        {
            uint scanMemoryOffset = 0x00800000;
            uint prevScanMemoryOffset = scanMemoryOffset;
            uint blockSize = 3200;
            uint blockOffset = blockSize - 128;
            MemScanStringStruct scanStruct = new MemScanStringStruct();
            MemScanStringStruct prevScanStruct = scanStruct;

            string byteString;
            string prevByteString;

            for (int i = 0; i < 64000; i++)
            {
                // Specify the location that you're searching for the requested string.
                IntPtr scanAddress = Pointers.IncrementPointer(pol.FFXIBaseAddress, scanMemoryOffset);

                using (ProcessMemoryReading pmr = new ProcessMemoryReading(pol.Process.Handle, scanAddress, blockSize))
                {
                    if (pmr.ReadBufferPtr == IntPtr.Zero)
                        continue;

                    // Read a chunk of memory and convert it to a byte array.
                    scanStruct = (MemScanStringStruct)Marshal.PtrToStructure(pmr.ReadBufferPtr, typeof(MemScanStringStruct));
                }

                try
                {
                    // Convert the byte array to a string for examination.
                    byteString = new string(scanStruct.memScanCharacters);

                    // See if our requested string is found in this chunk of memory.
                    int j = byteString.IndexOf(stringToFind);

                    if (j >= 0)
                    {
                        // If it is, write out the offset that we're using.
                        Debug.WriteLine(string.Format("Offset = 0x{0:x8}, Index j = {1} (0x{1:x4})", scanMemoryOffset, j));

                        // Examine byteString and prevByteString to locate the start of the
                        // chat log (prevByteString in case the log overlaps two memory
                        // segments).

                        // If located, use scanStruct or prevScanStruct to find the specific
                        // index into the memoryOffset value to determine our 'real' starting  point.

                        Debug.WriteLine(string.Format("Previous Offset = 0x{0:x8}\n", prevScanMemoryOffset));

                        // EG: memory offset of 0x02a8fcc0 + index of 0x2d0 == 
                        // chat log start address of 0x02A8FF90

                    }

                    prevByteString = byteString;
                    prevScanStruct = scanStruct;
                }
                catch (Exception e)
                {
                    Logger.Instance.Log(e);
                }

                prevScanMemoryOffset = scanMemoryOffset;
                scanMemoryOffset += blockOffset;
            }
        }

        [Conditional("DEBUG")]
        private void FindAddress(uint address)
        {
            uint absoluteAddress = address;
            //absoluteAddress = address + (uint)pol.FFXIBaseAddress.ToInt32();

            uint scanMemoryOffset = 0;
            MemScanAddressStruct scanStruct = new MemScanAddressStruct();

            uint bytesToRead = (uint)(Marshal.SizeOf(typeof(MemScanAddressStruct)));

            for (int i = 0; i < 16000; i++)
            {
                IntPtr scanAddress = Pointers.IncrementPointer(pol.FFXIBaseAddress, scanMemoryOffset);
                IntPtr scanResults = IntPtr.Zero;

                using (ProcessMemoryReading pmr = new ProcessMemoryReading(pol.Process.Handle, scanAddress, bytesToRead))
                {
                    scanStruct = (MemScanAddressStruct)Marshal.PtrToStructure(pmr.ReadBufferPtr, typeof(MemScanAddressStruct));
                }

                try
                {
                    int j = Array.IndexOf(scanStruct.addressValues, absoluteAddress);
                    if (j >= 0)
                    {
                        uint pointerLocation = (uint) (scanAddress.ToInt32() + (j * 4) - pol.FFXIBaseAddress.ToInt32());

                        Debug.WriteLine(
                            string.Format(
                            "Scan Address 0x{0:x8} + Index j ({1}) * 4 - Base address 0x{2:x8} = Pointer location 0x{3:x8}\n",
                            scanAddress.ToInt32(), j, pol.FFXIBaseAddress.ToInt32(), pointerLocation));
                    }
                }
                catch (Exception e)
                {
                    Logger.Instance.Log(e);
                }

                scanMemoryOffset += bytesToRead;
            }
        }

        [Conditional("DEBUG")]
        private void CheckStructureAtAddress(uint checkRelativeAddress)
        {
            try
            {
                uint checkAddress = checkRelativeAddress + (uint)pol.FFXIBaseAddress.ToInt32();

                //Dereference that pointer to get our next address.
                IntPtr dataStructurePointer = new IntPtr(checkAddress);

                ChatLogDetails examineDetails = ReadChatLogDetails(dataStructurePointer);

                byte[][] scanChatLines = ReadChatLinesAsByteArrays(examineDetails.ChatLogInfo.PtrToCurrentChatLog,
                        examineDetails.ChatLogInfo.FinalOffset, examineDetails.ChatLogInfo.NumberOfLines);

                //string[] scanChatLinesStrings = new string[scanChatLines.Length];

            }
            catch (Exception e)
            {
                Logger.Instance.Log(e);
            }
            finally
            {
            }
        }


        [Conditional("DEBUG")]
        private void CycleStartPoint(uint startAddress)
        {
            // Start with the previous known base address.
            
            // Scan up to 8k addresses from that point.
            // The largest jump in addresses in the past has been about 2k.

            List<ChatLine> chatData = new List<ChatLine>();

            for (uint i = 0; i < 8000; i++)
            {
                try
                {
                    chatData.Clear();

                    uint checkAddress = startAddress + i * 0x10;

                    // initialMemoryOffset is the offset to the address that contains the
                    // first pointer in the hierarchy of FFXI's chat log data structures.
                    // It is an offset from the base address that FFXIMain.dll is loaded at.
                    // This is a pointer to a data structure we want to read.
                    IntPtr rootAddress = Pointers.IncrementPointer(pol.FFXIBaseAddress, checkAddress);

                    //Dereference that pointer to get our next address.
                    IntPtr firstControlStructureLocation = Pointers.FollowPointer(pol.Process.Handle, rootAddress);

                    if (firstControlStructureLocation == IntPtr.Zero)
                    {
                        continue;
                    }

                    // The dataStructurePointer points to a structure that contains
                    // the address to the chat log info in its second (uint) field.
                    chatLogControl = ReadControlStructure(firstControlStructureLocation);

                    if (chatLogControl.ChatLogInfoPtr == IntPtr.Zero)
                    {
                        continue;
                    }

                    // We then want to look at the (supposed) log support
                    // structure at that second address.

                    ChatLogDetails examineDetails = ReadChatLogDetails(chatLogControl.ChatLogInfoPtr);

                    if (examineDetails.ChatLogInfo.PtrToCurrentChatLog == IntPtr.Zero)
                        continue;

                    if ((examineDetails.ChatLogInfo.ChatLogBytes > 32000) ||
                        (examineDetails.ChatLogInfo.ChatLogBytes < 0))
                        continue;

                    byte[][] scanChatLines = ReadChatLinesAsByteArrays(examineDetails.ChatLogInfo.PtrToCurrentChatLog,
                            examineDetails.ChatLogInfo.FinalOffset, examineDetails.ChatLogInfo.NumberOfLines);

                    foreach (var newLine in scanChatLines)
                    {
                        if (newLine != null)
                            chatData.Add(new ChatLine(newLine));
                    }


                    uint j = i;
                }
                catch
                {
                }
            }
        }


        #endregion

        #region Signature scanning
        private ChatLogLocationInfo LocateChatLogViaSignature()
        {
            IntPtr sigPtr = LocateSigRoot();

            if (sigPtr == IntPtr.Zero)
                return null;

            // Sig pointer is the memory value of a structure where the second int is the location
            // usually considered the 'memloc'.

            return LocateChatLog(Pointers.IncrementPointer(sigPtr, 4));
        }

        private IntPtr LocateSigRoot()
        {
            // Expected location: 0x16733C
            //
            // Start at 0x167000 and read a block of 0x4000 bytes.

            uint sigStartSearch = 0x167000;
            IntPtr sigBlockAddress = Pointers.IncrementPointer(pol.FFXIBaseAddress, sigStartSearch);
            int[] sigBlockRead = new int[0x4000];
            byte[] byteBuffer = new byte[sigBlockRead.Length * 4];

            using (ProcessMemoryReading pmr =
                new ProcessMemoryReading(pol.Process.Handle, sigBlockAddress, (uint)byteBuffer.Length))
            {
                if (pmr.ReadBufferPtr == IntPtr.Zero)
                    throw new InvalidOperationException("Error reading sig memory.");

                Marshal.Copy(pmr.ReadBufferPtr, byteBuffer, 0, byteBuffer.Length);
            }

            Buffer.BlockCopy(byteBuffer, 0, sigBlockRead, 0, byteBuffer.Length);

            int rootAddress = 0;

            for (int i = 0; i < sigBlockRead.Length; i++)
            {
                if (MatchSignature(sigBlockRead, i, out rootAddress))
                {
                    // Signature search failed.  Log the first 0x400 values in the array.

                    //StringBuilder sb = new StringBuilder();

                    //sb.Append("\n");
                    //for (int k = 0; k < 128; k++)
                    //{
                    //    sb.AppendFormat("{0,3:X}: ", k * 8);

                    //    for (int l = 0; l < 8; l++)
                    //    {
                    //        sb.AppendFormat("{0,12:X}", sigBlockRead[k * 8 + l]);
                    //    }
                    //    sb.Append("\n");
                    //}

                    //Logger.Instance.Log("Sig Tracking", sb.ToString());

                    
                    return new IntPtr(rootAddress);
                }
            }

            
            Properties.Settings appSettings = new WaywardGamers.KParser.Properties.Settings();

            if (appSettings.DebugMode)
            {
                // Signature search failed.  Log the first 0x400 values in the array.

                StringBuilder sb = new StringBuilder();

                sb.Append("\n");
                for (int i = 0; i < 128; i++)
                {
                    sb.AppendFormat("{0,3:X}: ", i * 8);

                    for (int j = 0; j < 8; j++)
                    {
                        sb.AppendFormat("{0,12:X}", sigBlockRead[i * 8 + j]);
                    }
                    sb.Append("\n");
                }

                Logger.Instance.Log("Sig Tracking", sb.ToString());
            }

            return IntPtr.Zero;
        }

        /*
            10FF016A
            [int32]
            [int32]
            D8B0000
            [our address]
            1074C985
            16A118B
            5C712FF
            [repeat our address]
            0
         * */

        // Use -1 for an 'any' value
        // Use -2 for the address we're trying to find

        private readonly int[] signature = new int[10] {
                0x10FF016A,
                -1,
                -1,
                0xD8B0000,
                -2,
                0x1074C985,
                0x16A118B,
                0x5C712FF,
                -2,
                0
        };


        private bool MatchSignature(int[] sample, int sampleOffset, out int address)
        {
            if (sample == null)
                throw new ArgumentNullException("sample");

            address = 0;

            if ((sampleOffset + signature.Length) > sample.Length)
                return false;


            for (int i = 0; i < signature.Length; i++)
            {
                if (signature[i] == -1)
                    continue;

                if (signature[i] == -2)
                {
                    if (address == 0)
                        address = sample[i + sampleOffset];
                    else if (address != sample[i + sampleOffset])
                        return false;

                    continue;
                }

                if (signature[i] != sample[i + sampleOffset])
                    return false;
            }

            return (address > 0);
        }
        #endregion

    }
}
