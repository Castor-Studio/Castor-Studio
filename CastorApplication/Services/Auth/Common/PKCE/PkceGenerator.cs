using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace CastorApplication.Services.Auth.Common.PKCE
{
    public class PkceGenerator
    {
        public PkceChallenge Generate()
        {
            var bytes = RandomNumberGenerator.GetBytes(64);

            var verifier =
                Base64UrlEncoder.Encode(bytes);

            var hash = SHA256.HashData(
                Encoding.ASCII.GetBytes(verifier));

            var challenge =
                Base64UrlEncoder.Encode(hash);

            return new PkceChallenge
            {
                Verifier = verifier,
                Challenge = challenge
            };
        }
    }
}
