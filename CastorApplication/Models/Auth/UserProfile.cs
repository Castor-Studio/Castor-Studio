using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CastorApplication.Models.Auth
{
    public sealed class UserProfile
    {
        public string Id { get; init; }

        public string DisplayName { get; init; }

        public string? AvatarUrl { get; init; }
    }
}
