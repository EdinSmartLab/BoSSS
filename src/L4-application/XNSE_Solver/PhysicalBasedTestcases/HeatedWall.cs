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
using System.Threading.Tasks;

using ilPSP;
using ilPSP.Utils;
using BoSSS.Solution.Control;
using BoSSS.Solution.Multigrid;
using BoSSS.Solution.XNSECommon;
using BoSSS.Foundation.IO;
using BoSSS.Foundation.Grid;
using BoSSS.Foundation.Grid.Classic;
using BoSSS.Solution.XdgTimestepping;
using BoSSS.Solution.LevelSetTools.FourierLevelSet;
using BoSSS.Solution.Timestepping;
using BoSSS.Foundation.XDG;

namespace BoSSS.Application.XNSE_Solver.PhysicalBasedTestcases {
    
    /// <summary>
    /// class providing Controls for the Capillary rise testcases
    /// </summary>
    public static class HeatedWall {

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public static XNSE_Control HeatedWall_StartUp(int p = 2, int kelemR = 16, string _DbPath = null) {

            XNSE_Control C = new XNSE_Control();

            bool solveHeat = false;
            //_DbPath = @"\\dc1\userspace\smuda\cluster\CapillaryRise\CapillaryRise_studyDB";

            // basic database options
            // ======================
            #region db

            C.DbPath = _DbPath;
            C.savetodb = false; // C.DbPath != null;
            C.ProjectName = "XNSE/HeatedWall";
            C.ProjectDescription = "Leikonfiguration for SFB 1194";

            C.ContinueOnIoError = false;

            C.LogValues = XNSE_Control.LoggingValues.MovingContactLine;
            C.LogPeriod = 100;

            #endregion


            // DG degrees
            // ==========
            #region degrees

            C.FieldOptions.Add("VelocityX", new FieldOpts() {
                Degree = p,
                SaveToDB = FieldOpts.SaveToDBOpt.TRUE
            });
            C.FieldOptions.Add("VelocityY", new FieldOpts() {
                Degree = p,
                SaveToDB = FieldOpts.SaveToDBOpt.TRUE
            });
            C.FieldOptions.Add("GravityY", new FieldOpts() {
                SaveToDB = FieldOpts.SaveToDBOpt.TRUE
            });
            C.FieldOptions.Add("Pressure", new FieldOpts() {
                Degree = p - 1,
                SaveToDB = FieldOpts.SaveToDBOpt.TRUE
            });
            C.FieldOptions.Add("PhiDG", new FieldOpts() {
                SaveToDB = FieldOpts.SaveToDBOpt.TRUE
            });
            C.FieldOptions.Add("Phi", new FieldOpts() {
                Degree = p,
                SaveToDB = FieldOpts.SaveToDBOpt.TRUE
            });
            C.FieldOptions.Add("Curvature", new FieldOpts() {
                Degree = p,
                SaveToDB = FieldOpts.SaveToDBOpt.TRUE
            });
            C.FieldOptions.Add("Temperature", new FieldOpts() {
                Degree = p,
                SaveToDB = FieldOpts.SaveToDBOpt.TRUE
            });

            #endregion


            // Physical Parameters
            // ===================
            #region physics

            C.PhysicalParameters.rho_A = 1.0;
            C.PhysicalParameters.rho_B = 0.1;
            C.PhysicalParameters.mu_A = 1.0;
            C.PhysicalParameters.mu_B = 0.1;
            C.PhysicalParameters.Sigma = 1.0;

            C.solveCoupledHeatEquation = solveHeat;
            C.ThermalParameters.rho_A = C.PhysicalParameters.rho_A;
            C.ThermalParameters.rho_B = C.PhysicalParameters.rho_B;
            C.ThermalParameters.c_A = 1.0;
            C.ThermalParameters.c_B = 1.0;
            C.ThermalParameters.k_A = 1.0;
            C.ThermalParameters.k_B = 1.0;

            C.PhysicalParameters.betaS_A = 0.0;
            C.PhysicalParameters.betaS_B = 0.0;

            C.PhysicalParameters.betaL = 0.0;
            C.PhysicalParameters.theta_e = Math.PI / 3.0;

            C.PhysicalParameters.IncludeConvection = false;
            C.PhysicalParameters.Material = false;

            #endregion


            // grid generation
            // ===============
            #region grid

            double R = 4.0;
            double H = 12.0;

            C.GridFunc = delegate () {
                double[] Xnodes = GenericBlas.Linspace(0, R, kelemR + 1);
                double[] Ynodes = GenericBlas.Linspace(0, H, 3 * kelemR + 1);
                var grd = Grid2D.Cartesian2DGrid(Xnodes, Ynodes);

                if(solveHeat) {
                    grd.EdgeTagNames.Add(1, "wall_Dirichlet_lower");
                    grd.EdgeTagNames.Add(2, "pressure_outlet_ZeroGradient_upper");
                    grd.EdgeTagNames.Add(3, "slipsymmetry_ZeroGradient_left");
                    grd.EdgeTagNames.Add(4, "navierslip_linear_ZeroGradient_right");
                } else {
                    grd.EdgeTagNames.Add(1, "wall_lower");
                    grd.EdgeTagNames.Add(2, "pressure_outlet_upper");
                    grd.EdgeTagNames.Add(3, "slipsymmetry_left");
                    grd.EdgeTagNames.Add(4, "navierslip_linear_right");
                }

                grd.DefineEdgeTags(delegate (double[] X) {
                    byte et = 0;
                    if(Math.Abs(X[1]) <= 1.0e-8)
                        et = 1;
                    if(Math.Abs(X[1] - H) <= 1.0e-8)
                        et = 2;
                    if(Math.Abs(X[0]) <= 1.0e-8)
                        et = 3;
                    if(Math.Abs(X[0] - R) <= 1.0e-8)
                        et = 4;

                    return et;
                });

                return grd;
            };

            #endregion


            // Initial Values
            // ==============
            #region init

            double h0 = 3.0;

            Func<double[], double> PhiFunc = (X => X[1] - h0);

            C.InitialValues_Evaluators.Add("Phi", PhiFunc);

            double g = 1.5;
            C.InitialValues_Evaluators.Add("GravityY#A", X => -g);
            C.InitialValues_Evaluators.Add("GravityY#B", X => -g);

            //C.InitialValues_Evaluators.Add("Temperature#B", X => 10);

            //C.RestartInfo = new Tuple<Guid, TimestepNumber>(restartID, null);


            #endregion


            // boundary conditions
            // ===================
            #region BC

            if(solveHeat) {
                C.AddBoundaryValue("wall_Dirichlet_lower", "Temperature#A", (X, t) => 0.0);
                C.AddBoundaryValue("wall_Dirichlet_lower", "Temperature#B", (X, t) => 0.0);
                C.AddBoundaryValue("pressure_outlet_ZeroGradient_upper");

                C.AddBoundaryValue("slipsymmetry_ZeroGradient_left");
                C.AddBoundaryValue("navierslip_linear_ZeroGradient_right");
            } else {

                C.AddBoundaryValue("wall_lower");
                C.AddBoundaryValue("pressure_outlet_upper");

                C.AddBoundaryValue("slipsymmetry_left");
                C.AddBoundaryValue("navierslip_linear_right");
            }

            C.AdvancedDiscretizationOptions.GNBC_Localization = NavierSlip_Localization.Bulk;
            C.AdvancedDiscretizationOptions.GNBC_SlipLength = NavierSlip_SlipLength.Prescribed_SlipLength;
            C.PhysicalParameters.sliplength = 0.1;


            #endregion


            // misc. solver options
            // ====================
            #region solver

            C.ComputeEnergy = false;

            C.LSContiProjectionMethod = Solution.LevelSetTools.ContinuityProjectionOption.ContinuousDG;

            C.VelocityBlockPrecondMode = MultigridOperator.Mode.SymPart_DiagBlockEquilib;
            C.NoOfMultigridLevels = 1;
            C.Solver_MaxIterations = 50;
            C.Solver_ConvergenceCriterion = 1e-8;
            C.LevelSet_ConvergenceCriterion = 1e-6;


            C.AdvancedDiscretizationOptions.FilterConfiguration = CurvatureAlgorithms.FilterConfiguration.NoFilter;

            C.AdvancedDiscretizationOptions.SurfStressTensor = SurfaceSressTensor.Isotropic;
            //C.PhysicalParameters.mu_I = dt * 0.2;
            C.AdvancedDiscretizationOptions.UseLevelSetStabilization = false;

            C.AdvancedDiscretizationOptions.SST_isotropicMode = Solution.XNSECommon.SurfaceStressTensor_IsotropicMode.LaplaceBeltrami_ContactLine;


            C.AdaptiveMeshRefinement = false;
            C.RefineStrategy = XNSE_Control.RefinementStrategy.constantInterface;
            C.RefineNavierSlipBoundary = false;
            C.RefinementLevel = 1;

            #endregion


            // level-set
            // =========
            #region levelset

            C.Option_LevelSetEvolution = LevelSetEvolution.FastMarching;

            #endregion


            // Timestepping
            // ============
            #region time

            C.Timestepper_Scheme = XNSE_Control.TimesteppingScheme.ImplicitEuler;
            C.Timestepper_BDFinit = TimeStepperInit.SingleInit;
            C.Timestepper_LevelSetHandling = LevelSetHandling.Coupled_Once;

            C.CompMode = AppControl._CompMode.Transient;
            C.dtMax = 1e-1;
            C.dtMin = 1e-1;
            C.Endtime = 10000;
            C.NoOfTimesteps = 100;
            C.saveperiod = 1;

            #endregion


            return C;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public static XNSE_Control HeatedWall_Run(int p = 2, int kelemR = 8, string _DbPath = null) {

            XNSE_Control C = new XNSE_Control();

            bool solveHeat = true;
            //_DbPath = @"\\dc1\userspace\smuda\cluster\CapillaryRise\CapillaryRise_studyDB";

            // basic database options
            // ======================
            #region db

            C.DbPath = _DbPath;
            C.savetodb = false; // C.DbPath != null;
            C.ProjectName = "XNSE/HeatedWall";
            C.ProjectDescription = "Leikonfiguration for SFB 1194";

            C.ContinueOnIoError = false;

            C.LogValues = XNSE_Control.LoggingValues.MovingContactLine;
            C.LogPeriod = 100;

            #endregion


            // DG degrees
            // ==========
            #region degrees

            C.FieldOptions.Add("VelocityX", new FieldOpts() {
                Degree = p,
                SaveToDB = FieldOpts.SaveToDBOpt.TRUE
            });
            C.FieldOptions.Add("VelocityY", new FieldOpts() {
                Degree = p,
                SaveToDB = FieldOpts.SaveToDBOpt.TRUE
            });
            C.FieldOptions.Add("GravityY", new FieldOpts() {
                SaveToDB = FieldOpts.SaveToDBOpt.TRUE
            });
            C.FieldOptions.Add("Pressure", new FieldOpts() {
                Degree = p - 1,
                SaveToDB = FieldOpts.SaveToDBOpt.TRUE
            });
            C.FieldOptions.Add("PhiDG", new FieldOpts() {
                SaveToDB = FieldOpts.SaveToDBOpt.TRUE
            });
            C.FieldOptions.Add("Phi", new FieldOpts() {
                Degree = p,
                SaveToDB = FieldOpts.SaveToDBOpt.TRUE
            });
            C.FieldOptions.Add("Curvature", new FieldOpts() {
                Degree = p,
                SaveToDB = FieldOpts.SaveToDBOpt.TRUE
            });
            C.FieldOptions.Add("Temperature", new FieldOpts() {
                Degree = p,
                SaveToDB = FieldOpts.SaveToDBOpt.TRUE
            });

            #endregion


            // Physical Parameters
            // ===================
            #region physics


            // numerical values for various testing
            C.PhysicalParameters.rho_A = 1.0;
            C.PhysicalParameters.rho_B = 1.0;
            C.PhysicalParameters.mu_A = 0.25;
            C.PhysicalParameters.mu_B = 0.25;
            C.PhysicalParameters.Sigma = 7.5;

            C.solveCoupledHeatEquation = solveHeat;
            C.ThermalParameters.rho_A = C.PhysicalParameters.rho_A;
            C.ThermalParameters.rho_B = C.PhysicalParameters.rho_B;
            C.ThermalParameters.c_A = 1.0;
            C.ThermalParameters.c_B = 1.0;
            C.ThermalParameters.k_A = 1.0;
            C.ThermalParameters.k_B = 0.1;

            if(C.solveCoupledHeatEquation) {
                C.ThermalParameters.hVap_A = 0.0; // 1.0;
                C.ThermalParameters.hVap_B = 0.0; // -1.0;
            }
            double Tsat = 329.75;  
            C.ThermalParameters.T_sat = Tsat;

            C.PhysicalParameters.theta_e = Math.PI * (1.0 / 2.0);


            // FC-72 (A:liquid and B:vapor state)
            //C.PhysicalParameters.rho_A = 1.6198e-3;     // 1.6198e-3;
            //C.PhysicalParameters.rho_B = 1.336e-5;       // 1.336e-5;
            //C.PhysicalParameters.mu_A = 4.5306e-4;      // 4.5306e-6;
            //C.PhysicalParameters.mu_B = 9.4602e-6;      // 9.4602e-8;
            //C.PhysicalParameters.Sigma = 8.273e-3;

            //C.solveCoupledHeatEquation = solveHeat;
            //C.ThermalParameters.rho_A = C.PhysicalParameters.rho_A;
            //C.ThermalParameters.rho_B = C.PhysicalParameters.rho_B;
            //C.ThermalParameters.c_A = 1.0984e+7;
            //C.ThermalParameters.c_B = 8.8504e+6;
            //C.ThermalParameters.k_A = 5.216;
            //C.ThermalParameters.k_B = 0.864;

            //if(C.solveCoupledHeatEquation) {
            //    C.ThermalParameters.hVap_A = 8.4515e+8;
            //    C.ThermalParameters.hVap_B = -8.4515e+8;
            //} 
            //C.ThermalParameters.pc = 0.0;
            //double Tsat = 329.75;    // for pc=0, T_intMin = T_sat
            //C.ThermalParameters.T_sat = Tsat;
            //C.ThermalParameters.p_sat = 1000;   // 1bar
            //C.ThermalParameters.Rc = 2.445e+5;
            //C.ThermalParameters.fc = 0.5;

            //double A = 4.37e-17; //4.37e-17;    // dispersion constant

            //C.PhysicalParameters.betaS_A = 0.0;
            //C.PhysicalParameters.betaS_B = 0.0;

            //C.PhysicalParameters.betaL = 0.0;
            //C.PhysicalParameters.theta_e = Math.PI * (5.0 / 36.0);

            C.PhysicalParameters.IncludeConvection = false;
            C.PhysicalParameters.Material = false;

            #endregion


            // grid generation
            // ===============
            #region grid

            double R = 1.0;
            double H = 1.0;

            C.GridFunc = delegate () {
                double[] Xnodes = GenericBlas.Linspace(0, R, kelemR + 1);
                double[] Ynodes = GenericBlas.Linspace(0, H, (kelemR) + 1);
                var grd = Grid2D.Cartesian2DGrid(Xnodes, Ynodes);

                if(solveHeat) {
                    grd.EdgeTagNames.Add(1, "wall_ZeroGradient_lower");
                    grd.EdgeTagNames.Add(2, "pressure_outlet_ZeroGradient_upper");
                    grd.EdgeTagNames.Add(3, "slipsymmetry_ZeroGradient_left");
                    grd.EdgeTagNames.Add(4, "navierslip_linear_ConstantTemperature_right");
                    //grd.EdgeTagNames.Add(4, "navierslip_linear_ConstantHeatFlux_right");
                } else {
                    grd.EdgeTagNames.Add(1, "wall_lower");
                    //grd.EdgeTagNames.Add(1, "pressure_outlet_lower");
                    grd.EdgeTagNames.Add(2, "pressure_outlet_upper");
                    grd.EdgeTagNames.Add(3, "slipsymmetry_left");
                    //grd.EdgeTagNames.Add(3, "pressure_outlet_left");
                    grd.EdgeTagNames.Add(4, "navierslip_linear_right");
                }

                grd.DefineEdgeTags(delegate (double[] X) {
                    byte et = 0;
                    if(Math.Abs(X[1]) <= 1.0e-8)
                        et = 1;
                    if(Math.Abs(X[1] - H) <= 1.0e-8)
                        et = 2;
                    if(Math.Abs(X[0]) <= 1.0e-8)
                        et = 3;
                    if(Math.Abs(X[0] - R) <= 1.0e-8)
                        et = 4;

                    return et;
                });

                return grd;
            };

            #endregion


            // Initial Values
            // ==============
            #region init

            double h0 = 0.4;

            Func<double[], double> PhiFunc = (X => X[1] - h0);

            C.InitialValues_Evaluators.Add("Phi", PhiFunc);

            double g = 9.81;
            C.InitialValues_Evaluators.Add("GravityY#A", X => -g);
            C.InitialValues_Evaluators.Add("GravityY#B", X => -g);

            // disjoining pressure field
            //C.DisjoiningPressureFunc = (X => A / (Math.Abs(X[0] - R)).Pow(3));

            if(C.solveCoupledHeatEquation) {
                C.InitialValues_Evaluators.Add("Temperature#A", X => Tsat);
                C.InitialValues_Evaluators.Add("Temperature#B", X => Tsat);
            }

            //C.RestartInfo = new Tuple<Guid, TimestepNumber>(restartID, null);


            #endregion


            // boundary conditions
            // ===================
            #region BC

            double U = 1.0;
            double deltaK = 5;
            double WallTemp = Tsat + deltaK;
            //double HeatFlux = 10.0;

            if(solveHeat) {
                C.AddBoundaryValue("wall_ZeroGradient_lower");
                C.AddBoundaryValue("pressure_outlet_ZeroGradient_upper");

                C.AddBoundaryValue("slipsymmetry_ZeroGradient_left");

                C.AddBoundaryValue("navierslip_linear_ConstantTemperature_right", "VelocityY#A", (X, t) => U);
                C.AddBoundaryValue("navierslip_linear_ConstantTemperature_right", "VelocityY#B", (X, t) => U);
                C.AddBoundaryValue("navierslip_linear_ConstantTemperature_right", "Temperature#A", (X, t) => WallTemp);
                C.AddBoundaryValue("navierslip_linear_ConstantTemperature_right", "Temperature#B", (X, t) => WallTemp);
                //C.AddBoundaryValue("navierslip_linear_ConstantHeatFlux_right", "VelocityY#A", (X, t) => U);
                //C.AddBoundaryValue("navierslip_linear_ConstantHeatFlux_right", "VelocityY#B", (X, t) => U);
                //C.AddBoundaryValue("navierslip_linear_ConstantHeatFlux_right", "HeatFlux#A", (X, t) => HeatFlux);
                //C.AddBoundaryValue("navierslip_linear_ConstantHeatFlux_right", "HeatFlux#B", (X, t) => HeatFlux);
            } else {

                C.AddBoundaryValue("wall_lower");
                //C.AddBoundaryValue("pressure_outlet_lower");
                C.AddBoundaryValue("pressure_outlet_upper");

                C.AddBoundaryValue("slipsymmetry_left");
                //C.AddBoundaryValue("pressure_outlet_left");

                C.AddBoundaryValue("navierslip_linear_right", "VelocityY#A", (X, t) => U);
                C.AddBoundaryValue("navierslip_linear_right", "VelocityY#B", (X, t) => U);
            }

            C.AdvancedDiscretizationOptions.GNBC_Localization = NavierSlip_Localization.Bulk;
            C.AdvancedDiscretizationOptions.GNBC_SlipLength = NavierSlip_SlipLength.hmin_Grid;
            //C.PhysicalParameters.sliplength = 0.001;


            #endregion


            // misc. solver options
            // ====================
            #region solver

            C.ComputeEnergy = false;

            C.LSContiProjectionMethod = Solution.LevelSetTools.ContinuityProjectionOption.ContinuousDG;

            C.VelocityBlockPrecondMode = MultigridOperator.Mode.SymPart_DiagBlockEquilib;
            C.NoOfMultigridLevels = 1;
            C.Solver_MaxIterations = 50;
            C.Solver_ConvergenceCriterion = 1e-8;
            C.LevelSet_ConvergenceCriterion = 1e-6;


            C.AdvancedDiscretizationOptions.FilterConfiguration = CurvatureAlgorithms.FilterConfiguration.NoFilter;

            C.AdvancedDiscretizationOptions.SurfStressTensor = SurfaceSressTensor.Isotropic;
            //C.PhysicalParameters.mu_I = dt * 0.2;
            C.AdvancedDiscretizationOptions.UseLevelSetStabilization = false;

            C.AdvancedDiscretizationOptions.SST_isotropicMode = Solution.XNSECommon.SurfaceStressTensor_IsotropicMode.LaplaceBeltrami_ContactLine;
            C.AdvancedDiscretizationOptions.CurvatureNeeded = true;


            C.AdaptiveMeshRefinement = true;
            C.RefineStrategy = XNSE_Control.RefinementStrategy.constantInterface;
            C.RefineNavierSlipBoundary = true;
            C.BaseRefinementLevel = 1;

            #endregion


            // level-set
            // =========
            #region levelset

            C.Option_LevelSetEvolution = LevelSetEvolution.FastMarching;

            #endregion


            // Timestepping
            // ============
            #region time

            C.Timestepper_Scheme = XNSE_Control.TimesteppingScheme.ImplicitEuler;
            C.Timestepper_BDFinit = TimeStepperInit.SingleInit;
            C.Timestepper_LevelSetHandling = LevelSetHandling.LieSplitting;

            C.CompMode = AppControl._CompMode.Transient;
            C.dtMax = 5e-3;
            C.dtMin = 5e-3;
            C.Endtime = 10000;
            C.NoOfTimesteps = 10000;
            C.saveperiod = 10;

            #endregion


            return C;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="p"></param>
        /// <param name="kelemR"></param>
        /// <param name="_DbPath"></param>
        /// <returns></returns>
        public static XNSE_Control ThermodynamicEquilibrium_Test(int p = 2, int kelemR = 16, string _DbPath = null) {

            XNSE_Control C = new XNSE_Control();

            C.CutCellQuadratureType = XQuadFactoryHelper.MomentFittingVariants.Classic;

            //_DbPath = @"\\dc1\userspace\smuda\cluster\CapillaryRise\CapillaryRise_studyDB";

            // basic database options
            // ======================
            #region db

            C.DbPath = _DbPath;
            C.savetodb = false; // C.DbPath != null;
            C.ProjectName = "XNSE/HeatedWall";
            //C.ProjectDescription = "Leikonfiguration for SFB 1194";

            C.ContinueOnIoError = false;

            #endregion


            // DG degrees
            // ==========
            #region degrees

            C.FieldOptions.Add("VelocityX", new FieldOpts() {
                Degree = p,
                SaveToDB = FieldOpts.SaveToDBOpt.TRUE
            });
            C.FieldOptions.Add("VelocityY", new FieldOpts() {
                Degree = p,
                SaveToDB = FieldOpts.SaveToDBOpt.TRUE
            });
            C.FieldOptions.Add("Pressure", new FieldOpts() {
                Degree = p - 1,
                SaveToDB = FieldOpts.SaveToDBOpt.TRUE
            });
            C.FieldOptions.Add("PhiDG", new FieldOpts() {
                SaveToDB = FieldOpts.SaveToDBOpt.TRUE
            });
            C.FieldOptions.Add("Phi", new FieldOpts() {
                Degree = p,
                SaveToDB = FieldOpts.SaveToDBOpt.TRUE
            });
            C.FieldOptions.Add("Curvature", new FieldOpts() {
                Degree = p,
                SaveToDB = FieldOpts.SaveToDBOpt.TRUE
            });
            C.FieldOptions.Add("Temperature", new FieldOpts() {
                Degree = p,
                SaveToDB = FieldOpts.SaveToDBOpt.TRUE
            });

            #endregion


            // Physical Parameters
            // ===================
            #region physics


            // Water (A: liquid, B: gaseous)
            C.PhysicalParameters.rho_A = 1.0;
            C.PhysicalParameters.rho_B = 0.1;
            C.PhysicalParameters.mu_A = 1.0;    
            C.PhysicalParameters.mu_B = 1.0;
            C.PhysicalParameters.Sigma = 0.0; 

            C.solveCoupledHeatEquation = true;
            C.ThermalParameters.rho_A = C.PhysicalParameters.rho_A;
            C.ThermalParameters.rho_B = C.PhysicalParameters.rho_B;
            C.ThermalParameters.c_A = 1.0;
            C.ThermalParameters.c_B = 0.1;
            C.ThermalParameters.k_A = 1.0;
            double kv = 0.1;
            C.ThermalParameters.k_B = kv;

            if(C.solveCoupledHeatEquation) {
                C.ThermalParameters.hVap_A = 100.0; 
                C.ThermalParameters.hVap_B = -100.0;
            }

            double Tsat = 100.0;
            C.ThermalParameters.T_sat = Tsat;    


            C.PhysicalParameters.IncludeConvection = false;
            C.ThermalParameters.IncludeConvection = false;
            C.PhysicalParameters.Material = false;

            #endregion


            // grid generation
            // ===============
            #region grid

            double L = 1.0;

            C.GridFunc = delegate () {
                double[] Xnodes = GenericBlas.Linspace(0, L, kelemR + 1);
                double[] Ynodes = GenericBlas.Linspace(0, L, kelemR + 1);
                var grd = Grid2D.Cartesian2DGrid(Xnodes, Ynodes, periodicX:true);

                grd.EdgeTagNames.Add(1, "wall_ConstantHeatFlux_lower");
                grd.EdgeTagNames.Add(2, "pressure_Dirichlet_ZeroGradient_upper");

                grd.DefineEdgeTags(delegate (double[] X) {
                    byte et = 0;
                    if(Math.Abs(X[1]) <= 1.0e-8)
                        et = 1;
                    if(Math.Abs(X[1] - L) <= 1.0e-8)
                        et = 2;

                    return et;
                });

                return grd;
            };

            #endregion


            // Initial Values
            // ==============
            #region init

            double zi0 = 0.1;

            Func<double[], double> PhiFunc = (X => zi0 - X[1]);     // A: vapor ; B: liquid

            C.InitialValues_Evaluators.Add("Phi", PhiFunc);

            double qv = 10.0;

            C.InitialValues_Evaluators.Add("Temperature#A", X => Tsat);
            C.InitialValues_Evaluators.Add("Temperature#B", X => Tsat + (qv/kv)*(zi0 - X[1]));


            //C.RestartInfo = new Tuple<Guid, TimestepNumber>(restartID, null);


            #endregion


            // boundary conditions
            // ===================
            #region BC

            //C.AddBoundaryValue("wall_ConstantHeatFlux_lower", "HeatFlux#A", (X, t) => HeatFlux);
            C.AddBoundaryValue("wall_ConstantHeatFlux_lower", "HeatFlux#B", (X, t) => -qv);

            C.AddBoundaryValue("pressure_Dirichlet_ZeroGradient_upper");


            #endregion


            // misc. solver options
            // ====================
            #region solver

            C.ComputeEnergy = false;

            C.LSContiProjectionMethod = Solution.LevelSetTools.ContinuityProjectionOption.ContinuousDG;

            C.VelocityBlockPrecondMode = MultigridOperator.Mode.SymPart_DiagBlockEquilib;
            C.NoOfMultigridLevels = 1;
            C.Solver_MaxIterations = 50;
            C.Solver_ConvergenceCriterion = 1e-8;
            C.LevelSet_ConvergenceCriterion = 1e-6;


            C.AdvancedDiscretizationOptions.FilterConfiguration = CurvatureAlgorithms.FilterConfiguration.NoFilter;

            C.AdvancedDiscretizationOptions.SurfStressTensor = SurfaceSressTensor.Isotropic;
            //C.PhysicalParameters.mu_I = dt * 0.2;
            C.AdvancedDiscretizationOptions.UseLevelSetStabilization = false;

            C.AdvancedDiscretizationOptions.SST_isotropicMode = Solution.XNSECommon.SurfaceStressTensor_IsotropicMode.LaplaceBeltrami_ContactLine;
            //C.AdvancedDiscretizationOptions.CurvatureNeeded = true;


            C.AdaptiveMeshRefinement = false;
            C.RefineStrategy = XNSE_Control.RefinementStrategy.constantInterface;
            C.RefineNavierSlipBoundary = false;
            C.BaseRefinementLevel = 1;

            #endregion


            // level-set
            // =========
            #region levelset

            C.Option_LevelSetEvolution = LevelSetEvolution.FastMarching;

            #endregion


            // Timestepping
            // ============
            #region time

            C.Timestepper_Scheme = XNSE_Control.TimesteppingScheme.ImplicitEuler;
            C.Timestepper_BDFinit = TimeStepperInit.SingleInit;
            C.Timestepper_LevelSetHandling = LevelSetHandling.LieSplitting;

            C.CompMode = AppControl._CompMode.Transient;
            C.dtMax = 5e-3;
            C.dtMin = 5e-3;
            C.Endtime = 10000;
            C.NoOfTimesteps = 1000;
            C.saveperiod = 1;

            #endregion


            return C;
        }

        
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public static XNSE_Control Evaporation_test(int p = 2, int kelemR = 16, string _DbPath = null) {

            XNSE_Control C = new XNSE_Control();

            bool solveHeat = true;

            //_DbPath = @"\\dc1\userspace\smuda\cluster\CapillaryRise\CapillaryRise_studyDB";

            // basic database options
            // ======================
            #region db

            C.DbPath = _DbPath;
            C.savetodb = false; // C.DbPath != null;
            C.ProjectName = "XNSE/HeatedWall";
            C.ProjectDescription = "Leikonfiguration for SFB 1194";

            C.ContinueOnIoError = false;

            C.LogValues = XNSE_Control.LoggingValues.MovingContactLine;
            C.LogPeriod = 100;

            #endregion

            
            // DG degrees
            // ==========
            #region degrees

            C.FieldOptions.Add("VelocityX", new FieldOpts() {
                Degree = p,
                SaveToDB = FieldOpts.SaveToDBOpt.TRUE
            });
            C.FieldOptions.Add("VelocityY", new FieldOpts() {
                Degree = p,
                SaveToDB = FieldOpts.SaveToDBOpt.TRUE
            });
            C.FieldOptions.Add("GravityY", new FieldOpts() {
                SaveToDB = FieldOpts.SaveToDBOpt.TRUE
            });
            C.FieldOptions.Add("Pressure", new FieldOpts() {
                Degree = p - 1,
                SaveToDB = FieldOpts.SaveToDBOpt.TRUE
            });
            C.FieldOptions.Add("PhiDG", new FieldOpts() {
                SaveToDB = FieldOpts.SaveToDBOpt.TRUE
            });
            C.FieldOptions.Add("Phi", new FieldOpts() {
                Degree = p,
                SaveToDB = FieldOpts.SaveToDBOpt.TRUE
            });
            C.FieldOptions.Add("Curvature", new FieldOpts() {
                Degree = p,
                SaveToDB = FieldOpts.SaveToDBOpt.TRUE
            });
            C.FieldOptions.Add("Temperature", new FieldOpts() {
                Degree = p,
                SaveToDB = FieldOpts.SaveToDBOpt.TRUE
            });

            #endregion


            // Physical Parameters
            // ===================
            #region physics

            C.PhysicalParameters.rho_A = 1.0;
            C.PhysicalParameters.rho_B = 0.1;
            C.PhysicalParameters.mu_A = 1.0;
            C.PhysicalParameters.mu_B = 1.0;
            C.PhysicalParameters.Sigma = 0.0;

            C.solveCoupledHeatEquation = solveHeat;
            C.ThermalParameters.rho_A = C.PhysicalParameters.rho_A;
            C.ThermalParameters.rho_B = C.PhysicalParameters.rho_B;
            C.ThermalParameters.c_A = 1.0;
            C.ThermalParameters.c_B = 1.0;
            C.ThermalParameters.k_A = 1.0;
            C.ThermalParameters.k_B = 1.0;

            //C.ThermalParameters.prescribedVolumeFlux = 0.1;
            //C.PhysicalParameters.prescribedVolumeFlux = C.ThermalParameters.prescribedVolumeFlux;
            C.ThermalParameters.hVap_A = 1.0;
            C.ThermalParameters.hVap_B = -1.0;

            C.PhysicalParameters.betaS_A = 0.0;
            C.PhysicalParameters.betaS_B = 0.0;

            C.PhysicalParameters.betaL = 0.0;
            C.PhysicalParameters.theta_e = Math.PI / 2.0;

            C.PhysicalParameters.IncludeConvection = false;
            C.PhysicalParameters.Material = false;

            #endregion


            // grid generation
            // ===============
            #region grid

            double R = 1.0;
            double H = 3.0;

            C.GridFunc = delegate () {
                double[] Xnodes = GenericBlas.Linspace(0, R, kelemR + 1);
                double[] Ynodes = GenericBlas.Linspace(0, H, 3 * kelemR + 1);
                var grd = Grid2D.Cartesian2DGrid(Xnodes, Ynodes);


                grd.EdgeTagNames.Add(1, "velocity_inlet_ConstantTemperature_lower");
                grd.EdgeTagNames.Add(2, "pressure_outlet_ZeroGradient_upper");
                grd.EdgeTagNames.Add(3, "freeslip_ZeroGradient_left");
                grd.EdgeTagNames.Add(4, "freeslip_ZeroGradient_right");

                //grd.EdgeTagNames.Add(1, "wall_lower");
                //grd.EdgeTagNames.Add(2, "pressure_outlet_upper");
                //grd.EdgeTagNames.Add(3, "freeslip_left");
                //grd.EdgeTagNames.Add(4, "freeslip_right");

                //grd.EdgeTagNames.Add(1, "velocity_inlet_lower");
                //grd.EdgeTagNames.Add(2, "pressure_outlet_upper");
                //grd.EdgeTagNames.Add(3, "freeslip_left");
                //grd.EdgeTagNames.Add(4, "freeslip_right");


                grd.DefineEdgeTags(delegate (double[] X) {
                    byte et = 0;
                    if(Math.Abs(X[1]) <= 1.0e-8)
                        et = 1;
                    if(Math.Abs(X[1] - H) <= 1.0e-8)
                        et = 2;
                    if(Math.Abs(X[0]) <= 1.0e-8)
                        et = 3;
                    if(Math.Abs(X[0] - R) <= 1.0e-8)
                        et = 4;

                    return et;
                });

                return grd;
            };

            #endregion


            // Initial Values
            // ==============
            #region init

            double h0 = 1.05;

            Func<double[], double> PhiFunc = (X => X[1] - h0);

            C.InitialValues_Evaluators.Add("Phi", PhiFunc);

            //C.InitialValues_Evaluators.Add("GravityY#A", X => -g);
            //C.InitialValues_Evaluators.Add("GravityY#B", X => -g);

            //C.InitialValues_Evaluators.Add("VelocityY#A", X => 0.1);
            //C.InitialValues_Evaluators.Add("VelocityY#B", X => 1.0);

            //C.RestartInfo = new Tuple<Guid, TimestepNumber>(restartID, null);


            #endregion


            // boundary conditions
            // ===================
            #region BC


            C.AddBoundaryValue("velocity_inlet_ConstantTemperature_lower", "Temperature#A", (X, t) => 5.0);
            C.AddBoundaryValue("velocity_inlet_ConstantTemperature_lower", "VelocityY#A", (X, t) => 0.1);
            C.AddBoundaryValue("pressure_outlet_ZeroGradient_upper");
            C.AddBoundaryValue("freeslip_ZeroGradient_left");
            C.AddBoundaryValue("freeslip_ZeroGradient_right");


            //C.AddBoundaryValue("wall_lower");
            //C.AddBoundaryValue("pressure_outlet_upper");
            //C.AddBoundaryValue("freeslip_left");
            //C.AddBoundaryValue("freeslip_right");


            //C.AddBoundaryValue("velocity_inlet_lower", "VelocityY#A", (X, t) => 0.1);
            //C.AddBoundaryValue("pressure_outlet_upper");
            //C.AddBoundaryValue("freeslip_left");
            //C.AddBoundaryValue("freeslip_right");


            //C.AdvancedDiscretizationOptions.GNBC_Localization = NavierSlip_Localization.Bulk;
            //C.AdvancedDiscretizationOptions.GNBC_SlipLength = NavierSlip_SlipLength.hmin_Grid;


            #endregion


            // misc. solver options
            // ====================
            #region solver

            C.ComputeEnergy = false;

            C.LSContiProjectionMethod = Solution.LevelSetTools.ContinuityProjectionOption.ContinuousDG;

            C.VelocityBlockPrecondMode = MultigridOperator.Mode.SymPart_DiagBlockEquilib;
            C.NoOfMultigridLevels = 1;
            C.Solver_MaxIterations = 50;
            C.Solver_ConvergenceCriterion = 1e-8;
            C.LevelSet_ConvergenceCriterion = 1e-6;


            C.AdvancedDiscretizationOptions.FilterConfiguration = CurvatureAlgorithms.FilterConfiguration.NoFilter;

            C.AdvancedDiscretizationOptions.SurfStressTensor = SurfaceSressTensor.Isotropic;
            //C.PhysicalParameters.mu_I = dt * 0.2;
            C.AdvancedDiscretizationOptions.UseLevelSetStabilization = false;

            C.AdvancedDiscretizationOptions.SST_isotropicMode = Solution.XNSECommon.SurfaceStressTensor_IsotropicMode.LaplaceBeltrami_ContactLine;


            C.AdaptiveMeshRefinement = false;
            C.RefineStrategy = XNSE_Control.RefinementStrategy.constantInterface;
            C.RefineNavierSlipBoundary = false;
            C.RefinementLevel = 1;

            #endregion


            // level-set
            // =========
            #region levelset

            C.Option_LevelSetEvolution = LevelSetEvolution.None;

            #endregion


            // Timestepping
            // ============
            #region time

            C.Timestepper_Scheme = XNSE_Control.TimesteppingScheme.ImplicitEuler;
            C.Timestepper_BDFinit = TimeStepperInit.SingleInit;
            C.Timestepper_LevelSetHandling = LevelSetHandling.None;

            C.CompMode = AppControl._CompMode.Transient;
            C.dtMax = 1e-1;
            C.dtMin = 1e-1;
            C.Endtime = 10000;
            C.NoOfTimesteps = 100;
            C.saveperiod = 1;

            #endregion


            return C;
        }


    }
}
