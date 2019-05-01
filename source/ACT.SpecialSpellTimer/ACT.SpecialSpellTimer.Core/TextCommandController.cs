using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Text.RegularExpressions;
using ACT.SpecialSpellTimer.Config;
using ACT.SpecialSpellTimer.Models;
using ACT.SpecialSpellTimer.Sound;

namespace ACT.SpecialSpellTimer
{
    /// <summary>
    /// テキストコマンド Controller
    /// </summary>
    public static class TextCommandController
    {
        /// <summary>
        /// コマンド解析用の正規表現
        /// </summary>
        private readonly static Regex regexCommand = new Regex(
            @".*/spespe (?<command>refresh|changeenabled|set|clear|on|off|pos) ?(?<target>spells|telops|me|pt|pet|placeholder|$) ?(?<windowname>"".*""|all)? ?(?<value>.*)",
            RegexOptions.Compiled |
            RegexOptions.IgnoreCase);

        /// <summary>
        /// TTS読み仮名コマンド
        /// </summary>
        private readonly static Regex phoneticsCommand = new Regex(
            @".*/spespe phonetic ""(?<pcname>.+?)"" ""(?<phonetic>.+?)""",
            RegexOptions.Compiled |
            RegexOptions.IgnoreCase);

        /// <summary>
        /// Logコマンド
        /// </summary>
        private readonly static Regex logCommand = new Regex(
            @".*/spespe log (?<switch>on|off|open)",
            RegexOptions.Compiled |
            RegexOptions.IgnoreCase);

        /// <summary>
        /// TTSコマンド
        /// </summary>
        private readonly static Regex ttsCommand = new Regex(
            @".*/tts (?<text>.*)",
            RegexOptions.Compiled |
            RegexOptions.IgnoreCase);

        /// <summary>
        /// ログ1行とマッチングする
        /// </summary>
        /// <param name="logLine">ログ行</param>
        /// <returns>
        /// コマンドを実行したか？</returns>
        public static bool MatchCommandCore(
            string logLine)
        {
            var r = false;

            // 正規表現の前にキーワードがなければ抜けてしまう
            if (!logLine.ToLower().Contains("/spespe") &&
                !logLine.ToLower().Contains("/tts"))
            {
                return r;
            }

            // 読み仮名コマンドとマッチングする
            var isPhonetic = MatchPhoneticCommand(logLine);
            if (isPhonetic)
            {
                return isPhonetic;
            }

            // ログコマンドとマッチングする
            var isLog = MatchLogCommand(logLine);
            if (isLog)
            {
                return isLog;
            }

            // TTSコマンドとマッチングする
            MatchTTSCommand(logLine);

            // その他の通常コマンドとマッチングする
            var match = regexCommand.Match(logLine);
            if (!match.Success)
            {
                return r;
            }

            var command = match.Groups["command"].ToString().ToLower();
            var target = match.Groups["target"].ToString().ToLower();
            var windowname = match.Groups["windowname"].ToString().Replace(@"""", string.Empty);
            var valueAsText = match.Groups["value"].ToString();
            var value = false;
            if (!bool.TryParse(valueAsText, out value))
            {
                value = false;
            }

            switch (command)
            {
                case "refresh":
                    switch (target)
                    {
                        case "spells":
                            SpellsController.Instance.ClosePanels();
                            r = true;
                            break;

                        case "telops":
                            TickersController.Instance.CloseTelops();
                            r = true;
                            break;

                        case "pt":
                            TableCompiler.Instance.RefreshPlayerPlacceholder();
                            TableCompiler.Instance.RefreshPartyPlaceholders();
                            TableCompiler.Instance.RecompileSpells();
                            TableCompiler.Instance.RecompileTickers();
                            r = true;
                            break;

                        case "pet":
                            TableCompiler.Instance.RefreshPetPlaceholder();
                            TableCompiler.Instance.RecompileSpells();
                            TableCompiler.Instance.RecompileTickers();
                            r = true;
                            break;
                    }

                    break;

                case "changeenabled":
                    switch (target)
                    {
                        case "spells":
                            foreach (var spell in SpellTable.Instance.Table)
                            {
                                if (spell.Panel.PanelName.Trim().ToLower() == windowname.Trim().ToLower() ||
                                    spell.SpellTitle.Trim().ToLower() == windowname.Trim().ToLower() ||
                                    windowname.Trim().ToLower() == "all")
                                {
                                    spell.Enabled = value;
                                }
                            }

                            break;

                        case "telops":
                            foreach (var telop in TickerTable.Instance.Table)
                            {
                                if (telop.Title.Trim().ToLower() == windowname.Trim().ToLower() ||
                                    windowname.Trim().ToLower() == "all")
                                {
                                    telop.Enabled = value;
                                }
                            }

                            break;
                    }

                    break;

                case "set":
                    switch (target)
                    {
                        case "placeholder":
                            if (windowname.Trim().ToLower() != "all" &&
                                windowname.Trim() != string.Empty &&
                                valueAsText.Trim() != string.Empty)
                            {
                                TableCompiler.Instance.SetCustomPlaceholder(windowname.Trim(), valueAsText.Trim());

                                r = true;
                            }

                            break;
                    }

                    break;

                case "clear":
                    switch (target)
                    {
                        case "placeholder":
                            if (windowname.Trim().ToLower() == "all")
                            {
                                TableCompiler.Instance.ClearCustomPlaceholderAll();

                                r = true;
                            }
                            else if (windowname.Trim() != string.Empty)
                            {
                                TableCompiler.Instance.ClearCustomPlaceholder(windowname.Trim());

                                r = true;
                            }

                            break;
                    }

                    break;

                case "on":
                    PluginCore.Instance.ChangeSwitchVisibleButton(true);
                    r = true;
                    break;

                case "off":
                    PluginCore.Instance.ChangeSwitchVisibleButton(false);
                    r = true;
                    break;

                case "pos":
                    LogBuffer.DumpPosition();
                    r = true;
                    break;
            }

            return r;
        }

        /// <summary>
        /// 読み仮名設定コマンドとマッチングする
        /// </summary>
        /// <param name="logLine">ログ1行</param>
        /// <returns>
        /// マッチした？</returns>
        public static bool MatchPhoneticCommand(
            string logLine)
        {
            var r = false;

            var match = phoneticsCommand.Match(logLine);
            if (!match.Success)
            {
                return r;
            }

            var pcName = match.Groups["pcname"].ToString().ToLower();
            var phonetic = match.Groups["phonetic"].ToString().ToLower();

            if (!string.IsNullOrEmpty(pcName) &&
                !string.IsNullOrEmpty(phonetic))
            {
                r = true;

                var p = TTSDictionary.Instance.Phonetics.FirstOrDefault(x => x.Name == pcName);
                if (p != null)
                {
                    p.Phonetic = phonetic;
                }
                else
                {
                    TTSDictionary.Instance.Dictionary[pcName] = phonetic;
                }
            }

            return r;
        }

        public static bool MatchLogCommand(
            string logLine)
        {
            var r = false;

            var match = logCommand.Match(logLine);
            if (!match.Success)
            {
                return r;
            }

            var switchValue = match.Groups["switch"].ToString().ToLower();

            if (switchValue == "on")
            {
                r = true;
                Settings.Default.SaveLogEnabled = true;
            }

            if (switchValue == "off")
            {
                r = true;
                Settings.Default.SaveLogEnabled = false;
            }

            if (switchValue == "open")
            {
                r = true;
                var file = ChatLogWorker.Instance.OutputFile;
                if (File.Exists(file))
                {
                    Process.Start(file);
                }
            }

            return r;
        }

        public static void MatchTTSCommand(
            string logLine)
        {
            var match = ttsCommand.Match(logLine);
            if (!match.Success)
            {
                return;
            }

            var text = match.Groups["text"].ToString().ToLower();
            if (!string.IsNullOrEmpty(text))
            {
                SoundController.Instance.Play(text);
            }
        }

        /// <summary>
        /// Commandとマッチングする
        /// </summary>
        /// <param name="logLines">
        /// ログ行</param>
        public static void MatchCommand(
            IReadOnlyList<string> logLines)
        {
            var commandDone = false;
            logLines.AsParallel().ForAll(log =>
            {
                commandDone |= MatchCommandCore(log);
            });

            // コマンドを実行したらシステム音を鳴らす
            if (commandDone)
            {
                SystemSounds.Asterisk.Play();
            }
        }
    }
}
