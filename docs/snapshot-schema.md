# kparser snapshot schema

Version: **1**

Opt-in JSON dump of kparser parse/analytics state. Produced by `kparser.cli snapshot <chatlines.txt> --json` and `ParseSnapshot.ToJson`. Legacy `.sdf` files are not read or written.

kparser is **not** `AnalyticsSnapshotDto`. Combatant/battle IDs are in-memory autoincrement values. `HarmType` enums differ from kparser2 (kparser: Damage/Enfeeble/Drain; kparser2: Melee/Ranged/Spell). Compare `parity.interactions` by actor/target **name**.

## Top-level shape

```json
{
  "meta": {
    "schema_version": 1,
    "source": "kparser",
    "kparser_version": "1.6.5"
  },
  "counts": {
    "messages": 0,
    "parseSuccessful": 0,
    "combatants": 0,
    "battles": 0,
    "interactions": 0,
    "chat": 0,
    "loot": 0
  },
  "entities": [{ "name": "Motenten", "type": "Player" }],
  "messages": [],
  "combatants": [],
  "battles": [],
  "interactions": [],
  "chat": [],
  "loot": [],
  "parity": {
    "interactions": [{
      "actorName": "Motenten",
      "targetName": "Greater Colibri",
      "interactionType": "Harm",
      "actionType": "Melee",
      "amount": 128,
      "success": "hit"
    }],
    "chat": [{
      "speaker": "Alice",
      "mode": "Yell",
      "message": "hello"
    }]
  },
  "errors": []
}
```

## Fields

| Field | Description |
|---|---|
| `meta.schema_version` | `1` for this contract |
| `entities` | `EntityManager` name → `EntityType` at dump time |
| `messages` | Per-line `Parser.Parse` result (pre-database) |
| `combatants` / `battles` / `interactions` / `chat` / `loot` | In-memory `DatabaseEntry` tables. The dummy `DefaultBattle` row is omitted |
| `parity.interactions` | Name-keyed projection for kparser2 diffs |
| `parity.chat` | Speaker/mode/**body** projection for kparser2 diffs. Includes `MessageCategoryType.System` (speaker `System`) even though those rows are not stored in `ChatMessages`. Native `chat[].message` stays the full chatline text. |
| `errors` | Per-line parse exceptions; empty on a clean run |

## `parity.success`

Derived only from existing kparser fields, in order:

1. Target `DefenseType`: Parry → `parry`, Shadow → `shadow-absorb`, Evade/Evasion → `miss`
2. `FailedActionType.NoEffect` → `no-effect`
3. Else `SuccessLevel`: Successful → `hit`, Unsuccessful/Failed → `miss`

## `parity.chat`

Compare with kparser2 `--parity-chat` (incoming packets only) by `speaker`, `mode`, and body `message`:

- **mode**: `Yell` / `Say` / `Shout` / `Tell` / `Party` / `Linkshell` / `Emote` / `System` (kparser `Arena` / `Echo` kept if they appear)
- **message**: body only (`Name : text` and `Name[Zone]: text` prefixes stripped)
- Give each chatline a **unique `eventSeq`**. Reused dummy headers (`00000010`) stitch lines into one message.

DataSet `DateTime` columns are `Unspecified`; snapshot timestamps treat that as UTC (same clock as `messages[].chat`).

## Input

UTF-8 text, one ChatLine string per line (comma-hex header + message text, as in `TestParser.cs`). Blank lines and `#` comments are skipped. Extra unused header sequence fields found in some RAM/log captures are optional.

Not in v1: FFXI `*.log`, `.sdf` reparse.
