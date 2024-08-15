using BGADLL;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using static BGADLL.Macros;

namespace BGADLL
{

    public class Play
    {
        public List<Card> Cards { get; private set; }

        public Play()
        {
            Cards = new List<Card>();
        }

        public Play(IEnumerable<Card> cards)
        {
            Cards = new List<Card>(cards);
        }

        // Method to clear the hand
        public void Clear()
        {
            Cards.Clear();
        }

        // Method to add a card to the hand
        public void Add(Card card)
        {
            Cards.Add(card);
        }

        // Method to add a range of cards to the hand
        public void AddRange(Play play)
        {
            this.AddRange(play.Cards);
        }

        // Method to add a range of cards to the hand
        public void AddRange(List<Card> cards)
        {
            Cards = Cards.Concat(cards).Distinct().ToList();
        }

        // Method to remove a card from the hand
        public bool Remove(Card card)
        {
            return Cards.Remove(card);
        }

        // Property to get the number of cards current_trick
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

        public Card this[int index]
        {
            get
            {
                if (index < 0 || index >= Cards.Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index), "Index is out of range");
                }
                return Cards[index];
            }
            set
            {
                if (index < 0 || index >= Cards.Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index), "Index is out of range");
                }
                Cards[index] = value;
            }
        }
        // Method to get the last card in the hand
        public Card Last()
        {
            if (Cards.Count > 0)
            {
                return Cards[Cards.Count - 1];
            }
            else
            {
                throw new InvalidOperationException("Play is empty");
            }
        }

        public Card First(Func<Card, bool> predicate = null)
        {
            if (predicate != null)
            {
                var filteredCards = Cards.Where(predicate);
                if (filteredCards.Any())
                {
                    return filteredCards.First();
                }
                else
                {
                    throw new InvalidOperationException("No card in the play satisfies the predicate");
                }
            }
            else
            {
                if (Cards.Count > 0)
                {
                    return Cards[0];
                }
                else
                {
                    throw new InvalidOperationException("Hand is empty");
                }
            }
        }

        public IEnumerator<Card> GetEnumerator()
        {
            return Cards.GetEnumerator();
        }

        public int Sum(Func<Card, int> selector)
        {
            return Cards.Sum(selector);
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
            return string.Join(" ",Cards.ToList());
        }

    }
}