using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Timers;
using WaywardGamers.KParser.Interface;

namespace WaywardGamers.KParser.Parsing
{
    internal class MsgManager
    {
        #region Private enum for debugging purposes
        private enum DebugDumpMode
        {
            Init,
            Normal,
            Complete
        }
        #endregion

        #region Singleton Construction
        /// <summary>
        /// Make the class a singleton
        /// </summary>
        private static readonly MsgManager instance = new MsgManager();

        /// <summary>
        /// Gets the singleton instance of the NewMessageManager class.
        /// </summary>
        public static MsgManager Instance { get { return instance; } }
        
        /// <summary>
        /// Private constructor ensures singleton purity.
        /// </summary>
        private MsgManager()
		{
            periodicUpdates = new System.Timers.Timer(5000.0);
            periodicUpdates.AutoReset = true;
            periodicUpdates.Elapsed += new ElapsedEventHandler(periodicUpdates_Elapsed);
            periodicUpdates.Enabled = false;

            Monitoring.Monitor.Instance.ReaderDataChanged += ChatLinesListener;
            Monitoring.Monitor.Instance.ReaderStatusChanged += MonitorStatusListener;
        }
        #endregion

        #region Member variables
        List<Message> currentMessageCollection = new List<Message>();
        List<Message> pendingDeathsCollection = new List<Message>();

        string debugOutputFileName;
        bool dumpDebugDataToFile;

        System.Timers.Timer periodicUpdates;
        object periodicUpdateLock = new object();

        List<PlayerInfo> playerInfoList = null;
        #endregion

        #region Properties
        internal uint LastMessageEventNumber { get; private set; }

        /// <summary>
        /// Number of messages waiting in the current collection. Read-only.
        /// </summary>
        internal int CollectedMessageCount
        {
            get
            {
                lock (currentMessageCollection)
                {
                    return currentMessageCollection.Count;
                }
            }
        }

        internal List<PlayerInfo> PlayerInfoList
        {
            get
            {
                List<PlayerInfo> returnList = null;

                if (playerInfoList != null)
                {
                    returnList = playerInfoList;
                    playerInfoList = null;
                }

                return returnList;
            }
            set
            {
                playerInfoList = value;
            }
        }
        #endregion

        #region Public control methods
        /// <summary>
        /// Notify the MsgManager to start listening to the
        /// supplied IReader for new chat message lines.
        /// </summary>
        /// <param name="reader">The reader to listen to.</param>
        internal void StartNewSession()
        {
            Reset();

            // When we start listening to a new reader, note whether
            // we should be dumping the data to the debug output file.
            Properties.Settings appSettings = new WaywardGamers.KParser.Properties.Settings();
            dumpDebugDataToFile = appSettings.DebugMode;
#if DEBUG
            dumpDebugDataToFile = true;
#endif

            debugOutputFileName = Path.Combine(
                appSettings.DefaultParseSaveDirectory, "debugOutput.txt");

            DumpToFile(null, DebugDumpMode.Init);

            periodicUpdates.Start();
        }

        /// <summary>
        /// Notify the MsgManager that it no longer needs to listen
        /// to the given IReader.
        /// </summary>
        /// <param name="reader">The reader to stop listening to.</param>
        internal void EndSession()
        {
            periodicUpdates.Stop();

            ProcessRemainingMessages();
        }

        /// <summary>
        /// Have the MsgManager reset its internal state.
        /// </summary>
        internal void Reset()
        {
            // Clear the message collections
            lock (currentMessageCollection)
            {
                currentMessageCollection.Clear();
            }

            lock (pendingDeathsCollection)
            {
                pendingDeathsCollection.Clear();
            }

            // Notify the EntityManager to reset as well.
            EntityManager.Instance.Reset();
        }
        #endregion

        #region Event listeners
        /// <summary>
        /// Listener function for when messages have been collected by
        /// the reader class.  Takes the collected messages, parses them,
        /// and adds them to the queue to be stored in the database.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        internal void ChatLinesListener(object sender, ReaderDataEventArgs e)
        {
            if (periodicUpdates.Enabled == false)
                return;

            MessageLine messageLine = null;

            foreach (ChatLine chat in e.ChatLines)
            {
                try
                {
                    // Create a message line to extract the embedded data
                    // from the raw text chat line.  Run this first to
                    // weed out invalid/borked data.
                    messageLine = new MessageLine(chat);

                    // Add the chat line directly to the database before starting to parse.
                    DatabaseManager.Instance.AddChatLineToRecordLog(chat);

                    // Create a message based on parsing the message line.
                    Message msg = Parser.Parse(messageLine);

                    // Have the EntityManager update its entity list from
                    // the Message.
                    EntityManager.Instance.AddEntitiesFromMessage(msg);

                    // Deal with issues of determining if a mob dying is a 
                    // real mob or a pet.  If we add it to the pending queue,
                    // continue.
                    if (AddPossiblePetDeaths(msg))
                        continue;

                    // Do processing on any messages in the PendingDeaths queue.
                    ProcessWithPendingDeaths(msg);

                    // Add to the collection
                    AddMessageToMessageCollection(msg);
                }
                catch (Exception ex)
                {
                    Logger.Instance.Log(ex, messageLine);
                }
            }
        }

        /// <summary>
        /// Listener function for when the reader status changes.  If
        /// it's noted as completed or failed, end the session.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        internal void MonitorStatusListener(object sender, ReaderStatusEventArgs e)
        {
            if ((e.Completed == true) || (e.Failed == true))
            {
                try
                {
                    EndSession();
                }
                catch (Exception ex)
                {
                    Logger.Instance.Log(ex);
                }
            }
        }

        internal void AddMessageToMessageCollection(Message msg)
        {
            // Add processed messages to the collection that periodically
            // gets sent to the database manager.
            lock (currentMessageCollection)
            {
                if (currentMessageCollection.Contains(msg) == false)
                {
                    currentMessageCollection.Add(msg);
                    LastMessageEventNumber = msg.MessageID;
                }
            }
        }

        #endregion

        #region Private methods for dealing with incoming messages

        /// <summary>
        /// Function for dealing with uncertainties about death messages
        /// that may be pets or mobs.
        /// </summary>
        /// <param name="msg"></param>
        private bool AddPossiblePetDeaths(Message msg)
        {
            // Special handling for death messages where one might be a pet
            if (msg.EventDetails != null)
            {
                if (msg.EventDetails.CombatDetails != null)
                {
                    if (msg.EventDetails.CombatDetails.FlagPetDeath == true)
                    {
                        // If the message is flagged for pending, store it and move on
                        pendingDeathsCollection.Add(msg);
                        return true;
                    }
                }
            }

            return false;
        }

        private void ProcessWithPendingDeaths(Message msg)
        {
            // If we've built up any pending messages, look for xp reward
            // messages.  That marks the death as death of mob, so mark
            // actor of pending message as pet.
            if (pendingDeathsCollection.Count > 0)
            {
                if (msg.EventDetails != null)
                {
                    if (msg.EventDetails.EventMessageType == EventMessageType.Experience)
                    {
                        var pendingDeath = pendingDeathsCollection.First();
                        pendingDeathsCollection = pendingDeathsCollection.Skip(1).ToList();

                        pendingDeath.EventDetails.CombatDetails.ActorEntityType = EntityType.Pet;

                        lock (currentMessageCollection)
                        {
                            if (currentMessageCollection.Contains(pendingDeath) == false)
                            {
                                currentMessageCollection.Add(pendingDeath);
                            }
                        }
                    }
                }

                // If pending messages are still lying around 5+ seconds after
                // originally sent, assume it was a mob killing a pet instead, and mark
                // them as such.
                if (pendingDeathsCollection.Count > 0)
                {
                    var oldPending = pendingDeathsCollection
                        .Where(m => m.Timestamp < msg.Timestamp.AddSeconds(-5))
                        .ToList();

                    foreach (var pending in oldPending)
                    {
                        foreach (var target in pending.EventDetails.CombatDetails.Targets)
                        {
                            target.EntityType = EntityType.Pet;
                        }

                        lock (currentMessageCollection)
                        {
                            if (currentMessageCollection.Contains(pending) == false)
                            {
                                currentMessageCollection.Add(pending);
                            }
                        }

                        pendingDeathsCollection.Remove(pending);
                    }
                }
            }
        }

        #endregion

        #region Methods for querying the message collection
        /// <summary>
        /// Try to locate the most recent message in the collection that
        /// has the given event number value.
        /// </summary>
        /// <param name="eventNumber">The event number to locate.</param>
        /// <returns>Returns the message if found, or null if not.</returns>
        internal Message FindMessageWithEventNumber(uint eventNumber)
        {
            Message msg = null;

            lock (currentMessageCollection)
            {
                // Reverse search the collection list
                msg = currentMessageCollection.LastOrDefault(m => m.MessageID == eventNumber);

                // If no primary messages are using the event number, check to see if the
                // event number matches any sub-messages (ie: for line-wrapped additional effects, etc).
                if (msg == null)
                    msg = currentMessageCollection.LastOrDefault(m =>
                        m.MessageLineCollection.Any(ml => ml.EventSequence == eventNumber));
            }

            return msg;
        }

        /// <summary>
        /// Find a base spell cast or ability use or item use to match
        /// the queried message.
        /// </summary>
        /// <param name="messageLine"></param>
        /// <param name="altCodes"></param>
        /// <returns></returns>
        internal Message FindMatchingSpellCastOrAbilityUse(MessageLine messageLine, List<uint> altCodes)
        {
            // Don't allow alt codes to be null.
            if (altCodes == null)
                altCodes = new List<uint>();

            uint mcode = messageLine.MessageCode;
            uint ecode1 = messageLine.ExtraCode1;
            uint ecode2 = messageLine.ExtraCode2;
            DateTime timestamp = messageLine.Timestamp;

            if (mcode == 0)
                throw new ArgumentOutOfRangeException("mcode", "No proper message code provided.");

            if (currentMessageCollection.Count == 0)
                return null;

            // Raises or interrupted/uncompleteable actions.  No join message.
            if (mcode == 0x7a)
                return null;

            Message msg = null;
            // Don't attach to message that are too far back in time
            DateTime minTimestamp = timestamp - TimeSpan.FromSeconds(10);

            lock (currentMessageCollection)
            {
                // Search the last 50 messages of the collection (restricted in case of reparsing)
                int startIndex = currentMessageCollection.Count - 50;
                if (startIndex < 0)
                    startIndex = 0;

                var blockSet = currentMessageCollection.Skip(startIndex);
                Message lastMessage = currentMessageCollection.Last();

                // Check for lastTimestamp in case we're reading from logs
                // where all messages from 50 message blocks will have the same
                // timestamp, then an unknown interval before the next block.
                DateTime lastTimestamp = lastMessage.Timestamp;

                // Query set for everything except ecodes.  Based on main code only.
                var eCodeBlock = blockSet.Where(m =>
                        // Timestamp limits
                    (m.Timestamp == lastTimestamp || m.Timestamp >= minTimestamp) &&
                        // Code limits
                     (m.PrimaryMessageCode == mcode || altCodes.Contains(m.PrimaryMessageCode)) &&
                        // Object existance
                     m.EventDetails != null &&
                     m.EventDetails.CombatDetails != null &&
                        // Must have an Actor
                     string.IsNullOrEmpty(m.EventDetails.CombatDetails.ActorName) == false &&
                        // Type of action
                     (m.EventDetails.CombatDetails.ActionType == ActionType.Spell ||
                        // Extra limits on JAs
                       (m.EventDetails.CombatDetails.ActionType == ActionType.Ability &&
                        string.IsNullOrEmpty(m.EventDetails.CombatDetails.ActionName) == false &&
                        (JobAbilities.TwoHourJAs.Contains(m.EventDetails.CombatDetails.ActionName) == false ||
                        m.EventDetails.CombatDetails.ActionName == Resources.ParsedStrings.Benediction) &&
                        JobAbilities.SelfUseJAs.Contains(m.EventDetails.CombatDetails.ActionName) == false)
                      )
                    );


                if ((eCodeBlock != null) && (eCodeBlock.Any()))
                {
                    // Check for exact (non-0) ecodes 
                    msg = eCodeBlock.LastOrDefault(m =>
                        m.ExtraCode1 != 0 &&
                        m.ExtraCode2 != 0 &&
                        m.ExtraCode1 == ecode1 &&
                        m.ExtraCode2 == ecode2);

                    // Check for any non-0 ecodes
                    if (msg == null)
                    {
                        msg = eCodeBlock.LastOrDefault(m =>
                            m.ExtraCode1 != 0 &&
                            m.ExtraCode2 != 0);
                    }

                    // Allow any
                    if (msg == null)
                    {
                        msg = eCodeBlock.LastOrDefault();
                    }
                }
            }

            return msg;
        }

        /// <summary>
        /// Find a base spell cast or ability use or item use to match
        /// the queried message.
        /// </summary>
        /// <param name="messageLine"></param>
        /// <param name="altCodes"></param>
        /// <returns></returns>
        internal Message FindMatchingSpellCastOrAbilityUseForDamage(MessageLine messageLine, List<uint> altCodes,
            string targetName)
        {
            // Don't allow alt codes to be null.
            if (altCodes == null)
                altCodes = new List<uint>();

            // Get entity type of the target by checking the Entity Manager.
            List<EntityType> targetEntityTypes = EntityManager.Instance.LookupEntity(targetName);
            // If we haven't encountered this entity before, do a basic classification.
            if (targetEntityTypes.Count == 0)
                targetEntityTypes.Add(ClassifyEntity.ClassifyByName(targetName));

            HashSet<EntityType> actorEntityTypesAllowed = GetComplimentaryEntityTypes(targetEntityTypes);

            uint mcode = messageLine.MessageCode;
            uint ecode1 = messageLine.ExtraCode1;
            uint ecode2 = messageLine.ExtraCode2;
            DateTime timestamp = messageLine.Timestamp;

            if (mcode == 0)
                throw new ArgumentOutOfRangeException("mcode", "No proper message code provided.");

            if (currentMessageCollection.Count == 0)
                return null;

            // Raises or interrupted/uncompleteable actions.  No join message.
            if (mcode == 0x7a)
                return null;

            Message msg = null;
            // Don't attach to message that are too far back in time
            DateTime minTimestamp = timestamp - TimeSpan.FromSeconds(10);

            lock (currentMessageCollection)
            {
                // Search the last 50 messages of the collection (restricted in case of reparsing)
                int startIndex = currentMessageCollection.Count - 50;
                if (startIndex < 0)
                    startIndex = 0;

                var blockSet = currentMessageCollection.Skip(startIndex);
                Message lastMessage = currentMessageCollection.Last();

                // Check for lastTimestamp in case we're reading from logs
                // where all messages from 50 message blocks will have the same
                // timestamp, then an unknown interval before the next block.
                DateTime lastTimestamp = lastMessage.Timestamp;

                // Query set for everything except ecodes.  Based on main code only.
                var eCodeBlock = blockSet.Where(m =>
                    // Timestamp limits
                    (m.Timestamp == lastTimestamp || m.Timestamp >= minTimestamp) &&
                        // Code limits
                     (m.PrimaryMessageCode == mcode || altCodes.Contains(m.PrimaryMessageCode)) &&
                        // Object existance
                     m.EventDetails != null &&
                     m.EventDetails.CombatDetails != null &&
                        // Must have an Actor
                     string.IsNullOrEmpty(m.EventDetails.CombatDetails.ActorName) == false &&
                        // Check for valid actor types
                     actorEntityTypesAllowed.Contains(m.EventDetails.CombatDetails.ActorEntityType) &&
                        // Type of action
                     (m.EventDetails.CombatDetails.ActionType == ActionType.Spell ||
                        // Extra limits on JAs
                       (m.EventDetails.CombatDetails.ActionType == ActionType.Ability &&
                        string.IsNullOrEmpty(m.EventDetails.CombatDetails.ActionName) == false &&
                        JobAbilities.TwoHourJAs.Contains(m.EventDetails.CombatDetails.ActionName) == false &&
                        JobAbilities.SelfUseJAs.Contains(m.EventDetails.CombatDetails.ActionName) == false)
                      )
                    );

                if ((eCodeBlock != null) && (eCodeBlock.Any()))
                {
                    // Check for exact (non-0) ecodes 
                    msg = eCodeBlock.LastOrDefault(m =>
                        m.ExtraCode1 != 0 &&
                        m.ExtraCode2 != 0 &&
                        m.ExtraCode1 == ecode1 &&
                        m.ExtraCode2 == ecode2);

                    // Check for any non-0 ecodes
                    if (msg == null)
                    {
                        msg = eCodeBlock.LastOrDefault(m =>
                            m.ExtraCode1 != 0 &&
                            m.ExtraCode2 != 0);
                    }

                    // Allow any
                    if (msg == null)
                    {
                        msg = eCodeBlock.LastOrDefault();
                    }
                }
            }

            return msg;
        }

        /// <summary>
        /// Find a base spell cast or ability use or item use to match
        /// the queried message.
        /// </summary>
        /// <param name="messageLine"></param>
        /// <param name="altCodes"></param>
        /// <param name="effect"></param>
        /// <returns></returns>
        internal Message FindMatchingSpellCastOrAbilityUseWithEffect(MessageLine messageLine,
            List<uint> altCodes, string effectName, string targetName)
        {
            // Don't allow alt codes to be null.
            if (altCodes == null)
                altCodes = new List<uint>();

            uint mcode = messageLine.MessageCode;
            uint ecode1 = messageLine.ExtraCode1;
            uint ecode2 = messageLine.ExtraCode2;
            DateTime timestamp = messageLine.Timestamp;

            if (mcode == 0)
                throw new ArgumentOutOfRangeException("mcode", "No proper message code provided.");

            if (effectName == null)
                throw new ArgumentNullException("effect");

            if (currentMessageCollection.Count == 0)
                return null;

            // Raises or interrupted/uncompleteable actions.  No join message.
            if (mcode == 0x7a)
                return null;

            Message msg = null;
            // Don't attach to message that are too far back in time
            DateTime minTimestamp = timestamp - TimeSpan.FromSeconds(15);

            lock (currentMessageCollection)
            {
                // Search the last 50 messages of the collection (restricted in case of reparsing)
                int startIndex = currentMessageCollection.Count - 50;
                if (startIndex < 0)
                    startIndex = 0;

                var blockSet = currentMessageCollection.Skip(startIndex);
                Message lastMessage = currentMessageCollection.Last();

                // Check for lastTimestamp in case we're reading from logs
                // where all messages from 50 message blocks will have the same
                // timestamp, then an unknown interval before the next block.
                DateTime lastTimestamp = lastMessage.Timestamp;

                // First look for any message that contains existing targets
                // with the same effect as our current message, and that also
                // includes the chat line immediately preceeding the current one,
                // and that uses either the current message code or one of the
                // available alt codes.
                var eCodeBlock = blockSet.Where(m =>
                    // message code check
                    (m.PrimaryMessageCode == mcode ||
                      altCodes.Contains(m.PrimaryMessageCode)) &&
                         // Object existance
                     m.EventDetails != null &&
                     m.EventDetails.CombatDetails != null &&
                         // Type of action
                    (m.EventDetails.CombatDetails.ActionType == ActionType.Spell ||
                     m.EventDetails.CombatDetails.ActionType == ActionType.Ability) &&
                         // Effect check
                     (IsResist(messageLine) || IsEvade(messageLine) ||
                      m.EventDetails.CombatDetails.Targets.Any(t => t.EffectName == effectName)) &&
                         // Precursor message
                         //m.EventDetails.CombatDetails.Targets.Any(t => t.
                     m.MessageLineCollection.Any(ml => ml.UniqueSequence == (messageLine.UniqueSequence-1)));


                if ((eCodeBlock == null) || (eCodeBlock.Count() == 0))
                {
                    // Query set for everything except ecodes.
                    eCodeBlock = blockSet.Where(m =>
                        // Timestamp limits
                        (m.Timestamp == lastTimestamp || m.Timestamp >= minTimestamp) &&
                            // Code limits
                         m.PrimaryMessageCode == mcode &&
                            // Object existance
                         m.EventDetails != null &&
                         m.EventDetails.CombatDetails != null &&
                            // Must have an Actor
                         string.IsNullOrEmpty(m.EventDetails.CombatDetails.ActorName) == false &&
                            // Type of action
                        (m.EventDetails.CombatDetails.ActionType == ActionType.Spell ||
                         m.EventDetails.CombatDetails.ActionType == ActionType.Ability) &&
                            // Effect check
                         m.EventDetails.CombatDetails.Targets.Any(t => t.EffectName == effectName) &&
                            // Don't allow duplicate names if players are targets, but
                            // AOE effects can hit multiple mobs with the same name
                          ((m.EventDetails.CombatDetails.Targets.Any(t => t.Name == targetName) == false) ||
                           (m.EventDetails.CombatDetails.Targets.Any(t => (EntityType)t.EntityType == EntityType.Mob) == true))
                         );
                }

                // If no main code sets found, try alt codes
                if (((eCodeBlock == null) || (eCodeBlock.Count() == 0)) && (altCodes.Count > 0))
                {
                    eCodeBlock = blockSet.Where(m =>
                        // Timestamp limits
                        (m.Timestamp == lastTimestamp || m.Timestamp >= minTimestamp) &&
                            // Code limits
                         altCodes.Contains(m.PrimaryMessageCode) &&
                            // Object existance
                         m.EventDetails != null &&
                         m.EventDetails.CombatDetails != null &&
                            // Must have an Actor
                         string.IsNullOrEmpty(m.EventDetails.CombatDetails.ActorName) == false &&
                            // Type of action
                        (m.EventDetails.CombatDetails.ActionType == ActionType.Spell ||
                         m.EventDetails.CombatDetails.ActionType == ActionType.Ability) &&
                            // Effect check
                         (IsResist(messageLine) || IsEvade(messageLine) ||
                          m.EventDetails.CombatDetails.Targets.Any(t => t.EffectName == effectName)) &&
                            // Don't allow duplicate names if players are targets, but
                            // AOE effects can hit multiple mobs with the same name
                          ((m.EventDetails.CombatDetails.Targets.Any(t => t.Name == targetName) == false) ||
                           (m.EventDetails.CombatDetails.Targets.Any(t => (EntityType)t.EntityType == EntityType.Mob) == true) )
                         );
                }

                // If nothing found with an existing effect name provided, check for 0-count entries.
                // Query set for everything except ecodes.
                if ((eCodeBlock == null) || (eCodeBlock.Count() == 0))
                {
                    eCodeBlock = blockSet.Where(m =>
                        // Timestamp limits
                        (m.Timestamp == lastTimestamp || m.Timestamp >= minTimestamp) &&
                            // Code limits
                         m.PrimaryMessageCode == mcode &&
                            // Object existance
                         m.EventDetails != null &&
                         m.EventDetails.CombatDetails != null &&
                            // Must have an Actor
                         string.IsNullOrEmpty(m.EventDetails.CombatDetails.ActorName) == false &&
                            // Effect check
                         m.EventDetails.CombatDetails.Targets.Count == 0 &&
                            // Type of action
                         (m.EventDetails.CombatDetails.ActionType == ActionType.Spell ||
                            // Extra limits on JAs
                          (m.EventDetails.CombatDetails.ActionType == ActionType.Ability &&
                           string.IsNullOrEmpty(m.EventDetails.CombatDetails.ActionName) == false &&
                           JobAbilities.TwoHourJAs.Contains(m.EventDetails.CombatDetails.ActionName) == false &&
                           JobAbilities.SelfUseJAs.Contains(m.EventDetails.CombatDetails.ActionName) == false)
                         )
                     );

                    // If no main code sets found, try alt codes
                    if (((eCodeBlock == null) || (eCodeBlock.Count() == 0)) && (altCodes.Count > 0))
                    {
                        eCodeBlock = blockSet.Where(m =>
                            // Timestamp limits
                            (m.Timestamp == lastTimestamp || m.Timestamp >= minTimestamp) &&
                                // Code limits
                             altCodes.Contains(m.PrimaryMessageCode) &&
                                // Object existance
                             m.EventDetails != null &&
                             m.EventDetails.CombatDetails != null &&
                                // Must have an Actor
                             string.IsNullOrEmpty(m.EventDetails.CombatDetails.ActorName) == false &&
                                // Effect check
                             m.EventDetails.CombatDetails.Targets.Count == 0 &&
                                    // Type of action
                             (m.EventDetails.CombatDetails.ActionType == ActionType.Spell ||
                                    // Extra limits on JAs
                              (m.EventDetails.CombatDetails.ActionType == ActionType.Ability &&
                               string.IsNullOrEmpty(m.EventDetails.CombatDetails.ActionName) == false &&
                               JobAbilities.TwoHourJAs.Contains(m.EventDetails.CombatDetails.ActionName) == false &&
                               JobAbilities.SelfUseJAs.Contains(m.EventDetails.CombatDetails.ActionName) == false)
                             )
                         );
                    }
                }

                // If nothing found so far, check for failed/no effect actions on primary message.
                // Alt codes -must- be supplied for this.
                if (((eCodeBlock == null) || (eCodeBlock.Count() == 0)) && (altCodes.Count > 0))
                {
                    eCodeBlock = blockSet.Where(m =>
                        // Timestamp limits
                        (m.Timestamp == lastTimestamp || m.Timestamp >= minTimestamp) &&
                            // Code limits
                         altCodes.Contains(m.PrimaryMessageCode) &&
                            // Object existance
                         m.EventDetails != null &&
                         m.EventDetails.CombatDetails != null &&
                            // Must have an Actor
                         string.IsNullOrEmpty(m.EventDetails.CombatDetails.ActorName) == false &&
                            // Must have single target same as actor.  Any further
                            // messages will be able to find the effect name after the first additional
                            // one is added.
                         m.EventDetails.CombatDetails.Targets.Count == 1 &&
                         m.EventDetails.CombatDetails.Targets[0].Name == m.EventDetails.CombatDetails.ActorName &&
                            // Type of action
                        (m.EventDetails.CombatDetails.ActionType == ActionType.Spell ||
                         m.EventDetails.CombatDetails.ActionType == ActionType.Ability) &&
                            // Effect check -- empty effect name because of failure.
                         m.EventDetails.CombatDetails.FailedActionType == FailedActionType.NoEffect &&
                         m.EventDetails.CombatDetails.Targets[0].EffectName == string.Empty);
                }

                if ((eCodeBlock != null) && (eCodeBlock.Any()))
                {
                    // Check for exact ecodes
                    msg = eCodeBlock.LastOrDefault(m =>
                        m.ExtraCode1 != 0 &&
                        m.ExtraCode2 != 0 &&
                        m.ExtraCode1 == ecode1 &&
                        m.ExtraCode2 == ecode2);

                    // Forbid 0 codes
                    if (msg == null)
                    {
                        msg = eCodeBlock.LastOrDefault(m =>
                            m.ExtraCode1 != 0 &&
                            m.ExtraCode2 != 0);
                    }

                    // Allow any
                    if (msg == null)
                    {
                        msg = eCodeBlock.LastOrDefault();
                    }
                }
            }

            return msg;
        }

        private bool IsResist(MessageLine messageLine)
        {
            return Regex.Match(messageLine.TextOutput, Resources.ParsedStrings.ResistRegex).Success;
        }

        private bool IsEvade(MessageLine messageLine)
        {
            return false;
        }

        /// <summary>
        /// Find a melee or ranged attack to match an additional effect message.
        /// </summary>
        /// <param name="messageLine"></param>
        /// <param name="altCodes"></param>
        /// <returns></returns>
        internal Message FindMatchingMeleeOrRanged(MessageLine messageLine, List<uint> altCodes)
        {
            // Don't allow alt codes to be null.
            if (altCodes == null)
                altCodes = new List<uint>();

            uint mcode = messageLine.MessageCode;
            uint ecode1 = messageLine.ExtraCode1;
            uint ecode2 = messageLine.ExtraCode2;
            DateTime timestamp = messageLine.Timestamp;

            if (mcode == 0)
                throw new ArgumentOutOfRangeException("mcode", "No proper message code provided.");

            if (currentMessageCollection.Count == 0)
                return null;

            Message msg = null;
            // Don't attach to message that are too far back in time
            DateTime minTimestamp = timestamp - TimeSpan.FromSeconds(10);

            lock (currentMessageCollection)
            {
                // Search the last 50 messages of the collection (restricted in case of reparsing)
                int startIndex = currentMessageCollection.Count - 50;
                if (startIndex < 0)
                    startIndex = 0;

                var blockSet = currentMessageCollection.Skip(startIndex);
                Message lastMessage = currentMessageCollection.Last();

                // Check for lastTimestamp in case we're reading from logs
                // where all messages from 50 message blocks will have the same
                // timestamp, then an unknown interval before the next block.
                DateTime lastTimestamp = lastMessage.Timestamp;

                // Query set for everything except ecodes.
                var eCodeBlock = blockSet.Where(m =>
                    // Timestamp limits
                    (m.Timestamp == lastTimestamp || m.Timestamp >= minTimestamp) &&
                        // Code limits
                    (m.PrimaryMessageCode == mcode || altCodes.Contains(m.PrimaryMessageCode)) &&
                        // Object existance
                     m.EventDetails != null &&
                     m.EventDetails.CombatDetails != null &&
                        // Must have an Actor
                     string.IsNullOrEmpty(m.EventDetails.CombatDetails.ActorName) == false &&
                        // Type of action
                    (m.EventDetails.CombatDetails.ActionType == ActionType.Melee ||
                     m.EventDetails.CombatDetails.ActionType == ActionType.Ranged) &&
                        // Can only have 1 additional effect per attack
                     m.EventDetails.CombatDetails.HasAdditionalEffect == false &&
                        // This function is only called for additional effects, so the
                        // target must have already been parsed.  Needs to be a single
                        // target, and not a miss.
                     m.EventDetails.CombatDetails.Targets.Count == 1 &&
                     m.EventDetails.CombatDetails.Targets[0].DefenseType == DefenseType.None);

                if ((eCodeBlock != null) && (eCodeBlock.Any()))
                {
                    Match targetMatch = ParseExpressions.TargetTakesDamage.Match(messageLine.TextOutput);
                    if (targetMatch.Success)
                    {
                        msg = eCodeBlock.LastOrDefault(m =>
                            m.EventDetails != null &&
                            m.EventDetails.CombatDetails != null &&
                            m.EventDetails.CombatDetails.Targets.Any(t =>
                                t.Name == targetMatch.Groups[ParseFields.Target].Value));
                    }

                    if (msg == null)
                    {
                        // Check for exact ecodes
                        msg = eCodeBlock.LastOrDefault(m =>
                            m.ExtraCode1 == ecode1 &&
                            m.ExtraCode2 == ecode2);
                    }

                    // Forbid 0 codes
                    if (msg == null)
                    {
                        msg = eCodeBlock.LastOrDefault(m =>
                            m.ExtraCode1 != 0 &&
                            m.ExtraCode2 != 0);
                    }

                    // Allow any
                    if (msg == null)
                    {
                        msg = eCodeBlock.LastOrDefault();
                    }
                }
            }

            return msg;
        }

        /// <summary>
        /// Utility function to find allowable actor entity types for a set of target types.
        /// </summary>
        /// <param name="targetEntityTypes"></param>
        /// <returns></returns>
        private HashSet<EntityType> GetComplimentaryEntityTypes(List<EntityType> targetEntityTypes)
        {
            HashSet<EntityType> complimentaryEntityTypes = new HashSet<EntityType>();

            complimentaryEntityTypes.Add(EntityType.Unknown);

            // If we have an unknown entity, pretty much anything is allowed.
            if (((targetEntityTypes.Count == 1) && (targetEntityTypes.Contains(EntityType.Unknown))) ||
                (targetEntityTypes.Count == 0))
            {
                complimentaryEntityTypes.Add(EntityType.Mob);
                complimentaryEntityTypes.Add(EntityType.Player);
                complimentaryEntityTypes.Add(EntityType.Pet);
                complimentaryEntityTypes.Add(EntityType.NPC);
                complimentaryEntityTypes.Add(EntityType.Fellow);
                complimentaryEntityTypes.Add(EntityType.CharmedMob);
                complimentaryEntityTypes.Add(EntityType.CharmedPlayer);
            }
            else
            {
                if (targetEntityTypes.Contains(EntityType.Player))
                {
                    complimentaryEntityTypes.Add(EntityType.Mob);
                    complimentaryEntityTypes.Add(EntityType.CharmedPlayer);
                }

                if (targetEntityTypes.Contains(EntityType.Pet))
                {
                    complimentaryEntityTypes.Add(EntityType.Mob);
                }

                if (targetEntityTypes.Contains(EntityType.NPC))
                {
                    complimentaryEntityTypes.Add(EntityType.Mob);
                }

                if (targetEntityTypes.Contains(EntityType.Fellow))
                {
                    complimentaryEntityTypes.Add(EntityType.Mob);
                }

                if (targetEntityTypes.Contains(EntityType.CharmedMob))
                {
                    complimentaryEntityTypes.Add(EntityType.Mob);
                }

                if (targetEntityTypes.Contains(EntityType.Mob))
                {
                    complimentaryEntityTypes.Add(EntityType.Player);
                    complimentaryEntityTypes.Add(EntityType.Pet);
                    complimentaryEntityTypes.Add(EntityType.NPC);
                    complimentaryEntityTypes.Add(EntityType.Fellow);
                    complimentaryEntityTypes.Add(EntityType.CharmedMob);
                }

                if (targetEntityTypes.Contains(EntityType.CharmedPlayer))
                {
                    complimentaryEntityTypes.Add(EntityType.Player);
                }
            }

            return complimentaryEntityTypes;
        }

        #endregion

        #region Methods to periodically push the accumulated messages to the database.

        /// <summary>
        /// Go through the messages in the messageCollection and send excess
        /// and older messages to the database for storage.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void periodicUpdates_Elapsed(object sender, ElapsedEventArgs e)
        {
            // Since it's possible that this could be called multiple
            // times by the timer thread before the processing is completed,
            // make sure we don't allow additional attempts through.
            if (!Monitor.TryEnter(periodicUpdateLock))
                return;

            //Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;

            try
            {
                List<Message> messagesToProcess = GetMessagesToProcess();

                // send those messages to the database
                if (messagesToProcess.Count > 0)
                {
                    try
                    {
                        DatabaseManager.Instance.ProcessNewMessages(messagesToProcess, false);
                    }
                    finally
                    {
                        // Save dump of that data if debug flag is set.
                        DumpToFile(messagesToProcess, DebugDumpMode.Normal);
                    }
                }

            }
            finally
            {
                Monitor.Exit(periodicUpdateLock);
            }
        }

        /// <summary>
        /// Get a filtered subset of the currentMessageList to actually
        /// send to the database.
        /// </summary>
        /// <returns>Returns the list of messages that should be sent
        /// to the database based on filter conditions.</returns>
        private List<Message> GetMessagesToProcess()
        {
            // If we're in RAM mode, take anything more than 15 seconds old.
            DateTime shortCheckTime = DateTime.Now.ToUniversalTime() - TimeSpan.FromSeconds(15);
            // If we're in LOG mode, leave the last 10 messages with the same timestamp
            // for at least 2 minutes in case of log file cross-over.
            DateTime longCheckTime = DateTime.Now.ToUniversalTime() - TimeSpan.FromMinutes(2);

            List<Message> messagesToProcess = new List<Message>();

            switch (Monitoring.Monitor.Instance.ParseMode)
            {
                case DataSource.Ram:
                    lock (currentMessageCollection)
                    {
                        messagesToProcess.AddRange(currentMessageCollection.FindAll(m => m.Timestamp.CompareTo(shortCheckTime) < 0));

                        // Remove those messages from the list.
                        currentMessageCollection.RemoveAll(m => m.Timestamp.CompareTo(shortCheckTime) < 0);
                    }
                    break;
                case DataSource.Log:
                    // If we're in LOG mode, leave the last 10 messages with the same timestamp
                    // for at least 2 minutes in case of log file cross-over.
                    lock (currentMessageCollection)
                    {
                        if (currentMessageCollection.Count > 0)
                        {
                            Message lastMessage = currentMessageCollection.Last();
                            int numberOfMessagesToLeave = 0;

                            // But only if they haven't crossed the long time threshhold (2 minutes)
                            if (lastMessage.Timestamp.CompareTo(longCheckTime) >= 0)
                                numberOfMessagesToLeave = currentMessageCollection.Count(m => m.Timestamp == lastMessage.Timestamp);

                            // cap at 10
                            if (numberOfMessagesToLeave > 20)
                                numberOfMessagesToLeave = 20;

                            int numberOfMessagesToTake = currentMessageCollection.Count() - numberOfMessagesToLeave;

                            if (numberOfMessagesToTake < 0)
                                numberOfMessagesToTake = 0;

                            if (numberOfMessagesToTake > 0)
                            {
                                messagesToProcess.AddRange(currentMessageCollection.Take(numberOfMessagesToTake));
                                currentMessageCollection.RemoveRange(0, numberOfMessagesToTake);
                            }
                        }
                    }
                    break;
                case DataSource.Database:
                    // In database reading mode, the messages are going to come very quickly
                    // Leave the last 30 messages always.  When the re-parse ends, the
                    // ProcessRemainingMessages method will clean up the leftovers.
                    lock (currentMessageCollection)
                    {
                        if (currentMessageCollection.Count > 30)
                        {
                            messagesToProcess.AddRange(currentMessageCollection.GetRange(0, currentMessageCollection.Count - 30));
                            currentMessageCollection.RemoveRange(0, currentMessageCollection.Count - 30);
                        }
                    }
                    break;
            }

            return messagesToProcess;
        }

        /// <summary>
        /// Send all remaining messages from the currentMessageList to the
        /// database.
        /// </summary>
        private void ProcessRemainingMessages()
        {
            // This locks while waiting for the timer thread function
            // (periodicUpdates_Elapsed) to complete its partial update
            // before completing any remaining messages.
            Monitor.Enter(periodicUpdateLock);

            try
            {
                lock (currentMessageCollection)
                {
                    try
                    {
                        DatabaseManager.Instance.ProcessNewMessages(currentMessageCollection, true);
                    }
                    finally
                    {
                        // Save dump of that data if debug flag is set.  Save out entities
                        // at the end as well.
                        DumpToFile(currentMessageCollection, DebugDumpMode.Complete);
                        currentMessageCollection.Clear();
                    }
                }
            }
            finally
            {
                Monitor.Exit(periodicUpdateLock);
            }
        }
        #endregion

        #region Debug output
        bool entitiesDumped = false;

        private void DumpToFile(List<Message> messagesToDump, DebugDumpMode dumpMode)
        {
            if (dumpDebugDataToFile == false)
                return;

            if (dumpMode == DebugDumpMode.Init)
            {
                using (StreamWriter sw = File.CreateText(debugOutputFileName))
                {
                    if (messagesToDump != null)
                    {
                        foreach (Message msg in messagesToDump)
                        {
                            sw.Write(msg.ToString());
                        }
                    }
                }

                entitiesDumped = false;
                return;
            }

            if (messagesToDump == null)
                throw new ArgumentNullException("messagesToDump");


            // Normal mode
            using (StreamWriter sw = File.AppendText(debugOutputFileName))
            {
                foreach (Message msg in messagesToDump)
                {
                    sw.Write(msg.ToString());
                }
            }

            // Finalization -- have the EntityManager dump its data too.
            if (dumpMode == DebugDumpMode.Complete)
            {
                if (entitiesDumped == false)
                {
                    using (StreamWriter sw = File.AppendText(debugOutputFileName))
                    {
                        EntityManager.Instance.DumpData(sw);
                    }

                    entitiesDumped = true;
                }
            }
        }
        #endregion

    }
}
