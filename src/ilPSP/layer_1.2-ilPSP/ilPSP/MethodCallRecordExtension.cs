﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ilPSP.Tracing;

namespace ilPSP
{
    public static class MethodCallRecordExtension
    {
        public static void PrintMostExpensiveCalls(this MethodCallRecord mcr, int count) {

            GetMostExpensiveCalls(Console.Out, mcr, count);
            Console.Out.Flush();

        }

        public static void PrintMostExpensiveBlocking(this MethodCallRecord mcr, int count) {

            GetMostExpensiveBlocking(Console.Out, mcr, count);
            Console.Out.Flush();

        }

        public static void GetMostExpensiveCalls(TextWriter wrt, MethodCallRecord R, int cnt = 0) {
            int i = 1;
            var mostExpensive = R.CompleteCollectiveReport().OrderByDescending(cr => cr.ExclusiveTicks);
            foreach (var cr in mostExpensive) {
                wrt.Write("Rank " + i + ": ");
                wrt.WriteLine(cr.ToString());
                if (i == cnt) return;
                i++;
            }
        }

        private struct Stats
        {
            public Stats(double[] times) {
                m_Max=times.Max();
                m_Min = times.Min();
                m_Average = times.Sum() / times.Length;
                m_Imbal = m_Max - m_Min;
                Debug.Assert(m_Imbal >= 0);
                Debug.Assert(m_Average >= 0);
            }
            private double m_Min;
            private double m_Max;
            private double m_Average;
            private double m_Imbal;

            public double Min {
                get { return m_Min; }
            }
            public double Max {
                get { return m_Max; }
            }
            public double Average {
                get { return m_Average; }
            }
            public double Imbalance {
                get { return m_Imbal; }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="mcr"></param>
        /// <returns></returns>
        public static Dictionary<string, Tuple<double, double, int>> GetFuncImbalance(MethodCallRecord[] mcrs) {
            return GetImbalance(mcrs, s => s.TimeExclusive.TotalSeconds);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="mcr"></param>
        /// <returns></returns>
        public static Dictionary<string, Tuple<double, double, int>> GetMPIImbalance(MethodCallRecord[] mcrs) {
            return GetImbalance(mcrs, s => s.TimeSpentinBlocking.TotalSeconds);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="mcr"></param>
        /// <param name="TimeToCollect"></param>
        /// <returns></returns>
        private static Dictionary<string, Tuple<double, double, int>> GetImbalance(MethodCallRecord[] mcrs, Func<MethodCallRecord, double> TimeToCollect) {
            var kv = new Dictionary<string, Stats>();
            var methodImblance = new Dictionary<string, Tuple<double, double, int>>();
            List<string> method_names = new List<string>();

            mcrs[0].CompleteCollectiveReport().ForEach(r => method_names.Add(r.Name));
            double[] rootTimes = new double[mcrs.Length];

            for (int j = 0; j < mcrs.Length; j++) {
                rootTimes[j] = mcrs[j].TimeSpentInMethod.TotalSeconds;
            }

            var rootStat = new Stats(rootTimes);

            foreach (string method in method_names) {
                double[] times = new double[mcrs.Length];
                int cnt = 0;

                for (int j = 0; j < times.Length; j++) {
                    mcrs[j].FindChildren(method).ForEach(s => times[j] += TimeToCollect(s));
                }

                mcrs[0].FindChildren(method).ForEach(s => cnt += s.CallCount);

                var TStats = new Stats(times);
                kv.Add(method, TStats);
                methodImblance.Add(method, new Tuple<double, double, int>(TStats.Imbalance / rootStat.Average, TStats.Imbalance, cnt));
            }
            return methodImblance;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="wrt"></param>
        /// <param name="R"></param>
        /// <param name="printcnt"></param>
        private static void GetMostExpensiveBlocking(TextWriter wrt, MethodCallRecord R, int printcnt = 0) {
            int i = 1;
            var mostExpensive = R.CompleteCollectiveReport().OrderByDescending(cr => cr.TicksSpentInBlocking);
            foreach (var kv in mostExpensive) {
                wrt.Write("#" + i + ": ");
                wrt.WriteLine(string.Format(
                "'{0}': {1} calls, {2:0.##E-00} sec. runtime exclusivesec",
                    kv.Name,
                    kv.CallCount,
                    new TimeSpan(kv.TicksSpentInBlocking).TotalSeconds));
                if (i == printcnt) return;
                i++;
            }
            Console.Out.Flush();
        }
    }
}
