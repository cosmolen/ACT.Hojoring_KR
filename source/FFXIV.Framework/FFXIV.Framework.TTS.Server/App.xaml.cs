using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using FFXIV.Framework.Common;
using NLog;

namespace FFXIV.Framework.TTS.Server
{
    /// <summary>
    /// App.xaml の相互作用ロジック
    /// </summary>
    public partial class App :
        Application
    {
        #region Singleton

        private static App instance;

        public static App Instance => instance;

        #endregion Singleton

        #region Logger

        private Logger Logger => AppLog.DefaultLogger;

        #endregion Logger

        private TaskTrayComponent taskTrayComponet = new TaskTrayComponent();

        private DispatcherTimer shutdownTimer = new DispatcherTimer(DispatcherPriority.ContextIdle)
        {
            Interval = TimeSpan.FromSeconds(10),
        };

        public App()
        {
            instance = this;

            AssemblyResolver.Instance.Initialize();
            Assembly.Load("FFXIV.Framework");

            Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            this.Startup += this.App_Startup;
            this.Exit += this.App_Exit;
            this.DispatcherUnhandledException += this.App_DispatcherUnhandledException;
        }

        public static void ShowMessageBoxException(
            string message,
            Exception ex)
        {
            var caption = $"{EnvironmentHelper.GetProductName()} {EnvironmentHelper.GetVersion().ToStringShort()}";

            var sb = new StringBuilder();
            sb.AppendLine(message);
            sb.AppendLine();
            sb.AppendLine(ex.Message);
            sb.AppendLine(ex.StackTrace);

            if (ex.InnerException != null)
            {
                sb.AppendLine();
                sb.AppendLine("Inner Exception");
                sb.AppendLine(ex.InnerException.Message);
                sb.AppendLine(ex.InnerException.StackTrace);
            }

            MessageBox.Show(
                sb.ToString(),
                caption,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                var message = "Dispatcher Unhandled Exception";

                try
                {
                    this.Logger.Fatal(e.Exception, message);
                    LogManager.Flush();
                }
                catch (Exception)
                {
                }

                ShowMessageBoxException(message, e.Exception);
            }
            finally
            {
                Application.Current.Shutdown();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void App_Exit(object sender, ExitEventArgs e)
        {
            try
            {
                this.Logger.Trace("begin.");

                // サーバを終了する
                RemoteTTSServer.Instance.Close();

                if (this.taskTrayComponet != null)
                {
                    this.taskTrayComponet.Dispose();
                    this.taskTrayComponet = null;
                }
            }
            catch (Exception ex)
            {
                var message = "App exit error";
                this.Logger.Fatal(ex, message);
                ShowMessageBoxException(message, ex);
            }
            finally
            {
                AssemblyResolver.Free();
                this.Logger.Trace("end.");
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void App_Startup(object sender, StartupEventArgs e)
        {
            // NLogを設定する
            AppLog.LoadConfiguration(Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                "FFXIV.Framework.TTS.Server.NLog.config"));

            try
            {
                this.Logger.Trace("begin.");

                // バージョンを出力する
                this.Logger.Info($"{EnvironmentHelper.GetProductName()} {EnvironmentHelper.GetVersion().ToStringShort()}");

                // サーバを開始する
                RemoteTTSServer.Instance.Open();

                // シャットダウンタイマーをセットする
                this.shutdownTimer.Tick -= this.ShutdownTimerOnTick;
                this.shutdownTimer.Tick += this.ShutdownTimerOnTick;
                this.shutdownTimer.Start();
            }
            catch (Exception ex)
            {
                var message = "App initialize error";
                this.Logger.Fatal(ex, message);
                ShowMessageBoxException(message, ex);
            }
            finally
            {
                this.Logger.Trace("end.");
            }
        }

        private void ShutdownTimerOnTick(object sender, EventArgs e)
        {
            if (Process.GetProcessesByName("Advanced Combat Tracker").Length < 1 &&
                Process.GetProcessesByName("ACTx86").Length < 1)
            {
                this.Logger.Trace("ACT not found. shutdown server.");

                this.shutdownTimer.Stop();
                this.Shutdown();
            }
        }
    }
}
