#include "model.hpp"

#include <sched.h>
#include <thread>

#include <cstring>
#include <unordered_map>

namespace bga {

namespace {

// Hand.dataDict: shape -> percentage. Two entries are written 2028/635013559600 and
// 4/635013559600 in C#, integer divisions that give 0; they are kept as 0.
const std::unordered_map<int, double>& shape_table() {
  static const std::unordered_map<int, double> t = {
      {4432, 21.551227}, {5332, 15.516802}, {5431, 12.930688}, {5422, 10.579684}, {4333, 10.536052},
      {6322, 5.642537},  {6421, 4.702125},  {6331, 3.448233},  {5521, 3.173856},  {4441, 2.993176},
      {7321, 1.880856},  {6430, 1.326197},  {5440, 1.243331},  {5530, 0.895202},  {6511, 0.705351},
      {6520, 0.651107},  {7222, 0.513007},  {7411, 0.391817},  {7420, 0.361691},  {7330, 0.265180},
      {8221, 0.192337},  {8311, 0.117558},  {7510, 0.108510},  {8320, 0.108510},  {6610, 0.072353},
      {8410, 0.045211},  {9211, 0.017809},  {9310, 0.010047},  {9220, 0.008217},  {7600, 0.005566},
      {8500, 0.003130},  {10210, 0.001096}, {9400, 0.000966},  {10111, 0.000396}, {10300, 0.000154},
      {11110, 0.000025}, {11200, 0.000011}, {12100, 0.0},      {13000, 0.0},
  };
  return t;
}

struct BinomialTable {
  int64_t t[53][53];
  BinomialTable() {
    for (int n = 0; n < 53; n++)
      for (int k = 0; k < 53; k++) {
        if (k > n) { t[n][k] = 0; continue; }
        if (k == 0 || k == n) { t[n][k] = 1; continue; }
        int64_t r = 1;
        int m = n;
        for (int i = 1; i <= k; i++) {
          r *= m--;
          r /= i;
        }
        t[n][k] = r;
      }
  }
};
const BinomialTable g_binomial;

// ----- SHA-256 (FIPS 180-4), for the seed only -----
struct Sha256 {
  uint32_t h[8] = {0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a, 0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19};
  static uint32_t rotr(uint32_t x, int n) { return (x >> n) | (x << (32 - n)); }
  void block(const uint8_t* p) {
    static const uint32_t k[64] = {
        0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
        0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
        0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
        0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
        0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
        0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
        0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
        0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2};
    uint32_t w[64];
    for (int i = 0; i < 16; i++)
      w[i] = uint32_t(p[4 * i]) << 24 | uint32_t(p[4 * i + 1]) << 16 | uint32_t(p[4 * i + 2]) << 8 | p[4 * i + 3];
    for (int i = 16; i < 64; i++) {
      uint32_t s0 = rotr(w[i - 15], 7) ^ rotr(w[i - 15], 18) ^ (w[i - 15] >> 3);
      uint32_t s1 = rotr(w[i - 2], 17) ^ rotr(w[i - 2], 19) ^ (w[i - 2] >> 10);
      w[i] = w[i - 16] + s0 + w[i - 7] + s1;
    }
    uint32_t a = h[0], b = h[1], c = h[2], d = h[3], e = h[4], f = h[5], g = h[6], hh = h[7];
    for (int i = 0; i < 64; i++) {
      uint32_t S1 = rotr(e, 6) ^ rotr(e, 11) ^ rotr(e, 25);
      uint32_t ch = (e & f) ^ (~e & g);
      uint32_t t1 = hh + S1 + ch + k[i] + w[i];
      uint32_t S0 = rotr(a, 2) ^ rotr(a, 13) ^ rotr(a, 22);
      uint32_t mj = (a & b) ^ (a & c) ^ (b & c);
      uint32_t t2 = S0 + mj;
      hh = g; g = f; f = e; e = d + t1; d = c; c = b; b = a; a = t1 + t2;
    }
    h[0] += a; h[1] += b; h[2] += c; h[3] += d; h[4] += e; h[5] += f; h[6] += g; h[7] += hh;
  }
  void digest(const std::string& msg, uint8_t out[32]) {
    std::vector<uint8_t> m(msg.begin(), msg.end());
    uint64_t bits = uint64_t(m.size()) * 8;
    m.push_back(0x80);
    while (m.size() % 64 != 56) m.push_back(0);
    for (int i = 7; i >= 0; i--) m.push_back(uint8_t(bits >> (8 * i)));
    for (size_t i = 0; i < m.size(); i += 64) block(&m[i]);
    for (int i = 0; i < 8; i++)
      for (int j = 0; j < 4; j++) out[4 * i + j] = uint8_t(h[i] >> (24 - 8 * j));
  }
};

}  // namespace

double shape_odds(Mask hand) {
  int c[4] = {suit_count(hand, 3), suit_count(hand, 2), suit_count(hand, 1), suit_count(hand, 0)};
  std::sort(c, c + 4, [](int a, int b) { return a > b; });
  std::string s = std::to_string(c[0]) + std::to_string(c[1]) + std::to_string(c[2]) + std::to_string(c[3]);
  int key = std::stoi(s);
  auto it = shape_table().find(key);
  return it == shape_table().end() ? 1.0 : it->second / 100;
}

int64_t binomial(int n, int k) {
  if (k < 0 || k > n || n < 0 || n > 52) return 0;
  return g_binomial.t[n][k];
}

void combination_at(int n, int k, int64_t index, uint8_t* out) {
  int64_t x = index;
  int r = 0;
  for (int i = 1; i <= n && r < k; i++) {
    int64_t c = binomial(n - i, k - r - 1);
    if (x < c) out[r++] = uint8_t(i);
    else x -= c;
  }
  for (; r < k; r++) out[r] = 0;
}

int calculate_seed(const std::string& input) {
  uint8_t d[32];
  Sha256().digest(input, d);
  uint32_t v = uint32_t(d[0]) | uint32_t(d[1]) << 8 | uint32_t(d[2]) << 16 | uint32_t(d[3]) << 24;
  return int(v);
}

// The same swaps, in the same order, as
//   while (n > 1) { n--; int k = rnd.next(n + 1); swap(a[k], a[n]); }
// The swap targets are drawn a block ahead so the random a[k] can be prefetched: with
// C(26,13) = 10.4M entries (40 MB) nearly every swap is a cache miss otherwise.
void shuffle(std::vector<int>& a, int n, CrossPlatformRandom& rnd) {
  constexpr int kBlock = 4096, kAhead = 32;
  thread_local int ks[kBlock];
  int* p = a.data();
  while (n > 1) {
    const int cnt = std::min(kBlock, n - 1);
    for (int i = 0; i < cnt; i++) ks[i] = rnd.next(n - i);  // step i swaps a[k] with a[n - 1 - i]
    for (int i = 0; i < kAhead && i < cnt; i++) __builtin_prefetch(p + ks[i], 1);
    for (int i = 0; i < cnt; i++) {
      if (i + kAhead < cnt) __builtin_prefetch(p + ks[i + kAhead], 1);
      std::swap(p[ks[i]], p[n - 1 - i]);
    }
    n -= cnt;
  }
}

int processor_count() {
#if defined(__linux__)
  cpu_set_t set;
  if (sched_getaffinity(0, sizeof set, &set) == 0) {
    int n = CPU_COUNT(&set);
    if (n > 0) return n;
  }
  return 1;
#else  // macOS has no CPU affinity: the CPUs online, as .NET's Environment.ProcessorCount
  const unsigned n = std::thread::hardware_concurrency();
  return n > 0 ? int(n) : 1;
#endif
}

}  // namespace bga
