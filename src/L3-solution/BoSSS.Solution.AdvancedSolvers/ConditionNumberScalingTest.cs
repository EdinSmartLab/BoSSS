﻿using BoSSS.Foundation.Grid;
using BoSSS.Solution.Control;
using NUnit.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BoSSS.Solution.AdvancedSolvers.Testing {
    
    /// <summary>
    /// Utility class for executing a series of solver runs and studying the condition number slope over mesh resolution;
    /// Works only for solvers which implemented <see cref="Application{T}.OperatorAnalysis()"/>,
    /// see also <see cref="OpAnalysisBase.GetNamedProperties"/>
    /// </summary>
    public class ConditionNumberScalingTest {


        /// <summary>
        /// ctor
        /// </summary>
        public ConditionNumberScalingTest() {
            var ExpectedSlopes = new List<ValueTuple<XAxisDesignation, string, double>>();

            ExpectedSlopes.Add((XAxisDesignation.Grid_1Dres, "TotCondNo-*", 2.2));
            ExpectedSlopes.Add((XAxisDesignation.Grid_1Dres, "StencilCondNo-innerUncut-*", 0.5));
            ExpectedSlopes.Add((XAxisDesignation.Grid_1Dres, "StencilCondNo-innerUncut-*", 0.5));
        }


        /// <summary>
        /// A range of control objects over which the condition number scaling is performed.
        /// </summary>
        public void SetControls(IEnumerable<AppControl> controls) {
            this.Controls = controls.ToArray();
        }


        /// <summary>
        /// Entsetzlich viel code für was primitives
        /// </summary>
        class MyEnu : IEnumerable<AppControl> {

            Func<IGrid>[] GridFuncs;
            AppControl BaseControl;

            public MyEnu(AppControl __baseControl, Func<IGrid>[] __GridFuncs) {

            }

            public IEnumerator<AppControl> GetEnumerator() {
                return new E() { GridFuncs = this.GridFuncs, BaseControl = this.BaseControl };
            }

            IEnumerator IEnumerable.GetEnumerator() {
                return new E() { GridFuncs = this.GridFuncs, BaseControl = this.BaseControl };
            }

            class E : IEnumerator<AppControl> {
                public AppControl BaseControl;
                int position = -1;
                public Func<IGrid>[] GridFuncs;

                public AppControl Current {
                    get {
                        return GetCurrent();
                    }
                }

                private AppControl GetCurrent() {
                    var c = BaseControl;
                    c.GridGuid = default(Guid);
                    c.GridFunc = GridFuncs[position];
                    return c;
                }

                object IEnumerator.Current {
                    get {
                        return GetCurrent();
                    }
                }

                public void Dispose() { }

                public bool MoveNext() {
                    position++;
                    return position < GridFuncs.Length;
                }

                public void Reset() {
                    position = -1;
                }
            }
        }


        /// <summary>
        /// A range of control objects over which the condition number scaling is performed.
        /// </summary>
        /// <param name="BaseControl">
        /// basic settings
        /// </param>
        /// <param name="GridFuncs">
        /// a sequence of grid-generating functions, (<see cref="AppControl.GridFunc"/>),
        /// which defines the grid for each run
        /// </param>
        public void SetControls(AppControl BaseControl, IEnumerable<Func<IGrid>> GridFuncs) {
            Controls = new MyEnu(BaseControl, GridFuncs.ToArray());
        }



        IEnumerable<AppControl> Controls;

        /// <summary>
        /// One tuple for each slope that should be tested
        /// - 1st item: name of x-axis
        /// - 2nd item: name of y-axis (wildcards accepted)
        /// - 3rd item expected slope in the log-log-regression
        /// </summary>
        public IList<ValueTuple<XAxisDesignation, string, double>> ExpectedSlopes;


        /// <summary>
        /// 
        /// </summary>
        public virtual void ExecuteTest() {
            

            TestSlopes(Controls, this.ExpectedSlopes);
        }



        /// <summary>
        /// Utility routine, performs an operator analysis on a sequence of control objects and returns a table of results.
        /// </summary>
        /// <returns>
        /// A table, containing grid resolutions and measurements on condition number
        /// - keys: column names
        /// - values: measurements of each column
        /// </returns>
        public IDictionary<string, double[]> RunAndLog() {

            var ret = new Dictionary<string, List<double>>();
           

            int Counter = 0;
            foreach(var C in this.Controls) {
                var st = C.GetSolverType();

                Counter++;
                Console.WriteLine("================================================================");
                Console.WriteLine($"Condition Number Scaling Analysis:  Run {Counter} of {this.Controls.Count()}");
                Console.WriteLine("================================================================");
                

                using(var solver = (BoSSS.Solution.IApplication)Activator.CreateInstance(st)) {
                    Console.WriteLine("  Starting Solver...");
                    solver.Init(C);
                    solver.RunSolverMode();

                    Console.WriteLine("  Done solver; Now performing operator analysis...");

                    int J = Convert.ToInt32(solver.CurrentSessionInfo.KeysAndQueries["Grid:NoOfCells"]);
                    double hMin = Convert.ToDouble(solver.CurrentSessionInfo.KeysAndQueries["Grid:hMin"]);
                    double hMax = Convert.ToDouble(solver.CurrentSessionInfo.KeysAndQueries["Grid:hMax"]);
                    int D = Convert.ToInt32(solver.CurrentSessionInfo.KeysAndQueries["Grid:SpatialDimension"]);
                    double J1d = Math.Pow(J, 1.0 / D);

                    var prop = solver.OperatorAnalysis();
                    Console.WriteLine("  finished analysis.");


                    if(ret.Count == 0) {
                        ret.Add(XAxisDesignation.Grid_NoOfCells.ToString(), new List<double>());
                        ret.Add(XAxisDesignation.Grid_hMin.ToString(), new List<double>());
                        ret.Add(XAxisDesignation.Grid_hMax.ToString(), new List<double>());
                        ret.Add(XAxisDesignation.Grid_1Dres.ToString(), new List<double>());

                        foreach(var kv in prop) {
                            ret.Add(kv.Key, new List<double>());
                        }
                    }

                    {
                        ret[XAxisDesignation.Grid_NoOfCells.ToString()].Add(J);
                        ret[XAxisDesignation.Grid_hMin.ToString()].Add(hMin);
                        ret[XAxisDesignation.Grid_hMax.ToString()].Add(hMax);
                        ret[XAxisDesignation.Grid_1Dres.ToString()].Add(J1d);

                        foreach(var kv in prop) {
                            ret[kv.Key].Add(kv.Value);
                        }
                    }
                }
            }

            // write statistics
            // ================
            {
                var xdes = XAxisDesignation.Grid_1Dres.ToString();
                var xVals = ret[xdes];

                Console.WriteLine("Regression of condition number slopes:");
                foreach(string ydes in ret.Keys) {
                    if(!Enum.TryParse(ydes, out XAxisDesignation dummy)) {
                        var yVals = ret[ydes];

                        double slope = LogLogRegression(xVals, yVals);
                        Console.WriteLine($"   slope of {ydes}: {slope}");

                    }
                }
                


            }


            // data conversion & return 
            // ========================
            {
                var realRet = new Dictionary<string, double[]>();
                foreach(var kv in ret) {
                    realRet.Add(kv.Key, kv.Value.ToArray());
                }

                return realRet;
            }
        }


        

        /// <summary>
        /// Names for the x-axis, over which condition number scaling slopes are computed.
        /// </summary>
        public enum XAxisDesignation {

            /// <summary>
            /// total number of cells, <see cref="IGridData.CellPartitioning"/>
            /// </summary>
            Grid_NoOfCells,

            /// <summary>
            /// maximum cell size, <see cref="IGeometricalCellsData.h_min"/>
            /// </summary>
            Grid_hMin,

            /// <summary>
            /// maximum cell size, <see cref="IGeometricalCellsData.h_max"/>
            /// </summary>
            Grid_hMax,

            /// <summary>
            /// D-th root of number of cells, where D is the spatial dimension
            /// </summary>
            Grid_1Dres
        }


        private static double LogLogRegression(IEnumerable<double> _xValues, IEnumerable<double> _yValues) {
            double[] xValues = _xValues.Select(x => Math.Log10(x)).ToArray();
            double[] yValues = _yValues.Select(y => Math.Log10(y)).ToArray();

            double xAvg = xValues.Average();
            double yAvg = yValues.Average();

            double v1 = 0.0;
            double v2 = 0.0;

            for (int i = 0; i < yValues.Length; i++) {
                v1 += (xValues[i] - xAvg) * (yValues[i] - yAvg);
                v2 += Math.Pow(xValues[i] - xAvg, 2);
            }

            double a = v1 / v2;
            double b = yAvg - a * xAvg;

            return a;
        }

        /// <summary>
        /// Utility, which tests the slope of operator condition number estimates over a series of meshes resp. control files
        /// </summary>
        /// <param name="Controls"></param>
        /// <param name="ExpectedSlopes">
        /// One tuple for each slope that should be tested
        /// - 1st item: name of x-axis
        /// - 2nd item: name of y-axis
        /// - 3rd item expected slope in the log-log-regression
        /// </param>
        public static void TestSlopes<T>(T Controls, List<ValueTuple<XAxisDesignation, string, double>> ExpectedSlopes)
           where T : IEnumerable<BoSSS.Solution.Control.AppControl> //
        {
            var data = RunAndLog(Controls);
            

            foreach (var ttt in ExpectedSlopes) {
                double[] xVals = data[ttt.Item1.ToString()];
                double[] yVals = data[ttt.Item2];

                double Slope = LogLogRegression(xVals, yVals);

                Console.WriteLine($"Slope for {ttt.Item2}: {Slope:0.###e-00}");
            }

            foreach (var ttt in ExpectedSlopes) {
                double[] xVals = data[ttt.Item1.ToString()];
                double[] yVals = data[ttt.Item2];

                double Slope = LogLogRegression(xVals, yVals);

                Assert.LessOrEqual(Slope, ttt.Item3, $"Condition number slope for {ttt.Item2} to high; at max. {ttt.Item3}");
            }
        }

    }
}
