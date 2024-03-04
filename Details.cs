using System;
using static BGA.Macros;

namespace BGA
{
    internal class Details : ICloneable
    {
        internal int MinClubs { get; set; }
        internal int MaxClubs { get; set; }
        internal int MinDiamonds { get; set; }
        internal int MaxDiamonds { get; set; }
        internal int MinHearts { get; set; }
        internal int MaxHearts { get; set; }
        internal int MinSpades { get; set; }
        internal int MaxSpades { get; set; }
        internal int MinHCP { get; set; }
        internal int MaxHCP { get; set; }

        public Details(int minClubs, int maxClubs, int minDiamonds,
            int maxDiamonds, int minHearts, int maxHearts,
            int minSpades, int maxSpades, int minHcp, int maxHcp)
        {
            this.MinClubs = minClubs;
            this.MaxClubs = maxClubs;
            this.MinDiamonds = minDiamonds;
            this.MaxDiamonds = maxDiamonds;
            this.MinHearts = minHearts;
            this.MaxHearts = maxHearts;
            this.MinSpades = minSpades;
            this.MaxSpades = maxSpades;
            this.MinHCP = minHcp;
            this.MaxHCP = maxHcp;
        }

        public int this[Suit suit, int sel]
        {
            get
            {
                if (suit == Suit.Club && sel == 0) return this.MinClubs;
                if (suit == Suit.Club && sel == 1) return this.MaxClubs;
                if (suit == Suit.Diamond && sel == 0) return this.MinDiamonds;
                if (suit == Suit.Diamond && sel == 1) return this.MaxDiamonds;
                if (suit == Suit.Heart && sel == 0) return this.MinHearts;
                if (suit == Suit.Heart && sel == 1) return this.MaxHearts;
                if (suit == Suit.Spade && sel == 0) return this.MinSpades;
                if (suit == Suit.Spade && sel == 1) return this.MaxSpades;
                return 0;
            }
            set
            {
                if (suit == Suit.Club && sel == 0) this.MinClubs = value;
                if (suit == Suit.Club && sel == 1) this.MaxClubs = value;
                if (suit == Suit.Diamond && sel == 0) this.MinDiamonds = value;
                if (suit == Suit.Diamond && sel == 1) this.MaxDiamonds = value;
                if (suit == Suit.Heart && sel == 0) this.MinHearts = value;
                if (suit == Suit.Heart && sel == 1) this.MaxHearts = value;
                if (suit == Suit.Spade && sel == 0) this.MinSpades = value;
                if (suit == Suit.Spade && sel == 1) this.MaxSpades = value;
            }
        }

        public object Clone() => this.MemberwiseClone();

        internal Details Copy() => (Details)this.Clone();

        public override string ToString()
        {
            return $"{MinClubs} {MaxClubs} {MinDiamonds} {MaxDiamonds} {MinHearts}"
                + $" {MaxHearts} {MinSpades} {MaxSpades} {MinHCP} {MaxHCP}";
        }
    }
}
