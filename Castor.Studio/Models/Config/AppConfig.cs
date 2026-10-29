using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CastorApplication.Models.Config
{
    public class AppConfig
    {
        public Dictionary<string, ProviderConfig> Providers { get; set; } = new();

        // Older config files still carry "aiServer" (remote AI endpoint): unknown keys are
        // ignored on load, so they keep working without any migration step.
        public AiWorkerConfig AiWorker { get; set; } = new();
    }

    /// <summary>Local AI worker (Castor.AI) started by the app.</summary>
    public class AiWorkerConfig
    {
        /// <summary>"gpu" (DirectML, CPU fallback) or "cpu".</summary>
        public string Device { get; set; } = "gpu";

        /// <summary>DXGI adapter index used by DirectML, 0 = default display adapter.</summary>
        public int GpuAdapter { get; set; }
    }
}
