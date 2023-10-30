﻿using Comfort.Common;
using EFT;
using HarmonyLib;
using SPTQuestingBots.BehaviorExtensions;
using SPTQuestingBots.Controllers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace SPTQuestingBots.BotLogic.HiveMind
{
    public enum BotHiveMindSensorType
    {
        Undefined,
        InCombat,
        CanQuest,
        CanSprintToObjective,
        WantsToLoot
    }

    public class BotHiveMindMonitor : MonoBehaviourDelayedUpdate
    {
        internal static Dictionary<BotOwner, BotOwner> botBosses = new Dictionary<BotOwner, BotOwner>();
        internal static Dictionary<BotOwner, List<BotOwner>> botFollowers = new Dictionary<BotOwner, List<BotOwner>>();
        private static Dictionary<BotOwner, bool> botFriendlinessUpdated = new Dictionary<BotOwner, bool>();

        private static Dictionary<BotHiveMindSensorType, BotHiveMindAbstractSensor> sensors = new Dictionary<BotHiveMindSensorType, BotHiveMindAbstractSensor>();

        public BotHiveMindMonitor()
        {
            UpdateInterval = 50;

            sensors.Add(BotHiveMindSensorType.InCombat, new BotHiveMindIsInCombatSensor());
            sensors.Add(BotHiveMindSensorType.CanQuest, new BotHiveMindCanQuestSensor());
            sensors.Add(BotHiveMindSensorType.CanSprintToObjective, new BotHiveMindCanSprintToObjectiveSensor());
            sensors.Add(BotHiveMindSensorType.WantsToLoot, new BotHiveMindWantsToLootSensor());
        }

        public static void Clear()
        {
            botBosses.Clear();
            botFollowers.Clear();
            botFriendlinessUpdated.Clear();

            sensors.Clear();
        }

        private void Update()
        {
            if (!canUpdate())
            {
                return;
            }

            if (LocationController.CurrentLocation == null)
            {
                Clear();
                return;
            }

            updateBosses();
            updateBossFollowers();
            updateBotGroupFriendliness();

            foreach (BotHiveMindAbstractSensor sensor in sensors.Values)
            {
                sensor.Update();
            }
        }

        public static void UpdateValueForBot(BotHiveMindSensorType sensorType, BotOwner bot, bool value)
        {
            throwIfSensorNotRegistred(sensorType);
            sensors[sensorType].UpdateForBot(bot, value);
        }

        public static bool GetValueForBot(BotHiveMindSensorType sensorType, BotOwner bot)
        {
            throwIfSensorNotRegistred(sensorType);
            return sensors[sensorType].CheckForBot(bot);
        }

        public static bool GetValueForBossOfBot(BotHiveMindSensorType sensorType, BotOwner bot)
        {
            throwIfSensorNotRegistred(sensorType);
            return sensors[sensorType].CheckForBossOfBot(bot);
        }

        public static bool GetValueForFollowers(BotHiveMindSensorType sensorType, BotOwner bot)
        {
            throwIfSensorNotRegistred(sensorType);
            return sensors[sensorType].CheckForFollowers(bot);
        }

        public static bool GetValueForGroup(BotHiveMindSensorType sensorType, BotOwner bot)
        {
            throwIfSensorNotRegistred(sensorType);
            return sensors[sensorType].CheckForGroup(bot);
        }

        public static DateTime GetLastLootingTimeForBoss(BotOwner bot)
        {
            throwIfSensorNotRegistred(BotHiveMindSensorType.WantsToLoot);
            BotHiveMindWantsToLootSensor sensor = sensors[BotHiveMindSensorType.WantsToLoot] as BotHiveMindWantsToLootSensor;

            return sensor.GetLastLootingTimeForBoss(bot);
        }

        public static void RegisterBot(BotOwner bot)
        {
            if (bot == null)
            {
                throw new ArgumentNullException("Cannot register a null bot", nameof(bot));
            }

            if (!botBosses.ContainsKey(bot))
            {
                botBosses.Add(bot, null);
            }

            if (!botFollowers.ContainsKey(bot))
            {
                botFollowers.Add(bot, new List<BotOwner>());
            }

            if (!botFriendlinessUpdated.ContainsKey(bot))
            {
                botFriendlinessUpdated.Add(bot, false);
            }

            foreach (BotHiveMindAbstractSensor sensor in sensors.Values)
            {
                sensor.RegisterBot(bot);
            }
        }

        public static bool IsRegistered(BotOwner bot)
        {
            if (bot == null)
            {
                return false;
            }

            return botBosses.ContainsKey(bot);
        }

        public static bool HasBoss(BotOwner bot)
        {
            return botBosses.ContainsKey(bot) && (botBosses[bot] != null);
        }

        public static bool HasFollowers(BotOwner bot)
        {
            return botFollowers.ContainsKey(bot) && (botFollowers[bot]?.Count > 0);
        }

        public static BotOwner GetBoss(BotOwner bot)
        {
            return botBosses.ContainsKey(bot) ? botBosses[bot] : null;
        }

        public static ReadOnlyCollection<BotOwner> GetFollowers(BotOwner bot)
        {
            return botFollowers.ContainsKey(bot) ? new ReadOnlyCollection<BotOwner>(botFollowers[bot]) : new ReadOnlyCollection<BotOwner>(new BotOwner[0]);
        }

        public static ReadOnlyCollection<BotOwner> GetAllGroupMembers(BotOwner bot)
        {
            BotOwner boss = GetBoss(bot) ?? bot;

            BotOwner[] allGroupMembers = GetFollowers(boss)
                .AddItem(boss)
                .Where(b => b.Id != bot.Id)
                .ToArray();

            return new ReadOnlyCollection<BotOwner>(allGroupMembers);
        }

        public static float GetDistanceToBoss(BotOwner bot)
        {
            if (!HasBoss(bot))
            {
                return 0;
            }

            return Vector3.Distance(bot.Position, botBosses[bot].Position);
        }

        public static void AssignTargetEnemyFromGroup(BotOwner bot)
        {
            if (bot.Memory.HaveEnemy || bot.Memory.DangerData.HaveCloseDanger)
            {
                return;
            }

            ReadOnlyCollection<BotOwner> groupMembers = GetAllGroupMembers(bot);
            //Controllers.LoggingController.LogInfo("Group members for " + bot.Profile.Nickname + ": " + string.Join(", ", groupMembers.Select(m => m.Profile.Nickname));

            foreach (BotOwner member in groupMembers)
            {
                if (!member.isActiveAndEnabled || member.IsDead)
                {
                    continue;
                }

                if (!member.Memory.HaveEnemy)
                {
                    continue;
                }

                Controllers.LoggingController.LogInfo(member.Profile.Nickname + " informed " + bot.Profile.Nickname + " about spotted enemy " + bot.Memory.GoalEnemy.Owner.Profile.Nickname);

                PlaceForCheck enemyLocation = new PlaceForCheck(member.Memory.GoalEnemy.GetPositionForSearch(), PlaceForCheckType.danger);
                bot.Memory.DangerData.SetTarget(enemyLocation, member.Memory.GoalEnemy.Owner);

                return;
            }
        }

        public static void MakeBotHateEveryoneOutsideOfItsGroup(BotOwner bot)
        {
            IReadOnlyCollection<BotOwner> groupMembers = BotGenerator.GetSpawnGroupMembers(bot);
            MakeBotHateEveryoneOutsideOfItsGroup(bot, groupMembers);
        }

        public static void MakeBotHateEveryoneOutsideOfItsGroup(BotOwner bot, IEnumerable<BotOwner> allegedGroupMembers)
        {
            string[] actualGroupMemberIds = allegedGroupMembers.Select(m => m.Profile.Id).ToArray();
            
            IEnumerable<BotOwner> allPlayersOutsideGroup = Singleton<IBotGame>.Instance.BotsController.Bots.BotOwners
                .Where(p => !actualGroupMemberIds.Contains(p.Profile.Id));

            //Controllers.LoggingController.LogInfo(bot.Profile.Nickname + "'s group contains: " + string.Join(",", allegedGroupMembers.Select(m => m.Profile.Nickname)));

            foreach (BotOwner player in allPlayersOutsideGroup)
            {
                if (player.BotsGroup.Allies.Contains(bot))
                {
                    Controllers.LoggingController.LogInfo(player.Profile.Nickname + "'s group was initially friendly with " + bot.Profile.Nickname + ". Not anymore..");

                    player.BotsGroup.RemoveAlly(bot);
                    player.BotsGroup.AddEnemy(bot, EBotEnemyCause.initial);
                }

                if (bot.BotsGroup.Allies.Contains(player))
                {
                    Controllers.LoggingController.LogInfo(bot.Profile.Nickname + "'s group was initially friendly with " + player.Profile.Nickname + ". Not anymore..");

                    bot.BotsGroup.RemoveAlly(player);
                    bot.BotsGroup.AddEnemy(player, EBotEnemyCause.initial);
                }
            }

            if (BotQuestController.IsBotAPMC(bot) && !bot.BotsGroup.IsPlayerEnemy(Singleton<GameWorld>.Instance.MainPlayer))
            {
                Controllers.LoggingController.LogInfo(bot.Profile.Nickname + " doesn't like you anymore");

                bot.BotsGroup.AddEnemy(Singleton<GameWorld>.Instance.MainPlayer, EBotEnemyCause.initial);
            }

            //Controllers.LoggingController.LogInfo(bot.Profile.Nickname + "'s group has the following allies: " + string.Join(",", bot.BotsGroup.Allies.Select(a => a.Profile.Nickname)));
            //Controllers.LoggingController.LogInfo(bot.Profile.Nickname + "'s group has the following enemies: " + string.Join(",", bot.BotsGroup.Enemies.Keys.Select(a => a.Profile.Nickname)));
        }

        private static void throwIfSensorNotRegistred(BotHiveMindSensorType sensorType)
        {
            if (!sensors.ContainsKey(sensorType))
            {
                throw new InvalidOperationException("Sensor type " + sensorType.ToString() + " has not been registerd.");
            }
        }

        private void updateBosses()
        {
            foreach (BotOwner bot in botBosses.Keys.ToArray())
            {
                // Need to check if the reference is for a null object, meaning the bot was despawned and disposed
                if (bot == null)
                {
                    continue;
                }

                if (botBosses[bot] == null)
                {
                    botBosses[bot] = bot.BotFollower?.BossToFollow?.Player()?.AIData?.BotOwner;

                    if (botBosses[bot] != null)
                    {
                        addBossFollower(botBosses[bot], bot);
                    }

                    continue;
                }

                if (!botBosses[bot].isActiveAndEnabled || botBosses[bot].IsDead)
                {
                    Controllers.LoggingController.LogInfo("Boss " + botBosses[bot].Profile.Nickname + " is now dead.");

                    if (botFollowers.ContainsKey(botBosses[bot]))
                    {
                        botFollowers.Remove(botBosses[bot]);
                    }

                    botBosses[bot] = null;
                }
            }
        }

        private void addBossFollower(BotOwner boss, BotOwner bot)
        {
            if (boss == null)
            {
                throw new ArgumentNullException("Boss argument cannot be null", nameof(boss));
            }

            if (bot == null)
            {
                throw new ArgumentNullException("Bot argument cannot be null", nameof(bot));
            }

            if (!botFollowers.ContainsKey(boss))
            {
                throw new InvalidOperationException("Boss " + boss.Profile.Nickname + " has not been added to the follower dictionary");
            }

            if (!botFollowers[boss].Contains(bot))
            {
                Controllers.LoggingController.LogInfo("Bot " + bot.Profile.Nickname + " is now a follower for " + boss.Profile.Nickname);

                botFollowers[boss].Add(bot);
            }
        }

        private void updateBossFollowers()
        {
            foreach (BotOwner boss in botFollowers.Keys.ToArray())
            {
                // Need to check if the reference is for a null object, meaning the bot was despawned and disposed
                if (boss == null)
                {
                    continue;
                }

                foreach (BotOwner follower in botFollowers[boss].ToArray())
                {
                    if ((follower == null) || !follower.isActiveAndEnabled || follower.IsDead)
                    {
                        Controllers.LoggingController.LogInfo("Follower " + follower.Profile.Nickname + " for " + boss.Profile.Nickname + " is now dead.");

                        botFollowers[boss].Remove(follower);
                    }
                }
            }
        }

        private void updateBotGroupFriendliness()
        {
            foreach (BotOwner bot in botFriendlinessUpdated.Keys.ToArray())
            {
                // Need to check if the reference is for a null object, meaning the bot was despawned and disposed
                if (bot == null)
                {
                    continue;
                }

                if (botFriendlinessUpdated[bot])
                {
                    continue;
                }

                Objective.BotObjectiveManager objectiveManager = null;
                if (bot?.GetPlayer?.gameObject?.TryGetComponent(out objectiveManager) == false)
                {
                    continue;
                }

                double timeSinceInitialized = objectiveManager?.TimeSinceInitialization ?? 0;

                IReadOnlyCollection<BotOwner> groupMembers = BotGenerator.GetSpawnGroupMembers(bot);
                if ((timeSinceInitialized < 3) && (groupMembers.Count > 0) && (bot.BotsGroup.Allies.Count == 0) && (bot.BotsGroup.Enemies.Count == 0))
                {
                    continue;
                }

                MakeBotHateEveryoneOutsideOfItsGroup(bot, groupMembers);
                botFriendlinessUpdated[bot] = true;
            }
        }
    }
}
