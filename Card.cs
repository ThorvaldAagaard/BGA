using System.Collections.Generic;
using static BGA.Macros;

namespace BGA
{
    using Values = Dictionary<char, int>;

    internal class CardComparer : EqualityComparer<Card>
    {
        public override bool Equals(Card c1, Card c2)
        {
            string s1 = c1.ToString();
            string s2 = c2.ToString();
            return s1.Equals(s2);
        }

        public override int GetHashCode(Card card)
        {
            int code = (int)card.Suit ^ card.Value();
            return code.GetHashCode();
        }
    }

    internal class Card
    {
        private readonly char rank;
        private readonly Suit suit;
        private readonly Values hcp = new Values
        {
            { '2', 0 }, { '3', 0 }, { '4', 0 },
            { '5', 0 }, { '6', 0 }, { '7', 0 },
            { '8', 0 }, { '9', 0 }, { 'T', 0 },
            { 'J', 1 }, { 'Q', 2 },
            { 'K', 3 }, { 'A', 4 }
        };
        private readonly Values ranks = new Values
        {
            { '2', 2 }, { '3', 3 }, { '4', 4 },
            { '5', 5 }, { '6', 6 }, { '7', 7 },
            { '8', 8 }, { '9', 9 }, { 'T', 10 },
            { 'J', 11 }, { 'Q', 12 },
            { 'K', 13 }, { 'A', 14 }
        };

        internal char Rank => this.rank;
        internal Suit Suit => this.suit;

        internal Card(char rank, Suit suit)
        {
            this.rank = rank;
            this.suit = suit;
        }

        internal int CompareTo(Card card)
        {
            return this.Value().CompareTo(card.Value());
        }

        internal int HCP() => this.hcp[this.rank];

        internal int Value() => this.ranks[this.rank];

        internal static Card Parse(string card)
        {
            Suit suit = (Suit)"CDHS".IndexOf(card[1]);
            return new Card(char.ToUpper(card[0]), suit);
        }

        public override string ToString()
        {
            return string.Format("{0}{1}",
                this.rank, "CDHS"[(int)this.suit]);
        }
    }
}
