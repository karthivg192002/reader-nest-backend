using System.Security.Cryptography;

namespace iucs.readernest.application.Helper
{
    /// <summary>
    /// Generates the initial credentials emailed to admin-created accounts.
    /// Users are expected to change it after first login (forced-change flow lands with Sprint 2 hardening).
    /// </summary>
    public static class TemporaryPasswordGenerator
    {
        private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%";

        public static string Generate(int length = 12)
        {
            var chars = new char[length];
            for (var i = 0; i < length; i++)
            {
                chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
            }

            return new string(chars);
        }
    }
}
