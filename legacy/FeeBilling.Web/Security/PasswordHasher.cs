using System;
using System.Security.Cryptography;
using System.Text;

namespace FeeBilling.Web.Security
{
    // SHA1(salt + password), base64. Written in 2011. Unsalted-iteration SHA1 is not a password hash.
    public static class PasswordHasher
    {
        public static string Hash(string salt, string password)
        {
            using (var sha1 = SHA1.Create())
            {
                var bytes = Encoding.Unicode.GetBytes(salt + password);
                return Convert.ToBase64String(sha1.ComputeHash(bytes));
            }
        }

        public static bool Verify(string salt, string password, string expectedHash)
        {
            return Hash(salt, password) == expectedHash;   // not constant-time
        }

        public static string NewSalt()
        {
            var bytes = new byte[8];
            using (var rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(bytes);
            }
            return Convert.ToBase64String(bytes).Substring(0, 10);
        }
    }
}
