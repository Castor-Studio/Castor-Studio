using System;
using System.Diagnostics;

namespace CastorApplication.Services
{
    public static class BrowserLauncher
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
                Console.WriteLine($"Failed to open URL: {ex.Message}");
            }
        }
    }
}
