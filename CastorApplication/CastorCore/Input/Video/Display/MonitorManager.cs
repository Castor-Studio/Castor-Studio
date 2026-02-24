using Vortice.DXGI;
using SharpGen.Runtime;
using System;
using System.Collections.Generic;

namespace CastorCore.Input.Video.Display
{
    public static class MonitorManager
    {
        public static List<MonitorInfo> GetMonitorInfos()
        {
            List<MonitorInfo> monitors = new List<MonitorInfo>();
            int globalIndex = 0;

            try
            {
                using IDXGIFactory1 factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
                
                for (uint adapterId = 0; adapterId < 10; adapterId++)
                {
                    Result adapterResult = factory.EnumAdapters1(adapterId, out IDXGIAdapter1? adapter);
                    if (adapterResult.Failure || adapter == null)
                        break;

                    using (adapter)
                    {
                        for (uint outputId = 0; outputId < 10; outputId++)
                        {
                            Result outputResult = adapter.EnumOutputs(outputId, out IDXGIOutput? output);
                            if (outputResult.Failure || output == null)
                                break;

                            using (output)
                            {
                                OutputDescription desc = output.Description;
                                
                                monitors.Add(new MonitorInfo
                                {
                                    GlobalIndex = globalIndex++,
                                    OutputId = outputId,
                                    AdapterId = adapterId,
                                    DeviceName = desc.DeviceName,
                                    Width = desc.DesktopCoordinates.Right - desc.DesktopCoordinates.Left,
                                    Height = desc.DesktopCoordinates.Bottom - desc.DesktopCoordinates.Top,
                                    IsAttached = desc.AttachedToDesktop,
                                    Rotation = desc.Rotation.ToString()
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MonitorManager] Erreur lors de l'énumération des moniteurs: {ex.Message}");
            }

            return monitors;
        }

        public static MonitorInfo GetMonitorByIndex(int globalIndex)
        {
            List<MonitorInfo> monitors = GetMonitorInfos();
            
            if (globalIndex < 0 || globalIndex >= monitors.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(globalIndex), 
                    $"Index de moniteur invalide. Valeurs valides: 0-{monitors.Count - 1}. Utilisez GetAvailableMonitors() pour voir tous les moniteurs.");
            }

            return monitors[globalIndex];
        }

        public static MonitorInfo? GetMonitorByDeviceName(string deviceName)
        {
            List<MonitorInfo> monitors = GetMonitorInfos();
            return monitors.Find(m => m.DeviceName.Equals(deviceName, StringComparison.OrdinalIgnoreCase));
        }

        public static MonitorInfo? GetMonitorByAdapterAndOutput(uint adapterId, uint outputId)
        {
            List<MonitorInfo> monitors = GetMonitorInfos();
            return monitors.Find(m => m.AdapterId == adapterId && m.OutputId == outputId);
        }

        public static void PrintAvailableMonitors()
        {
            Console.WriteLine("=== Moniteurs disponibles ===");
            List<MonitorInfo> monitors = GetMonitorInfos();
            
            if (monitors.Count == 0)
            {
                Console.WriteLine("Aucun moniteur trouvé !");
                return;
            }

            foreach (MonitorInfo monitor in monitors)
            {
                Console.WriteLine($"[{monitor.GlobalIndex}] {monitor.DeviceName}");
                Console.WriteLine($"     Résolution: {monitor.Width}x{monitor.Height}");
                Console.WriteLine($"     Adaptateur: {monitor.AdapterId}, Output: {monitor.OutputId}");
                Console.WriteLine($"     Attaché: {monitor.IsAttached}, Rotation: {monitor.Rotation}");
                Console.WriteLine();
            }
        }

        public static bool MonitorExists(int globalIndex)
        {
            List<MonitorInfo> monitors = GetMonitorInfos();
            return globalIndex >= 0 && globalIndex < monitors.Count;
        }

        public static int GetMonitorCount()
        {
            return GetMonitorInfos().Count;
        }
    }
}