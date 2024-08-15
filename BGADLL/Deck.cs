using BGADLL;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using static BGADLL.Macros;

namespace BGADLL
{

    public class Deck
    {
        public List<Card> Cards { get; private set; }

        public Deck()
        {
            Cards = new List<Card>();
        }

        public Deck(IEnumerable<Card> cards)
        {
            Cards = new List<Card>(cards);
        }

        // Method to clear the deck
        public void Clear()
        {
            Cards.Clear();
        }

        // Method to add a card to the deck
        public void Add(Card card)
        {
            Cards.Add(card);
        }

        // Method to add a range of cards to the deck
        public void AddRange(Hand hand)
        {
            this.AddRange(hand.Cards);
        }

        // Method to add a range of cards to the hand
        public void AddRange(List<Card> cards)
        {
            Cards = Cards.Concat(cards).Distinct().ToList();
        }

        // Property to get the number of cards in the hand
        public int Count => Cards.Count;

        public int CardsInSuit(Func<Card, bool> predicate)
        {
            return Cards.Count(predicate);
        }

        // Expose LINQ methods on the underlying list
        public List<Card> Select(Func<Card, bool> predicate)
        {
            return Cards.Where(predicate).ToList();
        }

        public List<Card> Where(Func<Card, bool> predicate)
        {
            return Cards.Where(predicate).ToList();
        }

        public List<TResult> Select<TResult>(Func<Card, TResult> selector)
        {
            return Cards.Select(selector).ToList();
        }

        // Method to merge two hands and return a new hand containing all distinct cards
        public Deck Union(Deck other)
        {
            return new Deck(Cards.Concat(other.Cards).Distinct());
        }

        public IEnumerator<Card> GetEnumerator()
        {
            return Cards.GetEnumerator();
        }

        public override string ToString()
        {
            return string.Join(".", Enumerable.Range(0, 4)
                 .Select(x => Cards.Where(c => (int)c.Suit == x)
                     .OrderByDescending(c => c.Value())
                 .Select(c => c.Rank)).Reverse()
                 .Select(s => string.Concat(s)));
        }
        public bool Any(Func<Card, bool> predicate)
        {
            return Cards.Any(predicate);
        }

        public string ListAsString()
        {
            return string.Join(" ", Cards.ToList());
        }

    }
}