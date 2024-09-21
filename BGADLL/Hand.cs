using BGADLL;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using static BGADLL.Macros;

namespace BGADLL
{

    public class Hand
    {
        static readonly Dictionary<int, (long value, double percentage)> dataDict = new Dictionary<int, (long value, double percentage)>
    {
        {4432, (136852887600, 21.551227)},
        {5332, (98534079072, 15.516802)},
        {5431, (82111732560, 12.930688)},
        {5422, (67182326640, 10.579684)},
        {4333, (66905856160, 10.536052)},
        {6322, (35830574208, 5.642537)},
        {6421, (29858811840, 4.702125)},
        {6331, (21896462016, 3.448233)},
        {5521, (20154697992, 3.173856)},
        {4441, (19007345500, 2.993176)},
        {7321, (11943524736, 1.880856)},
        {6430, (8421716160, 1.326197)},
        {5440, (7895358900, 1.243331)},
        {5530, (5684658408, 0.895202)},
        {6511, (4478821776, 0.705351)},
        {6520, (4134297024, 0.651107)},
        {7222, (3257324928, 0.513007)},
        {7411, (2488234320, 0.391817)},
        {7420, (2296831680, 0.361691)},
        {7330, (1684343232, 0.265180)},
        {8221, (1221496848, 0.192337)},
        {8311, (746470296, 0.117558)},
        {7510, (689049504, 0.108510)},
        {8320, (689049504, 0.108510)},
        {6610, (459366336, 0.072353)},
        {8410, (287103960, 0.045211)},
        {9211, (113101560, 0.017809)},
        {9310, (63800880, 0.010047)},
        {9220, (52200720, 0.008217)},
        {7600, (35335872, 0.005566)},
        {8500, (19876428, 0.003130)},
        {10210, (6960096, 0.001096)}, // A210 = 10210
        {9400, (6134700, 0.000966)},
        {10111, (2513368, 0.000396)}, // A111 = 10111
        {10300, (981552, 0.000154)}, // A300 = 10300
        {11110, (158184, 0.000025)}, // B110 = 11110
        {11200, (73008, 0.000011)}, // B200 = 11200
        {12100, (2028, 2028/635013559600)}, // C100 = 12100
        {13000, (4, 4/635013559600)}, // D000 = 13000
    };
        public List<Card> Cards { get; private set; }

        public Hand()
        {
            Cards = new List<Card>();
        }

        public Hand(IEnumerable<Card> cards)
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
        public void AddRange(Hand hand)
        {
            this.AddRange(hand.Cards);
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

        // Property to get the number of cards in the hand
        public int Count => Cards.Count;

        public int CardsInSuit(Func<Card, bool> predicate)
        {
            return Cards.Count(predicate);
        }

        public int GetShape()
        {
            var suitCounts = new List<int>
                {
                    CardsInSuit(c => c.Suit == Suit.Spade),
                    CardsInSuit(c => c.Suit == Suit.Heart),
                    CardsInSuit(c => c.Suit == Suit.Diamond),
                    CardsInSuit(c => c.Suit == Suit.Club)
                };

            var sortedCounts = suitCounts.OrderByDescending(count => count).ToList();
            return int.Parse(string.Join("", sortedCounts));
        }

        public string GetSuitCount()
        {
            var suitCounts = new List<int>
                {
                    CardsInSuit(c => c.Suit == Suit.Spade),
                    CardsInSuit(c => c.Suit == Suit.Heart),
                    CardsInSuit(c => c.Suit == Suit.Diamond),
                    CardsInSuit(c => c.Suit == Suit.Club)
                };

            return string.Join("=", suitCounts);
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

        // You can expose other LINQ methods as needed...
        // Indexer to access cards by index
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
                throw new InvalidOperationException("Hand is empty");
            }
        }

        // Method to merge two hands and return a new hand containing all distinct cards
        public Hand Union(Hand other)
        {
            return new Hand(Cards.Concat(other.Cards).Distinct());
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
                    throw new InvalidOperationException("No card in the hand satisfies the predicate");
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
        public Hand Concat(Hand other)
        {
            Hand concatenatedHand = new Hand();
            concatenatedHand.AddRange(this);
            concatenatedHand.AddRange(other);
            return concatenatedHand;
        }

        public Hand Except(Hand other)
        {
            Hand result = new Hand(this.Cards);
            foreach (var card in other.Cards)
            {
                result.Cards.RemoveAll(c => c.Equals(card));
            }
            return result;
        }

        public Hand Except(List<Card> cards)
        {
            Hand result = new Hand(this.Cards);
            foreach (var card in cards)
            {
                result.Cards.RemoveAll(c => c.Equals(card));
            }
            return result;
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
            return string.Join(" ", Cards.ToList());
        }

        public double getOdds()
        {
            int lookupValue = GetShape();
            if (dataDict.TryGetValue(lookupValue, out var result))
            { 
                return result.percentage / 100;
            }
            else
            {
                return 1;
            }
        }
    }


}