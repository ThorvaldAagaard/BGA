// The C ABI of BGADLL (BGADLL.Native/BGAFFI.cs), function for function: the same names,
// arguments and return values, so BEN's ctypes bindings (src/pimc/BGADLL_Native.py) load
// this library in place of the NativeAOT one. Objects are heap pointers used as handles;
// strings returned are malloc'ed and freed by bga_free_string.
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>

#include "dds.hpp"
#include "pimc.hpp"

#define BGA_API extern "C" __attribute__((visibility("default")))

using namespace bga;

namespace {

char* alloc_string(const std::string& s) {
  char* p = static_cast<char*>(std::malloc(s.size() + 1));
  if (p) std::memcpy(p, s.c_str(), s.size() + 1);
  return p;
}

template <class T>
T* as(void* h) {
  return static_cast<T*>(h);
}

std::string join(const std::vector<std::string>& v, const char* sep) {
  std::string s;
  for (size_t i = 0; i < v.size(); i++) {
    if (i) s += sep;
    s += v[i];
  }
  return s;
}

int get_tricks(CardTricks& out, const char* card, int* tricks, double* weights, int max_entries) {
  std::vector<CardTricks::Entry> r = out.get(card ? card : "");
  int n = std::min(int(r.size()), max_entries);
  for (int i = 0; i < n; i++) {
    tricks[i] = r[size_t(i)].tricks;
    weights[i] = r[size_t(i)].weight;
  }
  return n;
}

std::vector<Hand> hands_of(void** ptrs, int count) {
  std::vector<Hand> v;
  for (int i = 0; i < count; i++) v.push_back(*as<Hand>(ptrs[i]));
  return v;
}

}  // namespace

BGA_API void bga_free_string(char* p) { std::free(p); }

// ===== PIMC =====
BGA_API void* bga_pimc_create(int max_threads, int verbose) { return new PIMC(max_threads, verbose != 0); }
BGA_API void* bga_pimc_create_default() { return new PIMC(-1, false); }
BGA_API void bga_pimc_destroy(void* h) { delete as<PIMC>(h); }
BGA_API char* bga_pimc_version(void*) { return alloc_string(Engine::version()); }
BGA_API void bga_pimc_clear(void* h) { as<PIMC>(h)->clear(); }

BGA_API int bga_pimc_setup(void* h, void** our_hands, int our_count, void* oppos, void* current_trick,
                           void* previous_tricks, void* rho_consts, void* lho_consts, int next_to_lead,
                           int max_playout, int autoplay_singleton, int use_strategy) {
  try {
    if (dds_backend() == Backend::None) return -1;  // no solver: fail rather than score 0 tricks
    Constraints consts[2] = {*as<Constraints>(rho_consts), *as<Constraints>(lho_consts)};
    as<PIMC>(h)->setup(hands_of(our_hands, our_count), *as<Hand>(oppos), *as<Play>(current_trick),
                       *as<Play>(previous_tricks), consts, Player(next_to_lead & 3), max_playout,
                       autoplay_singleton != 0, use_strategy != 0);
    return 0;
  } catch (...) {
    return -1;
  }
}

BGA_API int bga_pimc_evaluate(void* h, int trump) {
  try {
    as<PIMC>(h)->evaluate(Trump(trump));
    return 0;
  } catch (...) {
    return -1;
  }
}

BGA_API int bga_pimc_await(void* h, int max_wait_ms) {
  try {
    as<PIMC>(h)->await_evaluation(max_wait_ms);
    return 0;
  } catch (...) {
    return -1;
  }
}

BGA_API int bga_pimc_get_playouts(void* h) { return as<PIMC>(h)->playouts(); }
BGA_API int bga_pimc_get_combinations(void* h) { return as<PIMC>(h)->combinations(); }
BGA_API int bga_pimc_get_examined(void* h) { return as<PIMC>(h)->examined(); }
BGA_API int bga_pimc_get_evaluating(void* h) { return as<PIMC>(h)->evaluating() ? 1 : 0; }
BGA_API char* bga_pimc_get_legal_moves(void* h) { return alloc_string(join(as<PIMC>(h)->legal_moves(), "|")); }
BGA_API char* bga_pimc_get_legal_moves_to_string(void* h) { return alloc_string(as<PIMC>(h)->legal_moves_to_string()); }
BGA_API void bga_pimc_output_sort(void* h) { as<PIMC>(h)->output().sort(); }
BGA_API int bga_pimc_output_get_tricks(void* h, const char* card, int* tricks, double* weights, int max_entries) {
  return get_tricks(as<PIMC>(h)->output(), card, tricks, weights, max_entries);
}

// ===== Hand =====
BGA_API void* bga_hand_create() { return new Hand(); }
BGA_API void bga_hand_destroy(void* h) { delete as<Hand>(h); }
BGA_API void* bga_hand_parse(const char* pbn) {
  try {
    return new Hand(Hand::parse(pbn ? pbn : ""));
  } catch (...) {
    return nullptr;
  }
}
BGA_API char* bga_hand_to_string(void* h) {
  try {
    return alloc_string(as<Hand>(h)->str());
  } catch (...) {
    return nullptr;
  }
}
BGA_API int bga_hand_count(void* h) { return as<Hand>(h)->count(); }
BGA_API void* bga_hand_union(void* h, void* other) { return new Hand(as<Hand>(h)->unite(*as<Hand>(other))); }
BGA_API void* bga_hand_except(void* h, void* other) { return new Hand(as<Hand>(h)->except(*as<Hand>(other))); }
BGA_API int bga_hand_remove(void* h, void* card) { return as<Hand>(h)->remove(*as<Card>(card)) ? 1 : 0; }
BGA_API void bga_hand_add(void* h, void* card) { as<Hand>(h)->add(*as<Card>(card)); }
BGA_API void bga_hand_add_range(void* h, void* other) { as<Hand>(h)->add_range(as<Hand>(other)->cards); }

// ===== Play =====
BGA_API void* bga_play_create() { return new Play(); }
BGA_API void bga_play_destroy(void* h) { delete as<Play>(h); }
BGA_API void bga_play_clear(void* h) { as<Play>(h)->clear(); }
BGA_API void bga_play_add(void* h, void* card) { as<Play>(h)->add(*as<Card>(card)); }
BGA_API void bga_play_add_range(void* h, void* other) { as<Play>(h)->add_range(as<Play>(other)->cards); }
BGA_API int bga_play_count(void* h) { return as<Play>(h)->count(); }
BGA_API char* bga_play_list_as_string(void* h) {
  try {
    return alloc_string(as<Play>(h)->list_str());
  } catch (...) {
    return nullptr;
  }
}

// ===== Card =====
BGA_API void* bga_card_create(const char* s) {
  try {
    return new Card(Card::from_string(s ? s : ""));
  } catch (...) {
    return nullptr;
  }
}
BGA_API void bga_card_destroy(void* h) { delete as<Card>(h); }
BGA_API char* bga_card_to_string(void* h) {
  try {
    return alloc_string(as<Card>(h)->str());
  } catch (...) {
    return nullptr;
  }
}

// ===== Constraints =====
BGA_API void* bga_constraints_create(int a, int b, int c, int d, int e, int f, int g, int h2, int i, int j) {
  return new Constraints(a, b, c, d, e, f, g, h2, i, j);
}
BGA_API void bga_constraints_destroy(void* h) { delete as<Constraints>(h); }
BGA_API char* bga_constraints_to_string(void* h) { return alloc_string(as<Constraints>(h)->str()); }
BGA_API void bga_constraints_set(void* h, int a, int b, int c, int d, int e, int f, int g, int h2, int i, int j) {
  *as<Constraints>(h) = Constraints(a, b, c, d, e, f, g, h2, i, j);
}
BGA_API void bga_constraints_get(void* h, int* out) {
  for (int i = 0; i < 10; i++) out[i] = *as<Constraints>(h)->prop(i);
}
BGA_API void bga_constraints_set_prop(void* h, int index, int value) {
  if (int* p = as<Constraints>(h)->prop(index)) *p = value;
}
BGA_API int bga_constraints_get_prop(void* h, int index) {
  int* p = as<Constraints>(h)->prop(index);
  return p ? *p : -1;
}

// ===== DDS diagnostics =====
BGA_API char* bga_dds_backend() { return alloc_string(dds_backend_name()); }

// ===== PIMCDef =====
BGA_API void* bga_pimcdef_create(int max_threads, int verbose) { return new PIMCDef(max_threads, verbose != 0); }
BGA_API void* bga_pimcdef_create_default() { return new PIMCDef(-1, false); }
BGA_API void bga_pimcdef_destroy(void* h) { delete as<PIMCDef>(h); }
BGA_API char* bga_pimcdef_version(void*) { return alloc_string(Engine::version()); }
BGA_API void bga_pimcdef_clear(void* h) { as<PIMCDef>(h)->clear(); }

BGA_API int bga_pimcdef_setup(void* h, void** our_hands, int our_count, void* oppos, void* current_trick,
                              void* previous_tricks, void* declarer_consts, void* partner_consts, int next_to_lead,
                              int max_playout, int autoplay_singleton, int over_dummy) {
  try {
    if (dds_backend() == Backend::None) return -1;  // no solver: fail rather than score 0 tricks
    Constraints consts[2] = {*as<Constraints>(declarer_consts), *as<Constraints>(partner_consts)};
    as<PIMCDef>(h)->setup(hands_of(our_hands, our_count), *as<Hand>(oppos), *as<Play>(current_trick),
                          *as<Play>(previous_tricks), consts, Player(next_to_lead & 3), max_playout,
                          autoplay_singleton != 0, over_dummy != 0);
    return 0;
  } catch (...) {
    return -1;
  }
}

BGA_API int bga_pimcdef_evaluate(void* h, int trump) {
  try {
    as<PIMCDef>(h)->evaluate(Trump(trump));
    return 0;
  } catch (...) {
    return -1;
  }
}

BGA_API int bga_pimcdef_await(void* h, int max_wait_ms) {
  try {
    as<PIMCDef>(h)->await_evaluation(max_wait_ms);
    return 0;
  } catch (...) {
    return -1;
  }
}

BGA_API int bga_pimcdef_get_playouts(void* h) { return as<PIMCDef>(h)->playouts(); }
BGA_API int bga_pimcdef_get_combinations(void* h) { return as<PIMCDef>(h)->combinations(); }
BGA_API int bga_pimcdef_get_examined(void* h) { return as<PIMCDef>(h)->examined(); }
BGA_API int bga_pimcdef_get_evaluating(void* h) { return as<PIMCDef>(h)->evaluating() ? 1 : 0; }
BGA_API char* bga_pimcdef_get_legal_moves(void* h) { return alloc_string(join(as<PIMCDef>(h)->legal_moves(), "|")); }
BGA_API char* bga_pimcdef_get_legal_moves_to_string(void* h) {
  return alloc_string(as<PIMCDef>(h)->legal_moves_to_string());
}
BGA_API void bga_pimcdef_output_sort(void* h) { as<PIMCDef>(h)->output().sort(); }
BGA_API int bga_pimcdef_output_get_tricks(void* h, const char* card, int* tricks, double* weights, int max_entries) {
  return get_tricks(as<PIMCDef>(h)->output(), card, tricks, weights, max_entries);
}
