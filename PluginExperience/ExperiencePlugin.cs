﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Data;
using System.Text;
using System.Drawing;


namespace WaywardGamers.KParser.Plugin
{
    public class ExperiencePlugin : BasePluginControl
    {
        public override string TabName
        {
            get { return "Experience"; }
        }

        public override void Reset()
        {
            richTextBox.Clear();
        }

        protected override bool FilterOnDatabaseChanging(DatabaseWatchEventArgs e, out KPDatabaseDataSet datasetToUse)
        {
            if (e.DatasetChanges != null)
            {
                if (e.DatasetChanges.Battles.Any(b => (b.Killed == true) || (b.EndTime != MagicNumbers.MinSQLDateTime)))
                {
                    datasetToUse = e.Dataset;
                    return true;
                }
            }

            datasetToUse = null;
            return false;
        }

        protected override void ProcessData(KPDatabaseDataSet dataSet)
        {
            richTextBox.Clear();
            StringBuilder sb1 = new StringBuilder();
            StringBuilder sb2 = new StringBuilder();

            var completedFights = dataSet.Battles.Where(b =>
                ((b.Killed == true) || (b.EndTime != MagicNumbers.MinSQLDateTime)) &&
                (b.EndTime != b.StartTime));
            int totalFights = completedFights.Count();

            if (totalFights > 0)
            {
                DateTime startTime;
                DateTime endTime;
                TimeSpan partyDuration;
                Double totalFightsLength = 0;
                TimeSpan minTime = TimeSpan.FromSeconds(1);

                int totalXP = 0;
                double xpPerHour = 0;
                double xpPerMinute = 0;
                double xpPerFight = 0;

                double avgFightLength;
                double timePerFight;

                int[] chainXPTotals = new int[11];
                int[] chainCounts = new int[11];
                int maxChain = 0;

                int chainNum;

                foreach (var fight in completedFights)
                {
                    totalFightsLength += fight.FightLength().TotalSeconds;

                    chainNum = fight.ExperienceChain;

                    if (chainNum > maxChain)
                        maxChain = chainNum;

                    if (chainNum < 10)
                    {
                        chainCounts[chainNum]++;
                        chainXPTotals[chainNum] += fight.ExperiencePoints;
                    }
                    else
                    {
                        chainCounts[10]++;
                        chainXPTotals[10] += fight.ExperiencePoints;
                    }

                    totalXP += fight.ExperiencePoints;
                }


                startTime = completedFights.First(b => b.Killed == true).StartTime;
                endTime = completedFights.Last(b => b.Killed == true).EndTime;
                partyDuration = endTime - startTime;

                if (partyDuration > minTime)
                {
                    double totalXPDouble = (double) totalXP;
                    xpPerHour = totalXPDouble / partyDuration.TotalHours;
                    xpPerMinute = totalXPDouble / partyDuration.TotalMinutes;
                    xpPerFight = totalXPDouble / totalFights;
                }

                avgFightLength = totalFightsLength / totalFights;
                timePerFight = partyDuration.TotalSeconds / totalFights;


                sb1.AppendFormat("Total Experience : {0}\n", totalXP);
                sb1.AppendFormat("Number of Fights : {0}\n", totalFights);
                sb1.AppendFormat("Start Time       : {0}\n", startTime.ToLongTimeString());
                sb1.AppendFormat("End Time         : {0}\n", endTime.ToLongTimeString());
                sb1.AppendFormat("Party Duration   : {0}:{1}:{2}\n",
                    partyDuration.Hours, partyDuration.Minutes, partyDuration.Seconds);
                sb1.AppendFormat("XP/Hour          : {0:F2}\n", xpPerHour);
                sb1.AppendFormat("XP/Minute        : {0:F2}\n", xpPerMinute);
                sb1.AppendFormat("XP/Fight         : {0:F2}\n", xpPerFight);
                sb1.AppendFormat("Avg Fight Length : {0:F2} seconds\n", avgFightLength);
                sb1.AppendFormat("Avg Time/Fight   : {0:F2} seconds\n", timePerFight);
                sb1.Append("\n\n");


                sb2.Append("Chain   Count   Total XP   Avg XP\n");

                for (int i = 0; i < 10; i++)
                {
                    if (chainCounts[i] > 0)
                        sb2.AppendFormat("{0,-6} {1,6} {2,10} {3,8:F2}\n", i, chainCounts[i], chainXPTotals[i],
                            (double)chainXPTotals[i] / chainCounts[i]);
                }

                if (chainCounts[10] > 0)
                {
                    sb2.AppendFormat("{0,-6}  {1,6}  {2,10}  {3,8:F2}\n", "10+", chainCounts[10], chainXPTotals[10],
                        (double)chainXPTotals[10] / chainCounts[10]);
                }

                sb2.Append("\n");
                sb2.AppendFormat("Highest Chain:  {0}\n\n", maxChain);


                // Dump all the constructed text above into the window.
                AppendBoldText("Experience Rates\n", Color.Black);
                AppendNormalText(sb1.ToString());

                AppendBoldText("Experience Chains\n", Color.Black);
                AppendNormalText(sb2.ToString());
            }
        }
    }
}
