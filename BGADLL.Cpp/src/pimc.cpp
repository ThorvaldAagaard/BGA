#include "pimc.hpp"

#include <algorithm>
#include <chrono>
#include <cstdio>
#include <deque>
#include <stdexcept>
#include <thread>

#include "dds.hpp"

namespace bga {

namespace {

// One pool of worker threads for the process, grown on demand and never shrunk. Threads
// live as long as the process, so ddfast's per-thread search state is set up once.
class Pool {
 public:
  void submit(std::function<void()> job) {
    std::unique_lock<std::mutex> l(m_);
    jobs_.push_back(std::move(job));
    if (idle_ >= int(jobs_.size())) {
      cv_.notify_one();
    } else {
      std::thread([this] { loop(); }).detach();
    }
  }

 private:
  void loop() {
    std::unique_lock<std::mutex> l(m_);
    for (;;) {
      idle_++;
      cv_.wait(l, [&] { return !jobs_.empty(); });
      idle_--;
      auto job = std::move(jobs_.front());
      jobs_.pop_front();
      l.unlock();
      job();
      l.lock();
    }
  }
  std::mutex m_;
  std::condition_variable cv_;
  std::deque<std::function<void()>> jobs_;
  int idle_ = 0;
};

Pool& pool() {
  static Pool* p = new Pool();  // never destroyed: detached threads may outlive static destructors
  return *p;
}

std::string join(const std::vector<std::string>& v, const char* sep) {
  std::string s;
  for (size_t i = 0; i < v.size(); i++) {
    if (i) s += sep;
    s += v[i];
  }
  return s;
}

}  // namespace

// ===== CardTricks =====

void CardTricks::clear() {
  std::lock_guard<std::mutex> l(m_);
  keys_.clear();
  values_.clear();
}

int CardTricks::add_key(const std::string& card) {
  std::lock_guard<std::mutex> l(m_);
  for (size_t i = 0; i < keys_.size(); i++)
    if (keys_[i] == card) return int(i);
  keys_.push_back(card);
  values_.emplace_back();
  return int(keys_.size() - 1);
}

void CardTricks::append(int key, const Entry& e) { values_[size_t(key)].push_back(e); }

void CardTricks::sort() {
  std::lock_guard<std::mutex> l(m_);
  for (auto& v : values_) {
    std::stable_sort(v.begin(), v.end(), [](const Entry& a, const Entry& b) {
      if (a.combination != b.combination) return a.combination < b.combination;
      if (a.tricks != b.tricks) return a.tricks < b.tricks;
      return a.weight < b.weight;
    });
    std::reverse(v.begin(), v.end());
  }
}

std::vector<CardTricks::Entry> CardTricks::get(const std::string& card) {
  std::lock_guard<std::mutex> l(m_);
  for (size_t i = 0; i < keys_.size(); i++)
    if (keys_[i] == card) return values_[i];
  return {};
}

// ===== Engine =====

Engine::Engine(int max_threads, bool verbose, const char* name)
    : threads_([&] {
        int t = std::max(1, processor_count() - 2);
        if (max_threads > 0) t = std::min(max_threads, t);
        return t;
      }()),
      verbose_(verbose),
      free_(threads_) {
  if (verbose) std::printf("%s Loaded - Threads: %d\n", name, threads_);
}

Engine::~Engine() {
  std::unique_lock<std::mutex> l(m_);
  evaluate_ = false;
  cv_.wait(l, [&] { return active_.load() == 0; });
}

std::string Engine::legal_moves_to_string() const { return join(legal_strings_, ", "); }

void Engine::set_legal(const std::vector<Card>& legal) {
  legal_ = legal;
  legal_strings_.clear();
  for (const Card& c : legal) legal_strings_.push_back(c.str());
}

std::vector<int> Engine::output_keys() {
  std::vector<int> keys;
  for (const std::string& s : legal_strings_) keys.push_back(output_.add_key(s));
  return keys;
}

void Engine::load_combinations(int n, int k) {
  n_combinations_ = int(binomial(n, k));
  comb_n_ = n;
  comb_k_ = k;
  combination_index_.resize(size_t(n_combinations_));
  for (int i = 0; i < n_combinations_; i++) combination_index_[size_t(i)] = i;
  shuffle(combination_index_, n_combinations_, random_);
}

void Engine::clear_common() {
  output_.clear();
  n_combinations_ = 0;
  examined_ = 0;
  head_ = queue_.size();  // drain the queue
}

void Engine::start(const std::vector<int>& samples, std::function<bool()> stop_check,
                   std::function<void(int)> body) {
  {
    std::unique_lock<std::mutex> l(m_);
    cv_.wait(l, [&] { return active_.load() == 0; });  // the queue is ours alone
    size_t h = std::min(head_.load(), queue_.size());
    queue_.erase(queue_.begin(), queue_.begin() + long(h));
    head_ = 0;
    queue_.insert(queue_.end(), samples.begin(), samples.end());
    evaluate_ = true;
    free_ = threads_;
    active_ = threads_;
  }
  for (int t = 0; t < threads_; t++) {
    pool().submit([this, stop_check, body] {
      free_--;
      try {
        while (evaluate_.load() && head_.load() < queue_.size()) {
          if (stop_check && stop_check()) {
            evaluate_ = false;
            continue;
          }
          size_t i = head_.fetch_add(1);
          if (i >= queue_.size()) break;
          body(queue_[i]);
        }
      } catch (const std::exception& e) {
        std::fprintf(stderr, "BGADLL: evaluation error: %s\n", e.what());
      }
      free_++;
      {
        std::lock_guard<std::mutex> l(m_);
        active_--;
        if (head_.load() >= queue_.size()) evaluate_ = false;
      }
      cv_.notify_all();
    });
  }
}

void Engine::await_evaluation(int max_wait_ms) {
  auto start = std::chrono::steady_clock::now();
  std::unique_lock<std::mutex> l(m_);
  cv_.wait_until(l, start + std::chrono::milliseconds(max_wait_ms), [&] { return !evaluate_.load(); });
  evaluate_ = false;
  cv_.wait(l, [&] { return active_.load() == 0; });
  if (verbose_)
    std::printf("Playouts %d Execution time %.3f Examined %d\n", playouts_.load(),
                std::chrono::duration<double>(std::chrono::steady_clock::now() - start).count(), examined_.load());
}

void Engine::end_evaluate() {
  if (verbose_) std::printf("EndEvaluation\n");
  evaluate_ = false;
}

// ===== PIMC =====

void PIMC::clear() {
  commands_.clear();
  north_.clear();
  south_.clear();
  east_.clear();
  west_.clear();
  remaining_.clear();
  clear_common();
}

std::vector<Card> PIMC::legit_moves(Player p) const {
  const Hand& cards = p == North ? north_ : p == South ? south_ : remaining_;
  if (current_trick_.count() == 0) return cards.cards;
  std::vector<Card> moves;
  for (const Card& c : cards.cards)
    if (c.suit == current_trick_.cards[0].suit) moves.push_back(c);
  return moves.empty() ? cards.cards : moves;
}

void PIMC::validate_input() {
  CardList deck;
  deck.add_range(north_.cards);
  deck.add_range(south_.cards);
  deck.add_range(east_.cards);
  deck.add_range(west_.cards);
  deck.add_range(remaining_.cards);
  deck.add_range(current_trick_.cards);
  if (deck.count() != north_.count() + south_.count() + east_.count() + west_.count() + remaining_.count() +
                          current_trick_.count()) {
    std::printf("Deck %s\n", deck.str().c_str());
    throw std::runtime_error("Duplicate cards in deck: " + deck.str());
  }
  if (deck.count() % 4 != 0) {
    std::printf("Deck %s\n", deck.str().c_str());
    throw std::runtime_error("Wrong number of cards: " + deck.str());
  }
  int cards_left = deck.count() / 4;
  if (north_.count() > cards_left || north_.count() < cards_left - 1) {
    std::printf("Wrong number of cards in North %s\n", north_.str().c_str());
    throw std::runtime_error("Wrong number of cards in North");
  }
  if (south_.count() > cards_left || south_.count() < cards_left - 1) {
    std::printf("Wrong number of cards in South %s\n", south_.str().c_str());
    throw std::runtime_error("Wrong number of cards in South");
  }
  int hcp = remaining_.hcp();
  if (east_c_.MinHCP + west_c_.MinHCP > hcp) {
    if (verbose_) std::printf("Constraints not possible - Min HCP %d %d\n", east_c_.MinHCP + west_c_.MinHCP, hcp);
    east_c_.MinHCP = 0;
    west_c_.MinHCP = 0;
  }
  if (east_c_.MaxHCP + west_c_.MaxHCP < hcp) {
    if (verbose_) std::printf("Constraints not possible - Max HCP %d %d\n", east_c_.MaxHCP + west_c_.MaxHCP, hcp);
    east_c_.MaxHCP = 37;
    west_c_.MaxHCP = 37;
  }
  int mn = 0, mx = 0;
  for (int s = 0; s <= 3; s++) {
    mn += east_c_.get(s, 0) + west_c_.get(s, 0);
    mx += east_c_.get(s, 1) + west_c_.get(s, 1);
  }
  if (mn > remaining_.count() || remaining_.count() > mx) {
    east_c_ = Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
    west_c_ = Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
  }
}

void PIMC::update_constraints() {
  for (int s = 0; s <= 3; s++) {
    int mn = east_c_.get(s, 0) + west_c_.get(s, 0);
    int mx = east_c_.get(s, 1) + west_c_.get(s, 1);
    int count = remaining_.suit_count(s);
    if (count == 0) {
      east_c_.at(s, 0) = 0;
      west_c_.at(s, 0) = 0;
      east_c_.at(s, 1) = 0;
      west_c_.at(s, 1) = 0;
    } else if (count < mn || count > mx) {
      if (verbose_) std::printf("Constraints not possible - Suit lengths %d count=%d min=%d max=%d\n", s, count, mn, mx);
      east_c_.at(s, 0) = 0;
      west_c_.at(s, 0) = 0;
      east_c_.at(s, 1) = 37;
      west_c_.at(s, 1) = 37;
    }
  }
}

void PIMC::setup(const std::vector<Hand>& our, const Hand& oppos, const Play& current_trick,
                 const Play& previous_tricks, const Constraints consts[2], Player next_to_lead, int max_playout,
                 bool autoplay_singleton, bool use_strategy) {
  current_trick_ = current_trick;
  previous_tricks_ = previous_tricks;
  commands_ = current_trick.cards;
  east_c_ = consts[0];
  west_c_ = consts[1];
  north_.add_range(our.at(0).cards);
  south_.add_range(our.at(1).cards);
  if (our.size() > 2) {
    east_shown_.add_range(our.at(2).cards);
    west_shown_.add_range(our.at(3).cards);
  }
  remaining_.add_range(oppos.cards);
  validate_input();
  max_playout_ = max_playout;
  use_fusion_ = use_strategy;
  set_legal(legit_moves(next_to_lead));
  if (autoplay_singleton && legal_.size() == 1) evaluate_ = false;
  for (const std::string& s : legal_strings_) output_.add_key(s);

  Player player = next_to_lead;
  for (auto it = current_trick.cards.rbegin(); it != current_trick.cards.rend(); ++it) {
    const Card& lead = *it;
    player = prev(player);
    switch (player) {
      case North: north_.add(lead); break;
      case South: south_.add(lead); break;
      case East:
        east_.add(lead);
        remaining_.add(lead);
        east_c_.at(lead.suit, 1) += 1;
        east_c_.MaxHCP += lead.hcp();
        east_c_.MinHCP += lead.hcp();
        break;
      case West:
        west_.add(lead);
        remaining_.add(lead);
        west_c_.at(lead.suit, 1) += 1;
        west_c_.MaxHCP += lead.hcp();
        west_c_.MinHCP += lead.hcp();
        break;
    }
  }
  update_constraints();
  playouts_ = 0;
  leader_ = player;

  seed_ = calculate_seed(north_.str() + south_.str());
  random_ = CrossPlatformRandom(seed_);
  load_combinations(remaining_.count(), remaining_.count() / 2);

  north_m_ = north_.mask();
  south_m_ = south_.mask();
  east_m_ = east_.mask();
  west_m_ = west_.mask();
  east_shown_m_ = east_shown_.mask();
  west_shown_m_ = west_shown_.mask();
  rem_bits_.clear();
  rem_mask_ = 0;
  for (const Card& c : remaining_.cards) {
    rem_bits_.push_back(c.mask());
    rem_mask_ |= c.mask();
  }
}

std::vector<int> PIMC::find_samples() {
  std::vector<int> samples;
  const int nn = popcount(north_m_), ns = popcount(south_m_);
  uint8_t set[52];
  for (int i = 0; i < n_combinations_; i++) {
    if (playouts_.load(std::memory_order_relaxed) >= max_playout_) break;
    int idx = combination_index_[size_t(i)];
    combination_at(comb_n_, comb_k_, idx, set);
    examined_.fetch_add(1, std::memory_order_relaxed);
    Mask w = 0;
    for (int j = 0; j < comb_k_; j++) w |= rem_bits_[size_t(set[j] - 1)];
    Mask e = (rem_mask_ & ~w) | east_m_;
    w |= west_m_;
    int ne = popcount(e);
    if (!(nn == ne && ne == ns && ns == popcount(w))) continue;
    if (ignore(e, east_c_) || ignore(w, west_c_)) continue;
    samples.push_back(idx);
    playouts_.fetch_add(1, std::memory_order_relaxed);
  }
  if (verbose_) std::printf("Examined %d Playout %d\n", examined_.load(), playouts_.load());
  return samples;
}

void PIMC::sample(int pos, Trump trump, const std::vector<int>& keys) {
  uint8_t set[52];
  combination_at(comb_n_, comb_k_, pos, set);
  Mask w = 0;
  for (int j = 0; j < comb_k_; j++) w |= rem_bits_[size_t(set[j] - 1)];
  Mask e = (rem_mask_ & ~w) | east_m_;
  w |= west_m_;
  double weight = shape_odds(east_shown_m_ | e) * shape_odds(west_shown_m_ | w);

  const Mask hands[4] = {north_m_, e, south_m_, w};
  DdsPosition dds(hands, trump, leader_);
  for (const Card& c : commands_) dds.execute(c);

  struct Out { int key; CardTricks::Entry e; };
  std::vector<Out> out(2 * legal_.size());
  size_t nout = 0;
  Player opposite = next(next(Player(leader_)));
  for (size_t m = 0; m < legal_.size(); m++) {
    const Card& card = legal_[m];
    int tricks = dds.tricks(card);
    out[nout++] = {keys[m], {uint8_t(tricks), weight, pos}};
    if (!use_fusion_) continue;
    int suit = card.suit;
    if (remaining_.count() > 2 && current_trick_.count() == 0 && suit_count(e, suit) > 0 &&
        suit_count(w, suit) > 0 && !ignore(e, west_c_) && !ignore(w, east_c_)) {
      DdsPosition d1 = dds.clone();
      const Mask reversed[4] = {north_m_, w, south_m_, e};
      DdsPosition d2(reversed, trump, leader_);
      d1.execute(card);
      d1.execute("x");
      d2.execute(card);
      d2.execute("x");
      const Hand& hand = opposite == North ? north_ : south_;
      std::vector<Card> next_moves;
      for (const Card& c : hand.cards)
        if (c.suit == suit) next_moves.push_back(c);
      if (next_moves.empty()) next_moves = hand.cards;
      int result = -1;
      for (const Card& nx : next_moves) result = std::max(result, std::min(d1.tricks(nx), d2.tricks(nx)));
      if (result > 13 || result < 0) throw std::runtime_error("fusion result out of range");
      out[nout++] = {keys[m], {uint8_t(result), weight, pos}};
    } else {
      out[nout++] = {keys[m], {uint8_t(tricks), weight, pos}};
    }
  }
  std::lock_guard<std::mutex> l(output_.lock());
  for (size_t i = 0; i < nout; i++) output_.append(out[i].key, out[i].e);
}

void PIMC::evaluate(Trump trump) {
  std::vector<int> samples = find_samples();
  if (trump != TNo) {  // trump cards first, as PIMC.cs reorders legalMoves
    std::vector<Card> moves = legal_;
    for (size_t i = 0; i < moves.size(); i++)
      if (moves[i].suit == int(trump)) {
        Card c = moves[i];
        moves.erase(moves.begin() + long(i));
        moves.insert(moves.begin(), c);
      }
    set_legal(moves);
  }
  std::vector<int> keys = output_keys();
  start(samples, nullptr, [this, trump, keys](int pos) { sample(pos, trump, keys); });
}

// ===== PIMCDef =====

void PIMCDef::clear() {
  commands_.clear();
  dummy_.clear();
  our_.clear();
  partner_.clear();
  declarer_.clear();
  remaining_.clear();
  clear_common();
}

std::vector<Card> PIMCDef::legit_moves(Player p, bool overdummy) const {
  const Hand* table[4];
  if (overdummy) {
    table[0] = &dummy_; table[1] = &our_; table[2] = &remaining_; table[3] = &remaining_;
  } else {
    table[0] = &dummy_; table[1] = &remaining_; table[2] = &remaining_; table[3] = &our_;
  }
  const Hand& cards = *table[p];
  if (current_trick_.count() == 0) return cards.cards;
  std::vector<Card> moves;
  for (const Card& c : cards.cards)
    if (c.suit == current_trick_.cards[0].suit) moves.push_back(c);
  return moves.empty() ? cards.cards : moves;
}

void PIMCDef::validate_input() {
  CardList deck;
  deck.add_range(dummy_.cards);
  deck.add_range(our_.cards);
  deck.add_range(partner_.cards);
  deck.add_range(declarer_.cards);
  deck.add_range(remaining_.cards);
  deck.add_range(current_trick_.cards);
  if (deck.count() != dummy_.count() + our_.count() + remaining_.count() + current_trick_.count()) {
    std::printf("Deck %s\n", deck.str().c_str());
    throw std::runtime_error("Duplicate cards");
  }
  if (deck.count() % 4 != 0) {
    std::printf("Deck %s\n", deck.str().c_str());
    throw std::runtime_error("Wrong number of cards: " + deck.str());
  }
  int cards_left = deck.count() / 4;
  if (dummy_.count() > cards_left || dummy_.count() < cards_left - 1) {
    std::printf("Wrong number of cards in dummy %s\n", dummy_.str().c_str());
    throw std::runtime_error("Wrong number of cards in dummy");
  }
  if (our_.count() > cards_left || our_.count() < cards_left - 1) {
    std::printf("Wrong number of cards in our hand %s\n", our_.str().c_str());
    throw std::runtime_error("Wrong number of cards in our hand");
  }
  int hcp = remaining_.hcp();
  if (declarer_c_.MinHCP + partner_c_.MinHCP > hcp) {
    if (verbose_) std::printf("Constraints not possible - Min HCP %d %d\n", declarer_c_.MinHCP + partner_c_.MinHCP, hcp);
    declarer_c_.MinHCP = 0;
    partner_c_.MinHCP = 0;
  }
  if (declarer_c_.MaxHCP + partner_c_.MaxHCP < hcp) {
    if (verbose_) std::printf("Constraints not possible - Max HCP %d %d\n", declarer_c_.MaxHCP + partner_c_.MaxHCP, hcp);
    declarer_c_.MaxHCP = 37;
    partner_c_.MaxHCP = 37;
  }
  int mn = 0, mx = 0;
  for (int s = 0; s <= 3; s++) {
    mn += declarer_c_.get(s, 0) + partner_c_.get(s, 0);
    mx += declarer_c_.get(s, 1) + partner_c_.get(s, 1);
  }
  if (mn > remaining_.count() || remaining_.count() > mx) {
    declarer_c_ = Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
    partner_c_ = Constraints(0, 13, 0, 13, 0, 13, 0, 13, 0, 37);
  } else {
    update_constraints();
  }
}

void PIMCDef::update_constraints() {
  for (int s = 0; s <= 3; s++) {
    int mn = declarer_c_.get(s, 0) + partner_c_.get(s, 0);
    int mx = declarer_c_.get(s, 1) + partner_c_.get(s, 1);
    int count = remaining_.suit_count(s);
    if (count == 0) {
      declarer_c_.at(s, 0) = 0;
      partner_c_.at(s, 0) = 0;
      declarer_c_.at(s, 1) = 0;
      partner_c_.at(s, 1) = 0;
    } else if (count < mn || count > mx) {
      if (verbose_) std::printf("Constraints not possible - Suit lengths %d count=%d min=%d max=%d\n", s, count, mn, mx);
      declarer_c_.at(s, 0) = 0;
      partner_c_.at(s, 0) = 0;
      declarer_c_.at(s, 1) = 37;
      partner_c_.at(s, 1) = 37;
    }
  }
}

std::string PIMCDef::setup(const std::vector<Hand>& our, const Hand& oppos, const Play& current_trick,
                           const Play& previous_tricks, const Constraints consts[2], Player leader, int max_playout,
                           bool autoplay_singleton, bool overdummy) {
  current_trick_ = current_trick;
  previous_tricks_ = previous_tricks;
  commands_ = current_trick.cards;
  declarer_c_ = consts[0];
  partner_c_ = consts[1];
  dummy_.add_range(our.at(0).cards);
  our_.add_range(our.at(1).cards);
  if (our.size() > 2) {
    partner_shown_.add_range(our.at(2).cards);
    declarer_shown_.add_range(our.at(3).cards);
  }
  remaining_.add_range(oppos.cards);
  validate_input();

  overdummy_ = overdummy;
  max_playout_ = max_playout;
  Player player = leader;
  set_legal(legit_moves(leader, overdummy));
  if (autoplay_singleton && legal_.size() == 1) return legal_strings_[0];
  for (const std::string& s : legal_strings_) output_.add_key(s);

  for (auto it = current_trick.cards.rbegin(); it != current_trick.cards.rend(); ++it) {
    const Card& lead = *it;
    player = prev(player);
    auto declarer_card = [&] {
      declarer_.add(lead);
      remaining_.add(lead);
      declarer_c_.at(lead.suit, 1) += 1;
      declarer_c_.MaxHCP += lead.hcp();
      declarer_c_.MinHCP += lead.hcp();
    };
    auto partner_card = [&] {
      partner_.add(lead);
      remaining_.add(lead);
      partner_c_.at(lead.suit, 1) += 1;
      partner_c_.MaxHCP += lead.hcp();
      partner_c_.MinHCP += lead.hcp();
    };
    switch (player) {
      case North: dummy_.add(lead); break;
      case South: declarer_card(); break;
      case East: if (overdummy) our_.add(lead); else partner_card(); break;
      case West: if (overdummy) partner_card(); else our_.add(lead); break;
    }
  }
  update_constraints();
  playouts_ = 0;
  leader_ = player;

  seed_ = calculate_seed(dummy_.str() + our_.str());
  random_ = CrossPlatformRandom(seed_);
  load_combinations(remaining_.count(), remaining_.count() / 2);

  dummy_m_ = dummy_.mask();
  our_m_ = our_.mask();
  partner_m_ = partner_.mask();
  declarer_m_ = declarer_.mask();
  rem_bits_.clear();
  rem_mask_ = 0;
  for (const Card& c : remaining_.cards) {
    rem_bits_.push_back(c.mask());
    rem_mask_ |= c.mask();
  }
  return "";
}

std::vector<int> PIMCDef::find_samples() {
  std::vector<int> samples;
  const int nd = popcount(dummy_m_), nw = popcount(our_m_);
  uint8_t set[52];
  for (int i = 0; i < n_combinations_; i++) {
    if (playouts_.load(std::memory_order_relaxed) >= max_playout_) break;
    int idx = combination_index_[size_t(i)];
    combination_at(comb_n_, comb_k_, idx, set);
    examined_.fetch_add(1, std::memory_order_relaxed);
    Mask p = 0;
    for (int j = 0; j < comb_k_; j++) p |= rem_bits_[size_t(set[j] - 1)];
    Mask d = (rem_mask_ & ~p) | declarer_m_;
    p |= partner_m_;
    // N E S W lengths all equal (E/W are partner and us, in either order)
    int np = popcount(p), ndecl = popcount(d);
    if (!(nd == np && np == ndecl && ndecl == nw)) continue;
    if (ignore(d, declarer_c_) || ignore(p, partner_c_)) continue;
    samples.push_back(idx);
    playouts_.fetch_add(1, std::memory_order_relaxed);
  }
  if (verbose_) std::printf("Examined %d Playout %d\n", examined_.load(), playouts_.load());
  return samples;
}

void PIMCDef::sample(int pos, Trump trump, const std::vector<int>& keys) {
  uint8_t set[52];
  combination_at(comb_n_, comb_k_, pos, set);
  examined_.fetch_add(1, std::memory_order_relaxed);
  Mask p = 0;
  for (int j = 0; j < comb_k_; j++) p |= rem_bits_[size_t(set[j] - 1)];
  Mask d = (rem_mask_ & ~p) | declarer_m_;
  p |= partner_m_;
  if (!(popcount(dummy_m_) == popcount(our_m_) && popcount(p) == popcount(d) && popcount(d) == popcount(our_m_)))
    throw std::runtime_error("Wrong number of cards");
  Mask hands[4];
  if (overdummy_) {
    hands[0] = dummy_m_; hands[1] = our_m_; hands[2] = d; hands[3] = p;
  } else {
    hands[0] = dummy_m_; hands[1] = p; hands[2] = d; hands[3] = our_m_;
  }
  DdsPosition dds(hands, trump, leader_);
  for (const Card& c : commands_) dds.execute(c);

  struct Out { int key; CardTricks::Entry e; };
  std::vector<Out> out(legal_.size());
  size_t nout = 0;
  for (size_t m = 0; m < legal_.size(); m++) {
    int tricks = dds.tricks(legal_[m]);
    // PIMCDef.cs Evaluate records the trick count as the weight as well.
    out[nout++] = {keys[m], {uint8_t(tricks), double(tricks), pos}};
  }
  std::lock_guard<std::mutex> l(output_.lock());
  for (size_t i = 0; i < nout; i++) output_.append(out[i].key, out[i].e);
}

void PIMCDef::evaluate(Trump trump) {
  std::vector<int> samples = find_samples();
  std::vector<int> keys = output_keys();
  start(
      samples, [this] { return max_playout_ > 0 && playouts_.load() > max_playout_; },
      [this, trump, keys](int pos) { sample(pos, trump, keys); });
}

}  // namespace bga
