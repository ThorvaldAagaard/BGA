// PIMC (declarer) and PIMCDef (defender): ports of PIMC.cs and PIMCDef.cs.
//
// The public behaviour is the C# one: SetupEvaluation / Evaluate / AwaitEvaluation, the
// same seed (SHA-256 of the two known hands), the same shuffled order of the opponents'
// card splits, the same constraint handling, and the same (tricks, weight, combination)
// results per legal card. What changed is how the work is done:
//   - samples are 64-bit card masks, not lists of Card objects and strings;
//   - the solver is DdsPosition (dds.hpp): ddfast or Haglund DDS;
//   - the worker threads come from one persistent pool (ddfast keeps per-OS-thread search
//     state, and C# started new threads on every Evaluate);
//   - AwaitEvaluation wakes on a condition variable instead of polling every 10 ms.
#pragma once

#include <atomic>
#include <condition_variable>
#include <functional>
#include <mutex>
#include <string>
#include <vector>

#include "model.hpp"

namespace bga {

// CardTricks: per legal card, the (tricks, weight, combination id) of every sample.
class CardTricks {
 public:
  struct Entry {
    uint8_t tricks;
    double weight;
    int combination;
  };

  void clear();
  int add_key(const std::string& card);  // index of the card's list (created if missing)
  void append(int key, const Entry& e);  // caller holds lock()
  std::mutex& lock() { return m_; }
  // SortResults: by combination, tricks, weight, ascending; then read back in reverse, as
  // the C# ConcurrentBag rebuilt from the sorted list enumerates (newest first).
  void sort();
  std::vector<Entry> get(const std::string& card);

 private:
  std::mutex m_;
  std::vector<std::string> keys_;
  std::vector<std::vector<Entry>> values_;
};

// What PIMC and PIMCDef share: thread count, the queue of samples, the workers,
// AwaitEvaluation and the counters BEN reads.
class Engine {
 public:
  Engine(int max_threads, bool verbose, const char* name);
  virtual ~Engine();

  int combinations() const { return n_combinations_; }
  int examined() const { return examined_.load(); }
  int playouts() const { return playouts_.load(); }
  bool evaluating() const { return evaluate_.load() || threads_ != free_.load(); }
  const std::vector<std::string>& legal_moves() const { return legal_strings_; }
  std::string legal_moves_to_string() const;  // ", "-joined
  CardTricks& output() { return output_; }
  static std::string version() { return "1.0.0.0"; }

  void await_evaluation(int max_wait_ms);
  void end_evaluate();

 protected:
  void load_combinations(int n, int k);
  void clear_common();
  // Queues the samples and starts the workers; body(pos) handles one sample.
  void start(const std::vector<int>& samples, std::function<bool()> stop_check, std::function<void(int)> body);
  void set_legal(const std::vector<Card>& legal);
  std::vector<int> output_keys();

  const int threads_;
  const bool verbose_;
  std::atomic<bool> evaluate_{false};
  std::atomic<int> free_;
  std::atomic<int> active_{0};
  std::atomic<int> examined_{0};
  std::atomic<int> playouts_{0};
  std::mutex m_;
  std::condition_variable cv_;

  std::vector<int> queue_;
  std::atomic<size_t> head_{0};

  int n_combinations_ = 0, comb_n_ = 0, comb_k_ = 0;
  std::vector<int> combination_index_;
  CrossPlatformRandom random_{0};
  int seed_ = 0;

  std::vector<Card> legal_;
  std::vector<std::string> legal_strings_;
  CardTricks output_;
  int max_playout_ = 0;
  int leader_ = 0;
  std::vector<Card> commands_;  // the cards of the current trick, in order

  // Remaining (hidden) cards: their list order (combination indexes refer to it) and mask.
  std::vector<Mask> rem_bits_;
  Mask rem_mask_ = 0;
};

class PIMC : public Engine {
 public:
  PIMC(int max_threads, bool verbose) : Engine(max_threads, verbose, "PIMC") {}

  void clear();
  void setup(const std::vector<Hand>& our, const Hand& oppos, const Play& current_trick, const Play& previous_tricks,
             const Constraints consts[2], Player next_to_lead, int max_playout, bool autoplay_singleton,
             bool use_strategy);
  void evaluate(Trump trump);

 private:
  void validate_input();
  void update_constraints();
  std::vector<Card> legit_moves(Player p) const;
  std::vector<int> find_samples();
  void sample(int pos, Trump trump, const std::vector<int>& keys);

  Hand north_, south_, east_, west_, east_shown_, west_shown_, remaining_;
  Play current_trick_, previous_tricks_;
  Constraints east_c_, west_c_;
  bool use_fusion_ = true;
  Mask north_m_ = 0, south_m_ = 0, east_m_ = 0, west_m_ = 0, east_shown_m_ = 0, west_shown_m_ = 0;
};

class PIMCDef : public Engine {
 public:
  PIMCDef(int max_threads, bool verbose) : Engine(max_threads, verbose, "PIMCDef") {}

  void clear();
  // Returns the single legal card when autoplay_singleton stops the setup early, else "".
  std::string setup(const std::vector<Hand>& our, const Hand& oppos, const Play& current_trick,
                    const Play& previous_tricks, const Constraints consts[2], Player leader, int max_playout,
                    bool autoplay_singleton, bool overdummy);
  void evaluate(Trump trump);

 private:
  void validate_input();
  void update_constraints();
  std::vector<Card> legit_moves(Player p, bool overdummy) const;
  std::vector<int> find_samples();
  void sample(int pos, Trump trump, const std::vector<int>& keys);

  Hand dummy_, our_, partner_, declarer_, partner_shown_, declarer_shown_, remaining_;
  Play current_trick_, previous_tricks_;
  Constraints declarer_c_, partner_c_;
  bool overdummy_ = false;
  Mask dummy_m_ = 0, our_m_ = 0, partner_m_ = 0, declarer_m_ = 0;
};

}  // namespace bga
