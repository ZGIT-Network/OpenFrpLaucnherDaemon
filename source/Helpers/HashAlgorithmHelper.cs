using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace OpenFrp.Service.Helpers
{
    public class HashAlgorithmHelper
    {
        static HashAlgorithmHelper()
        {
            Hash = new HMACSHA256();

            Hash.Key = Key;

            Hash.Initialize();

        }
        private static readonly byte[] Key = new byte[16] { 84, 98, 32, 45, 22, 14, 69, 21, 53, 13, 15, 84, 69, 21, 68, 14 };

        public static HMACSHA256 Hash { get; private set; }

        public static byte[] ComputeHash(byte[] data)
        {
            return Hash.ComputeHash(data, 0, data.Length);
        }
#if NET
        public static async Task<byte[]> ComputeHashAsync(ReadOnlyMemory<byte> data)
        {
            using var ms = new MemoryStream();

            await ms.WriteAsync(data);

            ms.Seek(0, SeekOrigin.Begin);

            return await Hash.ComputeHashAsync(ms);
        }

        public static Task<byte[]> ComputeHashAsync(string data)
        {
            ReadOnlyMemory<byte> bytes = Encoding.UTF8.GetBytes(data);

            return ComputeHashAsync(bytes);
        }

        public static async Task<string> ComputeHashStringAsync(string data)
        {
            StringBuilder @string = new StringBuilder();

            foreach (var item in await ComputeHashAsync(data))
            {
                @string.Append(item.ToString("x2"));
            }

            return @string.ToString();
        }
#endif

        public static byte[] ComputeHash(string data)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(data);

            return ComputeHash(bytes);
        }

        public static string ComputeHashToBase64String(string data)
        {
            return Convert.ToBase64String(ComputeHash(data));
        }

        public static string ComputeHashString(string data)
        {
            StringBuilder @string = new StringBuilder();

            foreach (var item in ComputeHash(data))
            {
                @string.Append(item.ToString("x2"));
            }

            return @string.ToString();
        }
        
    }
}
