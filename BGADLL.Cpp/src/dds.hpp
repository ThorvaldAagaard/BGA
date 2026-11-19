// The double-dummy solver behind PIMC / PIMCDef: a port of BGADLL's DDS class
// (DDS.cs) with two backends, chosen once per process:
//
//   ddfast   ddfast::card_results() on libddfast.so (github.com/randalburns/ddfast). One call
//            scores every legal card. No thread-index pool: ddfast keeps its search state per
//            OS thread, so any number of threads solve at once.
//   haglund  SolveBoardPBN(solutions 3) on Bo Haglund's libdds, exactly as DDS.cs calls it
//            (thread indexes 0..7 from a pool, SetMaxThreads(8)).
//
// Both libraries are opened with dlopen at first use, so BGADLL.so has no link-time
// dependency on either. A library already loaded in the process is used first (BEN loads
// libddfast.so for its own DDSolver, and may preload a libdds.so), so the process keeps one
// copy of ddfast's tables.
//
// Selection: BGA_DDS_BACKEND=ddfast|haglund; unset, ddfast if it can be loaded (it needs
// AVX-512 and BMI2), else haglund. Library paths: BGA_DDFAST_LIB, BGA_DDS_LIB, else the
// directory of BGADLL.so, else the loader's search path. ddfast's tables come from
// $DDFAST_TABLES (ddfast_compat::ensure_init, shared with BEN's ddfast module).
#pragma once

#include <string>
#include <vector>

#include "model.hpp"

namespace bga {

enum class Backend { Ddfast, Haglund, None };

Backend dds_backend();             // resolves (and loads) the backend on first call
const char* dds_backend_name();    // "ddfast", "haglund" or "none"

// A position as DDS.cs keeps it: the four hands at the start of the current trick (cards
// played to it still in the hands), the trump, the leader of that trick, and the cards
// executed since. Tricks(card) is the number of tricks the side on turn takes from here (the
// current trick included) if it plays `card`, as bcalcdds' getTricksToTakeEx and DDS.cs.
class DdsPosition {
 public:
  DdsPosition(const Mask hands_nesw[4], int trump, int leader);

  // CloneDDS: same position, no cached solve.
  DdsPosition clone() const;

  // Execute("AH KH x"): play cards in order; "x" plays the card DDS returns as best for the
  // player on turn (DDS.cs FindCheapestCard).
  void execute(const std::string& commands);
  void execute(const Card& card);

  int tricks(const Card& card);   // the side on turn's tricks if it plays card; 0 if illegal
  int tricks();                   // the best the side on turn can do

 private:
  struct Played { int dsuit; int rank; char rch; };  // DDS suit (S=0 .. C=3), rank 2..14
  struct Move { int dsuit, rank, equals, score; };

  void solve();
  void execute_token(const std::string& token);
  bool find_cheapest(Played& out);
  int current_leader() const;
  void remaining(Mask out[4]) const;

  Mask hands_[4];
  int trump_;
  int leader_;
  std::vector<Played> executed_;
  bool solved_ = false;
  int nmoves_ = 0;
  Move moves_[13];
};

}  // namespace bga
