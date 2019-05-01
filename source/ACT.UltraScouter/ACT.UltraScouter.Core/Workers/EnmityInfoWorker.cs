using System;
using System.Collections.Generic;
using System.Threading;
using ACT.UltraScouter.Config;
using ACT.UltraScouter.Models;
using ACT.UltraScouter.Models.Enmity;
using ACT.UltraScouter.ViewModels;
using FFXIV.Framework.Bridge;
using FFXIV.Framework.FFXIVHelper;
using Sharlayan.Core.Enums;

namespace ACT.UltraScouter.Workers
{
    public class EnmityInfoWorker :
        TargetInfoWorker
    {
        #region Singleton

        private static EnmityInfoWorker instance;

        public static new EnmityInfoWorker Instance => instance;

        public static new void Initialize() => instance = new EnmityInfoWorker();

        public static new void Free() => instance = null;

        private EnmityInfoWorker()
        {
        }

        #endregion Singleton

        /// <summary>
        /// サブオーバーレイである
        /// </summary>
        protected override bool IsSubOverlay => true;

        public override TargetInfoModel Model => TargetInfoModel.Instance;

        protected override void GetCombatant()
        {
            SharlayanHelper.Instance.IsSkipEnmity = !Settings.Instance.Enmity.Visible;

            if (!Settings.Instance.Enmity.Visible)
            {
                this.ClearCurrentEnmity();
                return;
            }

            var targetInfo = this.TargetInfo;

            if (targetInfo == null)
            {
                this.ClearCurrentEnmity();
                return;
            }

            if (!Settings.Instance.Enmity.IsDesignMode)
            {
                if (targetInfo.ObjectType == Actor.Type.Monster)
                {
                    var enmityEntryList = SharlayanHelper.Instance.EnmityList;
                    this.RefreshCurrentEnmityModelList(enmityEntryList);
                }
                else
                {
                    this.ClearCurrentEnmity();
                }
            }
        }

        private void RefreshCurrentEnmityModelList(
            IEnumerable<EnmityEntry> enmityEntryList)
        {
            lock (this.CurrentEnmityModelList)
            {
                this.CurrentEnmityModelList.Clear();

                if (enmityEntryList == null)
                {
                    return;
                }

                var config = Settings.Instance.Enmity;
                var pcNameStyle = ConfigBridge.Instance.PCNameStyle;

                var count = 0;
                foreach (var entry in enmityEntryList)
                {
                    if (entry.ID == 0)
                    {
                        continue;
                    }

                    count++;

                    if (count > config.MaxCountOfDisplay)
                    {
                        break;
                    }

                    var name = entry.Name;
                    if (string.IsNullOrEmpty(name))
                    {
                        name = Combatant.UnknownName;
                    }
                    else
                    {
                        name = config.IsSelfDisplayYou && entry.IsMe ?
                            "YOU" :
                            Combatant.NameToInitial(entry.Name, pcNameStyle);
                    }

                    var model = new EnmityModel();
                    model.Index = count;
                    model.ID = entry.ID;
                    model.Name = name;
                    model.JobID = (JobIDs)entry.Job;
                    model.Enmity = (double)entry.Enmity;
                    model.HateRate = entry.HateRate / 100f;
                    model.IsMe = entry.IsMe;
                    model.IsPet = entry.IsPet;
                    this.CurrentEnmityModelList.Add(model);

                    Thread.Yield();
                }
            }
        }

        private void ClearCurrentEnmity()
        {
            if (this.CurrentEnmityModelList.Count > 0)
            {
                lock (this.CurrentEnmityModelList)
                {
                    this.CurrentEnmityModelList.Clear();
                }
            }
        }

        private readonly List<EnmityModel> CurrentEnmityModelList = new List<EnmityModel>(32);

        protected override NameViewModel NameVM => null;

        protected override HPViewModel HpVM => null;

        protected override HPBarViewModel HpBarVM => null;

        protected override ActionViewModel ActionVM => null;

        protected override DistanceViewModel DistanceVM => null;

        protected override FFLogsViewModel FFLogsVM => null;

        protected override EnmityViewModel EnmityVM =>
            this.enmityVM ?? (this.enmityVM = new EnmityViewModel(Settings.Instance.Enmity, this.Model));

        protected override bool IsAllViewOff =>
            !FFXIVPlugin.Instance.IsFFXIVActive ||
            !Settings.Instance.Enmity.Visible;

        public override Combatant TargetInfo => TargetInfoWorker.Instance.TargetInfo;

        public override Combatant TargetInfoClone => TargetInfoWorker.Instance.TargetInfoClone;

        private DateTime enmityTimestamp = DateTime.MinValue;

        protected override void RefreshEnmityView(Combatant targetInfo)
        {
            if (this.EnmityView == null)
            {
                return;
            }

            if (!this.EnmityView.ViewModel.OverlayVisible)
            {
                return;
            }

            var now = DateTime.Now;
            if ((now - this.enmityTimestamp).TotalMilliseconds <= 100d)
            {
                return;
            }

            this.enmityTimestamp = now;

            EnmityModel.CreateBrushes();

            if (targetInfo == null)
            {
                this.Model.RefreshEnmityList(null);
                return;
            }

            lock (this.CurrentEnmityModelList)
            {
                this.Model.RefreshEnmityList(this.CurrentEnmityModelList);
            }
        }
    }
}
