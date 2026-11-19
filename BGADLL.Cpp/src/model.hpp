// C++ port of BGADLL's model classes (Card, Hand, Play, Constraints, Utils,
// CrossPlatformRandom). Semantics follow the C# sources in ../BGADLL line by line
// where they are observable through the C ABI (list order, Distinct-preserving
// AddRange, ToString formats, exceptions on bad input), so that a PIMC run makes
// the same samples, in the same order, as the C# library.
//
// Hot paths do not use these classes: they work on 64-bit card masks (see Mask
// below). A card's bit is suit * 16 + (rank - 2), suits in BGADLL order
// (Club = 0, Diamond = 1, Heart = 2, Spade = 3).
#pragma once

#include <algorithm>
#include <cstdint>
#include <stdexcept>
#include <string>
#include <vector>

namespace bga {

enum Suit : int { Club = 0, Diamond = 1, Heart = 2, Spade = 3 };
enum Trump : int { TClub = 0, TDiamond = 1, THeart = 2, TSpade = 3, TNo = 4 };
enum Player : int { North = 0, East = 1, South = 2, West = 3 };

inline Player next(Player p) { return Player((int(p) + 1) & 3); }
inline Player prev(Player p) { return Player((int(p) + 3) & 3); }

// 2..14, or 0 for a character that is not a rank (C# throws KeyNotFound on use).
inline int rank_value(char r) {
  switch (r) {
    case '2': return 2; case '3': return 3; case '4': return 4; case '5': return 5;
    case '6': return 6; case '7': return 7; case '8': return 8; case '9': return 9;
    case 'T': return 10; case 'J': return 11; case 'Q': return 12; case 'K': return 13;
    case 'A': return 14;
    default: return 0;
  }
}

inline char rank_char(int v) { return "??23456789TJQKA"[v]; }

struct Card {
  char rank = '?';
  int suit = 0;  // BGADLL Suit; -1 if parsed from a character outside "CDHS"

  Card() = default;
  Card(char r, int s) : rank(r), suit(s) {}

  // new Card("AH"): rank as given, suit must be one of C H S D.
  static Card from_string(const std::string& s) {
    if (s.size() < 2) throw std::invalid_argument("Invalid card");
    int suit;
    switch (s[1]) {
      case 'C': suit = Club; break;
      case 'H': suit = Heart; break;
      case 'S': suit = Spade; break;
      case 'D': suit = Diamond; break;
      default: throw std::invalid_argument("Invalid suit character");
    }
    return Card(s[0], suit);
  }

  // Card.Parse: rank upper-cased, suit by its index in "CDHS".
  static Card parse(char r, char suit_char) {
    const char* p = "CDHS";
    int s = -1;
    for (int i = 0; i < 4; i++)
      if (p[i] == suit_char) s = i;
    if (r >= 'a' && r <= 'z') r = char(r - 'a' + 'A');
    return Card(r, s);
  }

  int value() const {
    int v = rank_value(rank);
    if (v == 0) throw std::out_of_range("Invalid rank");
    return v;
  }
  int hcp() const {
    int v = value();
    return v >= 11 ? v - 10 : 0;
  }
  int bit() const { return suit * 16 + value() - 2; }
  uint64_t mask() const { return uint64_t(1) << bit(); }

  std::string str() const {
    if (suit < 0 || suit > 3) throw std::out_of_range("Invalid suit");
    return std::string{rank, "CDHS"[suit]};
  }

  bool operator==(const Card& o) const { return rank == o.rank && suit == o.suit; }
};

// ----- masks -----
using Mask = uint64_t;
constexpr Mask kSuitBits = 0x1FFF;

inline int popcount(Mask m) { return __builtin_popcountll(m); }
inline int suit_count(Mask m, int suit) { return popcount((m >> (16 * suit)) & kSuitBits); }

// High card points of a mask: J = 1, Q = 2, K = 3, A = 4 (bits 9..12 of each suit).
inline int mask_hcp(Mask m) {
  constexpr Mask J = 0x0200020002000200ull, Q = J << 1, K = J << 2, A = J << 3;
  return popcount(m & J) + 2 * popcount(m & Q) + 3 * popcount(m & K) + 4 * popcount(m & A);
}

// Hand.ToString of a mask: "S.H.D.C", high cards first.
inline void append_mask_pbn(std::string& out, Mask m) {
  for (int s = 3; s >= 0; s--) {
    unsigned h = unsigned(m >> (16 * s)) & kSuitBits;
    for (int r = 12; r >= 0; r--)
      if (h >> r & 1) out.push_back(rank_char(r + 2));
    if (s > 0) out.push_back('.');
  }
}

// ----- Hand / Play -----
// A list of cards with the C# collection semantics (duplicates allowed by Add,
// removed by AddRange's Distinct, order kept).
struct CardList {
  std::vector<Card> cards;

  void clear() { cards.clear(); }
  void add(const Card& c) { cards.push_back(c); }
  int count() const { return int(cards.size()); }

  // Cards = Cards.Concat(other).Distinct().ToList()
  void add_range(const std::vector<Card>& other) {
    std::vector<Card> r;
    r.reserve(cards.size() + other.size());
    auto push = [&](const Card& c) {
      for (const Card& x : r)
        if (x == c) return;
      r.push_back(c);
    };
    for (const Card& c : cards) push(c);
    for (const Card& c : other) push(c);
    cards.swap(r);
  }

  bool remove(const Card& c) {
    for (size_t i = 0; i < cards.size(); i++)
      if (cards[i] == c) {
        cards.erase(cards.begin() + long(i));
        return true;
      }
    return false;
  }

  // ToString: suits S, H, D, C joined by '.', ranks by value descending (stable).
  std::string str() const {
    std::string out;
    for (int s = 3; s >= 0; s--) {
      std::vector<Card> in;
      for (const Card& c : cards)
        if (c.suit == s) in.push_back(c);
      std::stable_sort(in.begin(), in.end(), [](const Card& a, const Card& b) { return a.value() > b.value(); });
      for (const Card& c : in) out.push_back(c.rank);
      if (s > 0) out.push_back('.');
    }
    return out;
  }

  // ListAsString: card strings joined by ' '.
  std::string list_str() const {
    std::string out;
    for (size_t i = 0; i < cards.size(); i++) {
      if (i) out.push_back(' ');
      out += cards[i].str();
    }
    return out;
  }

  int hcp() const {
    int h = 0;
    for (const Card& c : cards) h += c.hcp();
    return h;
  }
  int suit_count(int s) const {
    int n = 0;
    for (const Card& c : cards) n += c.suit == s;
    return n;
  }
  bool has_suit(int s) const { return suit_count(s) > 0; }

  Mask mask() const {
    Mask m = 0;
    for (const Card& c : cards) m |= c.mask();
    return m;
  }
};

struct Hand : CardList {
  Hand() = default;
  explicit Hand(std::vector<Card> c) { cards = std::move(c); }

  Hand except(const Hand& other) const {
    Hand r(cards);
    for (const Card& c : other.cards)
      r.cards.erase(std::remove(r.cards.begin(), r.cards.end(), c), r.cards.end());
    return r;
  }
  Hand unite(const Hand& other) const {  // Union: Concat + Distinct
    Hand r;
    r.add_range(cards);
    r.add_range(other.cards);
    return r;
  }

  // Extensions.Parse: "S.H.D.C".
  static Hand parse(const std::string& pbn) {
    std::vector<std::string> parts(1);
    for (char ch : pbn) {
      if (ch == '.') parts.emplace_back();
      else parts.back().push_back(ch);
    }
    if (parts.size() != 4) throw std::invalid_argument("Invalid PBN string format");
    Hand h;
    for (int i = 0; i < 4; i++)
      for (char ch : parts[size_t(i)]) h.cards.push_back(Card::parse(ch, "SHDC"[i]));
    return h;
  }
};

struct Play : CardList {};

// ----- Constraints -----
struct Constraints {
  int MinClubs = 0, MaxClubs = 13, MinDiamonds = 0, MaxDiamonds = 13;
  int MinHearts = 0, MaxHearts = 13, MinSpades = 0, MaxSpades = 13;
  int MinHCP = 0, MaxHCP = 37;

  Constraints() = default;
  Constraints(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j)
      : MinClubs(a), MaxClubs(b), MinDiamonds(c), MaxDiamonds(d), MinHearts(e), MaxHearts(f),
        MinSpades(g), MaxSpades(h), MinHCP(i), MaxHCP(j) {}

  // this[suit, sel]: sel 0 = min, 1 = max.
  int& at(int suit, int sel) {
    static int dummy;
    switch (suit) {
      case Club: return sel ? MaxClubs : MinClubs;
      case Diamond: return sel ? MaxDiamonds : MinDiamonds;
      case Heart: return sel ? MaxHearts : MinHearts;
      case Spade: return sel ? MaxSpades : MinSpades;
    }
    dummy = 0;
    return dummy;
  }
  int get(int suit, int sel) const { return const_cast<Constraints*>(this)->at(suit, sel); }

  std::string str() const {
    return std::to_string(MinClubs) + " " + std::to_string(MaxClubs) + " " + std::to_string(MinDiamonds) + " " +
           std::to_string(MaxDiamonds) + " " + std::to_string(MinHearts) + " " + std::to_string(MaxHearts) + " " +
           std::to_string(MinSpades) + " " + std::to_string(MaxSpades) + " " + std::to_string(MinHCP) + " " +
           std::to_string(MaxHCP);
  }

  int* prop(int index) {
    int* p[10] = {&MinClubs, &MaxClubs, &MinDiamonds, &MaxDiamonds, &MinHearts,
                  &MaxHearts, &MinSpades, &MaxSpades, &MinHCP, &MaxHCP};
    return index >= 0 && index < 10 ? p[index] : nullptr;
  }
};

// The C# Ignore(): HCP outside [MinHCP, MaxHCP] or a suit length outside its range.
inline bool ignore(Mask hand, const Constraints& c) {
  int hcp = mask_hcp(hand);
  if (hcp < c.MinHCP || hcp > c.MaxHCP) return true;
  for (int s = 0; s < 4; s++) {
    int n = suit_count(hand, s);
    if (n < c.get(s, 0) || n > c.get(s, 1)) return true;
  }
  return false;
}

// Hand.getOdds of a hand given by its suit lengths (S, H, D, C order does not matter).
double shape_odds(Mask hand);

// ----- Utils -----
// SplitMix32, as CrossPlatformRandom.
struct CrossPlatformRandom {
  uint32_t state;
  explicit CrossPlatformRandom(int seed) : state(uint32_t(seed)) {}
  uint32_t next_u32() {
    state += 0x9E3779B9u;
    uint32_t z = state;
    z = (z ^ (z >> 16)) * 0x85EBCA6Bu;
    z = (z ^ (z >> 13)) * 0xC2B2AE35u;
    return z ^ (z >> 16);
  }
  int next(int max_value) {
    if (max_value <= 0) return 0;
    return int((uint64_t(next_u32()) * uint64_t(max_value)) >> 32);
  }
};

// Utils.Binomial (exact for the sizes used: n <= 52).
int64_t binomial(int n, int k);

// Utils.GetCombinationAtIndex: the k (1-based) indexes of combination `index` of n.
void combination_at(int n, int k, int64_t index, uint8_t* out);

// Utils.CalculateSeed: the first 4 bytes of SHA-256(UTF-8 input), little-endian.
int calculate_seed(const std::string& input);

// Utils.Shuffle (Fisher-Yates from the end).
void shuffle(std::vector<int>& a, int n, CrossPlatformRandom& rnd);

// The number of processors as .NET's Environment.ProcessorCount sees it (affinity).
int processor_count();

}  // namespace bga
