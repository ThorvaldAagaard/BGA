using System;
using System.Collections.Generic;
using static BGADLL.Macros;

namespace BGADLL
{
    using Values = Dictionary<char, int>;

    public class Card : IEquatable<Card>
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

        public char Rank => this.rank;
        public Suit Suit => this.suit;

        public Card(char rank, Suit suit)
        {
            this.rank = rank;
            this.suit = suit;
        }

        public Card(string card)
        {
            this.rank = card[0];
            char suitChar  = card[1];
            // Set the suit
            switch (suitChar)
            {
                case 'C':
                    this.suit = Suit.Club;
                    break;
                case 'H':
                    this.suit = Suit.Heart;
                    break;
                case 'S':
                    this.suit = Suit.Spade;
                    break;
                case 'D':
                    this.suit = Suit.Diamond;
                    break;
                default:
                    throw new ArgumentException("Invalid suit character");
            }
        }

        public int CompareTo(Card card)
        {
            return this.Value().CompareTo(card.Value());
        }

        public int HCP() => this.hcp[this.rank];

        public int Value() => this.ranks[this.rank];

        public static Card Parse(string card)
        {
            Suit suit = (Suit)"CDHS".IndexOf(card[1]);
            return new Card(char.ToUpper(card[0]), suit);
        }

        public override string ToString()
        {
            return string.Format("{0}{1}",
                this.rank, "CDHS"[(int)this.suit]);
        }

        public bool Equals(Card other)
        {
            // Check if 'other' is null
            if (other is null)
                return false;

            // Check if 'other' is the same instance as 'this'
            if (ReferenceEquals(this, other))
                return true;

            // Compare the properties of 'this' and 'other'
            return Rank == other.Rank && Suit == other.Suit;
        }

        // Optionally, override Object.Equals(object) and GetHashCode

        public override bool Equals(object obj)
        {
            if (obj is Card otherCard)
                return Equals(otherCard);
            else
                return false;
        }

        public override int GetHashCode()
        {
            int code = (int)Suit ^ Value();
            return code.GetHashCode();
        }
    }
}
