#include "dds.hpp"

#include <dlfcn.h>

#include <atomic>
#include <condition_variable>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <mutex>
#include <set>

namespace bga {

namespace {

// ----- Haglund DDS C interface (dll.h) -----
struct DealPBN {
  int trump;
  int first;
  int currentTrickSuit[3];
  int currentTrickRank[3];
  char remainCards[80];
};
struct FutureTricks {
  int nodes;
  int cards;
  int suit[13];
  int rank[13];
  int equals[13];
  int score[13];
};
using SolveBoardPBNFn = int (*)(DealPBN, int, int, int, FutureTricks*, int);
using SetMaxThreadsFn = void (*)(int);
using ErrorMessageFn = void (*)(int, char*);

// ----- ddfast (include/ddfast.hpp), same layout -----
struct DfCard {
  uint8_t suit, rank;
};
struct DfPlayPosition {
  uint16_t hand[4][4];
  uint8_t trump;
  uint8_t leader;
  uint8_t played;
  DfCard trick[3];
};
struct DfCardResult {
  DfCard card;
  uint8_t ns_tricks;
};
static_assert(sizeof(DfPlayPosition) == 42, "ddfast::PlayPosition layout");
static_assert(sizeof(DfCardResult) == 3, "ddfast::CardResult layout");
// ddfast::card_results(PlayPosition const&, CardResult*, bool) and
// ddfast_compat::ensure_init(char const*, bool), by their Itanium-mangled names.
using CardResultsFn = int (*)(const DfPlayPosition&, DfCardResult*, bool);
using SolvePlayFn = int (*)(const DfPlayPosition&, bool);
using EnsureInitFn = void (*)(const char*, bool);
constexpr const char* kCardResultsSym = "_ZN6ddfast12card_resultsERKNS_12PlayPositionEPNS_10CardResultEb";
constexpr const char* kSolvePlaySym = "_ZN6ddfast10solve_playERKNS_12PlayPositionEb";
constexpr const char* kEnsureInitSym = "_ZN13ddfast_compat11ensure_initEPKcb";

struct Solver {
  Backend backend = Backend::None;
  // haglund
  SolveBoardPBNFn solve_pbn = nullptr;
  ErrorMessageFn error_message = nullptr;
  // ddfast
  CardResultsFn card_results = nullptr;
  SolvePlayFn solve_play = nullptr;
  bool keep_table = false;
};

std::once_flag g_once;
Solver g_solver;

// Library file names: ddfast's python/build.sh makes libddfast.so (Linux) / libddfast.dylib
// (macOS); Haglund's DDS is libdds.so / libdds.dylib (BEN ships it versioned on macOS,
// bin/BGA/macos/arm64/libdds.2.9.0.dylib: set BGA_DDS_LIB, or BEN preloads it).
#if defined(__APPLE__)
constexpr const char* kDdfastLib = "libddfast.dylib";
constexpr const char* kHaglundLib = "libdds.dylib";
#else
constexpr const char* kDdfastLib = "libddfast.so";
constexpr const char* kHaglundLib = "libdds.so";
#endif

std::string own_dir() {
  Dl_info info;
  if (dladdr(reinterpret_cast<void*>(&own_dir), &info) && info.dli_fname) {
    std::string p = info.dli_fname;
    size_t slash = p.rfind('/');
    if (slash != std::string::npos) return p.substr(0, slash);
  }
  return ".";
}

// A library already in the process under `soname`, else env_var's path, else beside
// BGADLL.so, else the loader's search.
void* open_library(const char* soname, const char* env_var) {
  if (void* h = dlopen(soname, RTLD_NOW | RTLD_NOLOAD)) return h;
  if (const char* p = std::getenv(env_var); p && *p)
    if (void* h = dlopen(p, RTLD_NOW | RTLD_GLOBAL)) return h;
  std::string beside = own_dir() + "/" + soname;
  if (void* h = dlopen(beside.c_str(), RTLD_NOW | RTLD_GLOBAL)) return h;
  return dlopen(soname, RTLD_NOW | RTLD_GLOBAL);
}

bool load_ddfast(Solver& s) {
#if defined(__x86_64__)
  if (!__builtin_cpu_supports("avx512f") || !__builtin_cpu_supports("bmi2")) return false;
#endif
  void* h = open_library(kDdfastLib, "BGA_DDFAST_LIB");
  if (!h) return false;
  auto cr = reinterpret_cast<CardResultsFn>(dlsym(h, kCardResultsSym));
  auto sp = reinterpret_cast<SolvePlayFn>(dlsym(h, kSolvePlaySym));
  auto init = reinterpret_cast<EnsureInitFn>(dlsym(h, kEnsureInitSym));
  if (!cr || !sp || !init) return false;
  init(nullptr, false);  // $DDFAST_TABLES; once per process, shared with BEN's ddfast module
  s.card_results = cr;
  s.solve_play = sp;
  const char* keep = std::getenv("BGA_DDFAST_KEEP_TABLE");
  s.keep_table = keep && *keep && *keep != '0';
  s.backend = Backend::Ddfast;
  return true;
}

bool load_haglund(Solver& s) {
  void* h = open_library(kHaglundLib, "BGA_DDS_LIB");
  if (!h) return false;
  auto solve = reinterpret_cast<SolveBoardPBNFn>(dlsym(h, "SolveBoardPBN"));
  auto smt = reinterpret_cast<SetMaxThreadsFn>(dlsym(h, "SetMaxThreads"));
  if (!solve) return false;
  if (smt) smt(8);
  s.solve_pbn = solve;
  s.error_message = reinterpret_cast<ErrorMessageFn>(dlsym(h, "ErrorMessage"));
  s.backend = Backend::Haglund;
  return true;
}

const Solver& solver() {
  std::call_once(g_once, [] {
    const char* want = std::getenv("BGA_DDS_BACKEND");
    std::string w = want ? want : "";
    for (char& c : w) c = char(tolower(c));
    if (w == "haglund") {
      if (!load_haglund(g_solver)) std::fprintf(stderr, "BGADLL: %s (Haglund DDS) not found\n", kHaglundLib);
      return;
    }
    if (load_ddfast(g_solver)) return;
    if (w == "ddfast") std::fprintf(stderr, "BGADLL: %s not found; falling back to Haglund DDS\n", kDdfastLib);
    if (!load_haglund(g_solver)) std::fprintf(stderr, "BGADLL: no double-dummy solver found (%s / %s)\n", kDdfastLib, kHaglundLib);
  });
  return g_solver;
}

// Thread indexes 0..7 for Haglund's SolveBoardPBN, as DDS.cs's _threadIndexPool.
class IndexPool {
 public:
  IndexPool() {
    for (int i = 7; i >= 0; i--) free_.push_back(i);
  }
  int take() {
    std::unique_lock<std::mutex> l(m_);
    cv_.wait(l, [&] { return !free_.empty(); });
    int i = free_.back();
    free_.pop_back();
    return i;
  }
  void give(int i) {
    {
      std::lock_guard<std::mutex> l(m_);
      free_.push_back(i);
    }
    cv_.notify_one();
  }

 private:
  std::mutex m_;
  std::condition_variable cv_;
  std::vector<int> free_;
};
IndexPool g_pool;

// A failed solve is scored as 0 tricks; report each distinct failure once (DDS.cs).
std::mutex g_report_m;
std::set<std::string> g_reported;
void report_failure(const std::string& key, const std::string& msg) {
  std::lock_guard<std::mutex> l(g_report_m);
  if (g_reported.insert(key).second)
    std::fprintf(stderr, "BGADLL DDS warning: %s (reported once; the solve is scored as 0 tricks)\n", msg.c_str());
}
void report_dds_error(const char* where, int code, int thread_index) {
  char text[80] = "unknown error";
  if (solver().error_message) solver().error_message(code, text);
  report_failure(std::string(where) + ":" + std::to_string(code),
                 std::string(where) + ": SolveBoardPBN returned " + std::to_string(code) + " (" + text +
                     ") on thread index " + std::to_string(thread_index));
}

int trump_to_dds(int trump) {
  switch (trump) {
    case TSpade: return 0;
    case THeart: return 1;
    case TDiamond: return 2;
    case TClub: return 3;
    default: return 4;
  }
}
int suit_char_to_dds(char c) {
  switch (c) {
    case 'S': return 0;
    case 'H': return 1;
    case 'D': return 2;
    case 'C': return 3;
    default: return 0;
  }
}
char dds_suit_char(int s) { return "SHDC?"[s >= 0 && s < 4 ? s : 4]; }

thread_local int t_last_trump = -1;  // ddfast keep_table: the strain of this thread's last solve

// ----- revokes in the current trick (ddfast) -----
// A follower who did not follow suit to the current trick but still holds the suit led: DDS
// plays on from such a position, ddfast's card_results / solve_play reject it. PIMC samples
// can produce one (the hidden hands are dealt without regard to the cards discarded to the
// current trick unless the constraints say so). As ddfast's own DDS layer does
// (python/src/dds_compat.cpp), the trick is played out card by card and each completed trick
// is solved from its start, which is always a valid position.
int df_cards_left(const DfPlayPosition& q) {
  const int seat = (q.leader + q.played) & 3;
  int n = 0;
  for (int s = 0; s < 4; s++) n += __builtin_popcount(q.hand[seat][s]);
  return n;
}

bool df_revoked(const DfPlayPosition& p) {
  for (int k = 1; k < p.played; k++)
    if (p.trick[k].suit != p.trick[0].suit && p.hand[(p.leader + k) & 3][p.trick[0].suit]) return true;
  return false;
}

// q with card c played by the seat on turn; won = 1 if this completes a trick North-South win.
void df_after(const DfPlayPosition& q, DfCard c, DfPlayPosition& r, int& won) {
  r = q;
  const int seat = (q.leader + q.played) & 3;
  r.hand[seat][c.suit] = uint16_t(r.hand[seat][c.suit] & ~(1u << (c.rank - 2)));
  won = 0;
  if (q.played < 3) {
    r.trick[q.played] = c;
    r.played = uint8_t(q.played + 1);
    return;
  }
  const DfCard t[4] = {q.trick[0], q.trick[1], q.trick[2], c};
  int best = 0;
  for (int i = 1; i < 4; i++)
    if (t[i].suit == t[best].suit ? t[i].rank > t[best].rank : t[i].suit == q.trump) best = i;
  const int winner = (q.leader + best) & 3;
  won = (winner & 1) == 0;
  r.leader = uint8_t(winner);
  r.played = 0;
  std::memset(r.trick, 0, sizeof r.trick);
}

// North-South's tricks from q on (the current trick included), revokes allowed.
int df_ns_value(const DfPlayPosition& q, SolvePlayFn solve_play) {
  if (q.played == 0 || !df_revoked(q)) return solve_play(q, false);
  const int seat = (q.leader + q.played) & 3;
  const bool ns = (seat & 1) == 0;
  const bool follow = q.hand[seat][q.trick[0].suit] != 0;
  int best = ns ? -1 : 99;
  for (int s = 0; s < 4; s++) {
    if (follow && s != q.trick[0].suit) continue;
    for (unsigned h = q.hand[seat][s]; h; h &= h - 1) {
      DfPlayPosition r;
      int won;
      df_after(q, DfCard{uint8_t(s), uint8_t(__builtin_ctz(h) + 2)}, r, won);
      const int v = won + (r.played == 0 ? (df_cards_left(r) ? solve_play(r, false) : 0) : df_ns_value(r, solve_play));
      best = ns ? std::max(best, v) : std::min(best, v);
    }
  }
  return best;
}

// card_results for a position with a revoke: every legal card of the seat on turn.
int df_card_results_revoked(const DfPlayPosition& p, DfCardResult* out, SolvePlayFn solve_play) {
  const int seat = (p.leader + p.played) & 3;
  const bool follow = p.hand[seat][p.trick[0].suit] != 0;
  int n = 0;
  for (int s = 0; s < 4; s++) {
    if (follow && s != p.trick[0].suit) continue;
    for (int r = 12; r >= 0; r--) {
      if (!(p.hand[seat][s] >> r & 1)) continue;
      const DfCard c{uint8_t(s), uint8_t(r + 2)};
      DfPlayPosition q;
      int won;
      df_after(p, c, q, won);
      int v = won;
      if (q.played > 0 || df_cards_left(q) > 0) v += df_ns_value(q, solve_play);
      out[n++] = DfCardResult{c, uint8_t(v)};
    }
  }
  return n;
}

}  // namespace

Backend dds_backend() { return solver().backend; }

const char* dds_backend_name() {
  switch (dds_backend()) {
    case Backend::Ddfast: return "ddfast";
    case Backend::Haglund: return "haglund";
    default: return "none";
  }
}

DdsPosition::DdsPosition(const Mask hands_nesw[4], int trump, int leader) : trump_(trump), leader_(leader) {
  for (int i = 0; i < 4; i++) hands_[i] = hands_nesw[i];
  executed_.reserve(8);
  solver();
}

DdsPosition DdsPosition::clone() const {
  DdsPosition c(hands_, trump_, leader_);
  c.executed_ = executed_;
  return c;
}

void DdsPosition::execute(const std::string& commands) {
  solved_ = false;
  size_t i = 0;
  while (i < commands.size()) {
    while (i < commands.size() && commands[i] == ' ') i++;
    size_t j = i;
    while (j < commands.size() && commands[j] != ' ') j++;
    if (j > i) execute_token(commands.substr(i, j - i));
    i = j;
  }
}

void DdsPosition::execute(const Card& card) {
  solved_ = false;
  executed_.push_back(Played{3 - card.suit, rank_value(card.rank), card.rank});
}

void DdsPosition::execute_token(const std::string& token) {
  if (token == "x" || token == "X") {
    Played p;
    if (find_cheapest(p)) executed_.push_back(p);
    return;
  }
  char r = token.empty() ? '?' : token[0];
  executed_.push_back(Played{token.size() >= 2 ? suit_char_to_dds(token[1]) : 0, rank_value(r), r});
}

// DDS.cs CalculateCurrentLeader over the complete tricks of executed_.
int DdsPosition::current_leader() const {
  int leader = leader_;
  int t = trump_to_dds(trump_);
  size_t complete = executed_.size() / 4;
  for (size_t trick = 0; trick < complete; trick++) {
    const Played* c = &executed_[trick * 4];
    int lead = c[0].dsuit, best = 0, best_pri = -1, best_rank = -1;
    for (int i = 0; i < 4; i++) {
      int pri = (c[i].dsuit == t && t < 4) ? 2 : (c[i].dsuit == lead) ? 1 : 0;
      if (pri > best_pri || (pri == best_pri && c[i].rank > best_rank)) {
        best_pri = pri;
        best_rank = c[i].rank;
        best = i;
      }
    }
    leader = (leader + best) % 4;
  }
  return leader;
}

void DdsPosition::remaining(Mask out[4]) const {
  Mask played = 0;
  for (const Played& p : executed_)
    if (p.rank >= 2) played |= Mask(1) << ((3 - p.dsuit) * 16 + p.rank - 2);
  for (int i = 0; i < 4; i++) out[i] = hands_[i] & ~played;
}

namespace {
void fill_pbn(DealPBN& d, const Mask rem[4]) {
  std::string s = "N:";
  for (int h = 0; h < 4; h++) {
    if (h) s.push_back(' ');
    append_mask_pbn(s, rem[h]);
  }
  std::memset(d.remainCards, 0, sizeof d.remainCards);
  std::memcpy(d.remainCards, s.data(), std::min(s.size(), sizeof d.remainCards - 1));
}
}  // namespace

void DdsPosition::solve() {
  const Solver& sv = solver();
  nmoves_ = 0;
  solved_ = true;
  Mask rem[4];
  remaining(rem);
  int first = current_leader();
  size_t n = executed_.size() % 4, start = executed_.size() - n;

  if (sv.backend == Backend::Ddfast) {
    DfPlayPosition p{};
    for (int seat = 0; seat < 4; seat++)
      for (int ds = 0; ds < 4; ds++) p.hand[seat][ds] = uint16_t((rem[seat] >> (16 * (3 - ds))) & kSuitBits);
    p.trump = uint8_t(trump_to_dds(trump_));
    p.leader = uint8_t(first);
    p.played = uint8_t(n);
    for (size_t i = 0; i < n && i < 3; i++)
      p.trick[i] = DfCard{uint8_t(executed_[start + i].dsuit), uint8_t(executed_[start + i].rank)};
    bool keep = sv.keep_table && t_last_trump == p.trump;
    t_last_trump = p.trump;
    DfCardResult out[13];
    int nc = (n > 0 && df_revoked(p)) ? df_card_results_revoked(p, out, sv.solve_play) : sv.card_results(p, out, keep);
    if (nc < 0) {
      report_failure("ddfast:invalid", "ddfast card_results: not a valid position");
      return;
    }
    int mover = (first + int(n)) % 4;
    int left = popcount(rem[mover]);
    for (int i = 0; i < nc; i++) {
      int ns = out[i].ns_tricks;
      moves_[nmoves_++] = Move{out[i].card.suit, out[i].card.rank, 0, (mover & 1) ? left - ns : ns};
    }
    return;
  }
  if (sv.backend != Backend::Haglund) return;

  DealPBN d{};
  d.trump = trump_to_dds(trump_);
  d.first = first;
  for (size_t i = 0; i < n && i < 3; i++) {
    d.currentTrickSuit[i] = executed_[start + i].dsuit;
    d.currentTrickRank[i] = executed_[start + i].rank;
  }
  fill_pbn(d, rem);
  FutureTricks ft{};
  int ti = g_pool.take();
  int result = sv.solve_pbn(d, -1, 3, 1, &ft, ti);
  g_pool.give(ti);
  if (result != 1) {
    report_dds_error("SolveHaglund", result, ti);
    return;
  }
  for (int i = 0; i < ft.cards && i < 13; i++) moves_[nmoves_++] = Move{ft.suit[i], ft.rank[i], ft.equals[i], ft.score[i]};
}

int DdsPosition::tricks(const Card& card) {
  if (!solved_) solve();
  if (nmoves_ == 0) return 0;
  int ds = 3 - card.suit, r = rank_value(card.rank);
  for (int i = 0; i < nmoves_; i++) {
    if (moves_[i].dsuit != ds) continue;
    if (moves_[i].rank == r || (moves_[i].equals & (1 << r)) != 0) return moves_[i].score;
  }
  report_failure("SolveHaglund:missing", "SolveHaglund: card " + std::string{card.rank, dds_suit_char(ds)} +
                                             " is not among the " + std::to_string(nmoves_) + " moves DDS returned");
  return 0;
}

int DdsPosition::tricks() {
  if (!solved_) solve();
  if (nmoves_ == 0) return 0;
  int best = moves_[0].score;
  for (int i = 1; i < nmoves_; i++) best = std::max(best, moves_[i].score);
  return best;
}

// DDS.cs FindCheapestCard: Haglund SolveBoardPBN with solutions 1 and the lowest rank among
// the cards returned (one card: DDS's best). ddfast: the lowest card among the best ones.
bool DdsPosition::find_cheapest(Played& out) {
  const Solver& sv = solver();
  if (sv.backend == Backend::Ddfast) {
    if (!solved_) solve();
    int best = -1, pick = -1;
    for (int i = 0; i < nmoves_; i++) best = std::max(best, moves_[i].score);
    for (int i = 0; i < nmoves_; i++)
      if (moves_[i].score == best && (pick < 0 || moves_[i].rank < moves_[pick].rank)) pick = i;
    solved_ = false;
    if (pick < 0) return false;
    out = Played{moves_[pick].dsuit, moves_[pick].rank, rank_char(moves_[pick].rank)};
    return true;
  }
  if (sv.backend != Backend::Haglund) return false;
  Mask rem[4];
  remaining(rem);
  size_t n = executed_.size() % 4, start = executed_.size() - n;
  DealPBN d{};
  d.trump = trump_to_dds(trump_);
  d.first = current_leader();
  for (size_t i = 0; i < n && i < 3; i++) {
    d.currentTrickSuit[i] = executed_[start + i].dsuit;
    d.currentTrickRank[i] = executed_[start + i].rank;
  }
  fill_pbn(d, rem);
  FutureTricks ft{};
  int ti = g_pool.take();
  int result = sv.solve_pbn(d, -1, 1, 1, &ft, ti);
  g_pool.give(ti);
  if (result == 1 && ft.cards > 0) {
    int min_rank = 1 << 30, min_suit = 0;
    for (int i = 0; i < ft.cards; i++)
      if (ft.rank[i] < min_rank) {
        min_rank = ft.rank[i];
        min_suit = ft.suit[i];
      }
    out = Played{min_suit, min_rank, rank_char(min_rank)};
    return true;
  }
  if (result != 1) report_dds_error("FindCheapestCard", result, ti);
  return false;
}

}  // namespace bga
