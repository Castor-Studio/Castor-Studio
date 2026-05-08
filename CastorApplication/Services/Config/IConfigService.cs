using CastorApplication.Models.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CastorApplication.Services.Config
{
    public interface IConfigService
    {
        AppConfig Config { get; }
        ProviderConfig GetProviderConfig(string name);
    }
}
