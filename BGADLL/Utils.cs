using System;
using System.Collections.Generic;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace BGADLL
{
    /// <summary>
    /// Cross-platform PRNG that produces identical sequences on all .NET versions.
    /// System.Random changed its algorithm in .NET 6, so we use our own implementation
    /// to ensure PIMC evaluations are reproducible across .NET Framework and .NET 9 NativeAOT.
    /// Based on SplitMix32 (derived from the seed initializer of xoshiro/xoroshiro generators).
    /// </summary>
    public class CrossPlatformRandom
    {
        private uint _state;

        public CrossPlatformRandom(int seed)
        {
            _state = (uint)seed;
        }

        /// <summary>
        /// Returns a non-negative random integer less than maxValue.
        /// </summary>
        public int Next(int maxValue)
        {
            if (maxValue <= 0) return 0;
            uint raw = NextUInt32();
            // Use 64-bit multiply + shift to avoid modulo bias
            return (int)(((ulong)raw * (ulong)maxValue) >> 32);
        }

        private uint NextUInt32()
        {
            _state += 0x9E3779B9u; // golden ratio
            uint z = _state;
            z = (z ^ (z >> 16)) * 0x85EBCA6Bu;
            z = (z ^ (z >> 13)) * 0xC2B2AE35u;
            return z ^ (z >> 16);
        }
    }

    public class Utils
    {
		public IEnumerable<byte[]> Generate(int n, int k)
		{
			byte[] result = new byte[k];
			if (k == 0)
                yield return result;
			else
			{
                var stack = new Stack<byte>();
                stack.Push(0);
                while (stack.Count > 0)
                {
                    int index = stack.Count - 1;
                    byte value = stack.Pop();
                    while (value < n)
                    {
                        result[index++] = ++value;
                        stack.Push(value);
                        if (index == k)
                        {
                            yield return result;
                            break;
                        }
                    }
                }
            }
        }

        public byte[] GetCombinationAtIndex(int n, int k, long index)
        {
            byte[] result = new byte[k];
            int a = n;
            int b = k;
            long x = index;

            int r = 0;
            for (int i = 1; i <= n && r < k; i++)
            {
                long c = Binomial(n - i, k - r - 1);
                if (x < c)
                {
                    result[r++] = (byte)i;
                }
                else
                {
                    x -= c;
                }
            }

            return result;
        }

        // Use a safe binomial function for large values
        public long Binomial(int n, int k)
        {
            if (k < 0 || k > n) return 0;
            if (k == 0 || k == n) return 1;

            long result = 1;
            for (int i = 1; i <= k; i++)
            {
                result *= n--;
                result /= i;
            }

            return result;
        }
        public int Count(int n, int k)
        {
			int result = 1;
			if (k > n - k) k = n - k;
			for (int i = 1; i <= k; i++)
            {
				result *= n - k + i;
				result /= i;
            }
			return result;
        }

		public void Shuffle(int[] array, int sum, CrossPlatformRandom random)
		{
			int n = sum;
			while (n > 1)
			{
				n--;
				int k = random.Next(n + 1);
				int value = array[k];
				array[k] = array[n];
				array[n] = value;
			}
		}

        public void ParallelShuffle(int[] array, CrossPlatformRandom random)
        {
            Parallel.For(0, array.Length / 2, i =>
            {
                int j = random.Next(array.Length);
                int temp = array[i];
                array[i] = array[j];
                array[j] = temp;
            });
        }

        public IEnumerable<int> LcgShuffle(int n, int seed)
        {
            const int Multiplier = 1664525; // Common multiplier
            const int Increment = 1013904223; // Common increment
            const int Modulus = (int)(1 << 31); // 2^31

            int x = seed;
            for (int i = 0; i < n; i++)
            {
                x = (Multiplier * x + Increment) % Modulus;
                yield return x % n; // Generate pseudo-random index
            }
        }
        public int CalculateSeed(string input)
        {
            // Calculate the SHA-256 hash
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] inputBytes = Encoding.UTF8.GetBytes(input);
                byte[] hashBytes = sha256.ComputeHash(inputBytes);

                // Convert the first 4 bytes of the hash to an integer and take modulus
                int hashInteger = BitConverter.ToInt32(hashBytes, 0);
                return hashInteger;
            }
        }

    }
}
