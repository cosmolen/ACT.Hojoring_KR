using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ACT.SpecialSpellTimer.Models;
using FFXIV.Framework.Bridge;
using FFXIV.Framework.Common;
using static ACT.SpecialSpellTimer.Models.TableCompiler;

namespace ACT.SpecialSpellTimer.RaidTimeline
{
    public class TimelineManager
    {
        #region Logger

        private NLog.Logger AppLogger => FFXIV.Framework.Common.AppLog.DefaultLogger;

        #endregion Logger

        #region Singleton

        private static TimelineManager instance;

        public static TimelineManager Instance =>
            instance ?? (instance = new TimelineManager());

        private TimelineManager()
        {
            this.Init();
        }

        #endregion Singleton

        public string TimelineDirectory => TimelineSettings.TimelineDirectory;

        private readonly ObservableCollection<TimelineModel> timelineModels
            = new ObservableCollection<TimelineModel>();

        public ObservableCollection<TimelineModel> TimelineModels => this.timelineModels;

        private readonly List<(string TimelineName, TimelineTriggerModel Trigger)> globalTriggers
            = new List<(string TimelineName, TimelineTriggerModel Trigger)>();

        public TimelineTriggerModel[] GlobalTriggers
            => this.globalTriggers
                .Where(x => x.Trigger.IsAvailable())
                .Select(x => x.Trigger)
                .ToArray();

        public void Init()
        {
            // テーブルコンパイラにイベントを設定する
            TableCompiler.Instance.ZoneChanged -= this.OnTimelineConditionChanged;
            TableCompiler.Instance.ZoneChanged += this.OnTimelineConditionChanged;

            // Help機能にバックアップコールバックを追加する
            HelpBridge.Instance.BackupCallback -= this.BackupTimelineDirectory;
            HelpBridge.Instance.BackupCallback += this.BackupTimelineDirectory;
        }

        private volatile bool isLoading = false;

        /// <summary>
        /// タイムラインに関連する条件が変わった
        /// </summary>
        /// <param name="sender">イベント発生元</param>
        /// <param name="e">イベント引数</param>
        private void OnTimelineConditionChanged(
            object sender,
            EventArgs e)
        {
            if (!TimelineSettings.Instance.Enabled)
            {
                return;
            }

            if (this.isLoading)
            {
                return;
            }

            this.isLoading = true;

            WPFHelper.BeginInvoke(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5));
                    this.LoadCurrentTimeline();
                }
                finally
                {
                    this.isLoading = false;
                }
            });
        }

        /// <summary>
        /// 現在のタイムラインをロードする
        /// </summary>
        public void LoadCurrentTimeline()
        {
            lock (this)
            {
                var timelines = TimelineManager.Instance.TimelineModels.ToArray();

                // グローバルトリガとリファンレスファイルをリロードする
                var toReloads = timelines.Where(x =>
                    x.IsGlobalZone ||
                    x.IsReference);
                foreach (var tl in toReloads)
                {
                    tl.Reload();
                    Thread.Yield();
                }

                // すでにコントローラがロードされていたらアンロードする
                foreach (var tl in timelines)
                {
                    tl.Controller.Unload();
                    tl.IsActive = false;
                    Thread.Yield();
                }

                // 現在のゾーンで有効なタイムラインを取得する
                var newTimeline = timelines
                    .FirstOrDefault(x => x.Controller.IsAvailable);

                // 有効なTLが存在しないならばグローバルのいずれかをカレントにする
                if (newTimeline == null)
                {
                    newTimeline = toReloads.FirstOrDefault(x => x.IsGlobalZone);
                }

                if (newTimeline != null)
                {
                    // 該当のタイムラインファイルをリロードする
                    if (!newTimeline.IsGlobalZone)
                    {
                        newTimeline.Reload();
                        Thread.Yield();
                    }

                    // コントローラをロードする
                    newTimeline.Controller.Load();
                    newTimeline.IsActive = true;
                    Thread.Yield();

                    this.AppLogger.Trace($"[TL] Timeline auto loaded. active_timeline={newTimeline.TimelineName}.");
                }

                // グローバルトリガを初期化する
                TimelineManager.Instance.InitGlobalTriggers();
            }
        }

        public async void LoadTimelineModels()
        {
            var dir = this.TimelineDirectory;
            if (!Directory.Exists(dir))
            {
                return;
            }

            await WPFHelper.InvokeAsync(() => this.TimelineModels.Clear());

            var sampleDirectory = Path.Combine(dir, "sample");

            if (!Directory.EnumerateFiles(dir).Where(x =>
                x.ToLower().EndsWith(".xml") ||
                x.ToLower().EndsWith(".cshtml")).
                Any())
            {
                foreach (var file in Directory.GetFiles(sampleDirectory))
                {
                    if (file.EndsWith(".config") ||
                        file.Contains("SampleInclude"))
                    {
                        continue;
                    }

                    var dest = Path.Combine(dir, Path.GetFileName(file));
                    File.Copy(file, dest, true);
                }
            }
            else
            {
                var reference = Path.Combine(dir, "Reference.cshtml");
                var referenceSample = Path.Combine(sampleDirectory, "Reference.cshtml");
                if (File.Exists(reference) &&
                    File.Exists(referenceSample))
                {
                    File.Copy(referenceSample, reference, true);
                }
            }

            Thread.Sleep(10);

            // RazorEngine にわたすモデルを更新する
            TimelineModel.RefreshRazorModel();

            var list = new List<TimelineModel>();
            foreach (var file in Directory.EnumerateFiles(dir).Where(x =>
                x.ToLower().EndsWith(".xml") ||
                x.ToLower().EndsWith(".cshtml")))
            {
                try
                {
                    var tl = TimelineModel.Load(file);
                    if (tl != null)
                    {
                        list.Add(tl);
                    }
                }
                catch (Exception ex)
                {
                    this.AppLogger.Error(
                        ex,
                        $"[TL] Load error. file={file}");

                    throw new FileLoadException(
                        $"Timeline file Load error.\n{Path.GetFileName(file)}",
                        ex);
                }

                Thread.Sleep(10);
            }

            // グローバルトリガをロードする
            this.globalTriggers.Clear();
            var globals = list.Where(x => x.IsGlobalZone);
            foreach (var tl in globals)
            {
                this.LoadGlobalTriggers(tl);
                Thread.Sleep(10);
            }

            await WPFHelper.InvokeAsync(() =>
            {
                foreach (var tl in this.TimelineModels)
                {
                    if (tl.IsActive)
                    {
                        tl.IsActive = false;
                        tl.Controller.Unload();
                        Thread.Yield();
                    }
                }

                this.TimelineModels.Clear();
                this.TimelineModels.AddRange(
                    from x in list
                    orderby
                    x.SourceFileName.Contains("Reference") ? 0 : 1,
                    x.IsGlobalZone ? 0 : 1,
                    x.SourceFileName
                    select
                    x);
            });
        }

        public void InitGlobalTriggers()
        {
            if (this.GlobalTriggers.Any())
            {
                this.InitElements(this.GlobalTriggers);
            }
        }

        public void LoadGlobalTriggers(
            TimelineModel timeline)
        {
            if (!timeline.IsGlobalZone)
            {
                return;
            }

            var name = timeline.Name.ToUpper();

            foreach (var tri in timeline.Triggers)
            {
                this.globalTriggers.Add((name, tri));
                this.InitElements(tri);
            }

            this.AppLogger.Trace("[TL] Loaded global triggers.");
        }

        public void ReloadGlobalTriggers(
            TimelineModel timeline)
        {
            if (!timeline.IsGlobalZone)
            {
                return;
            }

            var name = timeline.Name.ToUpper();

            this.globalTriggers.RemoveAll(x => x.TimelineName.ToUpper() == name);
            foreach (var tri in timeline.Triggers)
            {
                this.globalTriggers.Add((name, tri));
                this.InitElements(tri);
            }

            this.AppLogger.Trace("[TL] Reloaded global triggers.");
        }

        public void InitElements(
            TimelineBase timeline)
            => this.InitElements(timeline, null);

        public void InitElements(
            IList<TimelineBase> elements)
            => this.InitElements(null, elements);

        public bool InSimulation { get; set; } = false;

        public IEnumerable<PlaceholderContainer> GetPlaceholders() =>
            TableCompiler.Instance.GetPlaceholders(this.InSimulation, true);

        public string ReplacePlaceholder(
            string keyword,
            IEnumerable<PlaceholderContainer> placeholders = null)
        {
            var replacedKeyword = keyword;

            if (!string.IsNullOrEmpty(replacedKeyword))
            {
                if (placeholders == null)
                {
                    placeholders = this.GetPlaceholders();
                }

                foreach (var ph in placeholders)
                {
                    replacedKeyword = replacedKeyword.Replace(
                        ph.Placeholder,
                        ph.ReplaceString);
                }
            }

            return replacedKeyword;
        }

        /// <summary>
        /// Elementを初期化する
        /// </summary>
        private void InitElements(
            TimelineBase timeline = null,
            IList<TimelineBase> elements = null)
        {
            var defaultStyle = TimelineSettings.Instance.DefaultStyle;
            var defaultNoticeStyle = TimelineSettings.Instance.DefaultNoticeStyle;

            // <HOGE>を[HOGE]に置き換えたプレースホルダリストを生成する
            var placeholders = this.GetPlaceholders();

            // 初期化する
            if (timeline != null)
            {
                timeline.Walk((element) =>
                    initElement(element));
            }

            // 初期化する
            if (elements != null)
            {
                foreach (var element in elements)
                {
                    element.Walk((child) =>
                        initElement(child));
                }
            }

            void initElement(TimelineBase element)
            {
                // サブルーチンにトリガをインポートする
                if (element is TimelineSubroutineModel sub)
                {
                    sub.ExecuteImports();
                }

                // トリガのマッチカウンタを初期化する
                if (element is TimelineTriggerModel tri)
                {
                    tri.Init();
                }

                // ImageNoticeを準備する
                if (element is TimelineImageNoticeModel image)
                {
                    image.StanbyNotice();
                }

                // アクティビティにスタイルを設定する
                setStyle(element);

                // sync用の正規表現にプレースホルダをセットしてコンパイルし直す
                setRegex(element, placeholders);
            }

            // スタイルを適用する
            void setStyle(TimelineBase element)
            {
                if (element is TimelineActivityModel act)
                {
                    if (string.IsNullOrEmpty(act.Style))
                    {
                        act.StyleModel = defaultStyle;
                        return;
                    }

                    act.StyleModel = TimelineSettings.Instance.Styles
                        .FirstOrDefault(x => string.Equals(
                            x.Name,
                            act.Style,
                            StringComparison.OrdinalIgnoreCase)) ??
                        defaultStyle;
                }

                if (element is TimelineVisualNoticeModel notice)
                {
                    if (string.IsNullOrEmpty(notice.Style))
                    {
                        notice.StyleModel = defaultNoticeStyle;
                        return;
                    }

                    notice.StyleModel = TimelineSettings.Instance.Styles
                        .FirstOrDefault(x => string.Equals(
                            x.Name,
                            notice.Style,
                            StringComparison.OrdinalIgnoreCase)) ??
                        defaultNoticeStyle;
                }
            }

            // 正規表現をセットする
            void setRegex(
                TimelineBase element,
                IEnumerable<PlaceholderContainer> phs)
            {
                if (!(element is ISynchronizable sync))
                {
                    return;
                }

                var replacedKeyword = sync.SyncKeyword;

                if (!string.IsNullOrEmpty(replacedKeyword))
                {
                    foreach (var ph in phs)
                    {
                        replacedKeyword = replacedKeyword.Replace(
                            ph.Placeholder,
                            ph.ReplaceString);
                    }
                }

                sync.SyncKeywordReplaced = replacedKeyword;
            }
        }

        public void BackupTimelineDirectory(
             string destination)
        {
            var backup = Path.Combine(destination, @"ACT.Hojoring\resources\timeline");
            DirectoryHelper.DirectoryCopy(this.TimelineDirectory, backup);
        }
    }
}
