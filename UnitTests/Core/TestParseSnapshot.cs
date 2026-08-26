using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using WaywardGamers.KParser.Parsing;

namespace WaywardGamers.KParser
{
    [TestFixture]
    [Culture("en")]
    public class TestParseSnapshot
    {
        const string PlayerHitMob =
            "14,7a,98,80c08080,0000268b,00002c8b,003e,00,01,02,00,\u001e\u0001\u001e\u0001Motenten hits the Greater Colibri for 128 points of damage.\u007f1";

        const string PlayerMissMob =
            "15,28,99,80707070,000027ef,00002e39,0027,00,01,02,00,\u001e\u0001\u001e\u0001Motenten misses the Greater Colibri.\u007f1";

        const string YellAlice =
            "0b,00,00,80808080,00000021,00000021,0020,00,01,01,00,\u001e\u0001\u001e\u0001Alice[Windurst]: Hello from yell\u007f1";

        const string SayAlice =
            "09,00,00,80808080,00000022,00000022,001a,00,01,01,00,\u001e\u0001\u001e\u0001Alice : Hello from fixture\u007f1";

        const string SystemWelcome =
            "00,00,00,80808080,00000023,00000023,0015,00,01,00,00,\u001e\u0001\u001e\u0001Welcome to Vana'diel\u007f1";

        const string SayAliceCollide =
            "09,00,00,80808080,00000010,00000010,001a,00,01,01,00,\u001e\u0001\u001e\u0001Alice : Hello from fixture\u007f1";

        const string SayPoroCollide =
            "01,00,00,80808080,00000010,00000010,0010,00,01,01,00,\u001e\u0001\u001e\u0001Poroburu : hello\u007f1";

        [Test]
        public void SnapshotPlayerHitMobParity()
        {
            ParseSnapshotResult result = ParseSnapshot.FromChatLines(new[] { PlayerHitMob });

            Assert.That(result.Errors, Is.Empty);
            Assert.That(result.Counts.ParseSuccessful, Is.EqualTo(1));
            Assert.That(result.Parity.Interactions, Is.Not.Empty);

            ParseSnapshotParityInteraction row = result.Parity.Interactions[0];
            Assert.That(row.ActorName, Is.EqualTo("Motenten"));
            Assert.That(row.TargetName, Is.EqualTo("Greater Colibri"));
            Assert.That(row.InteractionType, Is.EqualTo("Harm"));
            Assert.That(row.ActionType, Is.EqualTo("Melee"));
            Assert.That(row.Amount, Is.EqualTo(128));
            Assert.That(row.Success, Is.EqualTo("hit"));
        }

        [Test]
        public void SnapshotPlayerMissMobParity()
        {
            ParseSnapshotResult result = ParseSnapshot.FromChatLines(new[] { PlayerMissMob });

            Assert.That(result.Errors, Is.Empty);
            Assert.That(result.Parity.Interactions, Is.Not.Empty);

            ParseSnapshotParityInteraction row = result.Parity.Interactions[0];
            Assert.That(row.ActorName, Is.EqualTo("Motenten"));
            Assert.That(row.TargetName, Is.EqualTo("Greater Colibri"));
            Assert.That(row.Amount, Is.EqualTo(0));
            Assert.That(row.Success, Is.EqualTo("miss"));
        }

        [Test]
        public void SnapshotResetsSingletons()
        {
            ParseSnapshot.FromChatLines(new[] { PlayerHitMob });

            Assert.That(EntityManager.Instance.SnapshotEntities(), Is.Empty);
            Assert.That(MsgManager.Instance.CollectedMessageCount, Is.EqualTo(0));
        }

        [Test]
        public void SnapshotDoesNotWriteDebugOrSdfFiles()
        {
            string temp = Path.Combine(Path.GetTempPath(), "kparser-snapshot-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            string previous = Environment.CurrentDirectory;

            try
            {
                Environment.CurrentDirectory = temp;
                ParseSnapshot.FromChatLines(new[] { PlayerHitMob, PlayerMissMob });

                string[] written = Directory.GetFiles(temp, "*.*", SearchOption.AllDirectories);
                Assert.That(written.Any(p => Path.GetFileName(p).Equals("debugOutput.txt", StringComparison.OrdinalIgnoreCase)), Is.False);
                Assert.That(written.Any(p => Path.GetExtension(p).Equals(".sdf", StringComparison.OrdinalIgnoreCase)), Is.False);
            }
            finally
            {
                Environment.CurrentDirectory = previous;
                try
                {
                    Directory.Delete(temp, true);
                }
                catch
                {
                }
            }
        }

        [Test]
        public void SnapshotJsonContainsParityFields()
        {
            ParseSnapshotResult result = ParseSnapshot.FromChatLines(new[] { PlayerHitMob });
            string json = ParseSnapshot.ToJson(result);

            Assert.That(json.Contains("\"success\": \"hit\""), Is.True);
            Assert.That(json.Contains("\"actorName\": \"Motenten\""), Is.True);
            Assert.That(json.Contains("\"targetName\": \"Greater Colibri\""), Is.True);
            Assert.That(json.Contains("\"schema_version\": 1"), Is.True);
        }

        [Test]
        public void SnapshotChatModesParityBodies()
        {
            ParseSnapshotResult result = ParseSnapshot.FromChatLines(new[] { YellAlice, SayAlice, SystemWelcome });

            Assert.That(result.Errors, Is.Empty);
            Assert.That(result.Parity.Chat.Count, Is.EqualTo(3));

            ParseSnapshotParityChat yell = result.Parity.Chat[0];
            Assert.That(yell.Speaker, Is.EqualTo("Alice"));
            Assert.That(yell.Mode, Is.EqualTo("Yell"));
            Assert.That(yell.Message, Is.EqualTo("Hello from yell"));

            ParseSnapshotParityChat say = result.Parity.Chat[1];
            Assert.That(say.Speaker, Is.EqualTo("Alice"));
            Assert.That(say.Mode, Is.EqualTo("Say"));
            Assert.That(say.Message, Is.EqualTo("Hello from fixture"));

            ParseSnapshotParityChat system = result.Parity.Chat[2];
            Assert.That(system.Speaker, Is.EqualTo("System"));
            Assert.That(system.Mode, Is.EqualTo("System"));
            Assert.That(system.Message, Is.EqualTo("Welcome to Vana'diel"));

            string json = ParseSnapshot.ToJson(result);
            Assert.That(json.Contains("\"mode\": \"Yell\""), Is.True);
            Assert.That(json.Contains("\"message\": \"Hello from yell\""), Is.True);
        }

        [Test]
        public void SnapshotCollidingEventSeqDoesNotDuplicateParityChat()
        {
            ParseSnapshotResult result = ParseSnapshot.FromChatLines(new[] { SayAliceCollide, SayPoroCollide });

            Assert.That(result.Errors, Is.Empty);
            Assert.That(result.Messages.Count, Is.EqualTo(1));
            Assert.That(result.Parity.Chat.Count, Is.EqualTo(1));
        }

        [Test]
        public void SnapshotChatTimestampsAgreeUtc()
        {
            ParseSnapshotResult result = ParseSnapshot.FromChatLines(new[] { SayAlice });

            Assert.That(result.Messages, Is.Not.Empty);
            Assert.That(result.Chat, Is.Not.Empty);
            Assert.That(result.Messages[0].Chat, Is.Not.Null);
            Assert.That(result.Messages[0].Chat.Timestamp, Is.EqualTo(result.Chat[0].Timestamp));
            Assert.That(result.Messages[0].Chat.Timestamp.EndsWith("Z"), Is.True);
        }
    }
}
