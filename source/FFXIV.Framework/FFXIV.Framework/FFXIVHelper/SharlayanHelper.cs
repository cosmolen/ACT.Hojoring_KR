using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FFXIV.Framework.Common;
using FFXIV.Framework.Globalization;
using Sharlayan;
using Sharlayan.Core;
using Sharlayan.Core.Enums;
using Sharlayan.Models;
using Sharlayan.Models.ReadResults;
using Sharlayan.Models.Structures;

using Newtonsoft.Json;

using ActorItem = Sharlayan.Core.ActorItem;
using EnmityItem = Sharlayan.Core.EnmityItem;
using TargetInfo = Sharlayan.Core.TargetInfo;

namespace FFXIV.Framework.FFXIVHelper
{
    [Flags]
    public enum PartyCompositions
    {
        Unknown = 0,
        LightParty,
        FullPartyT1,
        FullPartyT2
    }

    public class SharlayanHelper
    {
        private static readonly Lazy<SharlayanHelper> LazyInstance = new Lazy<SharlayanHelper>();

        public static SharlayanHelper Instance => LazyInstance.Value;

        public SharlayanHelper()
        {
            ReaderEx.GetActorCallback = id =>
            {
                if (this.ActorDictionary.ContainsKey(id))
                {
                    return this.ActorDictionary[id];
                }

                return null;
            };

            ReaderEx.GetNPCActorCallback = id =>
            {
                if (this.NPCActorDictionary.ContainsKey(id))
                {
                    return this.NPCActorDictionary[id];
                }

                return null;
            };
        }

        private ThreadWorker ffxivSubscriber;
        private static readonly double ProcessSubscribeInterval = 5000;

        private ThreadWorker memorySubscriber;
        private static readonly double MemorySubscribeDefaultInterval = 500;

        public void Start(
            double? memoryScanInterval = null)
        {
            lock (this)
            {
                if (this.ffxivSubscriber == null)
                {
                    this.ffxivSubscriber = new ThreadWorker(
                        this.DetectFFXIVProcess,
                        ProcessSubscribeInterval,
                        "sharlayan Process Subscriber",
                        ThreadPriority.Lowest);

                    Task.Run(() =>
                    {
                        Thread.Sleep((int)ProcessSubscribeInterval / 2);
                        this.ffxivSubscriber.Run();
                    });
                }

                if (this.memorySubscriber == null)
                {
                    this.memorySubscriber = new ThreadWorker(
                        this.ScanMemory,
                        memoryScanInterval ?? MemorySubscribeDefaultInterval,
                        "sharlayan Memory Subscriber",
                        ThreadPriority.BelowNormal);

                    this.memorySubscriber.Run();
                }
            }
        }

        public void End()
        {
            lock (this)
            {
                if (this.ffxivSubscriber != null)
                {
                    this.ffxivSubscriber.Abort();
                    this.ffxivSubscriber = null;
                }

                if (this.memorySubscriber != null)
                {
                    this.memorySubscriber.Abort();
                    this.memorySubscriber = null;
                }
            }
        }

        private void ClearData()
        {
            lock (this.ActorList)
            {
                this.ActorList.Clear();
            }
        }

        private Process currentFFXIVProcess;
        private string currentFFXIVLanguage;

        // for KR region
        public string currentFFXIVversion { get; private set; } = "4.45";

        private void DetectFFXIVProcess()
        {
            var ffxiv = FFXIVPlugin.Instance.Process;
            var ffxivLanguage = string.Empty;

            switch (FFXIVPlugin.Instance.LanguageID)
            {
                case Locales.EN:
                    ffxivLanguage = "English";
                    break;

                case Locales.JA:
                    ffxivLanguage = "Japanese";
                    break;

                case Locales.FR:
                    ffxivLanguage = "French";
                    break;

                case Locales.DE:
                    ffxivLanguage = "German";
                    break;

                default:
                    ffxivLanguage = "English";
                    break;
            }

            if (ffxiv == null)
            {
                return;
            }

            lock (this)
            {
                if (!MemoryHandler.Instance.IsAttached ||
                    this.currentFFXIVProcess != ffxiv ||
                    this.currentFFXIVLanguage != ffxivLanguage)
                {
                    this.currentFFXIVProcess = ffxiv;
                    this.currentFFXIVLanguage = ffxivLanguage;

                    var model = new ProcessModel
                    {
                        Process = ffxiv,
                        IsWin64 = true
                    };

                    MemoryHandler.Instance.SetProcess(
                        model,
                        ffxivLanguage,
                        currentFFXIVversion // for KR region
                        );

                    ReaderEx.ProcessModel = model;

                    this.ClearData();
                }
            }

            this.GarbageCombatantsDictionary();
        }

        private readonly List<ActorItem> ActorList = new List<ActorItem>(512);
        private readonly List<Combatant> ActorPCCombatantList = new List<Combatant>(512);
        private readonly List<Combatant> ActorCombatantList = new List<Combatant>(512);
        private readonly Dictionary<uint, ActorItem> ActorDictionary = new Dictionary<uint, ActorItem>(512);
        private readonly Dictionary<uint, ActorItem> NPCActorDictionary = new Dictionary<uint, ActorItem>(512);

        private readonly Dictionary<uint, Combatant> CombatantsDictionary = new Dictionary<uint, Combatant>(5120);
        private readonly Dictionary<uint, Combatant> NPCCombatantsDictionary = new Dictionary<uint, Combatant>(5120);

        public Func<uint, (int WorldID, string WorldName)> GetWorldInfoCallback { get; set; }

        public ActorItem CurrentPlayer => ReaderEx.CurrentPlayer;

        public List<ActorItem> Actors => this.ActorList.ToList();

        private static readonly object CombatantsLock = new object();

        public List<Combatant> PCCombatants
        {
            get
            {
                lock (CombatantsLock)
                {
                    return this.ActorPCCombatantList.ToList();
                }
            }
        }

        public List<Combatant> Combatants
        {
            get
            {
                lock (CombatantsLock)
                {
                    return this.ActorCombatantList.ToList();
                }
            }
        }

        public bool IsExistsActors { get; private set; } = false;

        public ActorItem GetActor(uint id)
        {
            if (this.ActorDictionary.ContainsKey(id))
            {
                return this.ActorDictionary[id];
            }

            return null;
        }

        public ActorItem GetNPCActor(uint id)
        {
            if (this.NPCActorDictionary.ContainsKey(id))
            {
                return this.NPCActorDictionary[id];
            }

            return null;
        }

        public TargetInfo TargetInfo { get; private set; }

        private readonly Dictionary<uint, EnmityEntry> EnmityDictionary = new Dictionary<uint, EnmityEntry>(128);

        public List<EnmityEntry> EnmityList
        {
            get
            {
                lock (this.EnmityDictionary)
                {
                    return this.EnmityDictionary.Values.OrderByDescending(x => x.Enmity).ToList();
                }
            }
        }

        public bool IsFirstEnmityMe { get; private set; }

        private readonly List<ActorItem> PartyMemberList = new List<ActorItem>(8);

        public List<ActorItem> PartyMembers => this.PartyMemberList.ToList();

        public int PartyMemberCount { get; private set; }

        public PartyCompositions PartyComposition { get; private set; } = PartyCompositions.Unknown;

        public string CurrentZoneName { get; private set; } = string.Empty;

        public DateTime ZoneChangedTimestamp { get; private set; } = DateTime.MinValue;

        public bool IsScanning { get; set; } = false;

        private void ScanMemory()
        {
            if (!MemoryHandler.Instance.IsAttached ||
                FFXIVPlugin.Instance.Process == null)
            {
                Thread.Sleep((int)ProcessSubscribeInterval);
                return;
            }

            try
            {
                if (this.IsScanning)
                {
                    return;
                }

                this.IsScanning = true;
                doScan();
            }
            finally
            {
                this.IsScanning = false;
            }

            void doScan()
            {
                var currentZoneName = FFXIVPlugin.Instance.GetCurrentZoneName();
                if (this.CurrentZoneName != currentZoneName)
                {
                    this.CurrentZoneName = currentZoneName;
                    this.ZoneChangedTimestamp = DateTime.Now;

                    this.IsExistsActors = false;
                    this.ActorList.Clear();
                    this.ActorDictionary.Clear();
                    this.NPCActorDictionary.Clear();
                    this.CombatantsDictionary.Clear();
                    this.NPCCombatantsDictionary.Clear();
                }

                if (this.IsSkipActor)
                {
                    if (this.ActorList.Any())
                    {
                        this.IsExistsActors = false;
                        this.ActorList.Clear();
                        this.ActorDictionary.Clear();
                        this.NPCActorDictionary.Clear();
                        this.CombatantsDictionary.Clear();
                        this.NPCCombatantsDictionary.Clear();
                    }
                }
                else
                {
                    this.GetActorsSimple();
                }

                if (this.IsSkipTarget)
                {
                    this.TargetInfo = null;
                }
                else
                {
                    this.GetTargetInfo();
                }

                if (this.IsSkipParty)
                {
                    if (this.PartyMemberList.Any())
                    {
                        this.PartyMemberList.Clear();
                    }
                }
                else
                {
                    this.GetPartyInfo();
                }
            }

            if (this.IsSkips.All(x => x))
            {
                Thread.Sleep((int)ProcessSubscribeInterval);
            }
        }

        public bool IsScanNPC { get; set; } = false;

        public bool IsSkipActor { get; set; } = false;

        public bool IsSkipTarget { get; set; } = false;

        public bool IsSkipEnmity { get; set; } = false;

        public bool IsSkipParty { get; set; } = false;

        private bool[] IsSkips => new[]
        {
            this.IsSkipActor,
            this.IsSkipTarget,
            this.IsSkipParty,
        };

        private void GetActorsSimple()
        {
            var actors = ReaderEx.GetActorSimple(this.IsScanNPC);

            if (!actors.Any())
            {
                this.IsExistsActors = false;
                this.ActorList.Clear();
                this.ActorDictionary.Clear();
                this.NPCActorDictionary.Clear();
                this.CombatantsDictionary.Clear();
                this.NPCCombatantsDictionary.Clear();

                lock (CombatantsLock)
                {
                    this.ActorCombatantList.Clear();
                    this.ActorPCCombatantList.Clear();
                }

                return;
            }

            this.IsExistsActors = false;

            this.ActorList.Clear();
            this.ActorList.AddRange(actors);

            lock (CombatantsLock)
            {
                this.ActorDictionary.Clear();
                this.NPCActorDictionary.Clear();

                this.ActorCombatantList.Clear();
                this.ActorPCCombatantList.Clear();
                foreach (var actor in this.ActorList)
                {
                    var combatatnt = this.ToCombatant(actor);
                    this.ActorCombatantList.Add(combatatnt);

                    if (actor.IsNPC())
                    {
                        this.NPCActorDictionary[actor.GetKey()] = actor;
                    }
                    else
                    {
                        this.ActorPCCombatantList.Add(combatatnt);
                        this.ActorDictionary[actor.GetKey()] = actor;
                    }
                }
            }

            this.IsExistsActors = this.ActorList.Count > 0;
        }

        private void GetTargetInfo()
        {
            var result = ReaderEx.GetTargetInfoSimple(this.IsSkipEnmity);

            this.TargetInfo = result.TargetsFound ?
                result.TargetInfo :
                null;

            this.GetEnmity();
        }

        private void GetEnmity()
        {
            if (this.IsSkipEnmity)
            {
                if (this.EnmityDictionary.Count > 0)
                {
                    lock (this.EnmityDictionary)
                    {
                        this.EnmityDictionary.Clear();
                    }
                }

                return;
            }

            lock (this.EnmityDictionary)
            {
                if (this.TargetInfo == null ||
                    !this.TargetInfo.EnmityItems.Any())
                {
                    this.EnmityDictionary.Clear();
                    return;
                }

                var currents = this.EnmityDictionary.Values.ToArray();
                foreach (var current in currents)
                {
                    if (!this.TargetInfo.EnmityItems.Any(x => x.ID == current.ID))
                    {
                        this.EnmityDictionary.Remove(current.ID);
                    }
                }

                var max = this.TargetInfo.EnmityItems.Max(x => x.Enmity);
                var player = this.CurrentPlayer;
                var first = default(EnmityEntry);

                foreach (var source in this.TargetInfo.EnmityItems)
                {
                    Thread.Yield();

                    var existing = false;
                    var enmity = default(EnmityEntry);

                    if (this.EnmityDictionary.ContainsKey(source.ID))
                    {
                        existing = true;
                        enmity = this.EnmityDictionary[source.ID];
                    }
                    else
                    {
                        existing = false;
                        enmity = new EnmityEntry() { ID = source.ID };
                    }

                    enmity.Enmity = source.Enmity;
                    enmity.HateRate = (int)(((double)enmity.Enmity / (double)max) * 100d);
                    enmity.IsMe = enmity.ID == player?.ID;

                    if (first == null)
                    {
                        first = enmity;
                    }

                    if (!existing)
                    {
                        var actor = this.ActorDictionary.ContainsKey(enmity.ID) ?
                            this.ActorDictionary[enmity.ID] :
                            null;

                        enmity.Name = actor?.Name;
                        enmity.OwnerID = actor?.OwnerID ?? 0;
                        enmity.Job = (byte)(actor?.Job ?? 0);

                        if (string.IsNullOrEmpty(enmity.Name))
                        {
                            enmity.Name = Combatant.UnknownName;
                        }

                        this.EnmityDictionary[enmity.ID] = enmity;
                    }
                }

                if (first != null)
                {
                    this.IsFirstEnmityMe = first.IsMe;
                }
            }
        }

        private DateTime partyListTimestamp = DateTime.MinValue;

        public DateTime PartyListChangedTimestamp { get; private set; } = DateTime.MinValue;

        private void GetPartyInfo()
        {
            var newPartyList = new List<ActorItem>(8);

            if (!this.IsExistsActors ||
                string.IsNullOrEmpty(this.CurrentZoneName))
            {
                if (ReaderEx.CurrentPlayer != null)
                {
                    newPartyList.Add(ReaderEx.CurrentPlayer);
                }

                this.PartyMemberList.Clear();
                this.PartyMemberList.AddRange(newPartyList);
                this.PartyMemberCount = newPartyList.Count();
                this.PartyComposition = PartyCompositions.Unknown;

                return;
            }

            var now = DateTime.Now;
            if ((now - this.partyListTimestamp).TotalSeconds <= 0.5)
            {
                return;
            }

            this.partyListTimestamp = now;

            var result = ReaderEx.GetPartyMemberIDs();

            foreach (var id in result)
            {
                var actor = this.GetActor(id);
                if (actor != null)
                {
                    newPartyList.Add(actor);
                }
            }

            if (!newPartyList.Any() &&
                ReaderEx.CurrentPlayer != null)
            {
                newPartyList.Add(ReaderEx.CurrentPlayer);
            }

            if (this.PartyMemberList.Count != newPartyList.Count ||
                newPartyList.Except(this.PartyMemberList).Any() ||
                this.PartyMemberList.Except(newPartyList).Any())
            {
                this.PartyListChangedTimestamp = DateTime.Now;
            }

            this.PartyMemberList.Clear();
            this.PartyMemberList.AddRange(newPartyList);
            this.PartyMemberCount = newPartyList.Count();

            var composition = PartyCompositions.Unknown;

            if (this.PartyMemberCount == 4)
            {
                this.PartyComposition = PartyCompositions.LightParty;
            }
            else
            {
                if (this.PartyMemberCount == 8)
                {
                    var tanks = this.PartyMemberList.Count(x => x.GetJobInfo().Role == Roles.Tank);
                    switch (tanks)
                    {
                        case 1:
                            this.PartyComposition = PartyCompositions.FullPartyT1;
                            break;

                        case 2:
                            this.PartyComposition = PartyCompositions.FullPartyT2;
                            break;
                    }
                }
            }

            this.PartyComposition = composition;
        }

        public List<Combatant> ToCombatantList(
            IEnumerable<ActorItem> actors)
        {
            var combatantList = new List<Combatant>(actors.Count());

            foreach (var actor in actors)
            {
                var combatant = this.TryGetOrNewCombatant(actor);
                combatantList.Add(combatant);
                Thread.Yield();
            }

            return combatantList;
        }

        public Combatant ToCombatant(
            ActorItem actor)
        {
            if (actor == null)
            {
                return null;
            }

            return this.TryGetOrNewCombatant(actor);
        }

        private Combatant TryGetOrNewCombatant(
            ActorItem actor)
        {
            if (actor == null)
            {
                return null;
            }

            var combatant = default(Combatant);
            var dictionary = !actor.IsNPC() ?
                this.CombatantsDictionary :
                this.NPCCombatantsDictionary;
            var key = actor.GetKey();

            if (dictionary.ContainsKey(key))
            {
                combatant = dictionary[key];
                this.CreateCombatant(actor, combatant);
            }
            else
            {
                combatant = this.CreateCombatant(actor);
                dictionary[key] = combatant;
            }

            return combatant;
        }

        private Combatant CreateCombatant(
            ActorItem actor,
            Combatant current = null)
        {
            var c = current ?? new Combatant();

            c.ActorItem = actor;
            c.ID = (int)actor.ID;
            c.ObjectType = actor.Type;
            c.Name = actor.Name;
            c.Level = actor.Level;
            c.Job = (byte)actor.Job;
            c.TargetID = (int)actor.TargetID;
            c.OwnerID = (int)actor.OwnerID;

            c.EffectiveDistance = actor.Distance;
            c.IsAvailableEffectiveDictance = true;

            c.Heading = actor.Heading;
            c.PosX = (float)actor.X;
            c.PosY = (float)actor.Y;
            c.PosZ = (float)actor.Z;

            c.CurrentHP = actor.HPCurrent;
            c.MaxHP = actor.HPMax;
            c.CurrentMP = actor.MPCurrent;
            c.MaxMP = actor.MPMax;
            c.CurrentTP = (short)actor.TPCurrent;
            c.MaxTP = (short)actor.TPMax;

            c.IsCasting = actor.IsCasting;
            c.CastTargetID = (int)actor.CastingTargetID;
            c.CastBuffID = actor.CastingID;
            c.CastDurationCurrent = actor.CastingProgress;
            c.CastDurationMax = actor.CastingTime;

            this.SetTargetOfTarget(c);

            FFXIVPlugin.Instance.SetSkillName(c);
            c.SetName(actor.Name);

            var worldInfo = GetWorldInfoCallback?.Invoke((uint)c.ID);
            if (worldInfo.HasValue)
            {
                c.WorldID = worldInfo.Value.WorldID;
                c.WorldName = worldInfo.Value.WorldName ?? string.Empty;
            }

            c.Timestamp = DateTime.Now;

            return c;
        }

        public void SetTargetOfTarget(
            Combatant player)
        {
            if (!player.IsPlayer ||
                player.TargetID == 0)
            {
                return;
            }

            if (this.TargetInfo != null &&
                this.TargetInfo.CurrentTarget != null)
            {
                player.TargetOfTargetID = (int)this.TargetInfo.CurrentTarget?.TargetID;
            }
            else
            {
                player.TargetOfTargetID = 0;
            }
        }

        private void GarbageCombatantsDictionary()
        {
            var threshold = CommonHelper.Random.Next(10 * 60, 15 * 60);
            var now = DateTime.Now;

            var array = new[]
            {
                this.CombatantsDictionary,
                this.NPCCombatantsDictionary
            };

            try
            {
                if (this.IsScanning)
                {
                    return;
                }

                this.IsScanning = true;

                foreach (var dictionary in array)
                {
                    dictionary
                        .Where(x => (now - x.Value.Timestamp).TotalSeconds > threshold)
                        .ToArray()
                        .Walk(x =>
                        {
                            this.CombatantsDictionary.Remove(x.Key);
                            Thread.Yield();
                        });
                }
            }
            finally
            {
                this.IsScanning = false;
            }
        }
    }

    public static class ActorItemExtensions
    {
        public static List<Combatant> ToCombatantList(
            this IEnumerable<ActorItem> actors)
            => SharlayanHelper.Instance.ToCombatantList(actors);

        public static uint GetKey(
            this ActorItem actor)
            => actor.IsNPC() ? actor.NPCID2 : actor.ID;

        public static bool IsNPC(
            this ActorItem actor)
            => IsNPC(actor?.Type);

        public static bool IsNPC(
            this Combatant actor)
            => IsNPC(actor?.ObjectType);

        private static bool IsNPC(
            Actor.Type? actorType)
        {
            if (!actorType.HasValue)
            {
                return true;
            }

            switch (actorType)
            {
                case Actor.Type.NPC:
                case Actor.Type.Aetheryte:
                case Actor.Type.EventObject:
                    return true;

                default:
                    return false;
            }
        }

        public static JobIDs GetJobID(
            this ActorItem actor)
        {
            var id = actor.JobID;
            var jobEnum = JobIDs.Unknown;

            if (Enum.IsDefined(typeof(JobIDs), (int)id))
            {
                jobEnum = (JobIDs)Enum.ToObject(typeof(JobIDs), (int)id);
            }

            return jobEnum;
        }

        public static Job GetJobInfo(
            this ActorItem actor)
        {
            var jobEnum = actor.GetJobID();
            return Jobs.Find(jobEnum);
        }
    }

    public static class ReaderEx
    {
        public static ActorItem CurrentPlayer { get; private set; }

        public static Combatant CurrentPlayerCombatant { get; private set; }

        public static ProcessModel ProcessModel { get; set; }

        public static Func<uint, ActorItem> GetActorCallback { get; set; }

        public static Func<uint, ActorItem> GetNPCActorCallback { get; set; }

        private static readonly Lazy<Func<StructuresContainer>> LazyGetStructuresDelegate = new Lazy<Func<StructuresContainer>>(() =>
        {
            var property = MemoryHandler.Instance.GetType().GetProperty(
                "Structures",
                BindingFlags.NonPublic | BindingFlags.Instance);

            return (Func<StructuresContainer>)Delegate.CreateDelegate(
                typeof(Func<StructuresContainer>),
                MemoryHandler.Instance,
                property.GetMethod);
        });

        // for KR region
        private static StructuresContainer GetCustomStructures()
        {
            Dictionary<string, string> structuresJson = new Dictionary<string, string>();
            structuresJson.Add("4.4", "{\"ActorItem\":{\"SourceSize\":9200,\"DefaultBaseOffset\":0,\"DefaultStatOffset\":0,\"DefaultStatusEffectOffset\":6152,\"Name\":48,\"ID\":116,\"NPCID1\":120,\"NPCID2\":128,\"OwnerID\":132,\"Type\":140,\"GatheringStatus\":145,\"Distance\":146,\"TargetFlags\":148,\"GatheringInvisible\":148,\"X\":160,\"Z\":164,\"Y\":168,\"Heading\":176,\"EventObjectType\":190,\"HitBoxRadius\":192,\"Fate\":228,\"IsGM\":396,\"ClaimedByID\":440,\"TargetType\":464,\"EntityCount\":474,\"TargetID\":5824,\"ModelID\":5928,\"ActionStatus\":5946,\"HPCurrent\":5968,\"HPMax\":5972,\"MPCurrent\":5976,\"MPMax\":5980,\"TPCurrent\":5984,\"GPCurrent\":5986,\"GPMax\":5988,\"CPCurrent\":5990,\"CPMax\":5992,\"Title\":5998,\"Job\":6024,\"Level\":6026,\"Icon\":6028,\"Status\":6034,\"GrandCompany\":6041,\"GrandCompanyRank\":6041,\"DifficultyRank\":6045,\"AgroFlags\":6049,\"CombatFlags\":6068,\"IsCasting1\":6896,\"IsCasting2\":6897,\"CastingID\":6900,\"CastingTargetID\":6912,\"CastingProgress\":6948,\"CastingTime\":6952},\"ChatLogPointers\":{\"OffsetArrayStart\":52,\"OffsetArrayPos\":60,\"OffsetArrayEnd\":68,\"LogStart\":76,\"LogNext\":84,\"LogEnd\":92},\"CurrentPlayer\":{\"SourceSize\":620,\"JobID\":102,\"PGL\":106,\"GLD\":108,\"MRD\":110,\"ARC\":112,\"LNC\":114,\"THM\":116,\"CNJ\":118,\"CPT\":120,\"BSM\":122,\"ARM\":124,\"GSM\":126,\"LTW\":128,\"WVR\":130,\"ALC\":132,\"CUL\":134,\"MIN\":136,\"BTN\":138,\"FSH\":140,\"ACN\":142,\"ROG\":144,\"MCH\":146,\"DRK\":148,\"AST\":150,\"SAM\":152,\"RDM\":154,\"PGL_CurrentEXP\":160,\"GLD_CurrentEXP\":164,\"MRD_CurrentEXP\":168,\"ARC_CurrentEXP\":172,\"LNC_CurrentEXP\":176,\"THM_CurrentEXP\":180,\"CNJ_CurrentEXP\":184,\"CPT_CurrentEXP\":188,\"BSM_CurrentEXP\":192,\"ARM_CurrentEXP\":196,\"GSM_CurrentEXP\":200,\"LTW_CurrentEXP\":204,\"WVR_CurrentEXP\":208,\"ALC_CurrentEXP\":212,\"CUL_CurrentEXP\":216,\"MIN_CurrentEXP\":220,\"BTN_CurrentEXP\":224,\"FSH_CurrentEXP\":228,\"ACN_CurrentEXP\":232,\"ROG_CurrentEXP\":236,\"MCH_CurrentEXP\":240,\"DRK_CurrentEXP\":244,\"AST_CurrentEXP\":248,\"SAM_CurrentEXP\":252,\"RDM_CurrentEXP\":256,\"BaseStrength\":296,\"BaseDexterity\":300,\"BaseVitality\":304,\"BaseIntelligence\":308,\"BaseMind\":312,\"BasePiety\":316,\"Strength\":324,\"Dexterity\":328,\"Vitality\":332,\"Intelligence\":336,\"Mind\":340,\"Piety\":344,\"HPMax\":348,\"MPMax\":352,\"TPMax\":356,\"GPMax\":360,\"CPMax\":364,\"Tenacity\":396,\"AttackPower\":400,\"Defense\":404,\"DirectHit\":408,\"MagicDefense\":416,\"CriticalHitRate\":428,\"AttackMagicPotency\":452,\"HealingMagicPotency\":456,\"Determination\":496,\"SkillSpeed\":500,\"SpellSpeed\":504,\"FireResistance\":0,\"IceResistance\":0,\"WindResistance\":0,\"EarthResistance\":0,\"LightningResistance\":0,\"WaterResistance\":0,\"SlashingResistance\":0,\"PiercingResistance\":0,\"BluntResistance\":0,\"Craftmanship\":600,\"Control\":604,\"Gathering\":608,\"Perception\":612},\"EnmityItem\":{\"SourceSize\":72,\"Name\":0,\"ID\":64,\"Enmity\":68},\"HotBarItem\":{\"ContainerSize\":3456,\"ItemSize\":216,\"Name\":0,\"KeyBinds\":103,\"ID\":150},\"InventoryContainer\":{\"Amount\":8},\"InventoryItem\":{\"Slot\":4,\"ID\":8,\"Amount\":12,\"SB\":16,\"Durability\":18,\"IsHQ\":20,\"GlamourID\":48},\"PartyMember\":{\"SourceSize\":912,\"DefaultStatusEffectOffset\":152,\"X\":0,\"Y\":4,\"Z\":8,\"ID\":24,\"HPCurrent\":36,\"HPMax\":40,\"MPCurrent\":44,\"MPMax\":46,\"TPCurrent\":48,\"Name\":52,\"Job\":117,\"Level\":118},\"RecastItem\":{\"ContainerSize\":704,\"ItemSize\":44,\"Category\":0,\"Type\":8,\"ID\":12,\"Icon\":16,\"CoolDownPercent\":20,\"ActionProc\":21,\"IsAvailable\":24,\"RemainingCost\":28,\"Amount\":32,\"InRange\":36},\"StatusItem\":{\"SourceSize\":12,\"StatusID\":0,\"Stacks\":2,\"Duration\":4,\"CasterID\":8},\"TargetInfo\":{\"Size\":9200,\"SourceSize\":200,\"Current\":40,\"MouseOver\":58,\"Previous\":160,\"Focus\":136,\"CurrentID\":192}}");
            structuresJson.Add("4.45", "{\"ActorItem\":{\"SourceSize\":9200,\"DefaultBaseOffset\":0,\"DefaultStatOffset\":0,\"DefaultStatusEffectOffset\":6152,\"Name\":48,\"ID\":116,\"NPCID1\":120,\"NPCID2\":128,\"OwnerID\":132,\"Type\":140,\"GatheringStatus\":145,\"Distance\":146,\"TargetFlags\":148,\"GatheringInvisible\":148,\"X\":160,\"Z\":164,\"Y\":168,\"Heading\":176,\"EventObjectType\":190,\"HitBoxRadius\":192,\"Fate\":228,\"IsGM\":396,\"ClaimedByID\":440,\"TargetType\":464,\"EntityCount\":474,\"TargetID\":5824,\"ModelID\":5928,\"ActionStatus\":5946,\"HPCurrent\":5968,\"HPMax\":5972,\"MPCurrent\":5976,\"MPMax\":5980,\"TPCurrent\":5984,\"GPCurrent\":5986,\"GPMax\":5988,\"CPCurrent\":5990,\"CPMax\":5992,\"Title\":5998,\"Job\":6024,\"Level\":6026,\"Icon\":6028,\"Status\":6034,\"GrandCompany\":6041,\"GrandCompanyRank\":6041,\"DifficultyRank\":6045,\"AgroFlags\":6049,\"CombatFlags\":6068,\"IsCasting1\":6896,\"IsCasting2\":6897,\"CastingID\":6900,\"CastingTargetID\":6912,\"CastingProgress\":6948,\"CastingTime\":6952},\"ChatLogPointers\":{\"OffsetArrayStart\":52,\"OffsetArrayPos\":60,\"OffsetArrayEnd\":68,\"LogStart\":76,\"LogNext\":84,\"LogEnd\":92},\"CurrentPlayer\":{\"SourceSize\":620,\"JobID\":102,\"PGL\":106,\"GLD\":108,\"MRD\":110,\"ARC\":112,\"LNC\":114,\"THM\":116,\"CNJ\":118,\"CPT\":120,\"BSM\":122,\"ARM\":124,\"GSM\":126,\"LTW\":128,\"WVR\":130,\"ALC\":132,\"CUL\":134,\"MIN\":136,\"BTN\":138,\"FSH\":140,\"ACN\":142,\"ROG\":144,\"MCH\":146,\"DRK\":148,\"AST\":150,\"SAM\":152,\"RDM\":154,\"PGL_CurrentEXP\":160,\"GLD_CurrentEXP\":164,\"MRD_CurrentEXP\":168,\"ARC_CurrentEXP\":172,\"LNC_CurrentEXP\":176,\"THM_CurrentEXP\":180,\"CNJ_CurrentEXP\":184,\"CPT_CurrentEXP\":188,\"BSM_CurrentEXP\":192,\"ARM_CurrentEXP\":196,\"GSM_CurrentEXP\":200,\"LTW_CurrentEXP\":204,\"WVR_CurrentEXP\":208,\"ALC_CurrentEXP\":212,\"CUL_CurrentEXP\":216,\"MIN_CurrentEXP\":220,\"BTN_CurrentEXP\":224,\"FSH_CurrentEXP\":228,\"ACN_CurrentEXP\":232,\"ROG_CurrentEXP\":236,\"MCH_CurrentEXP\":240,\"DRK_CurrentEXP\":244,\"AST_CurrentEXP\":248,\"SAM_CurrentEXP\":252,\"RDM_CurrentEXP\":256,\"BaseStrength\":296,\"BaseDexterity\":300,\"BaseVitality\":304,\"BaseIntelligence\":308,\"BaseMind\":312,\"BasePiety\":316,\"Strength\":324,\"Dexterity\":328,\"Vitality\":332,\"Intelligence\":336,\"Mind\":340,\"Piety\":344,\"HPMax\":348,\"MPMax\":352,\"TPMax\":356,\"GPMax\":360,\"CPMax\":364,\"Tenacity\":396,\"AttackPower\":400,\"Defense\":404,\"DirectHit\":408,\"MagicDefense\":416,\"CriticalHitRate\":428,\"AttackMagicPotency\":452,\"HealingMagicPotency\":456,\"Determination\":496,\"SkillSpeed\":500,\"SpellSpeed\":504,\"FireResistance\":0,\"IceResistance\":0,\"WindResistance\":0,\"EarthResistance\":0,\"LightningResistance\":0,\"WaterResistance\":0,\"SlashingResistance\":0,\"PiercingResistance\":0,\"BluntResistance\":0,\"Craftmanship\":600,\"Control\":604,\"Gathering\":608,\"Perception\":612},\"EnmityItem\":{\"SourceSize\":72,\"Name\":0,\"ID\":64,\"Enmity\":68},\"HotBarItem\":{\"ContainerSize\":3456,\"ItemSize\":216,\"Name\":0,\"KeyBinds\":103,\"ID\":150},\"InventoryContainer\":{\"Amount\":8},\"InventoryItem\":{\"Slot\":4,\"ID\":8,\"Amount\":12,\"SB\":16,\"Durability\":18,\"IsHQ\":20,\"GlamourID\":48},\"PartyMember\":{\"SourceSize\":912,\"DefaultStatusEffectOffset\":152,\"X\":0,\"Y\":4,\"Z\":8,\"ID\":24,\"HPCurrent\":36,\"HPMax\":40,\"MPCurrent\":44,\"MPMax\":46,\"TPCurrent\":48,\"Name\":52,\"Job\":117,\"Level\":118},\"RecastItem\":{\"ContainerSize\":704,\"ItemSize\":44,\"Category\":0,\"Type\":8,\"ID\":12,\"Icon\":16,\"CoolDownPercent\":20,\"ActionProc\":21,\"IsAvailable\":24,\"RemainingCost\":28,\"Amount\":32,\"InRange\":36},\"StatusItem\":{\"SourceSize\":12,\"StatusID\":0,\"Stacks\":2,\"Duration\":4,\"CasterID\":8},\"TargetInfo\":{\"Size\":9200,\"SourceSize\":200,\"Current\":40,\"MouseOver\":58,\"Previous\":160,\"Focus\":136,\"CurrentID\":192}}");
            structuresJson.Add("4.5", "{\"ActorItem\":{\"SourceSize\":9200,\"DefaultBaseOffset\":0,\"DefaultStatOffset\":0,\"DefaultStatusEffectOffset\":6168,\"Name\":48,\"ID\":116,\"NPCID1\":120,\"NPCID2\":128,\"OwnerID\":132,\"Type\":140,\"GatheringStatus\":145,\"Distance\":146,\"TargetFlags\":148,\"GatheringInvisible\":148,\"X\":160,\"Z\":164,\"Y\":168,\"Heading\":176,\"EventObjectType\":190,\"HitBoxRadius\":192,\"Fate\":228,\"IsGM\":396,\"TargetType\":464,\"EntityCount\":474,\"ClaimedByID\":5832,\"TargetID\":5956,\"ModelID\":6008,\"ActionStatus\":6030,\"HPCurrent\":5976,\"HPMax\":5980,\"MPCurrent\":5984,\"MPMax\":5988,\"TPCurrent\":5992,\"GPCurrent\":5994,\"GPMax\":5996,\"CPCurrent\":5998,\"CPMax\":6000,\"Title\":6002,\"Job\":6032,\"Level\":6034,\"Icon\":6036,\"Status\":6042,\"GrandCompany\":6049,\"GrandCompanyRank\":6049,\"DifficultyRank\":6053,\"AgroFlags\":6057,\"CombatFlags\":6077,\"IsCasting1\":6912,\"IsCasting2\":6913,\"CastingID\":6916,\"CastingTargetID\":6928,\"CastingProgress\":6964,\"CastingTime\":6968},\"ChatLogPointers\":{\"OffsetArrayStart\":52,\"OffsetArrayPos\":60,\"OffsetArrayEnd\":68,\"LogStart\":76,\"LogNext\":84,\"LogEnd\":92},\"CurrentPlayer\":{\"SourceSize\":620,\"JobID\":102,\"PGL\":106,\"GLD\":108,\"MRD\":110,\"ARC\":112,\"LNC\":114,\"THM\":116,\"CNJ\":118,\"CPT\":120,\"BSM\":122,\"ARM\":124,\"GSM\":126,\"LTW\":128,\"WVR\":130,\"ALC\":132,\"CUL\":134,\"MIN\":136,\"BTN\":138,\"FSH\":140,\"ACN\":142,\"ROG\":144,\"MCH\":146,\"DRK\":148,\"AST\":150,\"SAM\":152,\"RDM\":154,\"PGL_CurrentEXP\":160,\"GLD_CurrentEXP\":164,\"MRD_CurrentEXP\":168,\"ARC_CurrentEXP\":172,\"LNC_CurrentEXP\":176,\"THM_CurrentEXP\":180,\"CNJ_CurrentEXP\":184,\"CPT_CurrentEXP\":188,\"BSM_CurrentEXP\":192,\"ARM_CurrentEXP\":196,\"GSM_CurrentEXP\":200,\"LTW_CurrentEXP\":204,\"WVR_CurrentEXP\":208,\"ALC_CurrentEXP\":212,\"CUL_CurrentEXP\":216,\"MIN_CurrentEXP\":220,\"BTN_CurrentEXP\":224,\"FSH_CurrentEXP\":228,\"ACN_CurrentEXP\":232,\"ROG_CurrentEXP\":236,\"MCH_CurrentEXP\":240,\"DRK_CurrentEXP\":244,\"AST_CurrentEXP\":248,\"SAM_CurrentEXP\":252,\"RDM_CurrentEXP\":256,\"BaseStrength\":296,\"BaseDexterity\":300,\"BaseVitality\":304,\"BaseIntelligence\":308,\"BaseMind\":312,\"BasePiety\":316,\"Strength\":324,\"Dexterity\":328,\"Vitality\":332,\"Intelligence\":336,\"Mind\":340,\"Piety\":344,\"HPMax\":348,\"MPMax\":352,\"TPMax\":356,\"GPMax\":360,\"CPMax\":364,\"Tenacity\":396,\"AttackPower\":400,\"Defense\":404,\"DirectHit\":408,\"MagicDefense\":416,\"CriticalHitRate\":428,\"AttackMagicPotency\":452,\"HealingMagicPotency\":456,\"Determination\":496,\"SkillSpeed\":500,\"SpellSpeed\":504,\"FireResistance\":0,\"IceResistance\":0,\"WindResistance\":0,\"EarthResistance\":0,\"LightningResistance\":0,\"WaterResistance\":0,\"SlashingResistance\":0,\"PiercingResistance\":0,\"BluntResistance\":0,\"Craftmanship\":600,\"Control\":604,\"Gathering\":608,\"Perception\":612},\"EnmityItem\":{\"SourceSize\":72,\"Name\":0,\"ID\":64,\"Enmity\":68},\"HotBarItem\":{\"ContainerSize\":3456,\"ItemSize\":216,\"Name\":0,\"KeyBinds\":103,\"ID\":150},\"InventoryContainer\":{\"Amount\":8},\"InventoryItem\":{\"Slot\":4,\"ID\":8,\"Amount\":12,\"SB\":16,\"Durability\":18,\"IsHQ\":20,\"GlamourID\":48},\"PartyMember\":{\"SourceSize\":912,\"DefaultStatusEffectOffset\":152,\"X\":0,\"Y\":4,\"Z\":8,\"ID\":24,\"HPCurrent\":36,\"HPMax\":40,\"MPCurrent\":44,\"MPMax\":46,\"TPCurrent\":48,\"Name\":52,\"Job\":117,\"Level\":118},\"RecastItem\":{\"ContainerSize\":704,\"ItemSize\":44,\"Category\":0,\"Type\":8,\"ID\":12,\"Icon\":16,\"CoolDownPercent\":20,\"ActionProc\":21,\"IsAvailable\":24,\"RemainingCost\":28,\"Amount\":32,\"InRange\":36},\"StatusItem\":{\"SourceSize\":12,\"StatusID\":0,\"Stacks\":2,\"Duration\":4,\"CasterID\":8},\"TargetInfo\":{\"Size\":9200,\"SourceSize\":200,\"Current\":40,\"MouseOver\":58,\"Previous\":160,\"Focus\":136,\"CurrentID\":192}}");

            var resolved = JsonConvert.DeserializeObject<StructuresContainer>(
                structuresJson[SharlayanHelper.Instance.currentFFXIVversion],
                new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    DefaultValueHandling = DefaultValueHandling.Populate
                }
            );

            return resolved;
        }

#if false
        private static readonly Lazy<Func<byte[], bool, ActorItem, ActorItem>> LazyResolveActorFromBytesDelegate = new Lazy<Func<byte[], bool, ActorItem, ActorItem>>(() =>
        {
            var asm = MemoryHandler.Instance.GetType().Assembly;
            var type = asm.GetType("Sharlayan.Utilities.ActorItemResolver");

            var method = type.GetMethod(
                "ResolveActorFromBytes",
                BindingFlags.Static | BindingFlags.Public);

            return (Func<byte[], bool, ActorItem, ActorItem>)Delegate.CreateDelegate(
                typeof(Func<byte[], bool, ActorItem, ActorItem>),
                null,
                method);
        });

        private static ActorItem InvokeActorItemResolver(
            byte[] source,
            bool isCurrentUser = false,
            ActorItem entry = null)
            => LazyResolveActorFromBytesDelegate.Value.Invoke(
                source,
                isCurrentUser,
                entry);

        private static readonly Lazy<Func<byte[], ActorItem, PartyMember>> LazyResolvePartyMemberFromBytesDelegate = new Lazy<Func<byte[], ActorItem, PartyMember>>(() =>
        {
            var asm = MemoryHandler.Instance.GetType().Assembly;
            var type = asm.GetType("Sharlayan.Utilities.PartyMemberResolver");

            var method = type.GetMethod(
                "ResolvePartyMemberFromBytes",
                BindingFlags.Static | BindingFlags.Public);

            return (Func<byte[], ActorItem, PartyMember>)Delegate.CreateDelegate(
                typeof(Func<byte[], ActorItem, PartyMember>),
                null,
                method);
        });

        private static PartyMember InvokePartyMemberResolver(
            byte[] source,
            ActorItem entry = null)
            => LazyResolvePartyMemberFromBytesDelegate.Value.Invoke(
                source,
                entry);
#endif

        public static List<ActorItem> GetActorSimple(
            bool isScanNPC = false)
        {
            var result = new List<ActorItem>(256);

            if (!Reader.CanGetActors() || !MemoryHandler.Instance.IsAttached)
            {
                return result;
            }

            var isWin64 = ProcessModel?.IsWin64 ?? true;

            var targetAddress = IntPtr.Zero;
            var endianSize = isWin64 ? 8 : 4;

            //var structures = LazyGetStructuresDelegate.Value.Invoke();
            var structures = GetCustomStructures();
            var sourceSize = structures.ActorItem.SourceSize;
            var limit = structures.ActorItem.EntityCount;
            var characterAddressMap = MemoryHandler.Instance.GetByteArray(Scanner.Instance.Locations[Signatures.CharacterMapKey], endianSize * limit);
            var uniqueAddresses = new Dictionary<IntPtr, IntPtr>();
            var firstAddress = IntPtr.Zero;

            var firstTime = true;
            for (var i = 0; i < limit; i++)
            {
                Thread.Yield();

                var characterAddress = isWin64 ?
                    new IntPtr(BitConverter.TryToInt64(characterAddressMap, i * endianSize)) :
                    new IntPtr(BitConverter.TryToInt32(characterAddressMap, i * endianSize));

                if (characterAddress == IntPtr.Zero)
                {
                    continue;
                }

                if (firstTime)
                {
                    firstAddress = characterAddress;
                    firstTime = false;
                }

                uniqueAddresses[characterAddress] = characterAddress;
            }

            foreach (var kvp in uniqueAddresses)
            {
                Thread.Yield();

                var characterAddress = new IntPtr(kvp.Value.ToInt64());
                var source = MemoryHandler.Instance.GetByteArray(characterAddress, sourceSize);

                var ID = BitConverter.TryToUInt32(source, structures.ActorItem.ID);
                var NPCID2 = BitConverter.TryToUInt32(source, structures.ActorItem.NPCID2);
                var Type = (Actor.Type)source[structures.ActorItem.Type];

                var existing = default(ActorItem);
                switch (Type)
                {
                    case Actor.Type.Aetheryte:
                    case Actor.Type.EventObject:
                    case Actor.Type.NPC:
                        if (!isScanNPC)
                        {
                            continue;
                        }

                        existing = GetNPCActorCallback?.Invoke(NPCID2);
                        break;

                    default:
                        existing = GetActorCallback?.Invoke(ID);
                        break;
                }

                var isFirstEntry = kvp.Value.ToInt64() == firstAddress.ToInt64();
                var entry = ResolveActorFromBytes(structures, source, isFirstEntry, existing);

                if (isFirstEntry)
                {
                    CurrentPlayer = entry;
                    CurrentPlayerCombatant = SharlayanHelper.Instance.ToCombatant(entry);

                    if (targetAddress.ToInt64() > 0)
                    {
                        var targetInfoSource = MemoryHandler.Instance.GetByteArray(targetAddress, 128);
                        entry.TargetID = (int)BitConverter.TryToInt32(targetInfoSource, structures.ActorItem.ID);
                    }
                }

                result.Add(entry);
            }

            return result;
        }

        public static uint[] GetPartyMemberIDs()
        {
            var result = new List<uint>(8);

            if (!Reader.CanGetPartyMembers() || !MemoryHandler.Instance.IsAttached)
            {
                return result.ToArray();
            }

            //var structures = LazyGetStructuresDelegate.Value.Invoke();
            var structures = GetCustomStructures();

            var PartyInfoMap = (IntPtr)Scanner.Instance.Locations[Signatures.PartyMapKey];
            var PartyCountMap = Scanner.Instance.Locations[Signatures.PartyCountKey];

            var partyCount = MemoryHandler.Instance.GetByte(PartyCountMap);
            var sourceSize = structures.PartyMember.SourceSize;

            if (partyCount > 1 && partyCount < 9)
            {
                for (uint i = 0; i < partyCount; i++)
                {
                    var address = PartyInfoMap.ToInt64() + i * (uint)sourceSize;
                    var source = MemoryHandler.Instance.GetByteArray(new IntPtr(address), sourceSize);
                    var ID = BitConverter.TryToUInt32(source, structures.PartyMember.ID);

                    result.Add(ID);
                    Thread.Yield();
                }
            }

            return result.ToArray();
        }

#if false
        public static List<PartyMember> GetPartyMemberSimple()
        {
            var result = new List<PartyMember>(8);

            if (!Reader.CanGetPartyMembers() || !MemoryHandler.Instance.IsAttached)
            {
                return result;
            }

            var structures = LazyGetStructuresDelegate.Value.Invoke();

            var PartyInfoMap = (IntPtr)Scanner.Instance.Locations[Signatures.PartyMapKey];
            var PartyCountMap = Scanner.Instance.Locations[Signatures.PartyCountKey];

            var partyCount = MemoryHandler.Instance.GetByte(PartyCountMap);
            var sourceSize = structures.PartyMember.SourceSize;

            if (partyCount > 1 && partyCount < 9)
            {
                for (uint i = 0; i < partyCount; i++)
                {
                    var address = PartyInfoMap.ToInt64() + i * (uint)sourceSize;
                    var source = MemoryHandler.Instance.GetByteArray(new IntPtr(address), sourceSize);

                    var ID = BitConverter.TryToUInt32(source, structures.ActorItem.ID);
                    var NPCID2 = BitConverter.TryToUInt32(source, structures.ActorItem.NPCID2);
                    var Type = (Actor.Type)source[structures.ActorItem.Type];

                    var existing = default(ActorItem);
                    switch (Type)
                    {
                        case Actor.Type.Aetheryte:
                        case Actor.Type.EventObject:
                        case Actor.Type.NPC:
                            existing = GetNPCActorCallback?.Invoke(NPCID2);
                            break;

                        default:
                            existing = GetActorCallback?.Invoke(ID);
                            break;
                    }

                    var entry = InvokePartyMemberResolver(source, existing);

                    result.Add(entry);
                    Thread.Yield();
                }
            }

            if (partyCount <= 1)
            {
                if (CurrentPlayer != null)
                {
                    result.Add(InvokePartyMemberResolver(Array.Empty<byte>(), CurrentPlayer));
                }
            }

            return result;
        }
#endif

        public static ActorItem ResolveActorFromBytes(StructuresContainer structures, byte[] source, bool isCurrentUser = false, ActorItem entry = null)
        {
            entry = entry ?? new ActorItem();

            var defaultBaseOffset = structures.ActorItem.DefaultBaseOffset;
            var defaultStatOffset = structures.ActorItem.DefaultStatOffset;
            var defaultStatusEffectOffset = structures.ActorItem.DefaultStatusEffectOffset;

            entry.MapTerritory = 0;
            entry.MapIndex = 0;
            entry.MapID = 0;
            entry.TargetID = 0;
            entry.Name = MemoryHandler.Instance.GetStringFromBytes(source, structures.ActorItem.Name);
            entry.ID = BitConverter.TryToUInt32(source, structures.ActorItem.ID);
            entry.UUID = string.IsNullOrEmpty(entry.UUID)
                             ? Guid.NewGuid().ToString()
                             : entry.UUID;
            entry.NPCID1 = BitConverter.TryToUInt32(source, structures.ActorItem.NPCID1);
            entry.NPCID2 = BitConverter.TryToUInt32(source, structures.ActorItem.NPCID2);
            entry.OwnerID = BitConverter.TryToUInt32(source, structures.ActorItem.OwnerID);
            entry.TypeID = source[structures.ActorItem.Type];
            entry.Type = (Actor.Type)entry.TypeID;

            entry.TargetTypeID = source[structures.ActorItem.TargetType];
            entry.TargetType = (Actor.TargetType)entry.TargetTypeID;

            entry.GatheringStatus = source[structures.ActorItem.GatheringStatus];
            entry.Distance = source[structures.ActorItem.Distance];

            entry.X = BitConverter.TryToSingle(source, structures.ActorItem.X + defaultBaseOffset);
            entry.Z = BitConverter.TryToSingle(source, structures.ActorItem.Z + defaultBaseOffset);
            entry.Y = BitConverter.TryToSingle(source, structures.ActorItem.Y + defaultBaseOffset);
            entry.Heading = BitConverter.TryToSingle(source, structures.ActorItem.Heading + defaultBaseOffset);
            entry.HitBoxRadius = BitConverter.TryToSingle(source, structures.ActorItem.HitBoxRadius + defaultBaseOffset);
            entry.Fate = BitConverter.TryToUInt32(source, structures.ActorItem.Fate + defaultBaseOffset); // ??
            entry.TargetFlags = source[structures.ActorItem.TargetFlags]; // ??
            entry.GatheringInvisible = source[structures.ActorItem.GatheringInvisible]; // ??
            entry.ModelID = BitConverter.TryToUInt32(source, structures.ActorItem.ModelID);
            entry.ActionStatusID = source[structures.ActorItem.ActionStatus];
            entry.ActionStatus = (Actor.ActionStatus)entry.ActionStatusID;

            // 0x17D - 0 = Green name, 4 = non-agro (yellow name)
            entry.IsGM = BitConverter.TryToBoolean(source, structures.ActorItem.IsGM); // ?
            entry.IconID = source[structures.ActorItem.Icon];
            entry.Icon = (Actor.Icon)entry.IconID;

            entry.StatusID = source[structures.ActorItem.Status];
            entry.Status = (Actor.Status)entry.StatusID;

            entry.ClaimedByID = BitConverter.TryToUInt32(source, structures.ActorItem.ClaimedByID);
            var targetID = BitConverter.TryToUInt32(source, structures.ActorItem.TargetID);
            var pcTargetID = targetID;

            entry.JobID = source[structures.ActorItem.Job + defaultStatOffset];
            entry.Job = (Actor.Job)entry.JobID;

            entry.Level = source[structures.ActorItem.Level + defaultStatOffset];
            entry.GrandCompany = source[structures.ActorItem.GrandCompany + defaultStatOffset];
            entry.GrandCompanyRank = source[structures.ActorItem.GrandCompanyRank + defaultStatOffset];
            entry.Title = source[structures.ActorItem.Title + defaultStatOffset];
            entry.HPCurrent = BitConverter.TryToInt32(source, structures.ActorItem.HPCurrent + defaultStatOffset);
            entry.HPMax = BitConverter.TryToInt32(source, structures.ActorItem.HPMax + defaultStatOffset);
            entry.MPCurrent = BitConverter.TryToInt32(source, structures.ActorItem.MPCurrent + defaultStatOffset);
            entry.MPMax = BitConverter.TryToInt32(source, structures.ActorItem.MPMax + defaultStatOffset);
            entry.TPCurrent = BitConverter.TryToInt16(source, structures.ActorItem.TPCurrent + defaultStatOffset);
            entry.TPMax = 1000;
            entry.GPCurrent = BitConverter.TryToInt16(source, structures.ActorItem.GPCurrent + defaultStatOffset);
            entry.GPMax = BitConverter.TryToInt16(source, structures.ActorItem.GPMax + defaultStatOffset);
            entry.CPCurrent = BitConverter.TryToInt16(source, structures.ActorItem.CPCurrent + defaultStatOffset);
            entry.CPMax = BitConverter.TryToInt16(source, structures.ActorItem.CPMax + defaultStatOffset);

            // entry.Race = source[0x2578]; // ??
            // entry.Sex = (Actor.Sex) source[0x2579]; //?
            entry.AgroFlags = source[structures.ActorItem.AgroFlags];
            entry.CombatFlags = source[structures.ActorItem.CombatFlags];
            entry.DifficultyRank = source[structures.ActorItem.DifficultyRank];
            entry.CastingID = BitConverter.TryToInt16(source, structures.ActorItem.CastingID); // 0x2C94);
            entry.CastingTargetID = BitConverter.TryToUInt32(source, structures.ActorItem.CastingTargetID); // 0x2CA0);
            entry.CastingProgress = BitConverter.TryToSingle(source, structures.ActorItem.CastingProgress); // 0x2CC4);
            entry.CastingTime = BitConverter.TryToSingle(source, structures.ActorItem.CastingTime); // 0x2DA8);
            entry.Coordinate = new Coordinate(entry.X, entry.Z, entry.Y);
            if (targetID > 0)
            {
                entry.TargetID = (int)targetID;
            }
            else
            {
                if (pcTargetID > 0)
                {
                    entry.TargetID = (int)pcTargetID;
                }
            }

            if (entry.CastingTargetID == 3758096384)
            {
                entry.CastingTargetID = 0;
            }

            entry.MapIndex = 0;

            // handle empty names
            if (string.IsNullOrEmpty(entry.Name))
            {
                if (entry.Type == Actor.Type.EventObject)
                {
                    entry.Name = $"{nameof(entry.EventObjectTypeID)}: {entry.EventObjectTypeID}";
                }
                else
                {
                    entry.Name = $"{nameof(entry.TypeID)}: {entry.TypeID}";
                }
            }

            CleanXPValue(ref entry);

            return entry;
        }

        private static void CleanXPValue(ref ActorItem entity)
        {
            if (entity.HPCurrent < 0 || entity.HPMax < 0)
            {
                entity.HPCurrent = 1;
                entity.HPMax = 1;
            }

            if (entity.HPCurrent > entity.HPMax)
            {
                if (entity.HPMax == 0)
                {
                    entity.HPCurrent = 1;
                    entity.HPMax = 1;
                }
                else
                {
                    entity.HPCurrent = entity.HPMax;
                }
            }

            if (entity.MPCurrent < 0 || entity.MPMax < 0)
            {
                entity.MPCurrent = 1;
                entity.MPMax = 1;
            }

            if (entity.MPCurrent > entity.MPMax)
            {
                if (entity.MPMax == 0)
                {
                    entity.MPCurrent = 1;
                    entity.MPMax = 1;
                }
                else
                {
                    entity.MPCurrent = entity.MPMax;
                }
            }

            if (entity.GPCurrent < 0 || entity.GPMax < 0)
            {
                entity.GPCurrent = 1;
                entity.GPMax = 1;
            }

            if (entity.GPCurrent > entity.GPMax)
            {
                if (entity.GPMax == 0)
                {
                    entity.GPCurrent = 1;
                    entity.GPMax = 1;
                }
                else
                {
                    entity.GPCurrent = entity.GPMax;
                }
            }

            if (entity.CPCurrent < 0 || entity.CPMax < 0)
            {
                entity.CPCurrent = 1;
                entity.CPMax = 1;
            }

            if (entity.CPCurrent > entity.CPMax)
            {
                if (entity.CPMax == 0)
                {
                    entity.CPCurrent = 1;
                    entity.CPMax = 1;
                }
                else
                {
                    entity.CPCurrent = entity.CPMax;
                }
            }
        }

        public static TargetResult GetTargetInfoSimple(
            bool isSkipEnmity = false)
        {
            var result = new TargetResult();

            if (!Reader.CanGetTargetInfo() || !MemoryHandler.Instance.IsAttached)
            {
                return result;
            }

            //var structures = LazyGetStructuresDelegate.Value.Invoke();
            var structures = GetCustomStructures();
            var targetAddress = (IntPtr)Scanner.Instance.Locations[Signatures.TargetKey];

            if (targetAddress.ToInt64() > 0)
            {
                byte[] targetInfoSource = MemoryHandler.Instance.GetByteArray(targetAddress, structures.TargetInfo.SourceSize);

                var currentTarget = MemoryHandler.Instance.GetPlatformIntFromBytes(targetInfoSource, structures.TargetInfo.Current);
                var mouseOverTarget = MemoryHandler.Instance.GetPlatformIntFromBytes(targetInfoSource, structures.TargetInfo.MouseOver);
                var focusTarget = MemoryHandler.Instance.GetPlatformIntFromBytes(targetInfoSource, structures.TargetInfo.Focus);
                var previousTarget = MemoryHandler.Instance.GetPlatformIntFromBytes(targetInfoSource, structures.TargetInfo.Previous);

                var currentTargetID = BitConverter.TryToUInt32(targetInfoSource, structures.TargetInfo.CurrentID);

                if (currentTarget > 0)
                {
                    ActorItem entry = GetTargetActorItemFromSource(structures, currentTarget);
                    currentTargetID = entry.ID;
                    if (entry.IsValid)
                    {
                        result.TargetsFound = true;
                        result.TargetInfo.CurrentTarget = entry;
                    }
                }

                if (mouseOverTarget > 0)
                {
                    ActorItem entry = GetTargetActorItemFromSource(structures, mouseOverTarget);
                    if (entry.IsValid)
                    {
                        result.TargetsFound = true;
                        result.TargetInfo.MouseOverTarget = entry;
                    }
                }

                if (focusTarget > 0)
                {
                    ActorItem entry = GetTargetActorItemFromSource(structures, focusTarget);
                    if (entry.IsValid)
                    {
                        result.TargetsFound = true;
                        result.TargetInfo.FocusTarget = entry;
                    }
                }

                if (previousTarget > 0)
                {
                    ActorItem entry = GetTargetActorItemFromSource(structures, previousTarget);
                    if (entry.IsValid)
                    {
                        result.TargetsFound = true;
                        result.TargetInfo.PreviousTarget = entry;
                    }
                }

                if (currentTargetID > 0)
                {
                    result.TargetsFound = true;
                    result.TargetInfo.CurrentTargetID = currentTargetID;
                }
            }

            if (isSkipEnmity)
            {
                return result;
            }

            if (result.TargetInfo.CurrentTargetID > 0)
            {
                if (Reader.CanGetEnmityEntities())
                {
                    const int EnmityLimit = 16;

                    var enmityCount = MemoryHandler.Instance.GetInt16(Scanner.Instance.Locations[Signatures.EnmityCountKey]);
                    var enmityStructure = (IntPtr)Scanner.Instance.Locations[Signatures.EnmityMapKey];

                    if (enmityCount > EnmityLimit)
                    {
                        enmityCount = EnmityLimit;
                    }

                    if (enmityCount > 0 && enmityStructure.ToInt64() > 0)
                    {
                        var enmitySourceSize = structures.EnmityItem.SourceSize;
                        for (uint i = 0; i < enmityCount; i++)
                        {
                            Thread.Yield();

                            var address = new IntPtr(enmityStructure.ToInt64() + (i * enmitySourceSize));
                            var enmityEntry = new EnmityItem
                            {
                                ID = (uint)MemoryHandler.Instance.GetPlatformInt(address, structures.EnmityItem.ID),
                                Name = MemoryHandler.Instance.GetString(address + structures.EnmityItem.Name),
                                Enmity = MemoryHandler.Instance.GetUInt32(address + structures.EnmityItem.Enmity)
                            };

                            if (enmityEntry.ID <= 0)
                            {
                                continue;
                            }

                            result.TargetInfo.EnmityItems.Add(enmityEntry);
                        }
                    }
                }
            }

            return result;
        }

        private static ActorItem GetTargetActorItemFromSource(StructuresContainer structures, long address)
        {
            var targetAddress = new IntPtr(address);

            byte[] source = MemoryHandler.Instance.GetByteArray(targetAddress, structures.TargetInfo.Size);
            ActorItem entry = ResolveActorFromBytes(structures, source);

            return entry;
        }
    }

    internal static class BitConverter
    {
        public static bool TryToBoolean(byte[] value, int index)
        {
            try
            {
                return System.BitConverter.ToBoolean(value, index);
            }
            catch (Exception)
            {
                return default;
            }
        }

        public static char TryToChar(byte[] value, int index)
        {
            try
            {
                return System.BitConverter.ToChar(value, index);
            }
            catch (Exception)
            {
                return default;
            }
        }

        public static double TryToDouble(byte[] value, int index)
        {
            try
            {
                return System.BitConverter.ToDouble(value, index);
            }
            catch (Exception)
            {
                return default;
            }
        }

        public static long TryToDoubleToInt64Bits(double value)
        {
            try
            {
                return System.BitConverter.DoubleToInt64Bits(value);
            }
            catch (Exception)
            {
                return default;
            }
        }

        public static short TryToInt16(byte[] value, int index)
        {
            try
            {
                return System.BitConverter.ToInt16(value, index);
            }
            catch (Exception)
            {
                return default;
            }
        }

        public static int TryToInt32(byte[] value, int index)
        {
            try
            {
                return System.BitConverter.ToInt32(value, index);
            }
            catch (Exception)
            {
                return default;
            }
        }

        public static long TryToInt64(byte[] value, int index)
        {
            try
            {
                return System.BitConverter.ToInt64(value, index);
            }
            catch (Exception)
            {
                return default;
            }
        }

        public static double TryToInt64BitsToDouble(long value)
        {
            try
            {
                return System.BitConverter.Int64BitsToDouble(value);
            }
            catch (Exception)
            {
                return default;
            }
        }

        public static float TryToSingle(byte[] value, int index)
        {
            try
            {
                return System.BitConverter.ToSingle(value, index);
            }
            catch (Exception)
            {
                return default;
            }
        }

        public static string TryToString(byte[] value, int index)
        {
            try
            {
                return System.BitConverter.ToString(value, index);
            }
            catch (Exception)
            {
                return default;
            }
        }

        public static ushort TryToUInt16(byte[] value, int index)
        {
            try
            {
                return System.BitConverter.ToUInt16(value, index);
            }
            catch (Exception)
            {
                return default;
            }
        }

        public static uint TryToUInt32(byte[] value, int index)
        {
            try
            {
                return System.BitConverter.ToUInt32(value, index);
            }
            catch (Exception)
            {
                return default;
            }
        }

        public static ulong TryToUInt64(byte[] value, int index)
        {
            try
            {
                return System.BitConverter.ToUInt64(value, index);
            }
            catch (Exception)
            {
                return default;
            }
        }
    }
}
