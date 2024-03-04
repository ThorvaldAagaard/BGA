using System;
using System.Runtime.InteropServices;
using static BGADLL.Macros;

namespace BGADLL
{
    public class DDS
    {
        private IntPtr solver = IntPtr.Zero;

        [DllImport("libbcalcdds.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr bcalcDDS_new(IntPtr format, IntPtr hands, Int32 strain, Int32 leader);

        [DllImport("libbcalcdds.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr bcalcDDS_clone(IntPtr solver);

        [DllImport("libbcalcdds.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void bcalcDDS_delete(IntPtr solver);

        [DllImport("libbcalcdds.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int bcalcDDS_getTricksToTake(IntPtr solver);

        [DllImport("libbcalcdds.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int bcalcDDS_getTricksToTakeEx(IntPtr solver, Int32 tricks_target, IntPtr card);

        [DllImport("libbcalcdds.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void bcalcDDS_exec(IntPtr solver, IntPtr cmds);

        [DllImport("libbcalcdds.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr bcalcDDS_getCards(IntPtr solver, IntPtr result, Int32 player, Int32 suit);

        [DllImport("libbcalcdds.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr bcalcDDS_getLastError(IntPtr solver);

        public DDS(IntPtr solver) => this.solver = solver;

        public DDS(string hands, Trump trump, Player leader)
        {
            IntPtr deal = Marshal.StringToHGlobalAnsi(hands);
            IntPtr format = Marshal.StringToHGlobalAnsi("NESW");
            this.solver = bcalcDDS_new(format, deal, (Int32)trump, (Int32)leader);
        }

        public IntPtr Clone()
        {
            return bcalcDDS_clone(this.solver);
        }

        public void Delete()
        {
            bcalcDDS_delete(this.solver);
        }

        public void Execute(string commands)
        {
            IntPtr cmds = Marshal.StringToHGlobalAnsi(commands);
            bcalcDDS_exec(this.solver, cmds);
        }

        public string LastError()
        {
            IntPtr ptr = bcalcDDS_getLastError(this.solver);
            return Marshal.PtrToStringAnsi(ptr);
        }

        public int Tricks()
        {
            return bcalcDDS_getTricksToTake(this.solver);
        }

        public int Tricks(string card)
        {
            IntPtr move = Marshal.StringToHGlobalAnsi(card);
            return bcalcDDS_getTricksToTakeEx(this.solver, -1, move);
        }
    }
}
