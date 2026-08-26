using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using WaywardGamers.KParser.Database;
using WaywardGamers.KParser.Parsing;

namespace WaywardGamers.KParser
{
    /// <summary>
    /// Side-effect-free snapshot of kparser parse state (in-memory only).
    /// Does not write .sdf files, debug dumps, or error logs on the success path.
    /// </summary>
    public static class ParseSnapshot
    {
        public const int SchemaVersion = 1;

        /// <summary>
        /// Parse TestParser-style chat lines (one ChatLine string per line).
        /// Blank lines and lines starting with '#' are skipped.
        /// </summary>
        public static ParseSnapshotResult FromChatLines(IEnumerable<string> lines)
        {
            if (lines == null)
                throw new ArgumentNullException("lines");

            CultureInfo oldCulture = Thread.CurrentThread.CurrentCulture;
            CultureInfo oldUiCulture = Thread.CurrentThread.CurrentUICulture;
            DatabaseEntry entry = null;

            try
            {
                CultureInfo en = new CultureInfo("en-US");
                Thread.CurrentThread.CurrentCulture = en;
                Thread.CurrentThread.CurrentUICulture = en;

                MsgManager.Instance.Reset();

                entry = new DatabaseEntry();
                KPDatabaseDataSet ds = new KPDatabaseDataSet();
                DateTime nowUtc = DateTime.Now.ToUniversalTime();
                ds.Battles.AddBattlesRow(null, nowUtc, nowUtc, false, null, 0, 0, 0, 0, true);

                List<Message> parsed = new List<Message>();
                List<string> errors = new List<string>();
                int index = 0;

                foreach (string raw in lines)
                {
                    index++;
                    if (raw == null)
                        continue;

                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#')
                        continue;

                    try
                    {
                        ChatLine chatLine = new ChatLine(line);
                        MessageLine msgLine = new MessageLine(chatLine);
                        Message msg = Parser.Parse(msgLine);
                        EntityManager.Instance.AddEntitiesFromMessage(msg);
                        MsgManager.Instance.AddMessageToMessageCollection(msg);
                        entry.AddMessageToDatabase(ds, msg);
                        if (!parsed.Contains(msg))
                            parsed.Add(msg);
                    }
                    catch (Exception ex)
                    {
                        errors.Add(string.Format(CultureInfo.InvariantCulture,
                            "line {0}: {1}", index, ex.Message));
                    }
                }

                entry.MessageBatchSent();
                entry.CloseOutBattles();

                return BuildResult(parsed, ds, errors);
            }
            finally
            {
                try
                {
                    MsgManager.Instance.Reset();
                }
                catch
                {
                }

                try
                {
                    if (entry != null)
                        entry.Reset();
                }
                catch
                {
                }

                Thread.CurrentThread.CurrentCulture = oldCulture;
                Thread.CurrentThread.CurrentUICulture = oldUiCulture;
            }
        }

        /// <summary>
        /// Read a UTF-8 chatline file and snapshot it.
        /// </summary>
        public static ParseSnapshotResult FromChatLineFile(string path)
        {
            if (path == null)
                throw new ArgumentNullException("path");

            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            return FromChatLines(lines);
        }

        public static string ToJson(ParseSnapshotResult result)
        {
            return ParseSnapshotJson.Serialize(result);
        }

        /// <summary>
        /// JSON array of <c>parity.chat</c> rows only.
        /// </summary>
        public static string ToParityChatJson(ParseSnapshotResult result)
        {
            return ParseSnapshotJson.SerializeParityChat(result);
        }

        public static string FormatSummary(ParseSnapshotResult result)
        {
            if (result == null)
                throw new ArgumentNullException("result");

            StringBuilder sb = new StringBuilder();
            ParseSnapshotCounts c = result.Counts;
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "messages={0} parseSuccessful={1} combatants={2} battles={3} interactions={4} chat={5} loot={6}\n",
                c.Messages, c.ParseSuccessful, c.Combatants, c.Battles, c.Interactions, c.Chat, c.Loot);

            if (result.Parity != null && result.Parity.Chat != null && result.Parity.Chat.Count > 0)
            {
                sb.AppendLine("parity chat:");
                int shown = 0;
                foreach (ParseSnapshotParityChat p in result.Parity.Chat)
                {
                    if (shown >= 12)
                        break;
                    sb.AppendFormat("  [{0}] {1}: {2}\n", p.Mode, p.Speaker, p.Message);
                    shown++;
                }
            }

            if (result.Combatants != null && result.Combatants.Count > 0)
            {
                sb.AppendLine("combatants:");
                int shown = 0;
                foreach (ParseSnapshotCombatant combatant in result.Combatants)
                {
                    if (shown >= 12)
                        break;
                    sb.AppendFormat("  {0} ({1})\n", combatant.Name, combatant.Type);
                    shown++;
                }
            }

            if (result.Parity != null && result.Parity.Interactions != null && result.Parity.Interactions.Count > 0)
            {
                sb.AppendLine("parity interactions:");
                int shown = 0;
                foreach (ParseSnapshotParityInteraction p in result.Parity.Interactions)
                {
                    if (shown >= 12)
                        break;
                    sb.AppendFormat(CultureInfo.InvariantCulture,
                        "  {0} -> {1} {2} {3} {4} {5}\n",
                        p.ActorName, p.TargetName, p.InteractionType, p.ActionType, p.Amount, p.Success);
                    shown++;
                }
            }

            if (result.Errors != null && result.Errors.Count > 0)
            {
                sb.AppendFormat(CultureInfo.InvariantCulture, "errors={0}\n", result.Errors.Count);
                foreach (string err in result.Errors)
                    sb.AppendFormat("  {0}\n", err);
            }

            return sb.ToString();
        }

        static ParseSnapshotResult BuildResult(List<Message> parsed, KPDatabaseDataSet ds, List<string> errors)
        {
            List<ParseSnapshotMessage> messages = new List<ParseSnapshotMessage>();
            List<ParseSnapshotParityInteraction> parity = new List<ParseSnapshotParityInteraction>();
            List<ParseSnapshotParityChat> parityChat = new List<ParseSnapshotParityChat>();
            int successful = 0;

            foreach (Message msg in parsed)
            {
                if (msg.IsParseSuccessful)
                    successful++;

                ParseSnapshotMessage dump = MapMessage(msg);
                messages.Add(dump);
                AddParityFromMessage(msg, parity);
                AddParityChatFromMessage(msg, parityChat);
            }

            List<ParseSnapshotEntity> entities = new List<ParseSnapshotEntity>();
            List<KeyValuePair<string, EntityType>> entityPairs = new List<KeyValuePair<string, EntityType>>(
                EntityManager.Instance.SnapshotEntities());
            entityPairs.Sort(CompareEntityByName);
            foreach (KeyValuePair<string, EntityType> pair in entityPairs)
            {
                ParseSnapshotEntity entity = new ParseSnapshotEntity();
                entity.Name = pair.Key;
                entity.Type = pair.Value.ToString();
                entities.Add(entity);
            }

            List<ParseSnapshotCombatant> combatants = MapCombatants(ds);
            List<ParseSnapshotBattle> battles = MapBattles(ds);
            List<ParseSnapshotInteraction> interactions = MapInteractions(ds);
            List<ParseSnapshotChat> chat = MapChat(ds);
            List<ParseSnapshotLoot> loot = MapLoot(ds);

            ParseSnapshotCounts counts = new ParseSnapshotCounts();
            counts.Messages = messages.Count;
            counts.ParseSuccessful = successful;
            counts.Combatants = combatants.Count;
            counts.Battles = battles.Count;
            counts.Interactions = interactions.Count;
            counts.Chat = chat.Count;
            counts.Loot = loot.Count;

            ParseSnapshotMeta meta = new ParseSnapshotMeta();
            meta.SchemaVersion = SchemaVersion;
            meta.Source = "kparser";
            meta.KparserVersion = GetAssemblyVersion();

            ParseSnapshotParity parityWrap = new ParseSnapshotParity();
            parityWrap.Interactions = parity;
            parityWrap.Chat = parityChat;

            ParseSnapshotResult result = new ParseSnapshotResult();
            result.Meta = meta;
            result.Counts = counts;
            result.Entities = entities;
            result.Messages = messages;
            result.Combatants = combatants;
            result.Battles = battles;
            result.Interactions = interactions;
            result.Chat = chat;
            result.Loot = loot;
            result.Parity = parityWrap;
            result.Errors = errors;
            return result;
        }

        static int CompareEntityByName(KeyValuePair<string, EntityType> a, KeyValuePair<string, EntityType> b)
        {
            return string.Compare(a.Key, b.Key, StringComparison.Ordinal);
        }

        static string GetAssemblyVersion()
        {
            Version v = Assembly.GetExecutingAssembly().GetName().Version;
            if (v == null)
                return "0.0.0";
            return string.Format(CultureInfo.InvariantCulture, "{0}.{1}.{2}", v.Major, v.Minor, v.Build);
        }

        static ParseSnapshotMessage MapMessage(Message msg)
        {
            ParseSnapshotMessage dump = new ParseSnapshotMessage();
            dump.ParseSuccessful = msg.IsParseSuccessful;
            dump.Category = msg.MessageCategory.ToString();
            dump.MessageCode = string.Format(CultureInfo.InvariantCulture, "0x{0:x}", msg.MessageCode);
            dump.Text = msg.CompleteMessageText;

            if (msg.ChatDetails != null)
            {
                ParseSnapshotChat chat = new ParseSnapshotChat();
                chat.Timestamp = FormatTimestamp(msg.Timestamp);
                chat.Speaker = msg.ChatDetails.ChatSpeakerName;
                chat.ChatType = msg.ChatDetails.ChatMessageType.ToString();
                chat.Message = msg.ChatDetails.FullChatText;
                dump.Chat = chat;
            }

            if (msg.EventDetails != null)
            {
                if (msg.EventDetails.CombatDetails != null)
                    dump.Combat = MapCombat(msg.EventDetails.CombatDetails);

                if (msg.EventDetails.LootDetails != null)
                {
                    ParseSnapshotLoot loot = new ParseSnapshotLoot();
                    loot.ItemName = msg.EventDetails.LootDetails.ItemName;
                    loot.ActorName = msg.EventDetails.LootDetails.WhoObtained;
                    loot.Gil = msg.EventDetails.LootDetails.Gil;
                    loot.Lost = msg.EventDetails.LootDetails.WasLost;
                    dump.Loot = loot;
                }

                if (msg.EventDetails.ExperienceDetails != null)
                {
                    ParseSnapshotExperience xp = new ParseSnapshotExperience();
                    xp.Recipient = msg.EventDetails.ExperienceDetails.ExperienceRecipient;
                    xp.ExperiencePoints = msg.EventDetails.ExperienceDetails.ExperiencePoints;
                    xp.ExperienceChain = msg.EventDetails.ExperienceDetails.ExperienceChain;
                    dump.Experience = xp;
                }
            }

            return dump;
        }

        static ParseSnapshotCombat MapCombat(CombatDetails combat)
        {
            ParseSnapshotCombat dump = new ParseSnapshotCombat();
            dump.ActorName = combat.HasActor ? combat.ActorName : "";
            dump.ActorEntityType = combat.ActorEntityType.ToString();
            dump.InteractionType = combat.InteractionType.ToString();
            dump.ActionType = combat.ActionType.ToString();
            dump.HarmType = combat.HarmType.ToString();
            dump.AidType = combat.AidType.ToString();
            dump.ActionName = combat.ActionName;
            dump.FailedActionType = combat.FailedActionType.ToString();
            dump.SuccessLevel = combat.SuccessLevel.ToString();
            dump.IsPreparing = combat.IsPreparing;
            dump.HasAdditionalEffect = combat.HasAdditionalEffect;
            dump.Targets = new List<ParseSnapshotTarget>();

            foreach (TargetDetails target in combat.Targets)
            {
                ParseSnapshotTarget t = new ParseSnapshotTarget();
                t.Name = target.Name;
                t.EntityType = target.EntityType.ToString();
                t.HarmType = target.HarmType.ToString();
                t.AidType = target.AidType.ToString();
                t.DefenseType = target.DefenseType.ToString();
                t.FailedActionType = target.FailedActionType.ToString();
                t.Amount = target.Amount;
                t.DamageModifier = target.DamageModifier.ToString();
                t.ShadowsUsed = target.ShadowsUsed;
                dump.Targets.Add(t);
            }

            return dump;
        }

        static void AddParityChatFromMessage(Message msg, List<ParseSnapshotParityChat> chat)
        {
            if (msg == null || chat == null)
                return;

            if (msg.MessageCategory == MessageCategoryType.Chat && msg.ChatDetails != null)
            {
                ParseSnapshotParityChat row = new ParseSnapshotParityChat();
                string speaker = msg.ChatDetails.ChatSpeakerName ?? "";
                row.Speaker = speaker;
                row.Mode = msg.ChatDetails.ChatMessageType.ToString();
                string full = msg.ChatDetails.FullChatText;
                if (string.IsNullOrEmpty(full))
                    full = msg.CompleteMessageText;
                row.Message = ChatBody(speaker, full);
                chat.Add(row);
                return;
            }

            if (msg.MessageCategory == MessageCategoryType.System)
            {
                ParseSnapshotParityChat row = new ParseSnapshotParityChat();
                row.Speaker = "System";
                row.Mode = "System";
                row.Message = msg.CompleteMessageText ?? "";
                chat.Add(row);
            }
        }

        /// <summary>
        /// Strip <c>Name : body</c> / <c>Name[Zone]: body</c> prefixes for kparser2 diffs.
        /// </summary>
        internal static string ChatBody(string speaker, string full)
        {
            if (string.IsNullOrEmpty(full))
                return "";
            if (string.IsNullOrEmpty(speaker))
                return full;
            if (full.StartsWith(speaker, StringComparison.Ordinal))
            {
                int colon = full.IndexOf(':');
                if (colon >= 0)
                    return full.Substring(colon + 1).TrimStart();
            }

            return full;
        }

        static void AddParityFromMessage(Message msg, List<ParseSnapshotParityInteraction> parity)
        {
            if (msg.EventDetails == null || msg.EventDetails.CombatDetails == null)
                return;

            CombatDetails combat = msg.EventDetails.CombatDetails;
            string actorName = combat.HasActor ? combat.ActorName : "";

            if (combat.Targets == null || combat.Targets.Count == 0)
            {
                ParseSnapshotParityInteraction row = new ParseSnapshotParityInteraction();
                row.ActorName = actorName;
                row.TargetName = "";
                row.InteractionType = combat.InteractionType.ToString();
                row.ActionType = combat.ActionType.ToString();
                row.Amount = 0;
                row.Success = ParitySuccess(combat, null);
                parity.Add(row);
                return;
            }

            foreach (TargetDetails target in combat.Targets)
            {
                ParseSnapshotParityInteraction row = new ParseSnapshotParityInteraction();
                row.ActorName = actorName;
                row.TargetName = target.Name;
                row.InteractionType = combat.InteractionType.ToString();
                row.ActionType = combat.ActionType.ToString();
                row.Amount = target.Amount;
                row.Success = ParitySuccess(combat, target);
                parity.Add(row);
            }
        }

        /// <summary>
        /// Map kparser defense/failure/success fields onto kparser2-style success labels.
        /// </summary>
        internal static string ParitySuccess(CombatDetails combat, TargetDetails target)
        {
            if (target != null)
            {
                switch (target.DefenseType)
                {
                    case DefenseType.Parry:
                        return "parry";
                    case DefenseType.Shadow:
                        return "shadow-absorb";
                    case DefenseType.Evade:
                    case DefenseType.Evasion:
                        return "miss";
                }
            }

            FailedActionType failed = FailedActionType.None;
            if (target != null)
                failed = target.FailedActionType;
            else if (combat != null)
                failed = combat.FailedActionType;

            if (failed == FailedActionType.NoEffect)
                return "no-effect";

            if (combat == null)
                return "hit";

            switch (combat.SuccessLevel)
            {
                case SuccessType.Successful:
                    return "hit";
                case SuccessType.Unsuccessful:
                case SuccessType.Failed:
                    return "miss";
                default:
                    return combat.SuccessLevel.ToString();
            }
        }

        static List<ParseSnapshotCombatant> MapCombatants(KPDatabaseDataSet ds)
        {
            List<ParseSnapshotCombatant> list = new List<ParseSnapshotCombatant>();
            foreach (KPDatabaseDataSet.CombatantsRow row in ds.Combatants)
            {
                ParseSnapshotCombatant c = new ParseSnapshotCombatant();
                c.Id = row.CombatantID;
                c.Name = row.CombatantName;
                c.Type = ((EntityType)row.CombatantType).ToString();
                c.PlayerInfo = row.IsPlayerInfoNull() ? null : row.PlayerInfo;
                list.Add(c);
            }
            return list;
        }

        static List<ParseSnapshotBattle> MapBattles(KPDatabaseDataSet ds)
        {
            List<ParseSnapshotBattle> list = new List<ParseSnapshotBattle>();
            foreach (KPDatabaseDataSet.BattlesRow row in ds.Battles)
            {
                if (row.DefaultBattle)
                    continue;

                ParseSnapshotBattle b = new ParseSnapshotBattle();
                b.Id = row.BattleID;
                if (!row.IsEnemyIDNull() && row.CombatantsRowByEnemyCombatantRelation != null)
                {
                    b.EnemyId = row.EnemyID;
                    b.EnemyName = row.CombatantsRowByEnemyCombatantRelation.CombatantName;
                }
                else
                {
                    b.EnemyName = "";
                }

                b.StartTime = row.IsStartTimeNull() ? null : FormatTimestamp(row.StartTime);
                if (row.IsEndTimeNull() || row.EndTime == MagicNumbers.MinSQLDateTime)
                    b.EndTime = null;
                else
                    b.EndTime = FormatTimestamp(row.EndTime);

                b.Killed = row.Killed;
                if (!row.IsKillerIDNull() && row.CombatantsRowByBattleKillerRelation != null)
                    b.KillerName = row.CombatantsRowByBattleKillerRelation.CombatantName;
                b.ExperiencePoints = row.ExperiencePoints;
                b.ExperienceChain = row.ExperienceChain;
                list.Add(b);
            }
            return list;
        }

        static List<ParseSnapshotInteraction> MapInteractions(KPDatabaseDataSet ds)
        {
            List<ParseSnapshotInteraction> list = new List<ParseSnapshotInteraction>();
            foreach (KPDatabaseDataSet.InteractionsRow row in ds.Interactions)
            {
                ParseSnapshotInteraction i = new ParseSnapshotInteraction();
                i.Id = row.InteractionID;
                i.BattleId = row.IsBattleIDNull() ? (int?)null : row.BattleID;
                i.Timestamp = FormatTimestamp(row.Timestamp);

                if (!row.IsActorIDNull() && row.CombatantsRowByActorCombatantRelation != null)
                    i.ActorName = row.CombatantsRowByActorCombatantRelation.CombatantName;
                else
                    i.ActorName = "";

                if (!row.IsTargetIDNull() && row.CombatantsRowByTargetCombatantRelation != null)
                    i.TargetName = row.CombatantsRowByTargetCombatantRelation.CombatantName;
                else
                    i.TargetName = "";

                if (!row.IsActionIDNull() && row.ActionsRow != null)
                    i.ActionName = row.ActionsRow.ActionName;
                else
                    i.ActionName = "";

                i.ActionType = ((ActionType)row.ActionType).ToString();
                i.HarmType = ((HarmType)row.HarmType).ToString();
                i.AidType = ((AidType)row.AidType).ToString();
                i.DefenseType = ((DefenseType)row.DefenseType).ToString();
                i.FailedActionType = ((FailedActionType)row.FailedActionType).ToString();
                i.Amount = row.Amount;
                i.Preparing = row.Preparing;
                list.Add(i);
            }
            return list;
        }

        static List<ParseSnapshotChat> MapChat(KPDatabaseDataSet ds)
        {
            List<ParseSnapshotChat> list = new List<ParseSnapshotChat>();
            foreach (KPDatabaseDataSet.ChatMessagesRow row in ds.ChatMessages)
            {
                ParseSnapshotChat c = new ParseSnapshotChat();
                c.Timestamp = FormatTimestamp(row.Timestamp);
                c.Speaker = row.ChatSpeakersRow != null ? row.ChatSpeakersRow.SpeakerName : "";
                c.ChatType = ((ChatMessageType)row.ChatType).ToString();
                c.Message = row.Message;
                list.Add(c);
            }
            return list;
        }

        static List<ParseSnapshotLoot> MapLoot(KPDatabaseDataSet ds)
        {
            List<ParseSnapshotLoot> list = new List<ParseSnapshotLoot>();
            foreach (KPDatabaseDataSet.LootRow row in ds.Loot)
            {
                ParseSnapshotLoot l = new ParseSnapshotLoot();
                l.ItemName = row.ItemsRow != null ? row.ItemsRow.ItemName : "";
                l.ActorName = row.IsPlayerIDNull() || row.CombatantsRow == null
                    ? ""
                    : row.CombatantsRow.CombatantName;
                l.Gil = row.GilDropped;
                l.Lost = row.Lost;
                l.BattleId = row.IsBattleIDNull() ? (int?)null : row.BattleID;
                list.Add(l);
            }
            return list;
        }

        static string FormatTimestamp(DateTime timestamp)
        {
            DateTime utc;
            if (timestamp.Kind == DateTimeKind.Local)
                utc = timestamp.ToUniversalTime();
            else
                utc = DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);

            return utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        }
    }
}
