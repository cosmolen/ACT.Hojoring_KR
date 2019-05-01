using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;
using FFXIV.Framework.FFXIVHelper;

namespace FFXIV.Framework.Common
{
    public class JobIconDictionary
    {
        #region Singleton

        private static JobIconDictionary instance;

        public static JobIconDictionary Instance => instance ?? (instance = new JobIconDictionary());

        private JobIconDictionary()
        {
        }

        #endregion Singleton

        public Dictionary<JobIDs, BitmapSource> Icons { get; } = new Dictionary<JobIDs, BitmapSource>();

        public BitmapSource GetIcon(
            JobIDs job)
        {
            if (!this.isLoaded)
            {
                this.Load();
            }

            return this.Icons.ContainsKey(job) ?
                this.Icons[job] :
                null;
        }

        private volatile bool isLoaded;

        public async void Load()
        {
            var dir = DirectoryHelper.FindSubDirectory(
                @"resources\icon\job");
            if (!Directory.Exists(dir))
            {
                return;
            }

            await WPFHelper.InvokeAsync(() =>
            {
                foreach (var job in (JobIDs[])Enum.GetValues(typeof(JobIDs)))
                {
                    var png = Path.Combine(dir, $"{job}.png");
                    if (!File.Exists(png))
                    {
                        continue;
                    }

                    using (var fs = new FileStream(png, FileMode.Open))
                    {
                        var decoder = PngBitmapDecoder.Create(
                            fs,
                            BitmapCreateOptions.None,
                            BitmapCacheOption.OnLoad);
                        var bmp = new WriteableBitmap(decoder.Frames[0]);
                        bmp.Freeze();

                        this.Icons[job] = bmp;
                    }
                }
            });

            this.isLoaded = true;
        }
    }
}
