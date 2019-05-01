using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using Advanced_Combat_Tracker;
using Prism.Commands;

namespace ACT.XIVLog
{
    /// <summary>
    /// ConfigView.xaml の相互作用ロジック
    /// </summary>
    public partial class ConfigView :
        UserControl
    {
        public ConfigView()
        {
            this.InitializeComponent();
        }

        public Config Config => Config.Instance;

        public XIVLogPlugin Plugin => XIVLogPlugin.Instance;

        public string VersionInfo
        {
            get
            {
                var result = string.Empty;

                var plugin = ActGlobals.oFormActMain.PluginGetSelfData(XIVLogPlugin.Instance);
                if (plugin != null)
                {
                    var vi = FileVersionInfo.GetVersionInfo(plugin.pluginFile.FullName);
                    result = $"{vi.ProductName} v{vi.FileVersion}";
                }

                return result;
            }
        }

        private ICommand oepnLogCommand;

        public ICommand OpenLogCommand =>
            this.oepnLogCommand ?? (this.oepnLogCommand = new DelegateCommand(async () => await Task.Run(() =>
            {
                if (File.Exists(this.Plugin.LogfileName))
                {
                    Process.Start(this.Plugin.LogfileName);
                }
            })));

        private ICommand oepnLogDirectoryCommand;

        public ICommand OpenLogDirectoryCommand =>
            this.oepnLogDirectoryCommand ?? (this.oepnLogDirectoryCommand = new DelegateCommand(async () => await Task.Run(() =>
            {
                var directory = Path.GetDirectoryName(this.Plugin.LogfileName);

                if (Directory.Exists(directory))
                {
                    Process.Start(directory);
                }
            })));
    }
}
