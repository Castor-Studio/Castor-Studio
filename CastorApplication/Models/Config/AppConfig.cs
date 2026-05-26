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
    }
}
