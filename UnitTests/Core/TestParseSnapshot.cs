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
    }
}
