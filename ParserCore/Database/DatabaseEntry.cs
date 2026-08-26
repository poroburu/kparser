using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace WaywardGamers.KParser.Database
{
    /// <summary>
    /// This class is designed to handle the entry of data into the database
    /// using passed in Messages.
    /// </summary>
    internal class DatabaseEntry
    {
        #region Member Variables
        private KPDatabaseDataSet localDB = null;

        private Dictionary<KPDatabaseDataSet.BattlesRow, DateTime> activeBattleList;
        private Dictionary<string, KPDatabaseDataSet.BattlesRow> activeMobBattleList;
        private List<KPDatabaseDataSet.BattlesRow> activeChestBattleList;

        private List<Message> pendingCruorRewards;
        private List<Message> pendingExperienceRewards;
        private List<Message> pendingLightRewards;

        private List<KPDatabaseDataSet.InteractionsRow> deferredInteractions;

        private KPDatabaseDataSet.BattlesRow lastFinishedBattle;
        private DateTime mostRecentTimestamp = MagicNumbers.MinSQLDateTime;

        private string lsCruor = Resources.ParsedStrings.Cruor;
        private string lsGil = Resources.ParsedStrings.Gil;
        private string lsPyxis = Resources.PublicResources.SturdyPyxis;
        #endregion

        #region Constructor
        internal DatabaseEntry()
        {
            // Initialize record-keeping variables
            activeBattleList = new Dictionary<KPDatabaseDataSet.BattlesRow, DateTime>();
            activeMobBattleList = new Dictionary<string, KPDatabaseDataSet.BattlesRow>();
            deferredInteractions = new List<KPDatabaseDataSet.InteractionsRow>();

            activeChestBattleList = new List<KPDatabaseDataSet.BattlesRow>();
            pendingCruorRewards = new List<Message>();
            pendingExperienceRewards = new List<Message>();
            pendingLightRewards = new List<Message>();
        }

        /// <summary>
        /// Reset this class to empty tracking values.
        /// </summary>
        internal void Reset()
        {
            UpdateActiveBattleList(true);
            
            activeBattleList.Clear();
            activeMobBattleList.Clear();
            activeChestBattleList.Clear();
            deferredInteractions.Clear();

            pendingCruorRewards.Clear();
            pendingExperienceRewards.Clear();
            pendingLightRewards.Clear();

            lastFinishedBattle = null;
            localDB = null;
            mostRecentTimestamp = MagicNumbers.MinSQLDateTime;
        }
        #endregion

        #region Callable methods to request modifications to a given database.
        /// <summary>
        /// Add a new message to the supplied database.
        /// </summary>
        /// <param name="db">The database to insert the message into.</param>
        /// <param name="message">The message to be inserted.</param>
        internal void AddMessageToDatabase(KPDatabaseDataSet db, Message message)
        {
            if (db == null)
                throw new ArgumentNullException("db");

            if (message == null)
                throw new ArgumentNullException("message");

            if (message.Timestamp > mostRecentTimestamp)
                mostRecentTimestamp = message.Timestamp;

            // Don't try to insert data from unsuccessful parses.
            // Record log already defaults to false on ParseSuccessful, so don't have to update.
            if (message.IsParseSuccessful == false)
            {
                return;
            }

            try
            {
                this.localDB = db;

                // For successful parses, update the RecordLog table entries for each messageline in the
                // message.
                foreach (var msgLine in message.MessageLineCollection)
                {
                    if (msgLine.ChatRecordID >= 0)
                    {
                        var chatLogRow = localDB.RecordLog[msgLine.ChatRecordID];
                        chatLogRow.ParseSuccessful = message.IsParseSuccessful;
                    }
                    else
                    {
                        var recordRows = localDB.RecordLog.Where(t => t.Timestamp == msgLine.Timestamp);

                        if (recordRows.Any())
                            recordRows = recordRows.Where(t => t.MessageText == msgLine.OriginalText);

                        if (recordRows.Any())
                            recordRows = recordRows.Where(t => t.ParseSuccessful == false);

                        if (recordRows.Any())
                            recordRows.First().ParseSuccessful = true;
                    }
                }



                // Call functions depending on type of message.
                switch (message.MessageCategory)
                {
                    case MessageCategoryType.Chat:
                        InsertChat(message);
                        break;
                    case MessageCategoryType.Event:
                        // If this is a loot or experience message it needs to be attached to the most recently killed battle.
                        switch (message.EventDetails.EventMessageType)
                        {
                            case EventMessageType.Experience:
                                InsertExperience(message);
                                break;
                            case EventMessageType.Loot:
                                InsertLoot(message);
                                break;
                            case EventMessageType.Steal:
                            case EventMessageType.Interaction:
                                InsertCombat(message);
                                break;
                        }
                        // Other message types (fishing, crafting, etc) are ignored.
                        break;
                    case MessageCategoryType.System:
                        // Ignore system messages
                        break;
                }
            }
            catch (Exception e)
            {
                Logger.Instance.Log(e, message);
            }
            finally
            {
                localDB = null;
            }
        }

        /// <summary>
        /// Call this when each batch of messages is processed, to allow updating
        /// of the battle list.
        /// </summary>
        internal void MessageBatchSent()
        {
            UpdateActiveBattleList(false);
        }

        /// <summary>
        /// Close remaining open battles without resetting tracking tables.
        /// Used by the opt-in snapshot path; does not persist to disk.
        /// </summary>
        internal void CloseOutBattles()
        {
            UpdateActiveBattleList(true);
        }

        /// <summary>
        /// Update the provided database with the info supplied in the
        /// player info list.
        /// </summary>
        /// <param name="db">The database to insert the info into.</param>
        /// <param name="playerInfoList">The list of player information to update.</param>
        internal void UpdatePlayerInfo(KPDatabaseDataSet db, List<PlayerInfo> playerInfoList)
        {
            if (db == null)
                throw new ArgumentNullException("db");

            if (playerInfoList == null)
                return;

            foreach (var player in playerInfoList)
            {
                var combatantLine = db.Combatants.FirstOrDefault(c => c.CombatantName == player.Name &&
                    (EntityType)c.CombatantType == player.CombatantType);

                if (combatantLine != null)
                    combatantLine.PlayerInfo = player.Info;
            }
        }

        /// <summary>
        /// Call this function to purge all (potentially sensitive) chat
        /// info from the supplied database.
        /// </summary>
        /// <param name="db">The database to purge chat info from.</param>
        internal void PurgeChatInfo(KPDatabaseDataSet db)
        {
            if (db == null)
                throw new ArgumentNullException("db");

            // First delete all non-Arena rows in the message table.
            foreach (KPDatabaseDataSet.ChatMessagesRow row in db.ChatMessages)
            {
                if (((ChatMessageType)row.ChatType != ChatMessageType.Arena) &&
                    ((ChatMessageType)row.ChatType != ChatMessageType.Echo))
                    row.Delete();
            }

            // Then all the speakers (parent row to messages).
            foreach (KPDatabaseDataSet.ChatSpeakersRow row in db.ChatSpeakers)
            {
                if (row.GetChatMessagesRows().Count() == 0)
                    row.Delete();
            }

            // Then all chat messages from the original parse log.
            string[] chatCode = new string[] { "01", "02", "03", "04", "05", "06", "07",
                "09", "0a", "0b", "0c", "0d", "0e", "0f", "8e", "90", "98"};
            string rowChatValue;

            foreach (KPDatabaseDataSet.RecordLogRow row in db.RecordLog.Rows)
            {
                rowChatValue = row.MessageText.Substring(0, 2);
                if (chatCode.Contains(rowChatValue) == true)
                    row.Delete();
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Add chat messages to database.
        /// </summary>
        /// <param name="message">Chat message to add.</param>
        private void InsertChat(Message message)
        {
            var chatSpeakerRow = localDB.ChatSpeakers.GetSpeaker(message.ChatDetails.ChatSpeakerName);

            // If total chat line is longer than the database will accept, chop it up
            // into 256-char sequences.
            string limitChatMessage = message.CompleteMessageText;
            string frontChatMessage;
            while (limitChatMessage.Length > 256)
            {
                frontChatMessage = limitChatMessage.Substring(0, 256);
                limitChatMessage = limitChatMessage.Substring(256);

                localDB.ChatMessages.AddChatMessagesRow(message.Timestamp, (byte)message.ChatDetails.ChatMessageType,
                    chatSpeakerRow, frontChatMessage);
            }

            localDB.ChatMessages.AddChatMessagesRow(message.Timestamp, (byte)message.ChatDetails.ChatMessageType,
                 chatSpeakerRow, limitChatMessage);
        }

        /// <summary>
        /// Update battles with experience info.
        /// </summary>
        /// <param name="message">The message containing experience data.</param>
        private void InsertExperience(Message message)
        {
            // Where should we be inserting experience when it's from a scroll
            // or chest?
            bool isFromChest = (message.EventDetails.ExperienceDetails.ExperiencePoints % 250) == 0;
            KPDatabaseDataSet.BattlesRow useThisBattle = null;

            // Look for entries for 'killed' (unlocked) chests in the last 15 seconds
            // that haven't been used yet.
            if (isFromChest)
            {
                var chestBattles = from b in localDB.Battles
                                   where b.DefaultBattle == false &&
                                         b.Killed == true &&
                                         b.EndTime.AddSeconds(15) > message.Timestamp &&
                                         b.ExperiencePoints == 0 &&
                                         (EntityType)b.CombatantsRowByEnemyCombatantRelation.CombatantType == EntityType.TreasureChest &&
                                         b.GetLootRows().Count() == 0
                                   orderby b.EndTime
                                   select b;

                if (chestBattles.Any())
                {
                    useThisBattle = chestBattles.First();
                }
            }

            if (useThisBattle == null)
            {
                if ((lastFinishedBattle == null) || (lastFinishedBattle.ExperiencePoints != 0))
                {
                    lastFinishedBattle = null;

                    if (activeBattleList.Count > 0)
                    {
                        lastFinishedBattle = activeBattleList.FirstOrDefault(
                            b => b.Key.DefaultBattle == false &&
                                (b.Key.IsOver == false || b.Key.ExperiencePoints == 0)).Key;

                        if (lastFinishedBattle != null)
                        {
                            // Modifications:
                            // It's possible to receive experience awards for a mob kill
                            // *before* the message that mob was killed.  Cf: Abyssea.
                            // Because of this, we don't actually want to close the battle
                            // just yet.

                            // close out this battle
                            //if (lastFinishedBattle.IsOver == false)
                            //{
                            //    lastFinishedBattle.EndTime = message.Timestamp;
                            //    lastFinishedBattle.Killed = true;
                            //}

                            //if (lastFinishedBattle.IsEnemyIDNull() == false)
                            //    activeMobBattleList.Remove(lastFinishedBattle.CombatantsRowByEnemyCombatantRelation.CombatantName);

                            //lock (activeBattleList)
                            //    activeBattleList.Remove(lastFinishedBattle);

                        }
                    }

                    // Likewise, don't create a new battle; just add
                    // the experience reward to the pending list.
                    // *** Note: This will probably cause problems.  Need to get back to it.

                    //if (lastFinishedBattle == null)
                    //{
                    //    lastFinishedBattle = localDB.Battles.AddBattlesRow(null,
                    //        message.Timestamp, message.Timestamp, true,
                    //        null, (byte)ActorPlayerType.Unknown,
                    //        0, 0, // XP Points & Chain
                    //        (byte)MobDifficulty.Unknown, false);
                    //}
                }

                useThisBattle = lastFinishedBattle;
            }

            if (useThisBattle == null)
            {
                pendingExperienceRewards.Add(message);
            }
            else
            {
                useThisBattle.ExperiencePoints = message.EventDetails.ExperienceDetails.ExperiencePoints;
                useThisBattle.ExperienceChain = message.EventDetails.ExperienceDetails.ExperienceChain;
            }
        }

        /// <summary>
        /// Subset code for inserting loot table information.
        /// </summary>
        /// <param name="message">The message containing loot data.</param>
        private void InsertLoot(Message message)
        {
            KPDatabaseDataSet.ItemsRow itemRow = null;

            // Messages for when items are found on mob.
            if (message.EventDetails.LootDetails.IsFoundMessage == true)
            {
                // Special handling for treasure chest drops in Abyssea
                if (message.EventDetails.LootDetails.ItemName == ":treasurechest")
                {
                    // item row to link to
                    KPDatabaseDataSet.ItemsRow chestRow = localDB.Items.GetItem("Treasure Chest");

                    // Message usually occurs -before- the killshot message.
                    // Message does not include the mob the chest dropped off of,
                    // so we have no immediate target to check for.

                    // We want to pick the most likely battle to be ending soon
                    // (so the oldest open battle) that hasn't had a chest drop
                    // added to its loot pool.

                    KPDatabaseDataSet.BattlesRow firstNonChestBattle = null;

                    if (activeBattleList.Count > 0)
                    {
                        var nCBs = from b in activeBattleList
                                   where b.Key.DefaultBattle == false &&
                                         b.Key.GetLootRows().Any(l => l.ItemsRow == chestRow) == false
                                   orderby b.Value
                                   select b.Key;

                        firstNonChestBattle = nCBs.FirstOrDefault();
                    }

                    // If not found in the active battles, check for battles 
                    // that ended within the last 10 seconds that don't have
                    // a chest attached.

                    if (firstNonChestBattle == null)
                    {
                        var recentBattles = localDB.Battles.Where(
                            b => b.DefaultBattle == false &&
                            b.IsOver == true &&
                            b.EndTime > message.Timestamp.AddSeconds(-10) &&
                            b.GetLootRows().Any(l => l.ItemsRow == chestRow) == false);

                        if (recentBattles.Any())
                            firstNonChestBattle = recentBattles.Last();
                    }

                    if (firstNonChestBattle == null)
                    {
                        // Or if we didn't find any battles for this chest, create a new one.
                        firstNonChestBattle = localDB.Battles.AddBattlesRow(
                            null, message.Timestamp,
                            message.Timestamp, true, null, (byte)ActorPlayerType.Unknown, 0, 0,
                            (byte)MobDifficulty.Unknown, false);
                    }

                    // Add the entry to the loot table.
                    localDB.Loot.AddLootRow(chestRow, firstNonChestBattle, null, 0, false);

                    // At the same time as the chest is dropped as loot, we want to
                    // create a new battle entry for it.

                    KPDatabaseDataSet.CombatantsRow enemy =
                        localDB.Combatants.GetCombatant(Resources.PublicResources.SturdyPyxis,
                            EntityType.TreasureChest);

                    KPDatabaseDataSet.BattlesRow battle =
                        localDB.Battles.AddBattlesRow(
                            enemy, message.Timestamp, MagicNumbers.MinSQLDateTime,
                            false, null, 0, 0, 0, (byte)MobDifficulty.Unknown, false);

                    // And add it to the current active list so we can reference it later.
                    activeChestBattleList.Add(battle);
                }
                else
                {
                    // First locate the target (mob or chest) in the combatants table
                    var targetCombatant = localDB.Combatants.GetCombatant(message.EventDetails.LootDetails.TargetName,
                        message.EventDetails.LootDetails.TargetType);

                    // Get all battles the target has been involved in
                    var targetBattles = targetCombatant.GetBattlesRowsByEnemyCombatantRelation();

                    KPDatabaseDataSet.BattlesRow lastBattle = null;

                    // If any battles, get the last one.
                    if ((targetBattles != null) && (targetBattles.Any()))
                    {
                        lastBattle = targetBattles.OrderBy(b => b.EndTime).Last();
                    }

                    // If we found one, make sure it's within 1:00 of Now.  This is
                    // for initial 'found' messages.
                    if (lastBattle != null)
                    {
                        if (lastBattle.EndTime < (message.Timestamp.Subtract(TimeSpan.FromSeconds(60))))
                        {
                            lastBattle = localDB.Battles.AddBattlesRow(targetCombatant, message.Timestamp,
                                message.Timestamp, true, null, (byte)ActorPlayerType.Unknown, 0, 0,
                                (byte)MobDifficulty.Unknown, false);
                        }
                    }
                    else
                    {
                        // If we didn't find any battles for this target, look for recently
                        // ended battles with no target (ie: battle created when player
                        // gained xp, but no enemy name given so no targetCombatant).

                        var recentNonTargetBattles = localDB.Battles.Where(
                            b => b.DefaultBattle == false &&
                                b.IsOver == true &&
                                b.IsEnemyIDNull() == true &&
                                b.EndTime > message.Timestamp.AddSeconds(-30));

                        if ((recentNonTargetBattles != null) && (recentNonTargetBattles.Any()))
                        {
                            lastBattle = recentNonTargetBattles.Last();
                            lastBattle.EnemyID = targetCombatant.CombatantID;
                        }
                        else
                        {
                            // Or if we didn't find any battles for this target, create a new one.
                            lastBattle = localDB.Battles.AddBattlesRow(targetCombatant, message.Timestamp,
                                message.Timestamp, true, null, (byte)ActorPlayerType.Unknown, 0, 0,
                                (byte)MobDifficulty.Unknown, false);
                        }
                    }

                    // Locate the item by name in the item table.
                    itemRow = localDB.Items.GetItem(message.EventDetails.LootDetails.ItemName);

                    // Add the entry to the loot table.
                    localDB.Loot.AddLootRow(itemRow, lastBattle, null, 0, false);
                }
            }
            else
            {
                // Messages for when items or gil are distributed to players.

                if (message.EventDetails.LootDetails.LootType == LootType.Aura)
                {
                    // Auras can come from chests or battles
                    // Ebon, Gold and Silver can only come from chests.

                    Regex chestOnlyColors = new Regex("ebon|gold|silver");
                    bool chestOnly = chestOnlyColors.Match(message.EventDetails.LootDetails.ItemName).Success;

                    itemRow = localDB.Items.GetItem(message.EventDetails.LootDetails.ItemName);
                    KPDatabaseDataSet.BattlesRow sourceBattle = null;

                    // Look for entries for chests that were unlocked in the last 15 seconds
                    // that haven't been used yet.
                    var pyxis = localDB.Combatants.GetCombatant(lsPyxis,
                        EntityType.TreasureChest);

                    var pyxisBattles = pyxis.GetBattlesRowsByEnemyCombatantRelation();

                    var killedPyxis = pyxisBattles.Where(b => b.Killed == true);

                    var validPyxis = killedPyxis.Where(b =>
                        b.ExperiencePoints == 0 &&
                        b.GetLootRows().Count() == 0 &&
                        b.EndTime.AddSeconds(10) > message.Timestamp);

                    if (validPyxis.Any())
                    {
                        sourceBattle = validPyxis.First();
                    }

                    // chest-only auras need to create a new chest if one not found.
                    if ((sourceBattle == null) && (chestOnly == true))
                    {
                        sourceBattle = localDB.Battles.AddBattlesRow(pyxis, message.Timestamp,
                            message.Timestamp, true, null, (byte)ActorPlayerType.Unknown, 0, 0,
                            (byte)MobDifficulty.Unknown, false);
                    }

                    // If we have a pyxis to put the aura in, add it there.
                    if (sourceBattle != null)
                    {
                        localDB.Loot.AddLootRow(itemRow, sourceBattle, null,
                            message.EventDetails.LootDetails.Amount, false);
                    }
                    else
                    {
                        // Otherwise add it to the pending queue for when we get
                        // confirmation of a mob's death.
                        pendingLightRewards.Add(message);
                    }
                }
                else if ((message.EventDetails.LootDetails.Gil == 0) &&
                (message.EventDetails.LootDetails.Amount == 0))
                {
                    // handle item drops

                    // Locate the item in the item names table.
                    itemRow = localDB.Items.GetItem(message.EventDetails.LootDetails.ItemName);

                    // Get an array of all loot entries for this item.
                    var lootEntries = itemRow.GetLootRows();
                    KPDatabaseDataSet.LootRow lootEntry = null;

                    if (lootEntries != null)
                    {
                        // If there are any loot entries of the given type, search for an unclaimed one
                        lootEntry = lootEntries.FirstOrDefault(l => l.Lost == false && l.CombatantsRow == null);
                    }

                    // If there is no (free) loot entry, create a new one.
                    if (lootEntry == null)
                        lootEntry = localDB.Loot.AddLootRow(itemRow, null, null, 0, false);

                    // Update table based on whether someone got the drop or if it was lost
                    if (message.EventDetails.LootDetails.WasLost == true)
                    {
                        // Update Lost value of the entry
                        lootEntry.Lost = true;
                    }
                    else
                    {
                        // Ok, someone got the drop.  Check for WhoObtained;
                        // if it's empty, this is a bogus message (eg: warning when
                        // player isn't qualified to receive drop).
                        if (string.IsNullOrEmpty(message.EventDetails.LootDetails.WhoObtained) == false)
                        {
                            var player = localDB.Combatants.GetCombatant(message.EventDetails.LootDetails.WhoObtained, EntityType.Player);

                            lootEntry.CombatantsRow = player;
                        }
                    }
                }
                else
                {
                    // handle gil/cruor/TE drops

                    if (message.EventDetails.LootDetails.ItemName == ":cruor")
                    {
                        // Get the "Cruor" item from the items table (created if necessary).
                        itemRow = localDB.Items.GetItem(lsCruor);

                        // Cruor rewards can occur before the end of a battle, or after
                        // a chest is opened.  We can't rely on lastFinishedBattle, so
                        // need to figure out what battle to attach this to.

                        // Chest rewards are 200/400/600/800/1000
                        bool isFromChest = (message.EventDetails.LootDetails.Amount % 200) == 0;
                        KPDatabaseDataSet.BattlesRow cruorSourceBattle = null;

                        // Look for entries for chests that were unlocked in the last 15 seconds
                        // that haven't been used yet.
                        if (isFromChest)
                        {
                            var pyxis = localDB.Combatants.FirstOrDefault(
                                c => c.CombatantName == lsPyxis &&
                                    (EntityType)c.CombatantType == EntityType.TreasureChest);

                            if (pyxis != null)
                            {
                                var pyxisBattles = pyxis.GetBattlesRowsByEnemyCombatantRelation();

                                var killedPyxis = pyxisBattles.Where(b => b.Killed == true);

                                var validPyxis = killedPyxis.Where(b =>
                                    b.ExperiencePoints == 0 &&
                                    b.GetLootRows().Count() == 0 &&
                                    b.EndTime.AddSeconds(15) > message.Timestamp);

                                if (validPyxis.Any())
                                {
                                    cruorSourceBattle = validPyxis.First();
                                }
                            }
                        }

                        // If we didn't get it from a chest, the message would
                        // have been generated before the message that the mob
                        // was killed.  Add this to the pending list and move on.
                        if (cruorSourceBattle == null)
                        {
                            pendingCruorRewards.Add(message);
                            return;
                        }

                        // If we know who obtained the gil, use them as the player.
                        if (string.IsNullOrEmpty(message.EventDetails.LootDetails.WhoObtained) == false)
                        {
                            var player = localDB.Combatants.GetCombatant(message.EventDetails.LootDetails.WhoObtained, EntityType.Player);
                            localDB.Loot.AddLootRow(itemRow, cruorSourceBattle, player, message.EventDetails.LootDetails.Amount, false);
                        }
                        else
                        {
                            localDB.Loot.AddLootRow(itemRow, cruorSourceBattle, null, message.EventDetails.LootDetails.Amount, false);
                        }
                    }
                    else if (message.EventDetails.LootDetails.ItemName == ":gil")
                    {

                        // If no record of the last kill for this mob type, we cannot create a new one
                        // because we have no mob name.

                        // Get the "Gil" item from the items table (created if necessary).
                        itemRow = localDB.Items.GetItem(lsGil);

                        if (lastFinishedBattle != null)
                        {

                            // If we know who obtained the gil, use them as the player.
                            if (string.IsNullOrEmpty(message.EventDetails.LootDetails.WhoObtained) == false)
                            {
                                var player = localDB.Combatants.GetCombatant(message.EventDetails.LootDetails.WhoObtained, EntityType.Player);
                                localDB.Loot.AddLootRow(itemRow, lastFinishedBattle, player, message.EventDetails.LootDetails.Amount, false);
                            }
                            else
                            {
                                localDB.Loot.AddLootRow(itemRow, lastFinishedBattle, null, message.EventDetails.LootDetails.Amount, false);
                            }
                        }
                    }
                    else if (message.EventDetails.LootDetails.LootType == LootType.Time)
                    {
                        // Get the "Time Extension" item from the items table (created if necessary).
                        itemRow = localDB.Items.GetItem(message.EventDetails.LootDetails.ItemName);

                        // Time extensions can *only* come from chests.

                        KPDatabaseDataSet.BattlesRow sourceBattle = null;

                        // Look for entries for chests that were unlocked in the last 15 seconds
                        // that haven't been used yet.
                        var pyxis = localDB.Combatants.GetCombatant(lsPyxis,
                            EntityType.TreasureChest);

                        if (pyxis != null)
                        {
                            var pyxisBattles = pyxis.GetBattlesRowsByEnemyCombatantRelation();

                            var killedPyxis = pyxisBattles.Where(b => b.Killed == true);

                            var validPyxis = killedPyxis.Where(b =>
                                b.ExperiencePoints == 0 &&
                                b.GetLootRows().Count() == 0 &&
                                b.EndTime.AddSeconds(15) > message.Timestamp);

                            if (validPyxis.Any())
                            {
                                sourceBattle = validPyxis.First();
                            }
                        }

                        // If we can't find an existing chest, create a new entry.
                        if (sourceBattle == null)
                        {
                            sourceBattle = localDB.Battles.AddBattlesRow(pyxis, message.Timestamp,
                                message.Timestamp, true, null, (byte)ActorPlayerType.Unknown, 0, 0,
                                (byte)MobDifficulty.Unknown, false);
                        }

                        localDB.Loot.AddLootRow(itemRow, sourceBattle, null,
                            message.EventDetails.LootDetails.Amount, false);

                    }
                }
            }
        }

        /// <summary>
        /// Subset code for inserting combat detail information.
        /// </summary>
        /// <param name="message">The message containing combat data.</param>
        private void InsertCombat(Message message)
        {
            // Handle death events separately
            if (message.EventDetails.CombatDetails.InteractionType == InteractionType.Death)
            {
                ProcessDeath(message);
                return;
            }

            KPDatabaseDataSet.CombatantsRow actor = null;
            KPDatabaseDataSet.BattlesRow battle = null;
            KPDatabaseDataSet.ActionsRow action = null;
            KPDatabaseDataSet.ActionsRow secondAction = null;
            KPDatabaseDataSet.ItemsRow item = null;

            // Get the actor combatant, if any.
            if (string.IsNullOrEmpty(message.EventDetails.CombatDetails.ActorName) == false)
                actor = localDB.Combatants.GetCombatant(message.EventDetails.CombatDetails.ActorName,
                    message.EventDetails.CombatDetails.ActorEntityType);

            // Get the action row, if any is applicable to the message.
            if (string.IsNullOrEmpty(message.EventDetails.CombatDetails.ActionName) == false)
                action = localDB.Actions.GetAction(message.EventDetails.CombatDetails.ActionName);

            // If an item is used, get it.
            if (string.IsNullOrEmpty(message.EventDetails.CombatDetails.ItemName) == false)
                item = localDB.Items.GetItem(message.EventDetails.CombatDetails.ItemName);

            // Bogus target for passing in data on incomplete messages.
            TargetDetails bogusTarget = null;
            bogusTarget = message.EventDetails.CombatDetails
                                 .Targets.FirstOrDefault(t => string.IsNullOrEmpty(t.Name));

            // Get the battle (if any) this interaction is associated with.
            if ((message.EventDetails.CombatDetails.Targets.Count == 0) || (bogusTarget != null))
            {
                // No targets, so preparing a move

                if ((message.EventDetails.CombatDetails.ActorEntityType == EntityType.Mob) ||
                    (message.EventDetails.CombatDetails.ActorEntityType == EntityType.CharmedPlayer))
                {
                    // If a mob is taking action, look it up in the battle list, or
                    // create a new battle for it.

                    if (activeMobBattleList.TryGetValue(message.EventDetails.CombatDetails.ActorName, out battle) == false)
                    {
                        // First check the last finished battle.  It's possible a message at the end of the fight
                        // came out-of-order.  If the last finished battle ended within 1 second of the current
                        // message (and it's for the same mob), add it to that battle.
                        if (lastFinishedBattle != null)
                        {
                            if ((lastFinishedBattle.CombatantsRowByEnemyCombatantRelation.CombatantName ==
                                message.EventDetails.CombatDetails.ActorName) &&
                                (lastFinishedBattle.EndTime.AddSeconds(2) > message.Timestamp))
                            {
                                battle = lastFinishedBattle;
                            }
                        }

                        if (battle == null)
                        {
                            battle = localDB.Battles.AddBattlesRow(actor, message.Timestamp,
                                MagicNumbers.MinSQLDateTime, false, null, 0, 0, 0, (byte)MobDifficulty.Unknown, false);

                            activeMobBattleList[message.EventDetails.CombatDetails.ActorName] = battle;
                        }
                    }
                }
                else
                {
                    // If a player is taking action, put this in the most
                    // recent active battle.
                    battle = MostRecentActiveBattle();
                }
            }
            else
            {
                // Ok, in this case there are targets
                // If we know the actor, search for battles based on that.
                if ((actor != null) &&
                    ((message.EventDetails.CombatDetails.ActorEntityType == EntityType.Mob) ||
                     (message.EventDetails.CombatDetails.ActorEntityType == EntityType.CharmedPlayer)))
                {
                    // If there is none, create a new battle.
                    // If it's one mob buffing another, assume this is a link and must be
                    // considered the start of a new battle.
                    if (activeMobBattleList.TryGetValue(message.EventDetails.CombatDetails.ActorName, out battle) == false)
                    {
                        // First check the last finished battle.  It's possible a message at the end of the fight
                        // came out-of-order.  If the last finished battle ended within 1 second of the current
                        // message (and it's for the same mob), add it to that battle.
                        if (lastFinishedBattle != null)
                        {
                            if ((lastFinishedBattle.CombatantsRowByEnemyCombatantRelation.CombatantName ==
                                message.EventDetails.CombatDetails.ActorName) &&
                                (lastFinishedBattle.EndTime.AddSeconds(2) > message.Timestamp))
                            {
                                battle = lastFinishedBattle;
                            }
                        }

                        if (battle == null)
                        {
                            battle = localDB.Battles.AddBattlesRow(actor, message.Timestamp,
                                MagicNumbers.MinSQLDateTime, false, null, 0, 0, 0, (byte)MobDifficulty.Unknown, false);

                            activeMobBattleList[message.EventDetails.CombatDetails.ActorName] = battle;
                        }
                    }
                }
                else if (actor == null)
                {
                    // No actor specified (eg: shadows absorb attack)
                    // Place in most recent battle.
                    battle = MostRecentActiveBattle();
                }
                else if ((message.EventDetails.CombatDetails.ActorEntityType == EntityType.Player) &&
                    (message.EventDetails.CombatDetails.Targets.Any(t => t.EntityType == EntityType.Player)))
                {
                    // Buffs/Cures/etc where actor and target are both players should be
                    // put in the most recent active battle.
                    battle = MostRecentActiveBattle();
                }
            }

            if ((battle != null) && (battle != lastFinishedBattle))
            {
                // Update the most recent activity record for this fight.
                lock (activeBattleList)
                    activeBattleList[battle] = message.Timestamp;
            }

            if ((battle != null) && (battle != localDB.Battles.GetDefaultBattle()))
            {
                while (deferredInteractions.Count > 0)
                {
                    var deferredInteraction = deferredInteractions.First();
                    if (deferredInteraction != null)
                    {
                        deferredInteraction.BattlesRow = battle;
                        deferredInteractions = deferredInteractions.Skip(1).ToList();
                    }
                }
            }

            if (message.EventDetails.CombatDetails.Targets.Count == 0)
            {
                localDB.Interactions.AddInteractionsRow(
                    message.Timestamp,
                    actor,
                    null, // no target
                    battle,
                    (byte)message.EventDetails.CombatDetails.ActorPlayerType,
                    message.EventDetails.CombatDetails.IsPreparing,
                    action,
                    (byte)message.EventDetails.CombatDetails.ActionType,
                    (byte)message.EventDetails.CombatDetails.FailedActionType,
                    (byte)DefenseType.None,
                    0,
                    (byte)message.EventDetails.CombatDetails.AidType,
                    (byte)RecoveryType.None,
                    (byte)message.EventDetails.CombatDetails.HarmType,
                    0,
                    (byte)DamageModifier.None,
                    (byte)AidType.None,
                    (byte)RecoveryType.None,
                    (byte)HarmType.None,
                    0,
                    secondAction,
                    item);

            }
            else if (bogusTarget != null)
            {
                var targetRow = battle.CombatantsRowByEnemyCombatantRelation;

                var deferredBattleInteraction =
                localDB.Interactions.AddInteractionsRow(
                    message.Timestamp,
                    actor,
                    targetRow,
                    battle,
                    (byte)message.EventDetails.CombatDetails.ActorPlayerType,
                    message.EventDetails.CombatDetails.IsPreparing,
                    action,
                    (byte)message.EventDetails.CombatDetails.ActionType,
                    (byte)message.EventDetails.CombatDetails.FailedActionType,
                    (byte)bogusTarget.DefenseType,
                    0,
                    (byte)message.EventDetails.CombatDetails.AidType,
                    (byte)RecoveryType.None,
                    (byte)message.EventDetails.CombatDetails.HarmType,
                    0,
                    (byte)DamageModifier.None,
                    (byte)AidType.None,
                    (byte)RecoveryType.None,
                    (byte)HarmType.None,
                    0,
                    secondAction,
                    item);

                if (battle == localDB.Battles.GetDefaultBattle())
                    deferredInteractions.Add(deferredBattleInteraction);
            }
            else
            {

                foreach (var target in message.EventDetails.CombatDetails.Targets)
                {
                    // Get database row for target combatant.
                    KPDatabaseDataSet.CombatantsRow targetRow = null;
                    if (target.Name != null)
                        targetRow = localDB.Combatants.GetCombatant(target.Name, target.EntityType);

                    if (string.IsNullOrEmpty(target.SecondaryAction) == false)
                        secondAction = localDB.Actions.GetAction(target.SecondaryAction);

                    // Get the battle each time through the loop if the targets are mobs.
                    if ((target.EntityType == EntityType.Mob) ||
                        (target.EntityType == EntityType.CharmedPlayer))
                    {
                        if (activeMobBattleList.TryGetValue(target.Name, out battle) == false)
                        {
                            // No active battle for this mob.  Check if this is spillover
                            // messaging for a mob that just died (less than 4 seconds ago).

                            var recentKills = localDB.Battles.Where(b =>
                                b.Killed == true &&
                                b.EndTime >= (message.Timestamp.AddSeconds(-4)));

                            if (recentKills.Any())
                            {
                                battle = recentKills.LastOrDefault(b =>
                                    b.CombatantsRowByEnemyCombatantRelation.CombatantName == target.Name);
                            }

                            // If none found, create a new battle.
                            if (battle == null)
                            {
                                battle = localDB.Battles.AddBattlesRow(targetRow, message.Timestamp,
                                    MagicNumbers.MinSQLDateTime, false, null, 0, 0, 0, (byte)MobDifficulty.Unknown, false);

                                activeMobBattleList[target.Name] = battle;
                            }
                        }

                        // Update the most recent activity record for the fight.
                        lock (activeBattleList)
                            activeBattleList[battle] = message.Timestamp;
                    }

                    // Special double-entry for counters
                    if (message.EventDetails.CombatDetails.ActionType == ActionType.Counterattack)
                    {
                        // Initial attack is stopped by counter.
                        localDB.Interactions.AddInteractionsRow(
                           message.Timestamp,
                           targetRow, // swap with actor
                           actor, // swap with target
                           battle,
                           (byte)message.EventDetails.CombatDetails.ActorPlayerType,
                           message.EventDetails.CombatDetails.IsPreparing,
                           action,
                           (byte)ActionType.Melee, // original attack -must- be melee to be countered
                           (byte)target.FailedActionType,
                           (byte)DefenseType.Counter, // defense type, of course
                           0, // no shadows used here
                           (byte)target.AidType,
                           (byte)target.RecoveryType,
                           (byte)target.HarmType,
                           0, // no damage done
                           (byte)target.DamageModifier,
                           (byte)target.SecondaryAidType,
                           (byte)target.SecondaryRecoveryType,
                           (byte)target.SecondaryHarmType,
                           target.SecondaryAmount,
                           secondAction,
                           item);

                        // The attack that the combatant that countered got
                        localDB.Interactions.AddInteractionsRow(
                            message.Timestamp,
                            actor,
                            targetRow,
                            battle,
                            (byte)message.EventDetails.CombatDetails.ActorPlayerType,
                            message.EventDetails.CombatDetails.IsPreparing,
                            action,
                            (byte)message.EventDetails.CombatDetails.ActionType,
                            (byte)target.FailedActionType,
                            (byte)target.DefenseType,
                            target.ShadowsUsed,
                            (byte)target.AidType,
                            (byte)target.RecoveryType,
                            (byte)target.HarmType,
                            target.Amount,
                            (byte)target.DamageModifier,
                            (byte)target.SecondaryAidType,
                            (byte)target.SecondaryRecoveryType,
                            (byte)target.SecondaryHarmType,
                            target.SecondaryAmount,
                            secondAction,
                            item);
                    }
                    else
                    {
                        localDB.Interactions.AddInteractionsRow(
                            message.Timestamp,
                            actor,
                            targetRow,
                            battle,
                            (byte)message.EventDetails.CombatDetails.ActorPlayerType,
                            message.EventDetails.CombatDetails.IsPreparing,
                            action,
                            (byte)message.EventDetails.CombatDetails.ActionType,
                            (byte)target.FailedActionType,
                            (byte)target.DefenseType,
                            target.ShadowsUsed,
                            (byte)target.AidType,
                            (byte)target.RecoveryType,
                            (byte)target.HarmType,
                            target.Amount,
                            (byte)target.DamageModifier,
                            (byte)target.SecondaryAidType,
                            (byte)target.SecondaryRecoveryType,
                            (byte)target.SecondaryHarmType,
                            target.SecondaryAmount,
                            secondAction,
                            item);
                    }
                }
            }
        }

        /// <summary>
        /// Subset code for handling mob deaths.
        /// </summary>
        /// <param name="message"></param>
        private void ProcessDeath(Message message)
        {
            KPDatabaseDataSet.CombatantsRow actor = null;
            KPDatabaseDataSet.BattlesRow battle = null;

            // If there are no targets, we can't do anything.
            if ((message.EventDetails.CombatDetails.Targets == null) || (message.EventDetails.CombatDetails.Targets.Count == 0))
                return;

            // If we have an actor name, get the actor.  Otherwise the target just "fell to the ground".
            if (string.IsNullOrEmpty(message.EventDetails.CombatDetails.ActorName) == false)
            {
                // Determine the acting combatant for this event
                actor = localDB.Combatants.GetCombatant(message.EventDetails.CombatDetails.ActorName,
                    message.EventDetails.CombatDetails.ActorEntityType);
            }


            // Record an interaction entry for each death
            foreach (var target in message.EventDetails.CombatDetails.Targets)
            {
                var targetRow = localDB.Combatants.GetCombatant(target.Name, target.EntityType);
                KPDatabaseDataSet.ItemsRow item = null;

                if ((target.EntityType == EntityType.Mob) ||
                    (target.EntityType == EntityType.CharmedPlayer))
                {
                    // If target is mob, pick that battle

                    // If actorPlayerType is Other, make sure there is an Other action against it
                    if (message.EventDetails.CombatDetails.ActorPlayerType == ActorPlayerType.Other)
                    {
                        // If we find such a battle, use it.
                        foreach (var mobBattle in activeMobBattleList.Where(b => b.Key == target.Name))
                        {
                            var sameActorRows = mobBattle.Value.GetInteractionsRows()
                                .Where(i => i.IsActorIDNull() == false &&
                                       i.CombatantsRowByActorCombatantRelation == actor &&
                                       (FailedActionType)i.FailedActionType == FailedActionType.None);

                            //if (mobBattle.Value.GetInteractionsRows().Any(i => (ActorPlayerType)i.ActorType == ActorPlayerType.Other))
                            if (sameActorRows.Any())
                            {
                                battle = mobBattle.Value;

                                battle.EndTime = message.Timestamp;
                                battle.Killed = true;
                                battle.CombatantsRowByBattleKillerRelation = actor;
                                battle.KillerPlayerType = (byte)message.EventDetails.CombatDetails.ActorPlayerType;

                                break;
                            }
                        }

                        // Otherwise this is likely a death notice of a fight by someone outside the party.
                        // Create a new entry for it.
                        if (battle == null)
                        {
                            // Make a new one if one doesn't exist, created already killed
                            battle = localDB.Battles.AddBattlesRow(targetRow, message.Timestamp, message.Timestamp,
                                true, actor, (byte)message.EventDetails.CombatDetails.ActorPlayerType, 0, 0,
                                (byte)MobDifficulty.Unknown, false);
                        }
                        else
                        {
                            // Don't try to modify the enumeration during the foreach loop.
                            activeMobBattleList.Remove(target.Name);

                            lock (activeBattleList)
                                activeBattleList.Remove(battle);
                        }
                    }

                    if (battle == null)
                    {
                        if (activeMobBattleList.TryGetValue(target.Name, out battle) == true)
                        {
                            // close out this battle
                            battle.EndTime = message.Timestamp;
                            battle.Killed = true;
                            battle.CombatantsRowByBattleKillerRelation = actor;
                            battle.KillerPlayerType = (byte)message.EventDetails.CombatDetails.ActorPlayerType;

                            activeMobBattleList.Remove(target.Name);
                            lock (activeBattleList)
                                activeBattleList.Remove(battle);
                        }
                        else
                        {
                            // Make a new one if one doesn't exist, created already killed
                            battle = localDB.Battles.AddBattlesRow(targetRow, message.Timestamp, message.Timestamp,
                                true, actor, (byte)message.EventDetails.CombatDetails.ActorPlayerType, 0, 0,
                                (byte)MobDifficulty.Unknown, false);
                        }
                    }

                    lastFinishedBattle = battle;
                }
                else if (target.EntityType == EntityType.TreasureChest)
                {
                    // If target is a chest that was unlocked/killed, search our active
                    // chests for one we can use.

                    if (activeChestBattleList.Count > 0)
                    {
                        battle = activeChestBattleList.First();
                        lock (activeChestBattleList)
                            activeChestBattleList.Remove(battle);

                        // Mark the battle as completed
                        battle.EndTime = message.Timestamp;
                        
                        // Failure means expiring the chest, but not killed.
                        if (message.EventDetails.CombatDetails.FailedActionType == FailedActionType.None)
                            battle.Killed = true;

                        battle.CombatantsRowByBattleKillerRelation = actor;
                        battle.KillerPlayerType = (byte)message.EventDetails.CombatDetails.ActorPlayerType;
                    }

                    // If we didn't find one, create a new one
                    if (battle == null)
                    {
                        battle = localDB.Battles.AddBattlesRow(targetRow, message.Timestamp, message.Timestamp,
                            true, actor, (byte)message.EventDetails.CombatDetails.ActorPlayerType, 0, 0,
                            (byte)MobDifficulty.Unknown, false);
                    }

                    // Get the item row for the forbidden key, if used.
                    if (string.IsNullOrEmpty(
                        message.EventDetails.CombatDetails.ItemName) == false)
                    {
                        item = localDB.Items.GetItem(message.EventDetails.CombatDetails.ItemName);
                    }

                }
                else
                {
                    // If target is player, find the battle to put this in
                    if (actor == null)
                    {
                        // If no actor, get the most recent battle
                        battle = MostRecentActiveBattle();
                    }
                    else
                    {
                        // Otherwise get the actor's battle
                        if (activeMobBattleList.TryGetValue(actor.CombatantName, out battle) == false)
                        {
                            // First check the last finished battle.  It's possible a message at the end of the fight
                            // came out-of-order.  If the last finished battle ended within 1 second of the current
                            // message (and it's for the same mob), add it to that battle.
                            if (lastFinishedBattle != null)
                            {
                                if ((lastFinishedBattle.CombatantsRowByEnemyCombatantRelation.CombatantName ==
                                    message.EventDetails.CombatDetails.ActorName) &&
                                    (lastFinishedBattle.EndTime.AddSeconds(2) > message.Timestamp))
                                {
                                    battle = lastFinishedBattle;
                                }
                            }


                            // If they're not in the active battle list, create a new one.
                            if (battle == null)
                            {
                                battle = localDB.Battles.AddBattlesRow(actor, message.Timestamp,
                                    MagicNumbers.MinSQLDateTime, false, null, 0, 0, 0,
                                    (byte)MobDifficulty.Unknown, false);

                                activeMobBattleList.Add(actor.CombatantName, battle);
                                activeBattleList.Add(battle, message.Timestamp);
                            }
                        }
                    }
                }

                // Make a note of the death event in the interactions table
                localDB.Interactions.AddInteractionsRow(
                       message.Timestamp,
                       actor,
                       targetRow,
                       battle,
                       (byte)message.EventDetails.CombatDetails.ActorPlayerType,
                       message.EventDetails.CombatDetails.IsPreparing,
                       null,
                       (byte)message.EventDetails.CombatDetails.ActionType,
                       (byte)message.EventDetails.CombatDetails.FailedActionType,
                       (byte)target.DefenseType,
                       target.ShadowsUsed,
                       (byte)message.EventDetails.CombatDetails.AidType,
                       (byte)target.RecoveryType,
                       (byte)message.EventDetails.CombatDetails.HarmType,
                       target.Amount,
                       (byte)target.DamageModifier,
                       (byte)target.SecondaryAidType,
                       (byte)target.SecondaryRecoveryType,
                       (byte)target.SecondaryHarmType,
                       target.SecondaryAmount,
                       null,
                       item);

                // Add pending cruor or experience, if applicable.
                var pendingCruor = pendingCruorRewards.FirstOrDefault();
                if ((pendingCruor != null) && (target.EntityType != EntityType.TreasureChest))
                {
                    pendingCruorRewards = pendingCruorRewards.Skip(1).ToList();

                    KPDatabaseDataSet.ItemsRow itemRow = localDB.Items.GetItem(lsCruor);

                    if (string.IsNullOrEmpty(pendingCruor.EventDetails.LootDetails.WhoObtained) == false)
                    {
                        var player = localDB.Combatants.GetCombatant(pendingCruor.EventDetails.LootDetails.WhoObtained, EntityType.Player);
                        localDB.Loot.AddLootRow(itemRow, battle, player, pendingCruor.EventDetails.LootDetails.Amount, false);
                    }
                    else
                    {
                        localDB.Loot.AddLootRow(itemRow, battle, null, pendingCruor.EventDetails.LootDetails.Amount, false);
                    }
                }

                var pendingExp = pendingExperienceRewards.FirstOrDefault();
                if ((pendingExp != null) && (target.EntityType != EntityType.TreasureChest))
                {
                    pendingExperienceRewards = pendingExperienceRewards.Skip(1).ToList();

                    battle.ExperiencePoints = pendingExp.EventDetails.ExperienceDetails.ExperiencePoints;

                }

                var pendingLight = pendingLightRewards.FirstOrDefault();
                if ((pendingLight != null) && (target.EntityType != EntityType.TreasureChest))
                {
                    pendingLightRewards = pendingLightRewards.Skip(1).ToList();

                    KPDatabaseDataSet.ItemsRow itemRow = localDB.Items.GetItem(pendingLight.EventDetails.LootDetails.ItemName);

                    localDB.Loot.AddLootRow(itemRow, battle, null, 0, false);
                }

            }
        }
        #endregion

        #region Utility functions
        /// <summary>
        /// Close out inactive battles.  If parse is ending, close out all remaining battles.
        /// </summary>
        /// <param name="closeOutAllBattles">Flag to indicate whether the
        /// parse is ending and all battles should be closed.</param>
        private void UpdateActiveBattleList(bool closeOutAllBattles)
        {
            try
            {
                List<KPDatabaseDataSet.BattlesRow> battlesToRemove = new List<KPDatabaseDataSet.BattlesRow>();
                DateTime tenMinutesAgo = mostRecentTimestamp.Subtract(TimeSpan.FromMinutes(10));

                lock (activeBattleList)
                {
                    if (activeBattleList.Count > 0)
                    {
                        foreach (var activeBattle in activeBattleList)
                        {
                            // Never close out the default battle, if it's in the list.
                            if (activeBattle.Key.DefaultBattle == false)
                            {
                                // Anything that hasn't had any activity in more than
                                // 10 minutes is marked for removal.
                                if ((closeOutAllBattles == true) || (activeBattle.Value < tenMinutesAgo))
                                {
                                    battlesToRemove.Add(activeBattle.Key);
                                }
                            }
                        }

                        foreach (var battleToRemove in battlesToRemove)
                        {
                            // If the battle hasn't been marked as ended, set the
                            // ending timestamp to match the most recent message's.
                            // If reparsing, this will be the last message in the
                            // log.
                            if (battleToRemove.EndTime == MagicNumbers.MinSQLDateTime)
                                battleToRemove.EndTime = mostRecentTimestamp;
                            activeBattleList.Remove(battleToRemove);
                        }
                    }
                }

                lock (activeChestBattleList)
                {
                    if (activeChestBattleList.Count > 0)
                    {
                        battlesToRemove.Clear();

                        foreach (var chest in activeChestBattleList)
                        {
                            if ((closeOutAllBattles == true) ||
                                (chest.StartTime.AddSeconds(180) < mostRecentTimestamp))
                            {
                                chest.EndTime = chest.StartTime.AddSeconds(180);
                                battlesToRemove.Add(chest);
                            }
                        }

                        activeChestBattleList = activeChestBattleList.Except(battlesToRemove).ToList();
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Instance.Log(e);
            }
        }

        /// <summary>
        /// Gets the most recent still-active battle.  If there are none, returns
        /// the default battle.
        /// </summary>
        /// <returns></returns>
        private KPDatabaseDataSet.BattlesRow MostRecentActiveBattle()
        {
            if (activeBattleList.Count == 0)
                return localDB.Battles.GetDefaultBattle();

            // find the most recent non-default battle.
            KPDatabaseDataSet.BattlesRow mostRecentBattle;
            lock (activeBattleList)
                mostRecentBattle = (KPDatabaseDataSet.BattlesRow)activeBattleList
                    .Where(ab => ab.Key.DefaultBattle == false)
                    .MaxEntry(ab => ab.Value, ab => ab.Key);

            if (mostRecentBattle == null)
                mostRecentBattle = localDB.Battles.GetDefaultBattle();

            return mostRecentBattle;
        }
        #endregion
    }
}
