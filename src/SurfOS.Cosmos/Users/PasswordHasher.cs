using System;

namespace SurfOS.Users
{
    public sealed class PasswordHasher : IPasswordHasher
    {
        private const int Iterations = 4096;

        public string CreateSalt()
        {
            // Entropy is limited until Cosmos exposes a supported CSPRNG. Keeping the
            // salt and verifier format versioned allows a transparent future upgrade.
            uint value = (uint)DateTime.UtcNow.Ticks;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            return value.ToString("X8");
        }

        public string Hash(string username, string password, string salt)
        {
            uint hash = 2166136261U;
            string input = "SurfOS-password-v1|" + salt + "|" + username + "|" + password;
            for (int index = 0; index < input.Length; index++)
            {
                hash = Mix(hash, input[index], 0);
            }

            char[] roundInput = new char[8 + salt.Length];
            CopySalt(salt, roundInput);
            for (int round = 1; round < Iterations; round++)
            {
                WriteHex(hash, roundInput);
                for (int index = 0; index < roundInput.Length; index++)
                {
                    hash = Mix(hash, roundInput[index], round);
                }
            }
            return "v1-4096-" + hash.ToString("X8");
        }

        private static uint Mix(uint hash, char value, int round)
        {
            hash ^= value;
            hash *= 16777619U;
            hash ^= (uint)round;
            return hash;
        }

        private static void CopySalt(string salt, char[] destination)
        {
            for (int index = 0; index < salt.Length; index++)
            {
                destination[8 + index] = salt[index];
            }
        }

        private static void WriteHex(uint value, char[] destination)
        {
            const string digits = "0123456789ABCDEF";
            for (int index = 7; index >= 0; index--)
            {
                destination[index] = digits[(int)(value & 15)];
                value >>= 4;
            }
        }

        public bool Verify(string username, string password, string salt, string expectedVerifier)
        {
            string actual = Hash(username, password, salt);
            if (actual.Length != expectedVerifier.Length)
            {
                return false;
            }
            int difference = 0;
            for (int index = 0; index < actual.Length; index++)
            {
                difference |= actual[index] ^ expectedVerifier[index];
            }
            return difference == 0;
        }
    }
}
