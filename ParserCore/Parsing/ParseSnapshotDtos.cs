using System.Collections.Generic;

namespace WaywardGamers.KParser
{
    /// <summary>
    /// Opt-in dump of kparser parse/analytics state for parity comparison.
    /// Native fields plus a small name-keyed <see cref="Parity"/> projection.
    /// </summary>
    public class ParseSnapshotResult
    {
        public ParseSnapshotMeta Meta { get; set; }
        public ParseSnapshotCounts Counts { get; set; }
        public List<ParseSnapshotEntity> Entities { get; set; }
        public List<ParseSnapshotMessage> Messages { get; set; }
        public List<ParseSnapshotCombatant> Combatants { get; set; }
        public List<ParseSnapshotBattle> Battles { get; set; }
        public List<ParseSnapshotInteraction> Interactions { get; set; }
        public List<ParseSnapshotChat> Chat { get; set; }
        public List<ParseSnapshotLoot> Loot { get; set; }
        public ParseSnapshotParity Parity { get; set; }
        public List<string> Errors { get; set; }
    }

    public class ParseSnapshotMeta
    {
        public int SchemaVersion { get; set; }
        public string Source { get; set; }
        public string KparserVersion { get; set; }
    }

    public class ParseSnapshotCounts
    {
        public int Messages { get; set; }
        public int ParseSuccessful { get; set; }
        public int Combatants { get; set; }
        public int Battles { get; set; }
        public int Interactions { get; set; }
        public int Chat { get; set; }
        public int Loot { get; set; }
    }

    public class ParseSnapshotEntity
    {
        public string Name { get; set; }
        public string Type { get; set; }
    }

    public class ParseSnapshotMessage
    {
        public bool ParseSuccessful { get; set; }
        public string Category { get; set; }
        public string MessageCode { get; set; }
        public string Text { get; set; }
        public ParseSnapshotCombat Combat { get; set; }
        public ParseSnapshotChat Chat { get; set; }
        public ParseSnapshotLoot Loot { get; set; }
        public ParseSnapshotExperience Experience { get; set; }
    }

    public class ParseSnapshotCombat
    {
        public string ActorName { get; set; }
        public string ActorEntityType { get; set; }
        public string InteractionType { get; set; }
        public string ActionType { get; set; }
        public string HarmType { get; set; }
        public string AidType { get; set; }
        public string ActionName { get; set; }
        public string FailedActionType { get; set; }
        public string SuccessLevel { get; set; }
        public bool IsPreparing { get; set; }
        public bool HasAdditionalEffect { get; set; }
        public List<ParseSnapshotTarget> Targets { get; set; }
    }

    public class ParseSnapshotTarget
    {
        public string Name { get; set; }
        public string EntityType { get; set; }
        public string HarmType { get; set; }
        public string AidType { get; set; }
        public string DefenseType { get; set; }
        public string FailedActionType { get; set; }
        public int Amount { get; set; }
        public string DamageModifier { get; set; }
        public int ShadowsUsed { get; set; }
    }

    public class ParseSnapshotCombatant
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public string PlayerInfo { get; set; }
    }

    public class ParseSnapshotBattle
    {
        public int Id { get; set; }
        public string EnemyName { get; set; }
        public int? EnemyId { get; set; }
        public string StartTime { get; set; }
        public string EndTime { get; set; }
        public bool Killed { get; set; }
        public string KillerName { get; set; }
        public int ExperiencePoints { get; set; }
        public int ExperienceChain { get; set; }
    }

    public class ParseSnapshotInteraction
    {
        public int Id { get; set; }
        public int? BattleId { get; set; }
        public string Timestamp { get; set; }
        public string ActorName { get; set; }
        public string TargetName { get; set; }
        public string ActionName { get; set; }
        public string ActionType { get; set; }
        public string HarmType { get; set; }
        public string AidType { get; set; }
        public string DefenseType { get; set; }
        public string FailedActionType { get; set; }
        public int Amount { get; set; }
        public bool Preparing { get; set; }
    }

    public class ParseSnapshotChat
    {
        public string Timestamp { get; set; }
        public string Speaker { get; set; }
        public string ChatType { get; set; }
        public string Message { get; set; }
    }

    public class ParseSnapshotLoot
    {
        public string ItemName { get; set; }
        public string ActorName { get; set; }
        public int Gil { get; set; }
        public bool Lost { get; set; }
        public int? BattleId { get; set; }
    }

    public class ParseSnapshotExperience
    {
        public string Recipient { get; set; }
        public int ExperiencePoints { get; set; }
        public int ExperienceChain { get; set; }
    }

    public class ParseSnapshotParity
    {
        public List<ParseSnapshotParityInteraction> Interactions { get; set; }
        public List<ParseSnapshotParityChat> Chat { get; set; }
    }

    public class ParseSnapshotParityInteraction
    {
        public string ActorName { get; set; }
        public string TargetName { get; set; }
        public string InteractionType { get; set; }
        public string ActionType { get; set; }
        public int Amount { get; set; }
        public string Success { get; set; }
    }

    /// <summary>
    /// Name/mode/body projection for kparser2 chat diffs.
    /// <see cref="Message"/> is the body only (no speaker prefix).
    /// </summary>
    public class ParseSnapshotParityChat
    {
        public string Speaker { get; set; }
        public string Mode { get; set; }
        public string Message { get; set; }
    }
}
