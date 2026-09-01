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
        public AiServerConfig AiServer { get; set; } = new();
    }

    public class AiServerConfig
    {
        public string Endpoint { get; set; } = "http://127.0.0.1:50051";
    }
}
