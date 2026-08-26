using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WaywardGamers.KParser
{
    /// <summary>
    /// Deterministic indented JSON for <see cref="ParseSnapshotResult"/>.
    /// Key order is fixed; no extra dependencies (net 3.5).
    /// </summary>
    internal static class ParseSnapshotJson
    {
        public static string SerializeParityChat(ParseSnapshotResult result)
        {
            JsonWriter w = new JsonWriter();
            List<ParseSnapshotParityChat> chat = null;
            if (result != null && result.Parity != null)
                chat = result.Parity.Chat;
            WriteParityChatArray(w, chat);
            return w.ToString();
        }

        public static string Serialize(ParseSnapshotResult result)
        {
            if (result == null)
                throw new ArgumentNullException("result");

            JsonWriter w = new JsonWriter();
            w.WriteStartObject();
            w.WritePropertyName("meta");
            WriteMeta(w, result.Meta);
            w.WritePropertyName("counts");
            WriteCounts(w, result.Counts);
            w.WritePropertyName("entities");
            WriteEntityArray(w, result.Entities);
            w.WritePropertyName("messages");
            WriteMessageArray(w, result.Messages);
            w.WritePropertyName("combatants");
            WriteCombatantArray(w, result.Combatants);
            w.WritePropertyName("battles");
            WriteBattleArray(w, result.Battles);
            w.WritePropertyName("interactions");
            WriteInteractionArray(w, result.Interactions);
            w.WritePropertyName("chat");
            WriteChatArray(w, result.Chat);
            w.WritePropertyName("loot");
            WriteLootArray(w, result.Loot);
            w.WritePropertyName("parity");
            WriteParity(w, result.Parity);
            w.WritePropertyName("errors");
            WriteStringArray(w, result.Errors);
            w.WriteEndObject();
            return w.ToString();
        }

        static void WriteMeta(JsonWriter w, ParseSnapshotMeta meta)
        {
            w.WriteStartObject();
            if (meta != null)
            {
                w.WritePropertyName("schema_version");
                w.WriteNumber(meta.SchemaVersion);
                w.WritePropertyName("source");
                w.WriteString(meta.Source);
                w.WritePropertyName("kparser_version");
                w.WriteString(meta.KparserVersion);
            }
            w.WriteEndObject();
        }

        static void WriteCounts(JsonWriter w, ParseSnapshotCounts counts)
        {
            w.WriteStartObject();
            if (counts != null)
            {
                w.WritePropertyName("messages");
                w.WriteNumber(counts.Messages);
                w.WritePropertyName("parseSuccessful");
                w.WriteNumber(counts.ParseSuccessful);
                w.WritePropertyName("combatants");
                w.WriteNumber(counts.Combatants);
                w.WritePropertyName("battles");
                w.WriteNumber(counts.Battles);
                w.WritePropertyName("interactions");
                w.WriteNumber(counts.Interactions);
                w.WritePropertyName("chat");
                w.WriteNumber(counts.Chat);
                w.WritePropertyName("loot");
                w.WriteNumber(counts.Loot);
            }
            w.WriteEndObject();
        }

        static void WriteEntityArray(JsonWriter w, List<ParseSnapshotEntity> list)
        {
            w.WriteStartArray();
            if (list != null)
            {
                foreach (ParseSnapshotEntity e in list)
                {
                    w.WriteStartObject();
                    w.WritePropertyName("name");
                    w.WriteString(e.Name);
                    w.WritePropertyName("type");
                    w.WriteString(e.Type);
                    w.WriteEndObject();
                }
            }
            w.WriteEndArray();
        }

        static void WriteMessageArray(JsonWriter w, List<ParseSnapshotMessage> list)
        {
            w.WriteStartArray();
            if (list != null)
            {
                foreach (ParseSnapshotMessage m in list)
                {
                    w.WriteStartObject();
                    w.WritePropertyName("parseSuccessful");
                    w.WriteBoolean(m.ParseSuccessful);
                    w.WritePropertyName("category");
                    w.WriteString(m.Category);
                    w.WritePropertyName("messageCode");
                    w.WriteString(m.MessageCode);
                    w.WritePropertyName("text");
                    w.WriteString(m.Text);
                    if (m.Combat != null)
                    {
                        w.WritePropertyName("combat");
                        WriteCombat(w, m.Combat);
                    }
                    if (m.Chat != null)
                    {
                        w.WritePropertyName("chat");
                        WriteChatObject(w, m.Chat);
                    }
                    if (m.Loot != null)
                    {
                        w.WritePropertyName("loot");
                        WriteLootObject(w, m.Loot);
                    }
                    if (m.Experience != null)
                    {
                        w.WritePropertyName("experience");
                        WriteExperience(w, m.Experience);
                    }
                    w.WriteEndObject();
                }
            }
            w.WriteEndArray();
        }

        static void WriteCombat(JsonWriter w, ParseSnapshotCombat c)
        {
            w.WriteStartObject();
            w.WritePropertyName("actorName");
            w.WriteString(c.ActorName);
            w.WritePropertyName("actorEntityType");
            w.WriteString(c.ActorEntityType);
            w.WritePropertyName("interactionType");
            w.WriteString(c.InteractionType);
            w.WritePropertyName("actionType");
            w.WriteString(c.ActionType);
            w.WritePropertyName("harmType");
            w.WriteString(c.HarmType);
            w.WritePropertyName("aidType");
            w.WriteString(c.AidType);
            w.WritePropertyName("actionName");
            w.WriteString(c.ActionName);
            w.WritePropertyName("failedActionType");
            w.WriteString(c.FailedActionType);
            w.WritePropertyName("successLevel");
            w.WriteString(c.SuccessLevel);
            w.WritePropertyName("isPreparing");
            w.WriteBoolean(c.IsPreparing);
            w.WritePropertyName("hasAdditionalEffect");
            w.WriteBoolean(c.HasAdditionalEffect);
            w.WritePropertyName("targets");
            w.WriteStartArray();
            if (c.Targets != null)
            {
                foreach (ParseSnapshotTarget t in c.Targets)
                {
                    w.WriteStartObject();
                    w.WritePropertyName("name");
                    w.WriteString(t.Name);
                    w.WritePropertyName("entityType");
                    w.WriteString(t.EntityType);
                    w.WritePropertyName("harmType");
                    w.WriteString(t.HarmType);
                    w.WritePropertyName("aidType");
                    w.WriteString(t.AidType);
                    w.WritePropertyName("defenseType");
                    w.WriteString(t.DefenseType);
                    w.WritePropertyName("failedActionType");
                    w.WriteString(t.FailedActionType);
                    w.WritePropertyName("amount");
                    w.WriteNumber(t.Amount);
                    w.WritePropertyName("damageModifier");
                    w.WriteString(t.DamageModifier);
                    w.WritePropertyName("shadowsUsed");
                    w.WriteNumber(t.ShadowsUsed);
                    w.WriteEndObject();
                }
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }

        static void WriteCombatantArray(JsonWriter w, List<ParseSnapshotCombatant> list)
        {
            w.WriteStartArray();
            if (list != null)
            {
                foreach (ParseSnapshotCombatant c in list)
                {
                    w.WriteStartObject();
                    w.WritePropertyName("id");
                    w.WriteNumber(c.Id);
                    w.WritePropertyName("name");
                    w.WriteString(c.Name);
                    w.WritePropertyName("type");
                    w.WriteString(c.Type);
                    w.WritePropertyName("playerInfo");
                    w.WriteString(c.PlayerInfo);
                    w.WriteEndObject();
                }
            }
            w.WriteEndArray();
        }

        static void WriteBattleArray(JsonWriter w, List<ParseSnapshotBattle> list)
        {
            w.WriteStartArray();
            if (list != null)
            {
                foreach (ParseSnapshotBattle b in list)
                {
                    w.WriteStartObject();
                    w.WritePropertyName("id");
                    w.WriteNumber(b.Id);
                    w.WritePropertyName("enemyName");
                    w.WriteString(b.EnemyName);
                    w.WritePropertyName("enemyId");
                    w.WriteNullableNumber(b.EnemyId);
                    w.WritePropertyName("startTime");
                    w.WriteString(b.StartTime);
                    w.WritePropertyName("endTime");
                    w.WriteString(b.EndTime);
                    w.WritePropertyName("killed");
                    w.WriteBoolean(b.Killed);
                    w.WritePropertyName("killerName");
                    w.WriteString(b.KillerName);
                    w.WritePropertyName("experiencePoints");
                    w.WriteNumber(b.ExperiencePoints);
                    w.WritePropertyName("experienceChain");
                    w.WriteNumber(b.ExperienceChain);
                    w.WriteEndObject();
                }
            }
            w.WriteEndArray();
        }

        static void WriteInteractionArray(JsonWriter w, List<ParseSnapshotInteraction> list)
        {
            w.WriteStartArray();
            if (list != null)
            {
                foreach (ParseSnapshotInteraction i in list)
                {
                    w.WriteStartObject();
                    w.WritePropertyName("id");
                    w.WriteNumber(i.Id);
                    w.WritePropertyName("battleId");
                    w.WriteNullableNumber(i.BattleId);
                    w.WritePropertyName("timestamp");
                    w.WriteString(i.Timestamp);
                    w.WritePropertyName("actorName");
                    w.WriteString(i.ActorName);
                    w.WritePropertyName("targetName");
                    w.WriteString(i.TargetName);
                    w.WritePropertyName("actionName");
                    w.WriteString(i.ActionName);
                    w.WritePropertyName("actionType");
                    w.WriteString(i.ActionType);
                    w.WritePropertyName("harmType");
                    w.WriteString(i.HarmType);
                    w.WritePropertyName("aidType");
                    w.WriteString(i.AidType);
                    w.WritePropertyName("defenseType");
                    w.WriteString(i.DefenseType);
                    w.WritePropertyName("failedActionType");
                    w.WriteString(i.FailedActionType);
                    w.WritePropertyName("amount");
                    w.WriteNumber(i.Amount);
                    w.WritePropertyName("preparing");
                    w.WriteBoolean(i.Preparing);
                    w.WriteEndObject();
                }
            }
            w.WriteEndArray();
        }

        static void WriteChatArray(JsonWriter w, List<ParseSnapshotChat> list)
        {
            w.WriteStartArray();
            if (list != null)
            {
                foreach (ParseSnapshotChat c in list)
                    WriteChatObject(w, c);
            }
            w.WriteEndArray();
        }

        static void WriteChatObject(JsonWriter w, ParseSnapshotChat c)
        {
            w.WriteStartObject();
            w.WritePropertyName("timestamp");
            w.WriteString(c.Timestamp);
            w.WritePropertyName("speaker");
            w.WriteString(c.Speaker);
            w.WritePropertyName("chatType");
            w.WriteString(c.ChatType);
            w.WritePropertyName("message");
            w.WriteString(c.Message);
            w.WriteEndObject();
        }

        static void WriteLootArray(JsonWriter w, List<ParseSnapshotLoot> list)
        {
            w.WriteStartArray();
            if (list != null)
            {
                foreach (ParseSnapshotLoot l in list)
                    WriteLootObject(w, l);
            }
            w.WriteEndArray();
        }

        static void WriteLootObject(JsonWriter w, ParseSnapshotLoot l)
        {
            w.WriteStartObject();
            w.WritePropertyName("itemName");
            w.WriteString(l.ItemName);
            w.WritePropertyName("actorName");
            w.WriteString(l.ActorName);
            w.WritePropertyName("gil");
            w.WriteNumber(l.Gil);
            w.WritePropertyName("lost");
            w.WriteBoolean(l.Lost);
            w.WritePropertyName("battleId");
            w.WriteNullableNumber(l.BattleId);
            w.WriteEndObject();
        }

        static void WriteExperience(JsonWriter w, ParseSnapshotExperience e)
        {
            w.WriteStartObject();
            w.WritePropertyName("recipient");
            w.WriteString(e.Recipient);
            w.WritePropertyName("experiencePoints");
            w.WriteNumber(e.ExperiencePoints);
            w.WritePropertyName("experienceChain");
            w.WriteNumber(e.ExperienceChain);
            w.WriteEndObject();
        }

        static void WriteParity(JsonWriter w, ParseSnapshotParity parity)
        {
            w.WriteStartObject();
            w.WritePropertyName("interactions");
            w.WriteStartArray();
            if (parity != null && parity.Interactions != null)
            {
                foreach (ParseSnapshotParityInteraction p in parity.Interactions)
                {
                    w.WriteStartObject();
                    w.WritePropertyName("actorName");
                    w.WriteString(p.ActorName);
                    w.WritePropertyName("targetName");
                    w.WriteString(p.TargetName);
                    w.WritePropertyName("interactionType");
                    w.WriteString(p.InteractionType);
                    w.WritePropertyName("actionType");
                    w.WriteString(p.ActionType);
                    w.WritePropertyName("amount");
                    w.WriteNumber(p.Amount);
                    w.WritePropertyName("success");
                    w.WriteString(p.Success);
                    w.WriteEndObject();
                }
            }
            w.WriteEndArray();
            w.WritePropertyName("chat");
            WriteParityChatArray(w, parity != null ? parity.Chat : null);
            w.WriteEndObject();
        }

        static void WriteParityChatArray(JsonWriter w, List<ParseSnapshotParityChat> list)
        {
            w.WriteStartArray();
            if (list != null)
            {
                foreach (ParseSnapshotParityChat c in list)
                {
                    w.WriteStartObject();
                    w.WritePropertyName("speaker");
                    w.WriteString(c.Speaker);
                    w.WritePropertyName("mode");
                    w.WriteString(c.Mode);
                    w.WritePropertyName("message");
                    w.WriteString(c.Message);
                    w.WriteEndObject();
                }
            }
            w.WriteEndArray();
        }

        static void WriteStringArray(JsonWriter w, List<string> list)
        {
            w.WriteStartArray();
            if (list != null)
            {
                foreach (string s in list)
                    w.WriteString(s);
            }
            w.WriteEndArray();
        }

        sealed class JsonWriter
        {
            readonly StringBuilder sb = new StringBuilder();
            int indent;
            bool needsComma;
            bool afterProperty;

            public void WriteStartObject()
            {
                WriteContainerStart();
                sb.Append('{');
                indent++;
                needsComma = false;
                afterProperty = false;
            }

            public void WriteEndObject()
            {
                indent--;
                sb.Append('\n');
                WriteIndent();
                sb.Append('}');
                needsComma = true;
                afterProperty = false;
            }

            public void WriteStartArray()
            {
                WriteContainerStart();
                sb.Append('[');
                indent++;
                needsComma = false;
                afterProperty = false;
            }

            public void WriteEndArray()
            {
                indent--;
                sb.Append('\n');
                WriteIndent();
                sb.Append(']');
                needsComma = true;
                afterProperty = false;
            }

            public void WritePropertyName(string name)
            {
                if (needsComma)
                    sb.Append(',');
                sb.Append('\n');
                WriteIndent();
                WriteRawString(name);
                sb.Append(": ");
                needsComma = false;
                afterProperty = true;
            }

            public void WriteString(string value)
            {
                WriteScalarPrefix();
                if (value == null)
                    sb.Append("null");
                else
                    WriteRawString(value);
                needsComma = true;
            }

            public void WriteNumber(int value)
            {
                WriteScalarPrefix();
                sb.Append(value.ToString(CultureInfo.InvariantCulture));
                needsComma = true;
            }

            public void WriteNullableNumber(int? value)
            {
                WriteScalarPrefix();
                if (value.HasValue)
                    sb.Append(value.Value.ToString(CultureInfo.InvariantCulture));
                else
                    sb.Append("null");
                needsComma = true;
            }

            public void WriteBoolean(bool value)
            {
                WriteScalarPrefix();
                sb.Append(value ? "true" : "false");
                needsComma = true;
            }

            public override string ToString()
            {
                return sb.ToString();
            }

            void WriteContainerStart()
            {
                if (afterProperty)
                {
                    afterProperty = false;
                    return;
                }
                if (sb.Length == 0)
                    return;
                if (needsComma)
                    sb.Append(',');
                sb.Append('\n');
                WriteIndent();
            }

            void WriteScalarPrefix()
            {
                if (afterProperty)
                {
                    afterProperty = false;
                    return;
                }
                if (needsComma)
                    sb.Append(',');
                sb.Append('\n');
                WriteIndent();
            }

            void WriteIndent()
            {
                for (int i = 0; i < indent; i++)
                    sb.Append("  ");
            }

            void WriteRawString(string value)
            {
                sb.Append('"');
                foreach (char c in value)
                {
                    switch (c)
                    {
                        case '"':
                            sb.Append("\\\"");
                            break;
                        case '\\':
                            sb.Append("\\\\");
                            break;
                        case '\b':
                            sb.Append("\\b");
                            break;
                        case '\f':
                            sb.Append("\\f");
                            break;
                        case '\n':
                            sb.Append("\\n");
                            break;
                        case '\r':
                            sb.Append("\\r");
                            break;
                        case '\t':
                            sb.Append("\\t");
                            break;
                        default:
                            if (c < ' ')
                            {
                                sb.Append("\\u");
                                sb.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                            }
                            else
                            {
                                sb.Append(c);
                            }
                            break;
                    }
                }
                sb.Append('"');
            }
        }
    }
}
