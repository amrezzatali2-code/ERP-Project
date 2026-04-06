using System;
using System.Security.Cryptography;
using System.Text;

namespace ERP.Infrastructure
{
    /// <summary>
    /// تشفير والتحقق من كلمات المرور (SHA256 hex) — نفس منطق تسجيل الدخول.
    /// </summary>
    public static class PasswordHasher
    {
        public static string HashPassword(string plainPassword)
        {
            if (plainPassword == null) throw new ArgumentNullException(nameof(plainPassword));
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(plainPassword);
            var hashBytes = sha.ComputeHash(bytes);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>
        /// يقبل هاش SHA256 أو نصاً مخزناً قديماً (للتوافق مع بيانات قديمة).
        /// </summary>
        public static bool VerifyPassword(string inputPassword, string storedHash)
        {
            if (string.IsNullOrEmpty(storedHash))
                return false;

            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(inputPassword);
            var hashBytes = sha.ComputeHash(bytes);
            var inputHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

            if (string.Equals(storedHash, inputHash, StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(storedHash, inputPassword))
                return true;

            return false;
        }
    }
}
