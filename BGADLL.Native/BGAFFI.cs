using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using static BGADLL.Macros;

namespace BGADLL
{
    /// <summary>
    /// C FFI wrapper for BGADLL - exposes PIMC engine as flat C functions
    /// for consumption via ctypes (Python) on all platforms.
    ///
    /// Handle-based design: each object (PIMC, Hand, Play, Constraints)
    /// is tracked via GCHandle and exposed as IntPtr.
    /// </summary>
    public static class BGAFFI
    {
        // ===== Native library resolution =====
        // NativeAOT resolves P/Invoke DLLs relative to the host process (e.g. python.exe),
        // not relative to the loaded native library. We register a resolver so that
        // libbcalcdds.dll and dds.dll can be found next to BGADLL.dll.
        static BGAFFI()
        {
            NativeLibrary.SetDllImportResolver(typeof(BGAFFI).Assembly, (libraryName, assembly, searchPath) =>
            {
                // Find the directory containing the BGADLL native library itself
                string assemblyDir = Path.GetDirectoryName(typeof(BGAFFI).Assembly.Location) ?? ".";
                // For NativeAOT, Assembly.Location may be empty; fall back to process directory
                if (string.IsNullOrEmpty(assemblyDir) || assemblyDir == ".")
                {
                    assemblyDir = AppContext.BaseDirectory ?? ".";
                }
                // Also check next to BGADLL.dll by looking at loaded modules
                string[] searchDirs = GetNativeLibSearchDirs();

                foreach (string dir in searchDirs)
                {
                    string candidate = Path.Combine(dir, libraryName);
                    if (NativeLibrary.TryLoad(candidate, out IntPtr handle))
                        return handle;
                    // Try with platform extensions
                    if (!libraryName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
                        !libraryName.EndsWith(".so", StringComparison.OrdinalIgnoreCase) &&
                        !libraryName.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase))
                    {
                        string ext = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".dll" :
                                     RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? ".dylib" : ".so";
                        candidate = Path.Combine(dir, libraryName + ext);
                        if (NativeLibrary.TryLoad(candidate, out handle))
                            return handle;
                    }
                }
                // Fall back to default resolution
                return IntPtr.Zero;
            });
        }

        private static string[] GetNativeLibSearchDirs()
        {
            var dirs = new List<string>();
            // Try to find BGADLL.dll in loaded process modules
            try
            {
                var proc = System.Diagnostics.Process.GetCurrentProcess();
                foreach (System.Diagnostics.ProcessModule mod in proc.Modules)
                {
                    if (mod.ModuleName != null &&
                        mod.ModuleName.Equals("BGADLL.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        string dir = Path.GetDirectoryName(mod.FileName);
                        if (!string.IsNullOrEmpty(dir))
                            dirs.Add(dir);
                    }
                }
            }
            catch { }

            // Also add common search paths
            string baseDir = AppContext.BaseDirectory ?? ".";
            dirs.Add(baseDir);
            dirs.Add(Environment.CurrentDirectory);
            return dirs.Distinct().ToArray();
        }

        // ===== Handle management =====
        private static IntPtr Alloc(object obj)
        {
            var handle = GCHandle.Alloc(obj);
            return GCHandle.ToIntPtr(handle);
        }

        private static T Get<T>(IntPtr ptr)
        {
            var handle = GCHandle.FromIntPtr(ptr);
            return (T)handle.Target;
        }

        private static void Free(IntPtr ptr)
        {
            var handle = GCHandle.FromIntPtr(ptr);
            handle.Free();
        }

        // ===== String helpers =====
        // Returns a pointer to a UTF-8 string that the caller must free with bga_free_string
        private static IntPtr AllocString(string s)
        {
            if (s == null) return IntPtr.Zero;
            return Marshal.StringToCoTaskMemUTF8(s);
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_free_string")]
        public static void FreeString(IntPtr ptr)
        {
            if (ptr != IntPtr.Zero)
                Marshal.FreeCoTaskMem(ptr);
        }

        // ===== PIMC =====
        [UnmanagedCallersOnly(EntryPoint = "bga_pimc_create")]
        public static IntPtr PimcCreate(int maxThreads, int verbose)
        {
            var pimc = new PIMC(maxThreads, verbose != 0);
            return Alloc(pimc);
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_pimc_create_default")]
        public static IntPtr PimcCreateDefault()
        {
            var pimc = new PIMC();
            return Alloc(pimc);
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_pimc_destroy")]
        public static void PimcDestroy(IntPtr handle)
        {
            Free(handle);
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_pimc_version")]
        public static IntPtr PimcVersion(IntPtr handle)
        {
            var pimc = Get<PIMC>(handle);
            return AllocString(pimc.version());
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_pimc_clear")]
        public static void PimcClear(IntPtr handle)
        {
            var pimc = Get<PIMC>(handle);
            pimc.Clear();
        }

        /// <summary>
        /// SetupEvaluation with string-based hands for simplicity.
        /// ourHands: pipe-separated PBN strings (e.g. "dummy|declarer" or "dummy|declarer|rho|lho")
        /// opposPbn: PBN string for opponent cards
        /// currentTrickPbn: pipe-separated card strings (e.g. "S2|HA")
        /// previousTricksPbn: pipe-separated card strings
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "bga_pimc_setup")]
        public static int PimcSetup(IntPtr handle,
            IntPtr ourHandsPtr, int ourHandsCount,
            IntPtr opposPtr,
            IntPtr currentTrickPtr,
            IntPtr previousTricksPtr,
            IntPtr rhoConstsPtr,
            IntPtr lhoConstsPtr,
            int nextToLead, // 0=North, 1=East, 2=South, 3=West
            int maxPlayout,
            int autoplaySingleton,
            int useStrategy)
        {
            try
            {
                var pimc = Get<PIMC>(handle);

                // Parse our hands from handles
                var handPtrs = new IntPtr[ourHandsCount];
                Marshal.Copy(ourHandsPtr, handPtrs, 0, ourHandsCount);
                var ourHands = new Hand[ourHandsCount];
                for (int i = 0; i < ourHandsCount; i++)
                    ourHands[i] = Get<Hand>(handPtrs[i]);

                var oppos = Get<Hand>(opposPtr);
                var currentTrick = Get<Play>(currentTrickPtr);
                var previousTricks = Get<Play>(previousTricksPtr);
                var rhoConsts = Get<Constraints>(rhoConstsPtr);
                var lhoConsts = Get<Constraints>(lhoConstsPtr);

                var consts = new Constraints[] { rhoConsts, lhoConsts };
                var player = (Player)nextToLead;

                pimc.SetupEvaluation(ourHands, oppos, currentTrick, previousTricks,
                    consts, player, maxPlayout, autoplaySingleton != 0, useStrategy != 0);
                return 0;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_pimc_evaluate")]
        public static int PimcEvaluate(IntPtr handle, int trump)
        {
            try
            {
                var pimc = Get<PIMC>(handle);
                pimc.Evaluate((Trump)trump);
                return 0;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_pimc_await")]
        public static int PimcAwait(IntPtr handle, int maxWaitMs)
        {
            try
            {
                var pimc = Get<PIMC>(handle);
                pimc.AwaitEvaluation(maxWaitMs);
                return 0;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_pimc_get_playouts")]
        public static int PimcGetPlayouts(IntPtr handle)
        {
            return Get<PIMC>(handle).Playouts;
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_pimc_get_combinations")]
        public static int PimcGetCombinations(IntPtr handle)
        {
            return Get<PIMC>(handle).Combinations;
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_pimc_get_examined")]
        public static int PimcGetExamined(IntPtr handle)
        {
            return Get<PIMC>(handle).Examined;
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_pimc_get_evaluating")]
        public static int PimcGetEvaluating(IntPtr handle)
        {
            return Get<PIMC>(handle).Evaluating ? 1 : 0;
        }

        /// <summary>Returns pipe-separated legal moves string (e.g. "S2|HA|DK")</summary>
        [UnmanagedCallersOnly(EntryPoint = "bga_pimc_get_legal_moves")]
        public static IntPtr PimcGetLegalMoves(IntPtr handle)
        {
            var pimc = Get<PIMC>(handle);
            var moves = pimc.LegalMoves;
            return AllocString(string.Join("|", moves));
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_pimc_get_legal_moves_to_string")]
        public static IntPtr PimcGetLegalMovesToString(IntPtr handle)
        {
            return AllocString(Get<PIMC>(handle).LegalMovesToString);
        }

        // ===== CardTricks (Output) =====
        [UnmanagedCallersOnly(EntryPoint = "bga_pimc_output_sort")]
        public static void PimcOutputSort(IntPtr handle)
        {
            Get<PIMC>(handle).Output.SortResults();
        }

        /// <summary>
        /// Get tricks with weights for a card.
        /// Returns count, fills tricks[] and weights[] arrays.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "bga_pimc_output_get_tricks")]
        public static int PimcOutputGetTricks(IntPtr handle, IntPtr cardPtr,
            IntPtr tricksOut, IntPtr weightsOut, int maxEntries)
        {
            var pimc = Get<PIMC>(handle);
            string card = Marshal.PtrToStringUTF8(cardPtr);
            var results = pimc.Output.GetTricksWithWeights(card).ToList();
            int count = Math.Min(results.Count, maxEntries);

            for (int i = 0; i < count; i++)
            {
                Marshal.WriteInt32(tricksOut + i * 4, results[i].tricks);
                // Write double (8 bytes)
                var bytes = BitConverter.GetBytes(results[i].weight);
                Marshal.Copy(bytes, 0, weightsOut + i * 8, 8);
            }
            return count;
        }

        // ===== Hand =====
        [UnmanagedCallersOnly(EntryPoint = "bga_hand_create")]
        public static IntPtr HandCreate()
        {
            return Alloc(new Hand());
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_hand_destroy")]
        public static void HandDestroy(IntPtr handle)
        {
            Free(handle);
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_hand_parse")]
        public static IntPtr HandParse(IntPtr pbnPtr)
        {
            string pbn = Marshal.PtrToStringUTF8(pbnPtr);
            var hand = Extensions.Parse(pbn);
            return Alloc(hand);
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_hand_to_string")]
        public static IntPtr HandToString(IntPtr handle)
        {
            return AllocString(Get<Hand>(handle).ToString());
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_hand_count")]
        public static int HandCount(IntPtr handle)
        {
            return Get<Hand>(handle).Count;
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_hand_union")]
        public static IntPtr HandUnion(IntPtr handle, IntPtr otherHandle)
        {
            var result = Get<Hand>(handle).Union(Get<Hand>(otherHandle));
            return Alloc(result);
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_hand_except")]
        public static IntPtr HandExcept(IntPtr handle, IntPtr otherHandle)
        {
            var result = Get<Hand>(handle).Except(Get<Hand>(otherHandle));
            return Alloc(result);
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_hand_remove")]
        public static int HandRemove(IntPtr handle, IntPtr cardHandle)
        {
            return Get<Hand>(handle).Remove(Get<Card>(cardHandle)) ? 1 : 0;
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_hand_add")]
        public static void HandAdd(IntPtr handle, IntPtr cardHandle)
        {
            Get<Hand>(handle).Add(Get<Card>(cardHandle));
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_hand_add_range")]
        public static void HandAddRange(IntPtr handle, IntPtr otherHandle)
        {
            Get<Hand>(handle).AddRange(Get<Hand>(otherHandle));
        }

        // ===== Play =====
        [UnmanagedCallersOnly(EntryPoint = "bga_play_create")]
        public static IntPtr PlayCreate()
        {
            return Alloc(new Play());
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_play_destroy")]
        public static void PlayDestroy(IntPtr handle)
        {
            Free(handle);
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_play_clear")]
        public static void PlayClear(IntPtr handle)
        {
            Get<Play>(handle).Clear();
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_play_add")]
        public static void PlayAdd(IntPtr handle, IntPtr cardHandle)
        {
            Get<Play>(handle).Add(Get<Card>(cardHandle));
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_play_add_range")]
        public static void PlayAddRange(IntPtr handle, IntPtr otherHandle)
        {
            Get<Play>(handle).AddRange(Get<Play>(otherHandle));
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_play_count")]
        public static int PlayCount(IntPtr handle)
        {
            return Get<Play>(handle).Count;
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_play_list_as_string")]
        public static IntPtr PlayListAsString(IntPtr handle)
        {
            return AllocString(Get<Play>(handle).ListAsString());
        }

        // ===== Card =====
        [UnmanagedCallersOnly(EntryPoint = "bga_card_create")]
        public static IntPtr CardCreate(IntPtr cardStrPtr)
        {
            string cardStr = Marshal.PtrToStringUTF8(cardStrPtr);
            return Alloc(new Card(cardStr));
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_card_destroy")]
        public static void CardDestroy(IntPtr handle)
        {
            Free(handle);
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_card_to_string")]
        public static IntPtr CardToString(IntPtr handle)
        {
            return AllocString(Get<Card>(handle).ToString());
        }

        // ===== Constraints =====
        [UnmanagedCallersOnly(EntryPoint = "bga_constraints_create")]
        public static IntPtr ConstraintsCreate(
            int minClubs, int maxClubs,
            int minDiamonds, int maxDiamonds,
            int minHearts, int maxHearts,
            int minSpades, int maxSpades,
            int minHCP, int maxHCP)
        {
            return Alloc(new Constraints(
                minClubs, maxClubs, minDiamonds, maxDiamonds,
                minHearts, maxHearts, minSpades, maxSpades,
                minHCP, maxHCP));
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_constraints_destroy")]
        public static void ConstraintsDestroy(IntPtr handle)
        {
            Free(handle);
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_constraints_to_string")]
        public static IntPtr ConstraintsToString(IntPtr handle)
        {
            return AllocString(Get<Constraints>(handle).ToString());
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_constraints_set")]
        public static void ConstraintsSet(IntPtr handle,
            int minClubs, int maxClubs,
            int minDiamonds, int maxDiamonds,
            int minHearts, int maxHearts,
            int minSpades, int maxSpades,
            int minHCP, int maxHCP)
        {
            var c = Get<Constraints>(handle);
            c.MinClubs = minClubs; c.MaxClubs = maxClubs;
            c.MinDiamonds = minDiamonds; c.MaxDiamonds = maxDiamonds;
            c.MinHearts = minHearts; c.MaxHearts = maxHearts;
            c.MinSpades = minSpades; c.MaxSpades = maxSpades;
            c.MinHCP = minHCP; c.MaxHCP = maxHCP;
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_constraints_get")]
        public static void ConstraintsGet(IntPtr handle, IntPtr outValues)
        {
            var c = Get<Constraints>(handle);
            Marshal.WriteInt32(outValues + 0, c.MinClubs);
            Marshal.WriteInt32(outValues + 4, c.MaxClubs);
            Marshal.WriteInt32(outValues + 8, c.MinDiamonds);
            Marshal.WriteInt32(outValues + 12, c.MaxDiamonds);
            Marshal.WriteInt32(outValues + 16, c.MinHearts);
            Marshal.WriteInt32(outValues + 20, c.MaxHearts);
            Marshal.WriteInt32(outValues + 24, c.MinSpades);
            Marshal.WriteInt32(outValues + 28, c.MaxSpades);
            Marshal.WriteInt32(outValues + 32, c.MinHCP);
            Marshal.WriteInt32(outValues + 36, c.MaxHCP);
        }

        // Individual property setters for convenience
        [UnmanagedCallersOnly(EntryPoint = "bga_constraints_set_prop")]
        public static void ConstraintsSetProp(IntPtr handle, int propIndex, int value)
        {
            var c = Get<Constraints>(handle);
            switch (propIndex)
            {
                case 0: c.MinClubs = value; break;
                case 1: c.MaxClubs = value; break;
                case 2: c.MinDiamonds = value; break;
                case 3: c.MaxDiamonds = value; break;
                case 4: c.MinHearts = value; break;
                case 5: c.MaxHearts = value; break;
                case 6: c.MinSpades = value; break;
                case 7: c.MaxSpades = value; break;
                case 8: c.MinHCP = value; break;
                case 9: c.MaxHCP = value; break;
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_constraints_get_prop")]
        public static int ConstraintsGetProp(IntPtr handle, int propIndex)
        {
            var c = Get<Constraints>(handle);
            return propIndex switch
            {
                0 => c.MinClubs,
                1 => c.MaxClubs,
                2 => c.MinDiamonds,
                3 => c.MaxDiamonds,
                4 => c.MinHearts,
                5 => c.MaxHearts,
                6 => c.MinSpades,
                7 => c.MaxSpades,
                8 => c.MinHCP,
                9 => c.MaxHCP,
                _ => -1
            };
        }

        // ===== DDS diagnostics =====
        [UnmanagedCallersOnly(EntryPoint = "bga_dds_backend")]
        public static IntPtr DdsBackend()
        {
            // Trigger the static constructor by creating a temporary DDS instance
            try
            {
                var dds = new DDS("AKQJT.AKQJT.AKQJ.A 98765.98765.T987.K 432.432.6532.QJT9 .......98765432", Trump.No, Player.North);
                dds.Delete();
                return AllocString(DDS.UseHaglund ? "haglund" : "bcalcdds");
            }
            catch (Exception ex)
            {
                return AllocString("error: " + ex.Message);
            }
        }

        // ===== PIMCDef (Defending) =====
        [UnmanagedCallersOnly(EntryPoint = "bga_pimcdef_create")]
        public static IntPtr PimcDefCreate(int maxThreads, int verbose)
        {
            var pimc = new PIMCDef(maxThreads, verbose != 0);
            return Alloc(pimc);
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_pimcdef_create_default")]
        public static IntPtr PimcDefCreateDefault()
        {
            var pimc = new PIMCDef();
            return Alloc(pimc);
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_pimcdef_destroy")]
        public static void PimcDefDestroy(IntPtr handle)
        {
            Free(handle);
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_pimcdef_version")]
        public static IntPtr PimcDefVersion(IntPtr handle)
        {
            var pimc = Get<PIMCDef>(handle);
            return AllocString(pimc.version());
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_pimcdef_clear")]
        public static void PimcDefClear(IntPtr handle)
        {
            var pimc = Get<PIMCDef>(handle);
            pimc.Clear();
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_pimcdef_setup")]
        public static int PimcDefSetup(IntPtr handle,
            IntPtr ourHandsPtr, int ourHandsCount,
            IntPtr opposPtr,
            IntPtr currentTrickPtr,
            IntPtr previousTricksPtr,
            IntPtr declarerConstsPtr,
            IntPtr partnerConstsPtr,
            int nextToLead,
            int maxPlayout,
            int autoplaySingleton,
            int overDummy)
        {
            try
            {
                var pimc = Get<PIMCDef>(handle);

                var handPtrs = new IntPtr[ourHandsCount];
                Marshal.Copy(ourHandsPtr, handPtrs, 0, ourHandsCount);
                var ourHands = new Hand[ourHandsCount];
                for (int i = 0; i < ourHandsCount; i++)
                    ourHands[i] = Get<Hand>(handPtrs[i]);

                var oppos = Get<Hand>(opposPtr);
                var currentTrick = Get<Play>(currentTrickPtr);
                var previousTricks = Get<Play>(previousTricksPtr);
                var declarerConsts = Get<Constraints>(declarerConstsPtr);
                var partnerConsts = Get<Constraints>(partnerConstsPtr);

                var consts = new Constraints[] { declarerConsts, partnerConsts };
                var player = (Player)nextToLead;

                pimc.SetupEvaluation(ourHands, oppos, currentTrick, previousTricks,
                    consts, player, maxPlayout, autoplaySingleton != 0, overDummy != 0);
                return 0;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_pimcdef_evaluate")]
        public static int PimcDefEvaluate(IntPtr handle, int trump)
        {
            try
            {
                var pimc = Get<PIMCDef>(handle);
                pimc.Evaluate((Trump)trump);
                return 0;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_pimcdef_await")]
        public static int PimcDefAwait(IntPtr handle, int maxWaitMs)
        {
            try
            {
                var pimc = Get<PIMCDef>(handle);
                pimc.AwaitEvaluation(maxWaitMs);
                return 0;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_pimcdef_get_playouts")]
        public static int PimcDefGetPlayouts(IntPtr handle)
        {
            return Get<PIMCDef>(handle).Playouts;
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_pimcdef_get_combinations")]
        public static int PimcDefGetCombinations(IntPtr handle)
        {
            return Get<PIMCDef>(handle).Combinations;
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_pimcdef_get_examined")]
        public static int PimcDefGetExamined(IntPtr handle)
        {
            return Get<PIMCDef>(handle).Examined;
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_pimcdef_get_evaluating")]
        public static int PimcDefGetEvaluating(IntPtr handle)
        {
            return Get<PIMCDef>(handle).Evaluating ? 1 : 0;
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_pimcdef_get_legal_moves")]
        public static IntPtr PimcDefGetLegalMoves(IntPtr handle)
        {
            var pimc = Get<PIMCDef>(handle);
            var moves = pimc.LegalMoves;
            return AllocString(string.Join("|", moves));
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_pimcdef_get_legal_moves_to_string")]
        public static IntPtr PimcDefGetLegalMovesToString(IntPtr handle)
        {
            return AllocString(Get<PIMCDef>(handle).LegalMovesToString);
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_pimcdef_output_sort")]
        public static void PimcDefOutputSort(IntPtr handle)
        {
            Get<PIMCDef>(handle).Output.SortResults();
        }

        [UnmanagedCallersOnly(EntryPoint = "bga_pimcdef_output_get_tricks")]
        public static int PimcDefOutputGetTricks(IntPtr handle, IntPtr cardPtr,
            IntPtr tricksOut, IntPtr weightsOut, int maxEntries)
        {
            var pimc = Get<PIMCDef>(handle);
            string card = Marshal.PtrToStringUTF8(cardPtr);
            var results = pimc.Output.GetTricksWithWeights(card).ToList();
            int count = Math.Min(results.Count, maxEntries);

            for (int i = 0; i < count; i++)
            {
                Marshal.WriteInt32(tricksOut + i * 4, results[i].tricks);
                var bytes = BitConverter.GetBytes(results[i].weight);
                Marshal.Copy(bytes, 0, weightsOut + i * 8, 8);
            }
            return count;
        }
    }
}
