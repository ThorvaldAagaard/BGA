using System;
using System.Collections.Generic;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace BGADLL
{
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

		public void Shuffle(int[] array, int sum, Random random)
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

        public void ParallelShuffle(int[] array, Random random)
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
