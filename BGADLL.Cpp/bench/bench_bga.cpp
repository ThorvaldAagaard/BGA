// Benchmarks and cross-checks a BGADLL shared library (the C# NativeAOT build or the C++
// port) through its C ABI, the way BEN calls it: create, setup, evaluate, await, sort, read
// every legal card's (tricks, weight) list.
//
//   bench_bga --lib path/BGADLL.so [--preload libdds.so]... [--threads 12] [--positions 120]
//             [--seed 1] [--dump results.txt] [--iters 3]
//
// Positions: three fixed ones (from BGADLL's tests and BEN's diagnostic) and --positions
// random ones: a random deal (South declares, North is dummy, random strain) played with
// random legal cards to a random point of a random trick; PIMC when North or South is on
// play, PIMCDef when East or West is. Every result is written to --dump for comparison.
#include <dlfcn.h>

#include <algorithm>
#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <random>
#include <string>
#include <vector>

namespace {

struct Api {
  void (*free_string)(char*);
  void* (*pimc_create)(int, int);
  void (*pimc_destroy)(void*);
  int (*pimc_setup)(void*, void**, int, void*, void*, void*, void*, void*, int, int, int, int);
  int (*pimc_evaluate)(void*, int);
  int (*pimc_await)(void*, int);
  int (*pimc_get_playouts)(void*);
  int (*pimc_get_combinations)(void*);
  int (*pimc_get_examined)(void*);
  char* (*pimc_get_legal_moves)(void*);
  void (*pimc_output_sort)(void*);
  int (*pimc_output_get_tricks)(void*, const char*, int*, double*, int);
  void* (*pimcdef_create)(int, int);
  void (*pimcdef_destroy)(void*);
  int (*pimcdef_setup)(void*, void**, int, void*, void*, void*, void*, void*, int, int, int, int);
  int (*pimcdef_evaluate)(void*, int);
  int (*pimcdef_await)(void*, int);
  int (*pimcdef_get_playouts)(void*);
  int (*pimcdef_get_combinations)(void*);
  int (*pimcdef_get_examined)(void*);
  char* (*pimcdef_get_legal_moves)(void*);
  void (*pimcdef_output_sort)(void*);
  int (*pimcdef_output_get_tricks)(void*, const char*, int*, double*, int);
  void* (*hand_parse)(const char*);
  void (*hand_destroy)(void*);
  void* (*play_create)();
  void (*play_destroy)(void*);
  void (*play_add)(void*, void*);
  void* (*card_create)(const char*);
  void (*card_destroy)(void*);
  void* (*constraints_create)(int, int, int, int, int, int, int, int, int, int);
  void (*constraints_destroy)(void*);
  char* (*dds_backend)();
  char* (*pimc_version)(void*);
};

template <class F>
void bind(void* lib, F& f, const char* name) {
  f = reinterpret_cast<F>(dlsym(lib, name));
  if (!f) {
    std::fprintf(stderr, "missing symbol %s\n", name);
    std::exit(1);
  }
}

Api load_api(void* lib) {
  Api a{};
#define B(field) bind(lib, a.field, "bga_" #field)
  B(free_string); B(pimc_create); B(pimc_destroy); B(pimc_setup); B(pimc_evaluate); B(pimc_await);
  B(pimc_get_playouts); B(pimc_get_combinations); B(pimc_get_examined); B(pimc_get_legal_moves);
  B(pimc_output_sort); B(pimc_output_get_tricks); B(pimcdef_create); B(pimcdef_destroy); B(pimcdef_setup);
  B(pimcdef_evaluate); B(pimcdef_await); B(pimcdef_get_playouts); B(pimcdef_get_combinations);
  B(pimcdef_get_examined); B(pimcdef_get_legal_moves); B(pimcdef_output_sort); B(pimcdef_output_get_tricks);
  B(hand_parse); B(hand_destroy); B(play_create); B(play_destroy); B(play_add); B(card_create); B(card_destroy);
  B(constraints_create); B(constraints_destroy); B(dds_backend); B(pimc_version);
#undef B
  return a;
}

// ----- positions -----
// A card is suit*13 + (rank-2), suits C D H S = 0..3 (BGADLL order).
using Cards = std::vector<int>;
const char* kRanks = "23456789TJQKA";
const char* kSuits = "CDHS";
std::string card_str(int c) { return std::string{kRanks[c % 13], kSuits[c / 13]}; }
std::string pbn(const Cards& h) {
  std::string s;
  for (int suit = 3; suit >= 0; suit--) {
    for (int r = 12; r >= 0; r--)
      if (std::find(h.begin(), h.end(), suit * 13 + r) != h.end()) s.push_back(kRanks[r]);
    if (suit) s.push_back('.');
  }
  return s;
}
Cards parse_pbn(const std::string& s) {
  Cards h;
  int suit = 3;
  for (char ch : s) {
    if (ch == '.') { suit--; continue; }
    h.push_back(suit * 13 + int(std::strchr(kRanks, ch) - kRanks));
  }
  return h;
}

struct Position {
  std::string name;
  bool defence;               // PIMCDef (else PIMC)
  std::string our0, our1;     // PIMC: north, south; PIMCDef: dummy, our hand
  Cards oppos;                // hidden cards, in this order
  Cards trick;                // current trick
  Cards previous;             // previous tricks
  int to_play;                // seat on play (PIMC nextToLead / PIMCDef leader)
  int trump;                  // BGADLL Trump: C D H S N = 0..4
  bool overdummy;
  int consts[2][10];
};

const int kOpen[10] = {0, 13, 0, 13, 0, 13, 0, 13, 0, 37};

std::vector<Position> fixed_positions() {
  std::vector<Position> v;
  auto full_minus = [](std::initializer_list<const char*> hands, const Cards& played) {
    Cards all;
    for (int c = 51; c >= 0; c--) all.push_back((c / 13) * 13 + c % 13);
    // order as Extensions.Parse(full deck): S A..2, H A..2, D, C
    all.clear();
    for (int suit = 3; suit >= 0; suit--)
      for (int r = 12; r >= 0; r--) all.push_back(suit * 13 + r);
    Cards out;
    for (int c : all) {
      bool used = std::find(played.begin(), played.end(), c) != played.end();
      for (const char* h : hands) {
        Cards hc = parse_pbn(h);
        if (std::find(hc.begin(), hc.end(), c) != hc.end()) used = true;
      }
      if (!used) out.push_back(c);
    }
    return out;
  };
  auto cs = [](std::initializer_list<const char*> l) {
    Cards c;
    for (const char* s : l) c.push_back(int(std::strchr(kSuits, s[1]) - kSuits) * 13 + int(std::strchr(kRanks, s[0]) - kRanks));
    return c;
  };
  {
    Position p{};
    p.name = "decl-lead";
    p.defence = false;
    p.our0 = "52.K7.QT543.JT84";
    p.our1 = "AQ.A42.K72.AQ973";
    p.trick = cs({"QH"});
    p.oppos = full_minus({"52.K7.QT543.JT84", "AQ.A42.K72.AQ973"}, p.trick);
    p.to_play = 0;
    p.trump = 4;
    std::memcpy(p.consts[0], kOpen, sizeof kOpen);
    std::memcpy(p.consts[1], kOpen, sizeof kOpen);
    v.push_back(p);
  }
  {
    Position p{};
    p.name = "decl-mid";
    p.our0 = "A9873..96.K52";
    p.our1 = "KT.T9.Q84.AQ98";
    p.trick = cs({"TD", "AD", "3D"});
    p.previous = cs({"6H", "4H", "QH", "AH", "3H", "KH", "JH", "2H"});
    Cards played = p.trick;
    played.insert(played.end(), p.previous.begin(), p.previous.end());
    p.oppos = full_minus({"A9873..96.K52", "KT.T9.Q84.AQ98"}, played);
    p.to_play = 2;
    p.trump = 4;
    std::memcpy(p.consts[0], kOpen, sizeof kOpen);
    std::memcpy(p.consts[1], kOpen, sizeof kOpen);
    v.push_back(p);
  }
  {
    Position p{};
    p.name = "defence";
    p.defence = true;
    p.our0 = "432..7632.83";
    p.our1 = "Q976.9875.T9.";
    p.trick = cs({"JC", "6C"});
    p.oppos = parse_pbn("AKJT.KQJT6.AKQJ85.T752");
    p.to_play = 1;
    p.trump = 3;
    p.overdummy = true;
    int d[10] = {0, 0, 0, 7, 0, 6, 4, 6, 0, 30}, q[10] = {4, 7, 0, 6, 0, 6, 0, 1, 0, 5};
    std::memcpy(p.consts[0], d, sizeof d);
    std::memcpy(p.consts[1], q, sizeof q);
    v.push_back(p);
  }
  return v;
}

int trick_winner(const Cards& t, int leader, int trump) {
  int best = 0;
  for (int i = 1; i < 4; i++) {
    int s = t[size_t(i)] / 13, bs = t[size_t(best)] / 13;
    if (s == bs ? t[size_t(i)] % 13 > t[size_t(best)] % 13 : s == trump) best = i;
  }
  return (leader + best) % 4;
}

std::vector<Position> random_positions(int n, unsigned seed) {
  std::mt19937 rng(seed);
  std::vector<Position> v;
  while (int(v.size()) < n) {
    Cards deck(52);
    for (int i = 0; i < 52; i++) deck[size_t(i)] = i;
    std::shuffle(deck.begin(), deck.end(), rng);
    Cards hand[4];
    for (int i = 0; i < 52; i++) hand[i / 13].push_back(deck[size_t(i)]);
    int trump = int(rng() % 5);  // BGADLL: 0..3 = C D H S, 4 = NT
    int stop_trick = int(rng() % 12), stop_card = int(rng() % 4);
    int leader = 3;  // West leads against South
    Cards previous, trick;
    bool done = false;
    for (int t = 0; t <= stop_trick && !done; t++) {
      trick.clear();
      for (int k = 0; k < 4; k++) {
        int seat = (leader + k) % 4;
        if (t == stop_trick && k == stop_card) {
          done = true;
          Position p{};
          p.trick = trick;
          p.previous = previous;
          p.to_play = seat;
          p.trump = trump;
          std::memcpy(p.consts[0], kOpen, sizeof kOpen);
          std::memcpy(p.consts[1], kOpen, sizeof kOpen);
          if (seat == 0 || seat == 2) {
            p.defence = false;
            p.our0 = pbn(hand[0]);
            p.our1 = pbn(hand[2]);
            p.oppos = hand[1];
            p.oppos.insert(p.oppos.end(), hand[3].begin(), hand[3].end());
          } else {
            p.defence = true;
            p.overdummy = seat == 1;
            p.our0 = pbn(hand[0]);
            p.our1 = pbn(hand[seat]);
            p.oppos = hand[2];
            const Cards& partner = hand[seat == 1 ? 3 : 1];
            p.oppos.insert(p.oppos.end(), partner.begin(), partner.end());
          }
          p.name = "rand" + std::to_string(v.size()) + "-t" + std::to_string(stop_trick + 1) + "c" +
                   std::to_string(stop_card + 1) + (p.defence ? "-def" : "-decl");
          v.push_back(p);
          break;
        }
        Cards& h = hand[seat];
        Cards legal;
        if (!trick.empty())
          for (int c : h)
            if (c / 13 == trick[0] / 13) legal.push_back(c);
        if (legal.empty()) legal = h;
        int c = legal[rng() % legal.size()];
        h.erase(std::find(h.begin(), h.end(), c));
        trick.push_back(c);
      }
      if (!done) {
        leader = trick_winner(trick, leader, trump == 4 ? -1 : trump);
        previous.insert(previous.end(), trick.begin(), trick.end());
      }
    }
  }
  return v;
}

double now_ms() {
  return std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now().time_since_epoch()).count();
}

struct Result {
  double setup_ms, eval_ms, await_ms;
  int playouts, examined, combinations;
};

Result run(const Api& a, const Position& p, int threads, FILE* dump) {
  void* h0 = a.hand_parse(p.our0.c_str());
  void* h1 = a.hand_parse(p.our1.c_str());
  void* opp = a.hand_parse(pbn(Cards()).c_str());
  // oppos in the given order: build from cards one by one would need hand_add; use parse of
  // a PBN (order S..C high to low), as BEN does (it parses PBN strings too).
  a.hand_destroy(opp);
  opp = a.hand_parse(pbn(p.oppos).c_str());
  void* trick = a.play_create();
  void* prev = a.play_create();
  std::vector<void*> cards;
  for (int c : p.trick) {
    void* k = a.card_create(card_str(c).c_str());
    a.play_add(trick, k);
    cards.push_back(k);
  }
  for (int c : p.previous) {
    void* k = a.card_create(card_str(c).c_str());
    a.play_add(prev, k);
    cards.push_back(k);
  }
  const int* c0 = p.consts[0];
  const int* c1 = p.consts[1];
  void* k0 = a.constraints_create(c0[0], c0[1], c0[2], c0[3], c0[4], c0[5], c0[6], c0[7], c0[8], c0[9]);
  void* k1 = a.constraints_create(c1[0], c1[1], c1[2], c1[3], c1[4], c1[5], c1[6], c1[7], c1[8], c1[9]);
  void* hands[2] = {h0, h1};

  Result r{};
  void* e = p.defence ? a.pimcdef_create(threads, 0) : a.pimc_create(threads, 0);
  double t0 = now_ms();
  int rc = p.defence ? a.pimcdef_setup(e, hands, 2, opp, trick, prev, k0, k1, p.to_play, 200, 0, p.overdummy ? 1 : 0)
                     : a.pimc_setup(e, hands, 2, opp, trick, prev, k0, k1, p.to_play, 200, 0, 0);
  double t1 = now_ms();
  if (rc != 0) std::fprintf(stderr, "%s: setup failed\n", p.name.c_str());
  rc = p.defence ? a.pimcdef_evaluate(e, p.trump) : a.pimc_evaluate(e, p.trump);
  double t2 = now_ms();
  rc = p.defence ? a.pimcdef_await(e, 1000000) : a.pimc_await(e, 1000000);
  double t3 = now_ms();
  r.setup_ms = t1 - t0;
  r.eval_ms = t2 - t1;
  r.await_ms = t3 - t2;
  r.playouts = p.defence ? a.pimcdef_get_playouts(e) : a.pimc_get_playouts(e);
  r.examined = p.defence ? a.pimcdef_get_examined(e) : a.pimc_get_examined(e);
  r.combinations = p.defence ? a.pimcdef_get_combinations(e) : a.pimc_get_combinations(e);

  if (dump) {
    char* lm = p.defence ? a.pimcdef_get_legal_moves(e) : a.pimc_get_legal_moves(e);
    std::string moves = lm ? lm : "";
    a.free_string(lm);
    if (p.defence) a.pimcdef_output_sort(e);
    else a.pimc_output_sort(e);
    std::fprintf(dump, "%s legal=%s playouts=%d examined=%d combinations=%d\n", p.name.c_str(), moves.c_str(),
                 r.playouts, r.examined, r.combinations);
    static int tricks[100000];
    static double weights[100000];
    size_t s = 0;
    while (s <= moves.size()) {
      size_t bar = moves.find('|', s);
      if (bar == std::string::npos) bar = moves.size();
      std::string card = moves.substr(s, bar - s);
      s = bar + 1;
      if (card.empty()) continue;
      int n = p.defence ? a.pimcdef_output_get_tricks(e, card.c_str(), tricks, weights, 100000)
                        : a.pimc_output_get_tricks(e, card.c_str(), tricks, weights, 100000);
      std::fprintf(dump, "  %s %d:", card.c_str(), n);
      for (int i = 0; i < n; i++) std::fprintf(dump, " %d/%.17g", tricks[i], weights[i]);
      std::fprintf(dump, "\n");
    }
  }
  if (p.defence) a.pimcdef_destroy(e);
  else a.pimc_destroy(e);
  for (void* k : cards) a.card_destroy(k);
  a.play_destroy(trick);
  a.play_destroy(prev);
  a.hand_destroy(h0);
  a.hand_destroy(h1);
  a.hand_destroy(opp);
  a.constraints_destroy(k0);
  a.constraints_destroy(k1);
  return r;
}

}  // namespace

int main(int argc, char** argv) {
  std::string lib_path, dump_path;
  std::vector<std::string> preload;
  int threads = 12, npos = 120, iters = 3;
  unsigned seed = 1;
  for (int i = 1; i < argc; i++) {
    std::string a = argv[i];
    auto val = [&] { return i + 1 < argc ? std::string(argv[++i]) : std::string(); };
    if (a == "--lib") lib_path = val();
    else if (a == "--preload") preload.push_back(val());
    else if (a == "--threads") threads = std::stoi(val());
    else if (a == "--positions") npos = std::stoi(val());
    else if (a == "--seed") seed = unsigned(std::stoul(val()));
    else if (a == "--dump") dump_path = val();
    else if (a == "--iters") iters = std::stoi(val());
  }
  for (const std::string& p : preload)
    if (!dlopen(p.c_str(), RTLD_NOW | RTLD_GLOBAL)) {
      std::fprintf(stderr, "preload %s: %s\n", p.c_str(), dlerror());
      return 1;
    }
  void* lib = dlopen(lib_path.c_str(), RTLD_NOW | RTLD_LOCAL);
  if (!lib) {
    std::fprintf(stderr, "dlopen %s: %s\n", lib_path.c_str(), dlerror());
    return 1;
  }
  Api api = load_api(lib);
  char* be = api.dds_backend();
  std::printf("lib=%s backend=%s threads=%d\n", lib_path.c_str(), be, threads);
  api.free_string(be);

  FILE* dump = dump_path.empty() ? nullptr : std::fopen(dump_path.c_str(), "w");
  std::vector<Position> fixed = fixed_positions();
  std::vector<Position> rnd = random_positions(npos, seed);

  run(api, fixed[0], threads, nullptr);  // warm-up: loads the solver (and ddfast's tables)

  for (const Position& p : fixed) {
    Result best{1e30, 0, 0, 0, 0, 0};
    double best_total = 1e30;
    for (int it = 0; it < iters; it++) {
      Result r = run(api, p, threads, it == 0 ? dump : nullptr);
      double tot = r.setup_ms + r.eval_ms + r.await_ms;
      if (tot < best_total) best_total = tot, best = r;
    }
    std::printf("%-10s %8.1f ms = setup %6.1f + sample %6.1f + solve %7.1f   playouts=%d examined=%d/%d\n",
                p.name.c_str(), best_total, best.setup_ms, best.eval_ms, best.await_ms, best.playouts, best.examined,
                best.combinations);
  }

  // Random positions, grouped by stage of play.
  double tot[2][3] = {}, sum_setup = 0, sum_eval = 0, sum_await = 0;
  int cnt[2][3] = {};
  double t_all = now_ms();
  for (const Position& p : rnd) {
    Result r = run(api, p, threads, dump);
    int trick_no = int(p.previous.size() / 4);
    int stage = trick_no < 4 ? 0 : trick_no < 8 ? 1 : 2;
    double t = r.setup_ms + r.eval_ms + r.await_ms;
    tot[p.defence][stage] += t;
    cnt[p.defence][stage]++;
    sum_setup += r.setup_ms;
    sum_eval += r.eval_ms;
    sum_await += r.await_ms;
  }
  t_all = now_ms() - t_all;
  const char* stage_name[3] = {"tricks 1-4", "tricks 5-8", "tricks 9-12"};
  for (int d = 0; d < 2; d++)
    for (int s = 0; s < 3; s++)
      if (cnt[d][s])
        std::printf("random %-8s %-11s n=%3d  %8.1f ms/eval\n", d ? "PIMCDef" : "PIMC", stage_name[s], cnt[d][s],
                    tot[d][s] / cnt[d][s]);
  std::printf("random all %d positions: %.0f ms total, %.1f ms/eval (setup %.1f, sample %.1f, solve %.1f)\n",
              int(rnd.size()), t_all, t_all / double(rnd.size()), sum_setup / double(rnd.size()),
              sum_eval / double(rnd.size()), sum_await / double(rnd.size()));
  if (dump) std::fclose(dump);
  return 0;
}
