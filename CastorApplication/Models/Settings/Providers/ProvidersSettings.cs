using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CastorApplication.Models.Settings.Providers
{
    public sealed class ProvidersSettings
    {
        public List<ProviderSettings> Providers { get; set; } = new ();
    }
}
