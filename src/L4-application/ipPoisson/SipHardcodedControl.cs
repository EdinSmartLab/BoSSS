﻿/* =======================================================================
Copyright 2017 Technische Universitaet Darmstadt, Fachgebiet fuer Stroemungsdynamik (chair of fluid dynamics)

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BoSSS.Foundation;
using BoSSS.Foundation.Grid.Classic;
using BoSSS.Solution.Control;
using ilPSP.Utils;
using ilPSP;
using BoSSS.Foundation.Grid.RefElements;
using BoSSS.Foundation.Grid.Aggregation;
using ilPSP.Connectors.Matlab;
using BoSSS.Platform.LinAlg;
using System.Diagnostics;
using BoSSS.Platform.Utils.Geom;
using BoSSS.Solution.Gnuplot;
using BoSSS.Foundation.Grid;
using BoSSS.Solution.GridImport;

namespace BoSSS.Application.SipPoisson {

    /// <summary>
    /// predefined control-objects
    /// </summary>
    static public class SipHardcodedControl
    {

        public static SipControl ConvergenceTest(int Res = 20, int Dim = 2, LinearSolverCode solver_name = LinearSolverCode.exp_Kcycle_schwarz, int deg = 1)
        {

            if (Dim != 2 && Dim != 3)
                throw new ArgumentOutOfRangeException();

            var R = new SipControl();
            R.ProjectName = "ipPoison/cartesian";
            R.savetodb = false;

            R.FieldOptions.Add("T", new FieldOpts() { Degree = deg, SaveToDB = FieldOpts.SaveToDBOpt.TRUE });
            R.FieldOptions.Add("Tex", new FieldOpts() { Degree = deg + 2 });
            R.InitialValues_Evaluators.Add("RHS", X => -1.0); // constant force i.e. gravity
            R.ExactSolution_provided = false;//true;
            R.LinearSolver.NoOfMultigridLevels = int.MaxValue;
            R.LinearSolver.SolverCode = solver_name;
            R.LinearSolver.MaxSolverIterations = 200;
            R.LinearSolver.TargetBlockSize = 10000;
            R.LinearSolver.MaxKrylovDim = 2000;



            R.GridFunc = delegate () {
                GridCommons grd = null;
                if (Dim == 2)
                {
                    double[] xNodes = GenericBlas.Linspace(-10, 10, Res * 5 + 1);
                    double[] yNodes = GenericBlas.Linspace(-10, 10, Res * 5 + 1);
                    grd = Grid2D.Cartesian2DGrid(xNodes, yNodes);
                }
                else
                {
                    throw new NotSupportedException();
                }
                
                grd.EdgeTagNames.Add(1, BoundaryType.Dirichlet.ToString());
                grd.DefineEdgeTags(delegate (double[] X) {
                    byte ret;

                    ret = 1; // all dirichlet
                    return ret;
                });

                return grd;
            };

            R.AddBoundaryValue(BoundaryType.Dirichlet.ToString(), "T",
                 delegate (double[] X) {
                     double x = X[0], y = X[1];
                     return 0.0;
                 });

            return R;
        }

        /// <summary>
        /// Test on a curved grid.
        /// </summary>
        public static SipControl TestCurved()
        {
            var R = new SipControl();
            R.ProjectName = "ipPoison/curved";
            R.savetodb = false;

            //R.FieldOptions.Add("T", new FieldOpts() { Degree = 2, SaveToDB = FieldOpts.SaveToDBOpt.TRUE });
            R.FieldOptions.Add("T", new FieldOpts() { Degree = 2, SaveToDB = FieldOpts.SaveToDBOpt.TRUE });
            R.FieldOptions.Add("Tex", new FieldOpts() { Degree = 4 });
            R.InitialValues_Evaluators.Add("RHS", X => 0.0);
            R.InitialValues_Evaluators.Add("Tex", X => (Math.Log(X[0].Pow2() + X[1].Pow2()) / Math.Log(4.0)) + 1.0);
            R.ExactSolution_provided = true;
            R.LinearSolver.SolverCode = LinearSolverCode.exp_Kcycle_schwarz;
            R.LinearSolver.TargetBlockSize = 1000;
            R.SuperSampling = 2;
            R.NoOfMultigridLevels = 4;            

            R.GridFunc = delegate ()
            {
                var grd = Grid2D.CurvedSquareGrid(GenericBlas.Linspace(1, 2, 3), GenericBlas.Linspace(0, 1, 11), CellType.Square_9, true);
                grd.EdgeTagNames.Add(1, BoundaryType.Dirichlet.ToString());
                grd.DefineEdgeTags((Vector X) => 1);
                return grd;
            };

            R.AddBoundaryValue(BoundaryType.Dirichlet.ToString(), "T",
                 delegate (double[] X)
                 {
                     double x = X[0], y = X[1];
                     return Math.Sqrt(x * x + y * y);
                 });


            return R;
        }

        /// <summary>
        /// Creates Nodes, yes it really does!
        /// </summary>
        /// <param name="res"></param>
        /// <param name="stetch">
        /// Factor which determines how much the intervals in the output grow; 1.0 is no stretching.
        /// </param>
        /// <param name="min"></param>
        /// <param name="max"></param>
        /// <returns></returns>
        static double[] CreateNodes(int res, double stetch, double min, double max)
        {
            if (stetch == 1.0)
                return GenericBlas.Linspace(min, max, res + 1);
            else
                return Grid1D.ExponentialSpaceing(min, max, res + 1, stetch); // without proper preconditioning,
            // a stretched grid is much more expensive than
            // an equidistant grid !!!
        }

        /// <summary>
        /// Test on a Cartesian grid, with an exact polynomial solution.
        /// </summary>
        public static SipControl TestCartesian1(int xRes = 32, double xStretch = 1.0, int yRes = 16, double yStretch = 1.01, int pDG = 2)
        {
            //BoSSS.Application.SipPoisson.SipHardcodedControl.TestCartesian1()
            var RR = new SipControl();
            RR.ProjectName = "ipPoison/cartesian";
            RR.savetodb = false;

            RR.SetDGdegree(pDG);
            RR.InitialValues.Add("RHS", new Formula("X => 1.0"));
            RR.InitialValues.Add("Tex", new Formula("X => (0.5 * X[0]*X[0] - 10 * X[0])"));
            RR.ExactSolution_provided = true;

            RR.GridFunc = delegate () {
                double[] xNodes = CreateNodes(xRes, xStretch, 0, 10);
                double[] yNodes = CreateNodes(yRes, yStretch, -1, +1);

                var grd = Grid2D.Cartesian2DGrid(xNodes, yNodes);
                grd.EdgeTagNames.Add(1, BoundaryType.Dirichlet.ToString());
                grd.EdgeTagNames.Add(2, BoundaryType.Neumann.ToString());
                grd.DefineEdgeTags(delegate (double[] X) {
                    byte ret;
                    if (Math.Abs(X[0] - 0.0) <= 1.0e-6)
                        ret = 1;
                    else
                        ret = 2;
                    return ret;
                });

                return grd;
            };


            RR.AddBoundaryValue(BoundaryType.Dirichlet.ToString());
            RR.AddBoundaryValue(BoundaryType.Neumann.ToString());


            RR.GridPartType = BoSSS.Foundation.Grid.GridPartType.none;


            return RR;
        }

        /// <summary>
        /// Test on a Cartesian grid, with an exact polynomial solution.
        /// </summary>
        public static SipControl TestCartesian3D(int PowRes = 2, int DGdegree = 5, string blapath = null, int xRes = 2, double xStretch = 1.0, int yRes = 2, double yStretch = 1.0, int zRes = 2, double zStretch = 1.0) {
            xRes = (int)Math.Pow(xRes, PowRes);
            yRes = (int)Math.Pow(yRes, PowRes);
            zRes = (int)Math.Pow(zRes, PowRes);
            var R = new SipControl();
            R.ProjectName = "ipPoison/cartesian";
            R.savetodb = false;
            //R.WriteMeSomeAnalyse = @"D:\Analysis\CCpoisson\Study0_vary_Mlevel_n_blocks\";

            R.FieldOptions.Add("T", new FieldOpts() { Degree = DGdegree, SaveToDB = FieldOpts.SaveToDBOpt.TRUE });
            R.FieldOptions.Add("Tex", new FieldOpts() { Degree = DGdegree });
            R.InitialValues_Evaluators.Add("RHS", X => 1.0);
            R.InitialValues_Evaluators.Add("Tex", X => (0.5 * X[0].Pow2() - 10 * X[0]));
            R.ExactSolution_provided = true;

            R.GridFunc = delegate () {
                //double[] xNodes = CreateNodes(xRes, xStretch, 0, 10);
                double[] xNodes = CreateNodes(xRes, xStretch, -1, +1);
                double[] yNodes = CreateNodes(yRes, yStretch, -1, +1);
                //double[] zNodes = CreateNodes(zRes, zStretch, -1, +1);

                //var grd = Grid3D.Cartesian3DGrid(xNodes, yNodes, zNodes);
                var grd = Grid2D.Cartesian2DGrid(xNodes, yNodes);
                grd.EdgeTagNames.Add(1, BoundaryType.Dirichlet.ToString());
                grd.EdgeTagNames.Add(2, BoundaryType.Neumann.ToString());
                grd.DefineEdgeTags(delegate (double[] X) {
                    byte ret;
                    if (Math.Abs(X[0] - 0.0) <= 1.0e-6)
                        ret = 1;
                    else
                        ret = 2;
                    return ret;
                });

                return grd;
            };


            //R.LinearSolver.SolverCode = LinearSolverCode.exp_softpcg_jacobi_mg;
            //R.LinearSolver.SolverCode = LinearSolverCode.exp_decomposedMG_OrthoScheme;
            R.LinearSolver.SolverCode = LinearSolverCode.classic_pardiso;
            //R.LinearSolver.SolverCode = LinearSolverCode.exp_gmres_levelpmg;
            //R.LinearSolver.SolverCode = LinearSolverCode.exp_gmres_levelpmg;
            R.LinearSolver.NoOfMultigridLevels = 10;
            R.LinearSolver.TargetBlockSize = 40;
            R.LinearSolver.ConvergenceCriterion = 1e-8;
            R.LinearSolver.MaxSolverIterations = 10;
            //R.LinearSolver.MaxSolverIterations = 10;

            R.AddBoundaryValue(BoundaryType.Dirichlet.ToString());
            R.AddBoundaryValue(BoundaryType.Neumann.ToString());

            return R;
        }


        /// <summary>
        /// Test channel flow around a cylinder (half domain with symmetry condition)
        /// </summary>
        public static SipControl ConfinedCylinder(int k = 3)
        {
            var C = new SipControl();

            #region other settings

            // Miscellaneous Solver Settings
            C.ExactSolution_provided = false;
            C.savetodb = false;
            C.DbPath = @"D:\bosss_db_masterthesis";
            C.ProjectName = "ConfinedCylinderipPoisson";
            C.SessionName = "Confined Cylinder MG with ipPoisson";
            //C.WriteMeSomeAnalyse = @"C:\Users\Matth\Desktop";

            #endregion

            #region grid instantiation

            // GUID's to confined cylinder grids (half)
            List<string> grids = new List<string>();
            grids.Add("a8370a9b-86b6-4dda-8147-b30f897b2320");
            grids.Add("462f3f2e-8cd9-4563-a62f-191629ffd155");
            grids.Add("6270aeda-0ae8-4197-939b-4018cd9500fe");
            grids.Add("f97ff88f-8980-4760-ba54-76bd7071cdbd");
            /*grids.Add("0f6132db-a263-4140-b0b4-cca275f3af3c");*/ //half_0
            /*grids.Add("282f25a2-bb4e-4549-96c6-e5d8d806a607");*/ //half_1
            /*grids.Add("e174a74c-d2fc-40a1-af12-a16356264911");*/ //half_2
            /*grids.Add("de43ee58-c3b3-41bd-9df3-7deb883de36b");*/ //half_3

            Guid gridGuid;
            if (Guid.TryParse(grids[1], out gridGuid))
            {
                C.GridGuid = gridGuid;
            }
            else
            {
                throw new ArgumentException();
            }

            #endregion

            #region dgfields and bc

            // Setup DGFields
            C.FieldOptions.Add("T", new FieldOpts()
            {
                Degree = k,
                SaveToDB = FieldOpts.SaveToDBOpt.TRUE
            });
            C.FieldOptions.Add("Tex", new FieldOpts()
            {
                Degree = k,
                SaveToDB = FieldOpts.SaveToDBOpt.FALSE
            });

            // Boundary Values
            C.AddBoundaryValue("Dirichlet_inlet", "T", "X => 0", false);
            C.AddBoundaryValue("Dirichlet_top", "T", "X => 0", false);
            C.AddBoundaryValue("Dirichlet_outlet", "T", "X => 0", false);
            C.AddBoundaryValue("Dirichlet_bottom", "T", "X => 0", false);
            C.AddBoundaryValue("Dirichlet_cylinder", "T", "X => -10", false);

            #endregion

            #region RHS

            //Func<double[], double> exRhs = X => -1;

            //C.InitialValues_Evaluators.Add("RHS", exRhs);

            #endregion

            #region linear solver config

            // Linear Solver Settings
            C.LinearSolver.MaxKrylovDim = 5000;
            C.LinearSolver.MaxSolverIterations = 50;
            C.LinearSolver.NoOfMultigridLevels = 5;
            C.LinearSolver.SolverCode = LinearSolverCode.exp_Kcycle_schwarz;
            C.LinearSolver.TargetBlockSize = 10000;
            C.LinearSolver.SolverMode = LinearSolverMode.Solve;
            C.SuperSampling = 2;
            C.LinearSolver.ConvergenceCriterion = 1E-8;
            //C.LinearSolver.SolverMode = LinearSolverMode.SpectralAnalysis;

            #endregion

            return C;
        }


        /// <summary>
        /// Test on a Cartesian grid, with a sinusodial solution.
        /// </summary>
        /// <param name="Res">
        /// Grid resolution
        /// </param>
        /// <param name="Dim">
        /// spatial dimension
        /// </param>
        /// <param name="deg">
        /// polynomial degree
        /// </param>
        /// <param name="solver_name">
        /// Name of solver to use.
        /// </param>
        public static SipControl TestCartesian2(int Res, int Dim, LinearSolverCode solver_name = LinearSolverCode.exp_Kcycle_schwarz, int deg = 2) {
            //BoSSS.Application.SipPoisson.SipHardcodedControl.TestCartesian2(8,3,deg:2)


            if (Dim != 2 && Dim != 3)
                throw new ArgumentOutOfRangeException();

            var R = new SipControl();
            R.ProjectName = "ipPoison/cartesian";
            R.savetodb = false;

            R.FieldOptions.Add("T", new FieldOpts() { Degree = deg, SaveToDB = FieldOpts.SaveToDBOpt.TRUE });
            R.FieldOptions.Add("Tex", new FieldOpts() { Degree = deg + 2 });
            R.InitialValues_Evaluators.Add("RHS", X => -Math.Sin(X[0]));
            R.InitialValues_Evaluators.Add("Tex", X => Math.Sin(X[0]));
            R.ExactSolution_provided = true;
            R.LinearSolver.NoOfMultigridLevels = int.MaxValue;
            R.LinearSolver.SolverCode = solver_name;
            // exp_Kcycle_schwarz
            // exp_gmres_levelpmg

#if DEBUG
            // For testing in DEBUG mode, this setting enforces the use 
            // of many multigrid-levels. In 2D, the examples are so small that 
            R.LinearSolver.TargetBlockSize = 100;
#endif


            R.GridFunc = delegate () {
                GridCommons grd = null;
                if (Dim == 2) {
                    double[] xNodes = GenericBlas.Linspace(0, 10, Res * 5 + 1);
                    double[] yNodes = GenericBlas.SinLinSpacing(-1, +1, 0.6, Res + 1);

                    grd = Grid2D.Cartesian2DGrid(xNodes, yNodes);
                } else if (Dim == 3) {
                    double[] xNodes = GenericBlas.Linspace(0, 10, Res * 5 + 1);
                    //double[] yNodes = GenericBlas.SinLinSpacing(-1, +1, 0.6, Res + 1);
                    //double[] zNodes = GenericBlas.SinLinSpacing(-1, +1, 0.6, Res + 1);
                    double[] yNodes = GenericBlas.Linspace(-1, +1, Res + 1);
                    double[] zNodes = GenericBlas.Linspace(-1, +1, Res + 1);

                    grd = Grid3D.Cartesian3DGrid(xNodes, yNodes, zNodes);
                } else {
                    throw new NotSupportedException();
                }
                grd.EdgeTagNames.Add(1, BoundaryType.Dirichlet.ToString());
                grd.EdgeTagNames.Add(2, BoundaryType.Neumann.ToString());
                grd.DefineEdgeTags(delegate (double[] X) {
                    byte ret;
                    double x = X[0];
                    if (Math.Abs(x - 0.0) <= 1.0e-8)
                        ret = 1; // Dirichlet
                    else
                        ret = 2; // Neumann
                    return ret;
                });

                return grd;
            };

            R.AddBoundaryValue(BoundaryType.Dirichlet.ToString(), "T",
                 delegate (double[] X) {
                     double x = X[0], y = X[1];


                     return Math.Sin(x);
                     //if (Math.Abs(X[0] - (0.0)) < 1.0e-8)
                     //    return 0.0;

                     //throw new ArgumentOutOfRangeException();
                 });

            R.AddBoundaryValue(BoundaryType.Neumann.ToString(), "T",
                 delegate (double[] X) {
                     double x = X[0], y = X[1], z = X.Length > 2 ? X[2] : 0.0;

                     if (Math.Abs(y - 1.0) < 1.0e-8 || Math.Abs(y + 1.0) < 1.0e-8) // y = -1, y = +1
                         return 0;

                     if (X.Length > 2 && (Math.Abs(z - 1.0) < 1.0e-8 || Math.Abs(z + 1.0) < 1.0e-8)) // z = -1, z = +1
                         return 0;

                     //if (Math.Abs(X[0] - (+10.0)) < 1.0e-8)
                         return Math.Cos(x);

                     //throw new ArgumentOutOfRangeException();
                 });
            return R;
        }

        /// <summary>
        /// Poisson Equation on a (-1,1)x(-1,1), Dirichlet everywhere
        /// </summary>
        public static SipControl Square(int xRes = 5, int yRes = 5, int deg = 5) {

            //Func<double[], double> exRhs = X => 2 * X[0] * X[0] + 2 * X[1] * X[1] - 4;
            //Func<double[], double> exSol = X => (1.0 - X[0] * X[0]) * (1.0 - X[1] * X[1]);

            //Func<double[], double> exSol = X => (1.0 - X[1]);
            //Func<double[], double> exRhs = X => 0.0;

            Func<double[], double> exSol = X => -Math.Cos(X[0] * Math.PI * 0.5) * Math.Cos(X[1] * Math.PI * 0.5);
            Func<double[], double> exRhs = X => (Math.PI * Math.PI * 0.5 * Math.Cos(X[0] * Math.PI * 0.5) * Math.Cos(X[1] * Math.PI * 0.5)); // == - /\ exSol


            var R = new SipControl();
            R.ProjectName = "ipPoison/square";
            R.savetodb = false;
            //R.DbPath = "D:\\BoSSS-db";

            R.FieldOptions.Add("T", new FieldOpts() { Degree = deg, SaveToDB = FieldOpts.SaveToDBOpt.TRUE });
            R.FieldOptions.Add("Tex", new FieldOpts() { Degree = 4 });
            R.InitialValues_Evaluators.Add("RHS", exRhs);
            R.InitialValues_Evaluators.Add("Tex", exSol);
            R.ExactSolution_provided = true;
            //R.LinearSolver.NoOfMultigridLevels = 2;
            //R.LinearSolver.SolverCode = LinearSolverCode.exp_softpcg_mg;
            R.LinearSolver.SolverCode = LinearSolverCode.exp_softpcg_schwarz_directcoarse;
            R.SuppressExceptionPrompt = true;
            //R.LinearSolver.SolverCode = LinearSolverCode.classic_mumps;

            R.GridFunc = delegate () {
                double[] xNodes = GenericBlas.Linspace(-1, 1, xRes);
                double[] yNodes = GenericBlas.Linspace(-1, 1, yRes);
                var grd = Grid2D.Cartesian2DGrid(xNodes, yNodes);

                grd.EdgeTagNames.Add(1, BoundaryType.Dirichlet.ToString());
                grd.DefineEdgeTags(delegate (double[] X) {
                    byte ret = 1;
                    return ret;
                });


                return grd;
            };

            R.AddBoundaryValue(BoundaryType.Dirichlet.ToString(), "T", exSol);

            R.NoOfSolverRuns = 1;

            R.AdaptiveMeshRefinement = true;
            R.NoOfTimesteps = 5;


            return R;
        }

        /// <summary>
        /// Adaptive mesh refinement on a manufactured solution
        /// </summary>
        public static SipControl RefinementManufactured(int xRes = 2, int yRes = 2, int deg = 3) {

            double RHS(double[] X) {
                double x = X[0];
                double y = X[1];
                return (0.001 * Math.Pow(y, 2) * Math.Pow(y - 1, 2) * Math.Exp(10 * Math.Pow(x, 2) + 10 * y) * (200 * Math.Pow(x, 6)
                    - 400 * Math.Pow(x, 5) + 290 * Math.Pow(x, 4) - 140 * Math.Pow(x, 3) + 56 * Math.Pow(x, 2) - 6 * x + 1)
                    + 0.001 * Math.Pow(x, 2) * Math.Pow(x - 1, 2) * Math.Exp(10 * Math.Pow(x, 2) + 10 * y) * (50 * Math.Pow(y, 4)
                    - 60 * Math.Pow(y, 3)
                    - 4 * Math.Pow(y, 2) + 14 * y + 1));
            }
            double Tex(double[] X) {
                double x = X[0];
                double y = X[1];
                return (0.0005 * Math.Pow(x, 2) * Math.Pow(x - 1, 2) * Math.Pow(y, 2) * Math.Pow(y - 1, 2) * Math.Exp(10 * Math.Pow(x, 2)
                    + 10 * y));
            }

            Func<double[], double> exSol = Tex;
            Func<double[], double> exRhs = RHS;


            var R = new SipControl();
            R.ProjectName = "ipPoison/hRefinementManufactured";
            R.savetodb = false;
            //R.DbPath = "D:\\BoSSS-db";

            R.FieldOptions.Add("T", new FieldOpts() { Degree = deg, SaveToDB = FieldOpts.SaveToDBOpt.TRUE });
            R.FieldOptions.Add("Tex", new FieldOpts() { Degree = deg + 1 });
            R.InitialValues_Evaluators.Add("RHS", exRhs);
            R.InitialValues_Evaluators.Add("Tex", exSol);
            R.ExactSolution_provided = true;
            //R.LinearSolver.NoOfMultigridLevels = 2;
            //R.LinearSolver.SolverCode = LinearSolverCode.exp_softpcg_mg;
            R.LinearSolver.SolverCode = LinearSolverCode.classic_pardiso;
            R.SuppressExceptionPrompt = true;
            //R.LinearSolver.SolverCode = LinearSolverCode.classic_mumps;

            R.GridFunc = delegate () {
                double[] xNodes = GenericBlas.Linspace(0, 1, xRes + 1);
                double[] yNodes = GenericBlas.Linspace(0, 1, yRes + 1);
                var grd = Grid2D.Cartesian2DGrid(xNodes, yNodes);

                grd.EdgeTagNames.Add(1, BoundaryType.Dirichlet.ToString());
                grd.DefineEdgeTags(delegate (double[] X) {
                    byte ret = 1;
                    return ret;
                });


                return grd;
            };

            R.AddBoundaryValue(BoundaryType.Dirichlet.ToString(), "T", exSol);

            R.NoOfSolverRuns = 1;

            R.AdaptiveMeshRefinement = true;
            R.NoOfTimesteps = 100;

            return R;
        }
    }
}
