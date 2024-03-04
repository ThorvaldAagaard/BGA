using System;
using System.Collections.Generic;
using System.Linq;
using static BGADLL.Macros;

namespace BGADLL
{
    public static class Extensions
    {
        public static Player Next(this Player player)
        {
            int p = (int)player + 1;
            return (p > 3) ? Player.North : (Player)p;
        }

        public static Player Prev(this Player player)
        {
            int p = (int)player - 1;
            return (p < 0) ? Player.West : (Player)p;
        }

        public static Hand Parse(this string pbn)
        {
            string[] parts = pbn.Split('.');
            if (parts.Length != 4)
            {
                throw new ArgumentException("Invalid PBN string format", nameof(pbn));
            }

            var parsedCards = parts.Select((part, index) =>
            {
                char suitChar = "SHDC"[index];
                return part.Select(card => Card.Parse(card.ToString() + suitChar));
            }).SelectMany(cards => cards);

            return new Hand(parsedCards);
        }
    }
}
