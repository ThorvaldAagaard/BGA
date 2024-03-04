using System;
using System.Collections.Generic;

namespace BGA
{
    internal class Utils
    {
        internal IEnumerable<byte[]> Generate(int n, int k)
        {
            byte[] result = new byte[k];
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

        internal int Count(int n, int k)
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

        internal void Shuffle(int[] array, int sum, Random random)
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
    }
}
