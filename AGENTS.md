# Agent guide — kparser

Headless parse-state dump for parity with kparser2. Does **not** require the WinForms host or SQL CE on disk.

## Snapshot (preferred oracle)

One ChatLine string per line (same format as `UnitTests/Core/TestParser.cs`). Blank lines and `#` comments are skipped. Extra unused header seq fields present in some RAM/log captures are optional; TestParser lines omit them.

```powershell
powershell -File C:\Users\porob\git\kdev\kparser\scripts\snapshot.ps1 snapshot `
  C:\Users\porob\git\kdev\kparser\fixtures\chatlines\test_player_hit_mob.txt --json

powershell -File C:\Users\porob\git\kdev\kparser\scripts\snapshot.ps1 snapshot `
  C:\Users\porob\git\kdev\kparser\fixtures\chatlines\test_player_hit_mob.txt
```

Human-readable default prints counts, combatants, and `parity.interactions`. `--json` prints the full document (schema: [docs/snapshot-schema.md](docs/snapshot-schema.md)). `-o out.json` writes UTF-8 JSON without changing parse behavior.

Build without the wrapper:

```powershell
# Requires Visual Studio MSBuild (net 3.5 / x86). Not `dotnet run`.
msbuild C:\Users\porob\git\kdev\kparser\kparser.Cli\kparser.Cli.csproj /p:Configuration=Debug /p:Platform=x86
C:\Users\porob\git\kdev\kparser\kparser.Cli\bin\x86\Debug\kparser.cli.exe snapshot fixtures\chatlines\test_player_hit_mob.txt --json
```

## No side-effects

`ParseSnapshot.FromChatLines` is opt-in and in-memory:

- Does not call `DatabaseManager.CreateDatabase` / `UpdateDatabase` (no `.sdf`)
- Does not call `MsgManager.StartNewSession` (no `debugOutput.txt`)
- Does not start the 5s parse timer
- Resets `MsgManager` / `EntityManager` before and after
- Parse exceptions go into JSON `errors[]` instead of `Logger`

Pending pet-death queue handling used by the live GUI is **not** applied (v1). TestParser-style chatlines do not need it.

## Compare with kparser2

kparser IDs are DataSet autoincrement; kparser2 uses packet entity IDs. Diff `parity.interactions` by **name** against kparser2 `InteractionDto` (`actorName`, `targetName`, `interactionType`, `actionType` ≈ kparser2 category/harm, `amount`/`value`, `success`).

See [kparser2 AGENTS.md](../kparser2/AGENTS.md) for the dual-oracle loop.

## Tests

`UnitTests` is not in `FFXILogParser.sln`. After building `UnitTests.csproj`, run NUnit 2.6 against `UnitTests.dll` (`TestParseSnapshot`).
