using System.Collections.Generic;
using System.Linq;
using static BGA.Macros;

namespace BGA
{
    internal static class Extensions
    {
        internal static Player Next(this Player player)
        {
            int p = (int)player + 1;
            return (p > 3) ? Player.North : (Player)p;
        }

        internal static Player Prev(this Player player)
        {
            int p = (int)player - 1;
            return (p < 0) ? Player.West : (Player)p;
        }

        internal static string Parse(this IEnumerable<Card> hand)
        {
            return string.Join(".", Enumerable.Range(0, 4)
                .Select(x => hand.Where(c => (int)c.Suit == x)
                    .OrderByDescending(c => c.Value())
                .Select(c => c.Rank)).Reverse()
                .Select(s => string.Concat(s)));
        }

        internal static List<Card> Parse(this string pbn)
        {
            string[] cards = pbn.Split('.');
            return Enumerable.Range(0, 4).SelectMany(
                x => cards[x].ToCharArray(), (x, y) =>
                Card.Parse($"{y}{"SHDC"[x]}")).ToList();
        }
    }
}
