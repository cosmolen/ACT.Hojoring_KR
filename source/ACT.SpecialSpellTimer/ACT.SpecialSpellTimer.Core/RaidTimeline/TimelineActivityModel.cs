using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Serialization;
using FFXIV.Framework.Common;
using FFXIV.Framework.Extensions;

namespace ACT.SpecialSpellTimer.RaidTimeline
{
    [Serializable]
    [XmlType(TypeName = "a")]
    public class TimelineActivityModel :
        TimelineBase,
        ISynchronizable,
        IStylable
    {
        [XmlIgnore]
        public override TimelineElementTypes TimelineType => TimelineElementTypes.Activity;

        #region Children

        public override IList<TimelineBase> Children => this.statements;

        private List<TimelineBase> statements = new List<TimelineBase>();

        [XmlIgnore]
        public IReadOnlyList<TimelineBase> Statements => this.statements;

        [XmlElement(ElementName = "v-notice")]
        public TimelineVisualNoticeModel[] VisualNoticeStatements
        {
            get => this.Statements
                .Where(x => x.TimelineType == TimelineElementTypes.VisualNotice)
                .Cast<TimelineVisualNoticeModel>()
                .ToArray();

            set => this.AddRange(value);
        }

        [XmlElement(ElementName = "i-notice")]
        public TimelineImageNoticeModel[] ImageNoticeStatements
        {
            get => this.Statements
                .Where(x => x.TimelineType == TimelineElementTypes.ImageNotice)
                .Cast<TimelineImageNoticeModel>()
                .ToArray();

            set => this.AddRange(value);
        }

        /// <summary>
        /// 条件式
        /// </summary>
        /// <remarks>
        /// 構文上複数定義できるが最初の定義しか使用しない
        /// </remarks>
        [XmlElement(ElementName = "expressions")]
        public TimelineExpressionsModel[] ExpressionsStatements
        {
            get => this.Statements
                .Where(x => x.TimelineType == TimelineElementTypes.Expressions)
                .Cast<TimelineExpressionsModel>()
                .ToArray();

            set => this.AddRange(value);
        }

        [XmlIgnore]
        public bool IsExpressionAvailable =>
            this.ExpressionsStatements.Any(x => x.Enabled.GetValueOrDefault());

        public void SetExpressions()
        {
            var expressions = this.ExpressionsStatements.FirstOrDefault(x =>
                x.Enabled.GetValueOrDefault());

            if (expressions != null)
            {
                lock (TimelineExpressionsModel.ExpressionLocker)
                {
                    expressions.Set();
                }
            }
        }

        public void Add(TimelineBase timeline)
        {
            if (timeline.TimelineType == TimelineElementTypes.VisualNotice ||
                timeline.TimelineType == TimelineElementTypes.ImageNotice ||
                timeline.TimelineType == TimelineElementTypes.Expressions)
            {
                timeline.Parent = this;
                this.statements.Add(timeline);
            }
        }

        public void AddRange(IEnumerable<TimelineBase> timelines)
        {
            if (timelines != null)
            {
                foreach (var tl in timelines)
                {
                    this.Add(tl);
                }
            }
        }

        #endregion Children

        private TimeSpan time = TimeSpan.Zero;

        [XmlIgnore]
        public TimeSpan Time
        {
            get => this.time;
            set
            {
                if (this.SetProperty(ref this.time, value))
                {
                    this.RefreshProgress();
                }
            }
        }

        [XmlAttribute(AttributeName = "time")]
        public string TimeText
        {
            get => this.time.TotalSeconds.ToString("000");
            set => this.SetProperty(ref this.time, TimeSpanExtensions.FromTLString(value));
        }

        private string text = null;

        [XmlAttribute(AttributeName = "text")]
        public string Text
        {
            get => this.text;
            set => this.SetProperty(ref this.text, value?.Replace("\\n", Environment.NewLine));
        }

        /// <summary>
        /// 正規表現置換後のText
        /// </summary>
        /// <remarks>ただしActivityでは置換を使用しない</remarks>
        [XmlIgnore]
        public string TextReplaced
        {
            get => this.text;
            set { }
        }

        private string syncKeyword = null;

        [XmlAttribute(AttributeName = "sync")]
        public string SyncKeyword
        {
            get => this.syncKeyword;
            set
            {
                if (this.SetProperty(ref this.syncKeyword, value))
                {
                    this.SyncKeywordReplaced = null;
                }
            }
        }

        private string syncKeywordReplaced = null;

        [XmlIgnore]
        public string SyncKeywordReplaced
        {
            get => this.syncKeywordReplaced;
            set
            {
                if (this.SetProperty(ref this.syncKeywordReplaced, value))
                {
                    if (string.IsNullOrEmpty(this.syncKeywordReplaced))
                    {
                        this.SyncRegex = null;
                    }
                    else
                    {
                        this.SyncRegex = new Regex(
                            this.syncKeywordReplaced,
                            RegexOptions.Compiled |
                            RegexOptions.IgnoreCase);
                    }
                }
            }
        }

        private Regex syncRegex = null;

        [XmlIgnore]
        public Regex SyncRegex
        {
            get => this.syncRegex;
            private set => this.SetProperty(ref this.syncRegex, value);
        }

        private Match syncMatch = null;

        [XmlIgnore]
        public Match SyncMatch

        {
            get => this.syncMatch;
            set => this.SetProperty(ref this.syncMatch, value);
        }

        private double? syncOffsetStart = null;
        private double? syncOffsetEnd = null;

        [XmlIgnore]
        public double? SyncOffsetStart
        {
            get => this.syncOffsetStart;
            set => this.SetProperty(ref this.syncOffsetStart, value);
        }

        [XmlAttribute(AttributeName = "sync-s")]
        public string SyncOffsetStartXML
        {
            get => this.SyncOffsetStart?.ToString();
            set => this.SyncOffsetStart = double.TryParse(value, out var v) ? v : (double?)null;
        }

        [XmlIgnore]
        public double? SyncOffsetEnd
        {
            get => this.syncOffsetEnd;
            set => this.SetProperty(ref this.syncOffsetEnd, value);
        }

        [XmlAttribute(AttributeName = "sync-e")]
        public string SyncOffsetEndXML
        {
            get => this.syncOffsetEnd?.ToString();
            set => this.syncOffsetEnd = double.TryParse(value, out var v) ? v : (double?)null;
        }

        private string gotoDestination = null;

        [XmlAttribute(AttributeName = "goto")]
        public string GoToDestination
        {
            get => this.gotoDestination;
            set
            {
                if (this.SetProperty(ref this.gotoDestination, value))
                {
                    this.RaisePropertyChanged(nameof(this.JumpDestination));
                }
            }
        }

        private string callTarget = null;

        [XmlAttribute(AttributeName = "call")]
        public string CallTarget
        {
            get => this.callTarget;
            set
            {
                if (this.SetProperty(ref this.callTarget, value))
                {
                    this.RaisePropertyChanged(nameof(this.JumpDestination));
                }
            }
        }

        private string notice = null;

        [XmlAttribute(AttributeName = "notice")]
        public string Notice
        {
            get => this.notice;
            set => this.SetProperty(ref this.notice, value);
        }

        /// <summary>
        /// 正規表現置換後のNotice
        /// </summary>
        /// <remarks>ただしActivityでは置換を使用しない</remarks>
        [XmlIgnore]
        public string NoticeReplaced
        {
            get => this.notice;
            set { }
        }

        private NoticeDevices? noticeDevice = null;

        [XmlIgnore]
        public NoticeDevices? NoticeDevice
        {
            get => this.noticeDevice;
            set => this.SetProperty(ref this.noticeDevice, value);
        }

        [XmlAttribute(AttributeName = "notice-d")]
        public string NoticeDeviceXML
        {
            get => this.NoticeDevice?.ToString();
            set => this.NoticeDevice = Enum.TryParse<NoticeDevices>(value, out var v) ? v : (NoticeDevices?)null;
        }

        private double? noticeOffset = null;

        [XmlIgnore]
        public double? NoticeOffset
        {
            get => this.noticeOffset;
            set => this.SetProperty(ref this.noticeOffset, value);
        }

        [XmlAttribute(AttributeName = "notice-o")]
        public string NoticeOffsetXML
        {
            get => this.NoticeOffset?.ToString();
            set => this.NoticeOffset = double.TryParse(value, out var v) ? v : (double?)null;
        }

        private float? noticeVolume = null;

        [XmlIgnore]
        public float? NoticeVolume
        {
            get => this.noticeVolume;
            set => this.SetProperty(ref this.noticeVolume, value);
        }

        [XmlAttribute(AttributeName = "notice-vol")]
        public string NoticeVolumeXML
        {
            get => this.NoticeVolume?.ToString();
            set => this.NoticeVolume = float.TryParse(value, out var v) ? v : (float?)null;
        }

        private bool? noticeSync = null;

        [XmlIgnore]
        public bool? NoticeSync
        {
            get => this.noticeSync;
            set => this.SetProperty(ref this.noticeSync, value);
        }

        [XmlAttribute(AttributeName = "notice-sync")]
        public string NoticeSyncXML
        {
            get => this.NoticeSync?.ToString();
            set => this.NoticeSync = bool.TryParse(value, out var v) ? v : (bool?)null;
        }

        private string style = null;

        [XmlAttribute(AttributeName = "style")]
        public string Style
        {
            get => this.style;
            set => this.SetProperty(ref this.style, value);
        }

        private TimelineStyle styleModel = null;

        [XmlIgnore]
        public TimelineStyle StyleModel
        {
            get => this.styleModel;
            set => this.SetProperty(ref this.styleModel, value);
        }

        private string icon = null;

        [XmlAttribute(AttributeName = "icon")]
        public string Icon
        {
            get => this.icon;
            set
            {
                if (this.SetProperty(ref this.icon, value))
                {
                    this.RaisePropertyChanged(nameof(this.IconImage));
                    this.RaisePropertyChanged(nameof(this.ThisIconImage));
                    this.RaisePropertyChanged(nameof(this.ExistsIcon));
                }
            }
        }

        [XmlIgnore]
        public bool ExistsIcon => this.GetExistsIcon();

        [XmlIgnore]
        public BitmapSource IconImage => this.GetIconImage();

        [XmlIgnore]
        public BitmapSource ThisIconImage => this.GetThisIconImage();

        public TimelineActivityModel Clone()
        {
            var clone = this.MemberwiseClone() as TimelineActivityModel;
            clone.id = Guid.NewGuid();
            return clone;
        }

        public override string ToString()
            => $"{this.TimeText} {this.Text}";

        #region 動作を制御するためのフィールド

        [XmlIgnore]
        public TimelineSettings Config => TimelineSettings.Instance;

        private static TimeSpan currentTime = TimeSpan.Zero;

        [XmlIgnore]
        public static TimeSpan CurrentTime
        {
            get => currentTime;
            set
            {
                if (currentTime != value)
                {
                    currentTime = value;
                }
            }
        }

        public void RefreshProgress()
        {
            var progressStartTime =
                WPFHelper.IsDesignMode ?
                15 :
                TimelineSettings.Instance.ShowProgressBarTime;

            var remain = this.time - CurrentTime;
            this.RemainTime = remain.TotalSeconds >= 0 ?
                remain.TotalSeconds :
                0;

            if (remain.TotalSeconds <= progressStartTime)
            {
                var progress = (progressStartTime - remain.TotalSeconds) / progressStartTime;
                if (progress > 1)
                {
                    progress = 1;
                }

                if (progress > 0)
                {
                    this.Progress = progress.TruncateEx(3);
                    this.IsProgressBarActive = true;
                }
            }
        }

        private double remainTime = 0;

        [XmlIgnore]
        public double RemainTime
        {
            get => this.remainTime;
            set
            {
                if (this.SetProperty(ref this.remainTime, value))
                {
                    this.RemainTimeText = Math.Ceiling(this.remainTime).ToString("N0");
                }
            }
        }

        private string remainTimeText = string.Empty;

        [XmlIgnore]
        public string RemainTimeText
        {
            get => this.remainTimeText;
            set => this.SetProperty(ref this.remainTimeText, value);
        }

        private double progress = 0;

        [XmlIgnore]
        public double Progress
        {
            get => this.progress;
            set => this.SetProperty(ref this.progress, value);
        }

        private bool isProgressBarActive = false;

        [XmlIgnore]
        public bool IsProgressBarActive
        {
            get => this.isProgressBarActive;
            set
            {
                if (this.SetProperty(ref this.isProgressBarActive, value))
                {
                    this.RaisePropertyChanged(nameof(this.ActualBarColorBrush));
                }
            }
        }

        [XmlIgnore]
        public Brush ActualBarColorBrush =>
            this.IsProgressBarActive ?
            this.StyleModel?.BarColorBrush :
            Brushes.Transparent;

        private int seq = 0;

        [XmlIgnore]
        public int Seq
        {
            get => this.seq;
            set => this.SetProperty(ref this.seq, value);
        }

        private bool isActive = false;

        [XmlIgnore]
        public bool IsActive
        {
            get => this.isActive;
            set => this.SetProperty(ref this.isActive, value);
        }

        private bool isDone = false;

        [XmlIgnore]
        public bool IsDone
        {
            get => this.isDone;
            set => this.SetProperty(ref this.isDone, value);
        }

        private bool isNotified = false;

        [XmlIgnore]
        public bool IsNotified
        {
            get => this.isNotified;
            set => this.SetProperty(ref this.isNotified, value);
        }

        private bool isSynced = false;

        [XmlIgnore]
        public bool IsSynced
        {
            get => this.isSynced;
            set => this.SetProperty(ref this.isSynced, value);
        }

        private double opacity = 1.0d;

        [XmlIgnore]
        public double Opacity
        {
            get => this.opacity;
            set => this.SetProperty(ref this.opacity, value);
        }

        private double scale = 1.0d;

        [XmlIgnore]
        public double Scale
        {
            get => this.scale;
            set => this.SetProperty(ref this.scale, value);
        }

        private bool isVisible = false;

        [XmlIgnore]
        public bool IsVisible
        {
            get => this.isVisible;
            set => this.SetProperty(ref this.isVisible, value);
        }

        private bool isTop = false;

        [XmlIgnore]
        public bool IsTop
        {
            get => this.isTop;
            set => this.SetProperty(ref this.isTop, value);
        }

        [XmlIgnore]
        public string JumpDestination => (
            !string.IsNullOrEmpty(this.CallTarget) ?
            this.CallTarget :
            this.GoToDestination) ?? string.Empty;

        public void Init(
            int? seq = null)
        {
            if (seq.HasValue)
            {
                this.Seq = seq.Value;
            }

            this.IsActive = false;
            this.IsDone = false;
            this.IsNotified = false;
            this.IsSynced = false;
            this.Progress = 0;
            this.IsProgressBarActive = false;
        }

        #endregion 動作を制御するためのフィールド
    }
}
