using System;
using System.Text;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using WaywardGamers.KParser.Monitoring;

namespace WaywardGamers.KParser
{
	/// <summary>
	/// Breaks down a message line into its unique components.
    /// Does not attempt to interpret those components.
	/// 
	/// Message format from the log files:
	/// ##[mC],##[ec1],##[ec2],########[tC],########[eS],########[uS],
    /// ####[length],00,01,##[mC],00,0x1E+0x01+{??}+0x1E+0x01"TextString"[cC]0x00
	/// 
	/// In order:
	/// ## - msgCode (overall message code)
	/// ## - extraCode1 (unknown)
	/// ## - extraCode2 (unknown)
	/// ######## - textColor
	/// ######## - eventSequence
	/// ######## - uniqueSequence
	/// #### - textLength
	/// 00 - delimiter
	/// 01 - delimiter
	/// ## - message category [00 = system, 01 = chat, 02 = action]
	/// 00 - delimiter [GM flag?]
	/// 0x1E+0x01 - end codes marker
    /// 0x1E+0x01 - begin text marker
    /// "text string"
	/// [closeCode] (optional) - 0x7F+0x31
	/// 0x00 - end message string
    /// 
    /// 0x1e is a flag for a color change
    /// 0x1e 0x01 means change to color #1 (default white text, or possibly default msgColor), etc
    /// 
    /// </summary>
	internal class MessageLine
	{
		#region Member Variables
        readonly ChatLine originalChatLine;
        #endregion

        #region Message parsing regexes (static data)

        // Regex describing general chat line format
        // Extra xxxSeq/unk3 header fields are unused by Parser. They appear in some
        // RAM/log captures; TestParser chatlines omit them. Keep them optional so
        // both layouts extract the same msgCode/eventSeq/msgCat/remainder.
        static Regex msgLineBreakdown = new Regex(
            @"^(?<msgCode>[0-9a-f]{2}),(?<xCode1>[0-9a-f]{2}),(?<xCode2>[0-9a-f]{2}),(?<msgColor>[0-9a-f]{8})," +
            @"(?<eventSeq>[0-9a-f]{8}),(?<uniqSeq>[0-9a-f]{8}),(?<strLen>[0-9a-f]{4}),(?<unk1>[0-9a-f]{2})," +
            @"(?<unk2>[0-9a-f]{2}),(?<msgCat>[0-9a-f]{2}),(?<xxxSeq1>[0-9a-f]{2}),((?<xxxSeq2>[0-9a-f]{8}),(?<xxxSeq3>[0-9a-f]{8}),(?<xxxSeq4>[0-9a-f]{8}),(?<xxxSeq5>[0-9a-f]{8}),(?<xxxSeq6>[0-9a-f]{8}),(?<xxxSeq7>[0-9a-f]{8}),(?<xxxSeq8>[0-9a-f]{8}),(?<xxxSeq9>[0-9a-f]{8}),(?<xxxSeq10>[0-9a-f]{8})(?<unk3>[0-9a-f]{2}),)?" +
            @"((\x1e\x01)+(\x81\x40)?(\x1e\x01)*)?" +
            //@"(?<tsPlugin>((\x1e(\x3f|\xfa|\xfc)\[)|(\x1e.)|(\[))(?<time>\d{2}:\d{2}:\d{2})\s?(?<ampm>\w{2})?\] (\x1e\x01)?)?" +
            //@"(?<tsPlugin>((\x1e.)?\[)(?<time>\d{2}:\d{2}:\d{2})\s*(?<ampm>\w{2})?\]\s*\x1e\x01)?" +
            @"(?<tsPlugin>((\x1e.)?(\[)?)(?<time>\d{2}:\d{2}(:\d{2})?)\s*(?<ampm>\w{2})?(\])?\s*\x1e\x01)?" +
            @"(?<remainder>.+)$");

        static Regex msgLineBreakdownOfFilteredChat = new Regex(
            @"^(?<msgCode>[0-9a-f]{2}),(?<xCode1>[0-9a-f]{2}),(?<xCode2>[0-9a-f]{2}),(?<msgColor>[0-9a-f]{8})," +
            @"(?<eventSeq>[0-9a-f]{8}),(?<uniqSeq>[0-9a-f]{8}),(?<strLen>[0-9a-f]{4}),(?<unk1>[0-9a-f]{2})," +
            @"(?<unk2>[0-9a-f]{2}),(?<msgCat>[0-9a-f]{2}),(?<xxxSeq1>[0-9a-f]{2}),((?<xxxSeq2>[0-9a-f]{8}),(?<xxxSeq3>[0-9a-f]{8}),(?<xxxSeq4>[0-9a-f]{8}),(?<xxxSeq5>[0-9a-f]{8}),(?<xxxSeq6>[0-9a-f]{8}),(?<xxxSeq7>[0-9a-f]{8}),(?<xxxSeq8>[0-9a-f]{8}),(?<xxxSeq9>[0-9a-f]{8}),(?<xxxSeq10>[0-9a-f]{8}),(?<unk3>[0-9a-f]{2}),)?" +
            //@"(?<tsPlugin>\[?(?<time>\d{1,2}:\d{2}:\d{2})\s*(?<ampm>\w{2})?\]\s*)?" +
            @"(?<tsPlugin>(\[)?(?<time>\d{1,2}:\d{2}(:\d{2})?)\s*(?<ampm>\w{2})?(\])?\s*)?" +
            @"(?<remainder>.+)$");

        // The initial [ of the Windower Timestamp plugin may get lost in text corruption.

        // Regexes for finding words that have special coding that needs to be stripped
        static Regex autoTrans = new Regex(@"(\xEF\x27(?<autoTransWord>[^\xEF]+)\xEF\x28)|(\x3F\x27(?<autoTransWord>[^\x3F]+)\x3F\x28)");
        static Regex itemWords = new Regex(@"(\x1E\x02(?<itemWord>[^\x1E]+)\x1E\x01)");
        static Regex keyItemWords = new Regex(@"(\x1E\x03(?<itemWord>[^\x1E]+)\x1E\x01)");

        // Regexes for extraneous code values
        static Regex eolMark = new Regex(@"\x7F\x31$");
        static Regex clipText = new Regex(@"\x1f[\x79\x7f\x3f\x8d\x2019]");
        //(nextChar == 0x79) || // Item drops
        //(nextChar == 0x7F) || // Item distribution
        //(nextChar == 0x3F) || // Time limit warning
        //(nextChar == 0x8D) || // Limbus time limit, Moogle job change
        //(nextChar == 0x2019)) // Assault time limit, byte=0x92

        #endregion

        #region Constructor
        internal MessageLine(ChatLine chatLine)
		{
            if (chatLine == null)
                throw new ArgumentNullException("chatLine");

            if (chatLine.ChatText == "")
				throw new ArgumentException("Cannot process an empty message");

            originalChatLine = chatLine;

			ExtractDataFromOriginalChatLine();
		}
		#endregion

		#region Parsing functions
		/// <summary>
		/// Function to break down the message into its component parts.
		/// </summary>
        private void ExtractDataFromOriginalChatLine()
		{
            // Make a copy of the original text to work with
            string msg = originalChatLine.FilteredChatText;

            // Filtered text has already been through the Shift_JIS encoding process,
            // and had various bogus data removed.

            Match msgLineMatch = msgLineBreakdownOfFilteredChat.Match(msg);

            if (msgLineMatch.Success == false)
            {
                throw new FormatException("Unable to parse the chat message:\n" + msg);
            }

            MessageCode = uint.Parse(msgLineMatch.Groups["msgCode"].Value, NumberStyles.HexNumber);
            ExtraCode1 = uint.Parse(msgLineMatch.Groups["xCode1"].Value, NumberStyles.HexNumber);
            ExtraCode2 = uint.Parse(msgLineMatch.Groups["xCode2"].Value, NumberStyles.HexNumber);
            TextColor = uint.Parse(msgLineMatch.Groups["msgColor"].Value, NumberStyles.HexNumber);
            EventSequence = uint.Parse(msgLineMatch.Groups["eventSeq"].Value, NumberStyles.HexNumber);
            UniqueSequence = uint.Parse(msgLineMatch.Groups["uniqSeq"].Value, NumberStyles.HexNumber);
            TextLength = uint.Parse(msgLineMatch.Groups["strLen"].Value, NumberStyles.HexNumber);
            MessageCategoryNumber = uint.Parse(msgLineMatch.Groups["msgCat"].Value, NumberStyles.HexNumber);

            MessageCategory = (MessageCategoryType)MessageCategoryNumber;

            // Extract Timestamp plugin data if present
            if (msgLineMatch.Groups["tsPlugin"].Success == true)
            {
                // If we're parsing from logs, use the Timestamp field to refine
                // the message timestamps.
                if (Monitor.Instance.ParseMode == DataSource.Log)
                {
                    DateTime baseDate = originalChatLine.Timestamp.ToLocalTime().Date;
                    TimeSpan pluginTime;

                    if (TimeSpan.TryParse(msgLineMatch.Groups["time"].Value, out pluginTime))
                    {
                        bool addPM = false;

                        if (msgLineMatch.Groups["ampm"].Success == true)
                        {
                            if (string.Compare(msgLineMatch.Groups["ampm"].Value, "PM", true) == 0)
                                addPM = true;
                        }

                        baseDate += pluginTime;

                        if (addPM == true)
                            baseDate.AddHours(12);

                        originalChatLine.Timestamp = baseDate.ToUniversalTime();
                    }
                }
            }

            // All leftover text gets put into the TextOuput property for further conversion.
            TextOutput = msgLineMatch.Groups["remainder"].Value;

            // --
            // All character code filtering is now done at the ChatLine level
            // --

            // Adjustments for words with special display markup
            //TextOutput = autoTrans.Replace(TextOutput, "[${autoTransWord}]");
            //TextOutput = itemWords.Replace(TextOutput, "${itemWord}");
            //TextOutput = keyItemWords.Replace(TextOutput, "${itemWord}");


            // Remove other peculiar character values

            // Drop the extraneous characters at the end of non-chat messages.
            //TextOutput = eolMark.Replace(TextOutput, "");

            // Drop the extraneous characters at start (or middle, for moogles) of various messages.
            //TextOutput = clipText.Replace(TextOutput, "");

            // Convert text encoding for display of JP characters
            //byte[] originalBytes = UnicodeEncoding.Default.GetBytes(TextOutput);
            //byte[] convertedBytes = Encoding.Convert(Encoding.GetEncoding("Shift-JIS"), Encoding.Unicode, originalBytes);
            //TextOutput = Encoding.Unicode.GetString(convertedBytes).Trim();
        }
        #endregion

        #region Base chatline Properties
        /// <summary>
        /// Gets the timestamp of the original chat line the message was created from.
        /// </summary>
        internal DateTime Timestamp
        {
            get
            {
                return originalChatLine.Timestamp;
            }
        }

        /// <summary>
        /// Gets the full text string of the original chat line the message was created from.
        /// </summary>
        internal string OriginalText
        {
            get
            {
                return originalChatLine.ChatText;
            }
        }

        internal int ChatRecordID
        {
            get
            {
                return originalChatLine.RecordLogID;
            }
        }
        #endregion
        
        #region Raw Source Properties
        internal string TextOutput { get; private set; }

        internal uint MessageCode { get; private set; }

        internal uint ExtraCode1 { get; private set; }

        internal uint ExtraCode2 { get; private set; }

        internal uint TextColor { get; private set; }

        internal uint EventSequence { get; private set; }

        internal uint UniqueSequence { get; private set; }

        internal uint TextLength { get; private set; }

        internal uint MessageCategoryNumber { get; private set; }

        /// <summary>
        /// Get the message category enumeration.  Message category separates messages into
        /// three general types: system, chat and action/event/other.
        /// </summary>
        internal MessageCategoryType MessageCategory { get; private set; }
        #endregion
    }
}
