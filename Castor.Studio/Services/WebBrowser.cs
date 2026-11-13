using System;
using System.Diagnostics;
using CastorApplication.Services.Diagnostics;

namespace CastorApplication.Services
{
    public static class WebBrowser
    {
        public static void Open(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AppLog.Warning($"Could not open {url}", ex);
            }
        }
    }
}
