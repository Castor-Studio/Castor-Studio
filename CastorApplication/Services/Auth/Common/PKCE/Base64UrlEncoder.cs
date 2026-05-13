using System;

namespace CastorApplication.Services.Auth.Common.PKCE
{
    public static class Base64UrlEncoder
    {
        public static string Encode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .Replace("=", "");
        }
    }
}
