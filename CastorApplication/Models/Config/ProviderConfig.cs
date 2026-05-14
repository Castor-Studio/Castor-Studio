using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CastorApplication.Models.Config
{
    public class ProviderConfig
    {
        public string ClientId { get; set; } = string.Empty;
        public string? ClientSecret { get; set; } = string.Empty;
        public string[] Scopes { get; set; } = [];
    }
}
