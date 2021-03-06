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
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Numerics;

using ilPSP;
using ilPSP.Connectors.Matlab;
using ilPSP.Utils;
using ilPSP.Tracing;
using ilPSP.LinSolvers;

using BoSSS.Platform;

using BoSSS.Foundation;
using BoSSS.Foundation.Grid;
using BoSSS.Foundation.Grid.Classic;
using BoSSS.Foundation.IO;
using BoSSS.Foundation.Quadrature;
using BoSSS.Foundation.SpecFEM;
using BoSSS.Foundation.XDG;

using BoSSS.Solution;
using BoSSS.Solution.Control;
using BoSSS.Solution.LevelSetTools;
using BoSSS.Solution.LevelSetTools.FourierLevelSet;
using BoSSS.Solution.LevelSetTools.EllipticReInit;
using BoSSS.Solution.LevelSetTools.Reinit.FastMarch;
using BoSSS.Solution.LevelSetTools.Advection;
using BoSSS.Solution.AdvancedSolvers;
using BoSSS.Solution.NSECommon;
using BoSSS.Solution.Tecplot;
using BoSSS.Solution.Utils;
using BoSSS.Solution.XheatCommon;
using BoSSS.Solution.XNSECommon;
using BoSSS.Solution.Timestepping;
using BoSSS.Solution.XdgTimestepping;
using BoSSS.Foundation.Grid.Aggregation;
using NUnit.Framework;
using MPI.Wrappers;
using System.Collections;
using BoSSS.Solution.XNSECommon.Operator.SurfaceTension;
using BoSSS.Application.SemiLagrangianLevelSetTestSuite;
using BoSSS.Solution.LevelSetTools.PhasefieldLevelSet;

namespace BoSSS.Application.XNSE_Solver {

    /// <summary>
    /// Solver for Incompressible Multiphase flows; 
    /// </summary>
    public partial class XNSE_SolverMain : BoSSS.Solution.Application<XNSE_Control> {

        //=========================================
        // partial file for level-set related code
        //=========================================


        //=====================================
        // Field declaration and instantiation
        //=====================================
        #region fields

#pragma warning disable 649

        /// <summary>
        /// the DG representation of the level set.
        /// This one is used for level-set evolution in time; it is in general discontinuous.
        /// </summary>
        //[InstantiateFromControlFile("PhiDG", "Phi", IOListOption.ControlFileDetermined)]
        ScalarFieldHistory<SinglePhaseField> DGLevSet;

        /// <summary>
        /// corresponding DG representation of the gradient field
        /// </summary>
        VectorField<SinglePhaseField> DGLevSetGradient;

        /// <summary>
        /// The continuous level set field which defines the XDG space; 
        /// it is obtained from the projection of the discontinuous <see cref="DGLevSet"/> onto the 
        /// continuous element space.
        /// </summary>
        //[InstantiateFromControlFile("Phi", "Phi", IOListOption.ControlFileDetermined)]
        LevelSet LevSet;

        /// <summary>
        /// corresponding continuous representation of the gradient field
        /// </summary>
        VectorField<SinglePhaseField> LevSetGradient;

        /// <summary>
        /// Curvature; DG-polynomial degree should be 2 times the polynomial degree of <see cref="LevSet"/>.
        /// </summary>
        [InstantiateFromControlFile(VariableNames.Curvature, VariableNames.Curvature, IOListOption.ControlFileDetermined)]
        SinglePhaseField Curvature;


        /// <summary>
        /// If requested, performs the projection of the level-set on a continuous field
        /// </summary>
        ContinuityProjection ContinuityEnforcer;

        /// <summary>
        /// Lauritz' Fast Marching Solver
        /// !!! Caution !!! Only works in Single-Core
        /// </summary>
        FastMarchReinit FastMarchReinitSolver;

        /// <summary>
        /// PDE based elliptic reInitialization by Thomas
        /// </summary>
        EllipticReInit ReInitPDE;

        /// <summary>
        /// Phasefield object
        /// </summary>
        Phasefield PhaseField;

        /// <summary>
        /// The velocity for the level-set evolution; 
        /// since the velocity representation (<see cref="XDGvelocity"/>) is in the XDG space, int cannot be used directly for the level-set evolution.
        /// </summary>
        [InstantiateFromControlFile(
            new string[] { "Extension" + VariableNames.VelocityX, "Extension" + VariableNames.VelocityY, "Extension" + VariableNames.VelocityZ },
            new string[] { VariableNames.VelocityX, VariableNames.VelocityY, VariableNames.VelocityZ },
            true, true,
            IOListOption.ControlFileDetermined)]
        VectorFieldHistory<SinglePhaseField> ExtensionVelocity;


        /// <summary>
        /// Motion Algorithm for a Extension Velocity based on the density averaged velocity directly at the interface;
        /// </summary>
        ExtensionVelocityBDFMover ExtVelMover;

        /// <summary>
        /// Corrector used when employing <see cref="BoSSS.Solution.LevelSetTool.SemiLagrangianLevelSet"/>
        /// </summary>
        LagrangianCorrectors Corrector;
#pragma warning restore 649

        /// <summary>
        /// creates level-set and derived fields
        /// </summary>
        public void CreateLevelSetFields() {

            int D = this.GridData.SpatialDimension;

            this.DGLevSet = new ScalarFieldHistory<SinglePhaseField>(
                      new SinglePhaseField(new Basis(this.GridData, this.Control.FieldOptions["Phi"].Degree), "PhiDG"));

            if (this.Control.FieldOptions["PhiDG"].Degree >= 0 && this.Control.FieldOptions["PhiDG"].Degree != this.DGLevSet.Current.Basis.Degree) {
                throw new ApplicationException("Specification of polynomial degree for 'PhiDG' is not supported, since it is induced by polynomial degree of 'Phi'.");
            }

            // ===================================================================
            // Initialize ContinuityProjection (if needed, if not , Option: None)
            // ===================================================================
            this.LevSet = ContinuityProjection.CreateField(
                DGLevelSet: this.DGLevSet.Current,
                gridData: (GridData)GridData,
                Option: Control.LSContiProjectionMethod
                );

            this.LsTrk = new LevelSetTracker((GridData)this.GridData, base.Control.CutCellQuadratureType, base.Control.LS_TrackerWidth, new string[] { "A", "B" }, this.LevSet);
            base.RegisterField(this.LevSet);
            this.LevSetGradient = new VectorField<SinglePhaseField>(D.ForLoop(d => new SinglePhaseField(this.LevSet.Basis, "dPhi_dx[" + d + "]")));
            base.RegisterField(this.LevSetGradient);

            base.RegisterField(this.DGLevSet.Current);
            this.DGLevSetGradient = new VectorField<SinglePhaseField>(D.ForLoop(d => new SinglePhaseField(this.DGLevSet.Current.Basis, "dPhiDG_dx[" + d + "]")));
            base.RegisterField(this.DGLevSetGradient);


        }


        #endregion



        //===================
        // Fourier level-set
        //===================
        #region

        /// <summary>
        /// Information of the current Fourier Level-Set
        /// DFT_coeff
        /// </summary>
        FourierLevSetBase Fourier_LevSet;

        /// <summary>
        /// specialized timestepper (Runge-Kutta-based) for the evoultion of the Fourier-LS
        /// </summary>
        FourierLevSetTimestepper Fourier_Timestepper;


        /// <summary>
        /// init routine for the specialized Fourier level-set
        /// </summary>
        private void InitFourier() {
            if (this.Control.FourierLevSetControl == null)
                throw new ArgumentNullException("LevelSetEvolution needs and instance of FourierLevSetControl!");

            Fourier_LevSet = FourierLevelSetFactory.Build(this.Control.FourierLevSetControl);
            if (this.Control.EnforceLevelSetConservation) {
                throw new NotSupportedException("mass conservation correction currently not supported");
            }
            Fourier_LevSet.ProjectToDGLevelSet(this.DGLevSet.Current, this.LsTrk);

            if (base.MPIRank == 0 && this.CurrentSessionInfo.ID != Guid.Empty) {
                // restart information for Fourier LS
                Log_FourierLS = base.DatabaseDriver.FsDriver.GetNewLog("Log_FourierLS", this.CurrentSessionInfo.ID);
                Guid vecSamplP_id = this.DatabaseDriver.SaveVector<double>(Fourier_LevSet.getRestartInfo());
                Log_FourierLS.WriteLine(vecSamplP_id);
                Log_FourierLS.Flush();

                //if(this.Control.FourierLevSetControl.WriteFLSdata)
                //    Fourier_LevSet.setUpLogFiles(base.DatabaseDriver, this.CurrentSessionInfo, TimestepNo, PhysTime);

            }
            //create specialized fourier timestepper
            Fourier_Timestepper = FourierLevelSetFactory.Build_Timestepper(this.Control.FourierLevSetControl, Fourier_LevSet.GetFLSproperty(),
                                                            Fourier_LevSet.ComputeChangerate, Fourier_LevSet.EvolveFourierLS);
        }


        #endregion



        /// <summary>
        /// setUp for the Level set initialization (Level-set algorithm, continuity, conservation)
        /// </summary>
        private void InitLevelSet() {
            using (new FuncTrace()) {

                // check level-set
                if (this.LevSet.L2Norm() == 0) {
                    throw new NotSupportedException("Level set is not initialized - norm is 0.0 - ALL cells will be cut, no gradient can be defined!");
                }

                // tracker needs to be updated to get access to the cut-cell mask
                this.LsTrk.UpdateTracker(0.0);

                // ==============================
                // level-set initialization
                // ==============================

                //PlotCurrentState(0.0, new TimestepNumber(new int[] { 0, 0 }), 3);

                #region Initialize Level Set Evolution Algorithm
                switch (this.Control.Option_LevelSetEvolution) {
                    case LevelSetEvolution.Fourier:
                        InitFourier();
                        break;
                    case LevelSetEvolution.None:
                        if (this.Control.AdvancedDiscretizationOptions.SST_isotropicMode == SurfaceStressTensor_IsotropicMode.Curvature_Fourier) {
                            Fourier_LevSet = FourierLevelSetFactory.Build(this.Control.FourierLevSetControl);
                            Fourier_LevSet.ProjectToDGLevelSet(this.DGLevSet.Current, this.LsTrk);
                        } else {
                            goto default;
                        }
                        break;
                    case LevelSetEvolution.ExtensionVelocity: {
                            // Create ExtensionVelocity Motion Algorithm
                            this.DGLevSet.Current.Clear();
                            this.DGLevSet.Current.AccLaidBack(1.0, this.LevSet);
                            DGLevSetGradient.Gradient(1.0, DGLevSet.Current);
                            //VectorField<SinglePhaseField> VectorExtVel = ExtensionVelocity.Current;
                            base.RegisterField(ExtensionVelocity.Current);

                            //ReInitPDE = new EllipticReInit(this.LsTrk, this.Control.ReInitControl, DGLevSet.Current);
                            FastMarchReinitSolver = new FastMarchReinit(DGLevSet.Current.Basis);

                            // full initial reinitialization
                            //ReInitPDE.ReInitialize(Restriction: LsTrk.Regions.GetNearFieldSubgrid(1));

                            CellMask Accepted = LsTrk.Regions.GetNearFieldMask(1);
                            CellMask ActiveField = Accepted.Complement();
                            CellMask NegativeField = LsTrk.Regions.GetSpeciesMask("A");
                            FastMarchReinitSolver.FirstOrderReinit(DGLevSet.Current, Accepted, NegativeField, ActiveField);

                            //ReInitPDE.ReInitialize();

                            // setup extension velocity mover
                            switch (this.Control.TimeSteppingScheme) {
                                case TimeSteppingScheme.RK_CrankNic:
                                case TimeSteppingScheme.CrankNicolson: {
                                        //do not instantiate rksch, use bdf instead
                                        bdfOrder = -1;
                                        break;
                                    }
                                case TimeSteppingScheme.RK_ImplicitEuler:
                                case TimeSteppingScheme.ImplicitEuler: {
                                        //do not instantiate rksch, use bdf instead
                                        bdfOrder = 1;
                                        break;
                                    }
                                default: {
                                        if (this.Control.TimeSteppingScheme.ToString().StartsWith("BDF")) {
                                            //do not instantiate rksch, use bdf instead
                                            bdfOrder = Convert.ToInt32(this.Control.TimeSteppingScheme.ToString().Substring(3));
                                            break;
                                        } else
                                            throw new NotImplementedException();
                                    }
                            }

                            ExtVelMover = new ExtensionVelocityBDFMover(LsTrk, DGLevSet.Current, DGLevSetGradient, new VectorField<DGField>(XDGvelocity.Velocity.ToArray()),
                                Control.EllipticExtVelAlgoControl, BcMap, bdfOrder, ExtensionVelocity.Current, new double[2] { Control.PhysicalParameters.rho_A, Control.PhysicalParameters.rho_B });


                            break;
                        }
                    case LevelSetEvolution.SemiLagrangianLevelSet:

                        Corrector = new LagrangianCorrectors(LagrangianMode.Marker);
                        Corrector.Constructor(this.DGLevSet.Current, this.DGLevSetGradient, this.LsTrk, this.ExtensionVelocity.Current, this.ExtensionVelocity.Current, this.GridData);

                        this.DGLevSet.Current.Clear();
                        this.DGLevSet.Current.AccLaidBack(1.0, this.LevSet);
                        this.DGLevSetGradient.Gradient(1.0, this.DGLevSet.Current);
                        //this.LevSetGradient.Gradient(1.0, this.LevSet);
                        Corrector.Initialize();
                        break;
                    case LevelSetEvolution.Phasefield:
                        this.DGLevSet.Current.Clear();
                        this.DGLevSet.Current.AccLaidBack(1.0, this.LevSet);
                        PhaseField = new Phasefield(this.Control.PhasefieldControl, this.LevSet, this.DGLevSet.Current, this.LsTrk, this.ExtensionVelocity.Current, this.GridData, this.Control, this.MultigridSequence);
                        PhaseField.InitCH();
                        break;
                    case LevelSetEvolution.FastMarching:
                    case LevelSetEvolution.Prescribed:
                    case LevelSetEvolution.ScalarConvection:
                    default:
                        // evolution algorithms need a signed-distance level-set:
                        // do some reinit at startup
                        //BoSSS.Solution.LevelSetTools.Advection.NarrowMarchingBand.CutCellReinit(this.LsTrk, this.DGLevSet.Current);
                        // apply only the minimal necessary change
                        this.DGLevSet.Current.Clear();
                        this.DGLevSet.Current.AccLaidBack(1.0, this.LevSet);

                        //FastMarchReinitSolver = new FastMarchReinit(DGLevSet.Current.Basis);

                        break;
                }
                //PlotCurrentState(0.0, new TimestepNumber(new int[] { 0, 1 }), 3);
                #endregion

                // =========================================
                // Enforcing the continuity of the level-set
                // =========================================

                ContinuityEnforcer = new ContinuityProjection(
                    ContBasis: this.LevSet.Basis,
                    DGBasis: this.DGLevSet.Current.Basis,
                    gridData: GridData,
                    Option: Control.LSContiProjectionMethod
                    );
                
                //var CC = this.LsTrk.Regions.GetCutCellMask4LevSet(0);
                var Near1 = this.LsTrk.Regions.GetNearMask4LevSet(0, 1);
                var Near = this.LsTrk.Regions.GetNearMask4LevSet(0, this.Control.LS_TrackerWidth);
                var PosFF = this.LsTrk.Regions.GetLevelSetWing(0, +1).VolumeMask;

                if (this.Control.Option_LevelSetEvolution != LevelSetEvolution.ExtensionVelocity && this.Control.Option_LevelSetEvolution != LevelSetEvolution.Phasefield)
                    ContinuityEnforcer.SetFarField(this.DGLevSet.Current, Near1, PosFF);

                ContinuityEnforcer.MakeContinuous(this.DGLevSet.Current, this.LevSet, Near, PosFF);
                
                //PlotCurrentState(0.0, new TimestepNumber(new int[] { 0, 2 }), 3);

                this.LsTrk.UpdateTracker(0.0);

            }

        }

        /// <summary>
        /// 
        /// </summary>
        public void PushLevelSetAndRelatedStuff() {

            if (this.Control.Option_LevelSetEvolution == LevelSetEvolution.Fourier) {
                Fourier_Timestepper.updateFourierLevSet();
            }

            this.ExtensionVelocity.IncreaseHistoryLength(1);
            this.ExtensionVelocity.Push();

            this.DGLevSet.IncreaseHistoryLength(1);
            this.DGLevSet.Push();
        }


        /// <summary>
        /// Computes the new level set field at time <paramref name="Phystime"/> + <paramref name="dt"/>.
        /// This is a 'driver function' which provides a universal interface to the various level set evolution algorithms.
        /// It also acts as a callback to the time stepper (see <see cref="m_BDF_Timestepper"/> resp. <see cref="m_RK_Timestepper"/>),
        /// i.e. it matches the signature of 
        /// <see cref="BoSSS.Solution.XdgTimestepping.DelUpdateLevelset"/>.
        /// </summary>
        /// <param name="Phystime"></param>
        /// <param name="dt"></param>
        /// <param name="CurrentState">
        /// The current solution (velocity and pressure), since the complete solution is provided by the time stepper,
        /// only the velocity components(supposed to be at the beginning) are used.
        /// </param>
        /// <param name="underrelax">
        /// </param>
        double DelUpdateLevelSet(DGField[] CurrentState, double Phystime, double dt, double underrelax, bool incremental) {
            using (new FuncTrace()) {

                //dt *= underrelax;
                int D = base.Grid.SpatialDimension;
                int iTimestep = hack_TimestepIndex;
                DGField[] EvoVelocity = CurrentState.GetSubVector(0, D);


                // ========================================================
                // Backup old level-set, in order to compute the residual
                // ========================================================

                SinglePhaseField LsBkUp = new SinglePhaseField(this.LevSet.Basis);
                LsBkUp.Acc(1.0, this.LevSet);
                CellMask oldCC = LsTrk.Regions.GetCutCellMask();

                // ====================================================
                // set evolution velocity, but only on the CUT-cells
                // ====================================================

                #region Calculate density averaged Velocity for each cell

                ConventionalDGField[] meanVelocity = GetMeanVelocityFromXDGField(EvoVelocity);

                #endregion


                // =============================================
                // compute interface velocity due to evaporation
                // =============================================

                #region Compute evaporative velocity

                //SinglePhaseField LevSetSrc = new SinglePhaseField(meanVelocity[0].Basis, "LevelSetSource");

                if (this.Control.solveCoupledHeatEquation && (this.Control.ThermalParameters.hVap > 0.0)) {

                    SinglePhaseField[] evapVelocity = new SinglePhaseField[D];
                    BitArray EvapMicroRegion = new BitArray(this.LsTrk.GridDat.Cells.Count);  //this.LsTrk.GridDat.GetBoundaryCells().GetBitMask();

                    double kA = this.Control.ThermalParameters.k_A;
                    double kB = this.Control.ThermalParameters.k_B;
                    double rhoA = this.Control.ThermalParameters.rho_A;
                    double rhoB = this.Control.ThermalParameters.rho_B;


                    for (int d = 0; d < D; d++) {
                        evapVelocity[d] = new SinglePhaseField(meanVelocity[d].Basis, "evapVelocity_d" + d);

                        int order = evapVelocity[d].Basis.Degree * meanVelocity[d].Basis.Degree + 2;

                        evapVelocity[d].ProjectField(1.0,
                           delegate (int j0, int Len, NodeSet NS, MultidimensionalArray result) {
                               int K = result.GetLength(1); // No nof Nodes

                               MultidimensionalArray VelA = MultidimensionalArray.Create(Len, K, D);
                               MultidimensionalArray VelB = MultidimensionalArray.Create(Len, K, D);

                               for (int dd = 0; dd < D; dd++) {
                                   this.CurrentVel[dd].GetSpeciesShadowField("A").Evaluate(j0, Len, NS, VelA.ExtractSubArrayShallow(new int[] { -1, -1, dd }));
                                   this.CurrentVel[dd].GetSpeciesShadowField("B").Evaluate(j0, Len, NS, VelB.ExtractSubArrayShallow(new int[] { -1, -1, dd }));
                               }

                               MultidimensionalArray GradTempA_Res = MultidimensionalArray.Create(Len, K, D);
                               MultidimensionalArray GradTempB_Res = MultidimensionalArray.Create(Len, K, D);

                               this.Temperature.GetSpeciesShadowField("A").EvaluateGradient(j0, Len, NS, GradTempA_Res);
                               this.Temperature.GetSpeciesShadowField("B").EvaluateGradient(j0, Len, NS, GradTempB_Res);

                               MultidimensionalArray HeatFluxA_Res = MultidimensionalArray.Create(Len, K, D);
                               MultidimensionalArray HeatFluxB_Res = MultidimensionalArray.Create(Len, K, D);
                               if (XOpConfig.getConductMode != ConductivityInSpeciesBulk.ConductivityMode.SIP) {
                                   for (int dd = 0; dd < D; dd++) {
                                       this.HeatFlux[dd].GetSpeciesShadowField("A").Evaluate(j0, Len, NS, HeatFluxA_Res.ExtractSubArrayShallow(new int[] { -1, -1, dd }));
                                       this.HeatFlux[dd].GetSpeciesShadowField("B").Evaluate(j0, Len, NS, HeatFluxB_Res.ExtractSubArrayShallow(new int[] { -1, -1, dd }));
                                   }
                               }

                               MultidimensionalArray TempA_Res = MultidimensionalArray.Create(Len, K);
                               MultidimensionalArray TempB_Res = MultidimensionalArray.Create(Len, K);
                               MultidimensionalArray Curv_Res = MultidimensionalArray.Create(Len, K);
                               MultidimensionalArray Pdisp_Res = MultidimensionalArray.Create(Len, K);

                               this.Temperature.GetSpeciesShadowField("A").Evaluate(j0, Len, NS, TempA_Res);
                               this.Temperature.GetSpeciesShadowField("B").Evaluate(j0, Len, NS, TempB_Res);
                               this.Curvature.Evaluate(j0, Len, NS, Curv_Res);
                               this.DisjoiningPressure.Evaluate(j0, Len, NS, Pdisp_Res);

                               var Normals = LsTrk.DataHistories[0].Current.GetLevelSetNormals(NS, j0, Len);

                               for (int j = 0; j < Len; j++) {

                                   MultidimensionalArray globCoord = MultidimensionalArray.Create(K, D);
                                   this.GridData.TransformLocal2Global(NS, globCoord, j);

                                   for (int k = 0; k < K; k++) {

                                       double qEvap = 0.0;
                                       if (EvapMicroRegion[j]) {
                                           throw new NotImplementedException("Check consistency for micro regions");
                                           // micro region
                                           //double Tsat = this.Control.ThermalParameters.T_sat;
                                           //double pc = this.Control.ThermalParameters.pc;
                                           //double pc0 = (pc < 0.0) ? this.Control.PhysicalParameters.Sigma * Curv_Res[j, k] + Pdisp_Res[j, k] : pc;
                                           //double f = this.Control.ThermalParameters.fc;
                                           //double R = this.Control.ThermalParameters.Rc;
                                           //if (this.Control.ThermalParameters.hVap_A > 0) {
                                           //    hVap = this.Control.ThermalParameters.hVap_A;
                                           //    rho_l = this.Control.PhysicalParameters.rho_A;
                                           //    rho_v = this.Control.PhysicalParameters.rho_B;
                                           //    double TintMin = Tsat * (1 + (pc0 / (hVap * rho_l)));
                                           //    double Rint = ((2.0 - f) / (2 * f)) * Tsat * Math.Sqrt(2 * Math.PI * R * Tsat) / (rho_v * hVap.Pow2());
                                           //    if (TempA_Res[j, k] > TintMin)
                                           //        qEvap = -(TempA_Res[j, k] - TintMin) / Rint;
                                           //} else {
                                           //    hVap = -this.Control.ThermalParameters.hVap_A;
                                           //    rho_l = this.Control.PhysicalParameters.rho_B;
                                           //    rho_v = this.Control.PhysicalParameters.rho_A;
                                           //    double TintMin = Tsat * (1 + (pc0 / (hVap * rho_l)));
                                           //    double Rint = ((2.0 - f) / (2 * f)) * Tsat * Math.Sqrt(2 * Math.PI * R * Tsat) / (rho_v * hVap.Pow2());
                                           //    if (TempB_Res[j, k] > TintMin)
                                           //        qEvap = (TempB_Res[j, k] - TintMin) / Rint;
                                           //}

                                       } else {
                                           //macro region
                                           for (int dd = 0; dd < D; dd++) {
                                               if (XOpConfig.getConductMode == ConductivityInSpeciesBulk.ConductivityMode.SIP) {
                                                   qEvap += ((-kB) * GradTempB_Res[j, k, dd] - (-kA) * GradTempA_Res[j, k, dd]) * Normals[j, k, dd];
                                               } else {
                                                   qEvap += (HeatFluxB_Res[j, k, dd] - HeatFluxA_Res[j, k, dd]) * Normals[j, k, dd];
                                               }
                                           }
                                       }

                                       double[] globX = new double[] { globCoord[k, 0], globCoord[k, 1] };
                                       double mEvap = (this.XOpConfig.prescribedMassflux != null) ? this.XOpConfig.prescribedMassflux(globX, hack_Phystime) : qEvap / this.Control.ThermalParameters.hVap; // mass flux
                                       //Console.WriteLine("mEvap = {0}", mEvap);

                                       double sNeg = VelA[j, k, d] + mEvap * (1 / rhoA) * Normals[j, k, d];
                                       double sPos = VelB[j, k, d] + mEvap * (1 / rhoB) * Normals[j, k, d];

                                       result[j, k] = (rhoA * sNeg + rhoB * sPos) / (rhoA + rhoB);   // density averaged evap velocity 
                                   }
                               }
                           }, (new CellQuadratureScheme(false, LsTrk.Regions.GetCutCellMask())).AddFixedOrderRules(LsTrk.GridDat, order));

                        meanVelocity[d].Clear();
                        meanVelocity[d].Acc(1.0, evapVelocity[d]);
                    }


                    // check interface velocity (used for logging)
                    int p = evapVelocity[0].Basis.Degree;
                    SubGrid sgrd = LsTrk.Regions.GetCutCellSubgrid4LevSet(0);
                    NodeSet[] Nodes = LsTrk.GridDat.Grid.RefElements.Select(Kref => Kref.GetQuadratureRule(p * 2).Nodes).ToArray();

                    var cp = new ClosestPointFinder(LsTrk, 0, sgrd, Nodes);
                    MultidimensionalArray[] VelocityEval = evapVelocity.Select(sf => cp.EvaluateAtCp(sf)).ToArray();
                    double nNodes = VelocityEval[0].Length;

                    if (this.Control.LogValues == XNSE_Control.LoggingValues.EvaporationL) {
                        double evapVelY = VelocityEval[1].Sum() / nNodes;
                        EvapVelocMean = evapVelY;
                    }

                    if (this.Control.LogValues == XNSE_Control.LoggingValues.EvaporationC) {
                        EvapVelocMean = 0.0;
                        for (int s = 0; s < sgrd.GlobalNoOfCells; s++) {
                            for (int n = 0; n < Nodes.Length; n++) {
                                double velX = VelocityEval[0].To2DArray()[s, n];
                                double velY = VelocityEval[1].To2DArray()[s, n];
                                EvapVelocMean += Math.Sqrt(velX.Pow2() + velY.Pow2());
                            }
                        }
                        EvapVelocMean /= nNodes;
                    }

                    //Console.WriteLine("meanEvapVelocity = {0}", EvapVelocMean);

                    // plot
                    //Tecplot.PlotFields(evapVelocity.ToArray(), "EvapVelocity" + hack_TimestepIndex, hack_Phystime, 2);
                    //Tecplot.PlotFields(meanVelocity.ToArray(), "meanVelocity" + hack_TimestepIndex, hack_Phystime, 2);
                }

                #endregion

                // ===================================================================
                // backup interface properties (mass conservation, surface changerate)
                // ===================================================================

                #region backup interface props

                double oldSurfVolume = 0.0;
                double oldSurfLength = 0.0;
                double SurfChangerate = 0.0;
                if (this.Control.CheckInterfaceProps) {
                    oldSurfVolume = XNSEUtils.GetSpeciesArea(this.LsTrk, LsTrk.GetSpeciesId("A"));
                    oldSurfLength = XNSEUtils.GetInterfaceLength(this.LsTrk);
                    SurfChangerate = EnergyUtils.GetSurfaceChangerate(this.LsTrk, meanVelocity, this.m_HMForder);
                }

                #endregion

                // ====================================================
                // perform level-set evolution
                // ====================================================

                #region level-set evolution

                // set up for Strang splitting
                SinglePhaseField DGLevSet_old;
                if (incremental)
                    DGLevSet_old = this.DGLevSet.Current.CloneAs();
                else
                    DGLevSet_old = this.DGLevSet[0].CloneAs();


                // set up for underrelaxation
                SinglePhaseField DGLevSet_oldIter = this.DGLevSet.Current.CloneAs();

                //PlotCurrentState(hack_Phystime, new TimestepNumber(new int[] { hack_TimestepIndex, 0 }), 2);

                // actual evolution
                switch (this.Control.Option_LevelSetEvolution) {
                    case LevelSetEvolution.None:
                        throw new ArgumentException("illegal call");

                    case LevelSetEvolution.FastMarching: {

                            NarrowMarchingBand.Evolve_Mk2(
                             dt, this.LsTrk, DGLevSet_old, this.DGLevSet.Current, this.DGLevSetGradient,
                             meanVelocity, this.ExtensionVelocity.Current.ToArray(), //new DGField[] { LevSetSrc },
                             this.m_HMForder, iTimestep);

                            //FastMarchReinitSolver = new FastMarchReinit(DGLevSet.Current.Basis);
                            //CellMask Accepted = LsTrk.Regions.GetCutCellMask();
                            //CellMask ActiveField = LsTrk.Regions.GetNearFieldMask(1);
                            //CellMask NegativeField = LsTrk.Regions.GetSpeciesMask("A");
                            //FastMarchReinitSolver.FirstOrderReinit(DGLevSet.Current, Accepted, NegativeField, ActiveField);

                            break;
                        }

                    case LevelSetEvolution.Fourier: {
                            Fourier_Timestepper.moveLevelSet(dt, meanVelocity);
                            if (incremental)
                                Fourier_Timestepper.updateFourierLevSet();
                            Fourier_LevSet.ProjectToDGLevelSet(this.DGLevSet.Current, this.LsTrk);
                            break;
                        }

                    case LevelSetEvolution.Prescribed: {
                            this.DGLevSet.Current.Clear();
                            this.DGLevSet.Current.ProjectField(1.0, Control.Phi.Vectorize(Phystime + dt));
                            break;
                        }

                    case LevelSetEvolution.ScalarConvection: {
                            var LSM = new LevelSetMover(EvoVelocity,
                                this.ExtensionVelocity,
                                this.LsTrk,
                                XVelocityProjection.CutCellVelocityProjectiontype.L2_plain,
                                this.DGLevSet,
                                this.BcMap);

                            int check1 = this.ExtensionVelocity.PushCount;
                            int check2 = this.DGLevSet.PushCount;

                            this.DGLevSet[1].Clear();
                            this.DGLevSet[1].Acc(1.0, DGLevSet_old);
                            LSM.Advect(dt);

                            if (check1 != this.ExtensionVelocity.PushCount)
                                throw new ApplicationException();
                            if (check2 != this.DGLevSet.PushCount)
                                throw new ApplicationException();

                            break;
                        }
                    case LevelSetEvolution.ExtensionVelocity: {

                            DGLevSetGradient.Clear();
                            DGLevSetGradient.Gradient(1.0, DGLevSet.Current);

                            ExtVelMover.Advect(dt);

                            // Fast Marching: Specify the Domains first
                            // Perform Fast Marching only on the Far Field
                            if (this.Control.AdaptiveMeshRefinement) {
                                int NoCells = ((GridData)this.GridData).Cells.Count;
                                BitArray Refined = new BitArray(NoCells);
                                for (int j = 0; j < NoCells; j++) {
                                    if (((GridData)this.GridData).Cells.GetCell(j).RefinementLevel > 0)
                                        Refined[j] = true;
                                }
                                CellMask Accepted = new CellMask(this.GridData, Refined);
                                CellMask AcceptedNeigh = Accepted.AllNeighbourCells();

                                Accepted = Accepted.Union(AcceptedNeigh);
                                CellMask ActiveField = Accepted.Complement();
                                CellMask NegativeField = LsTrk.Regions.GetSpeciesMask("A");
                                FastMarchReinitSolver.FirstOrderReinit(DGLevSet.Current, Accepted, NegativeField, ActiveField);

                            } else {
                                CellMask Accepted = LsTrk.Regions.GetNearFieldMask(1);
                                CellMask ActiveField = Accepted.Complement();
                                CellMask NegativeField = LsTrk.Regions.GetSpeciesMask("A");
                                FastMarchReinitSolver.FirstOrderReinit(DGLevSet.Current, Accepted, NegativeField, ActiveField);

                            }
                            //SubGrid AcceptedGrid = new SubGrid(Accepted);
                            //ReInitPDE.ReInitialize(Restriction: AcceptedGrid);

                            //CellMask ActiveField = Accepted.Complement();
                            //CellMask NegativeField = LsTrk.Regions.GetSpeciesMask("A");
                            //FastMarchReinitSolver.FirstOrderReinit(DGLevSet.Current, Accepted, NegativeField, ActiveField);

                            //ReInitPDE.ReInitialize();

                            break;
                        }
                    case LevelSetEvolution.SemiLagrangianLevelSet:
                        // update velocity at Interface ??
                        //double[][] ExtVelMin = new double[ExtensionVelocity.Current.ToArray().Length][];
                        //double[][] ExtVelMax = new double[ExtensionVelocity.Current.ToArray().Length][];
                        //for (int i = 0; i < ExtensionVelocity.Current.ToArray().Length; i++)
                        //{
                        //    ExtVelMin[i] = new double[LsTrk.GridDat.Cells.NoOfLocalUpdatedCells];
                        //    ExtVelMax[i] = new double[LsTrk.GridDat.Cells.NoOfLocalUpdatedCells];
                        //}
                        //NarrowMarchingBand.ConstructExtVel_PDE(LsTrk, LsTrk.Regions.GetCutCellSubgrid4LevSet(0), ExtensionVelocity.Current.ToArray(), meanVelocity, LevSet, LevSetGradient, ExtVelMin, ExtVelMax, this.m_HMForder);

                        ExtensionVelocity.Push();
                        for (int g = 0; g < meanVelocity.Length; g++)
                        {
                            ExtensionVelocity.Current[g].Clear();
                            ExtensionVelocity.Current[g].Acc(1.0, meanVelocity[g]);
                        }

                        // advect particles and retrieve LevelSet
                        Corrector.Timestep(dt, 1, iTimestep);

                        break;
                    case LevelSetEvolution.Phasefield:
                        ExtensionVelocity.Push();
                        //CellMask CutCells = this.LsTrk.Regions.GetCutCellMask4LevSet(0);
                        //CellMask NegCells = this.LsTrk.Regions.GetLevelSetWing(0, -1).VolumeMask;
                        //CellMask PosCells = this.LsTrk.Regions.GetLevelSetWing(0, +1).VolumeMask;

                        for (int g = 0; g < this.XDGvelocity.Velocity.Count; g++)
                        {
                            // Momentan Projektion, wenn nicht materielles Interface nochmal überlegen wie zu lösen
                            ExtensionVelocity.Current[g].Clear();
                            ExtensionVelocity.Current[g].Acc(1.0, XDGvelocity.Velocity[g].ProjectToSinglePhaseField());
                            //ExtensionVelocity.Current[g].Acc(1.0, XDGvelocity.Velocity[g].GetSpeciesShadowField("A"), NegCells);
                            //ExtensionVelocity.Current[g].Acc(1.0, XDGvelocity.Velocity[g].GetSpeciesShadowField("B"), PosCells);
                            //ExtensionVelocity.Current[g].Acc(-0.5, XDGvelocity.Velocity[g].GetSpeciesShadowField("A"), CutCells);
                            //ExtensionVelocity.Current[g].Acc(-0.5, XDGvelocity.Velocity[g].GetSpeciesShadowField("B"), CutCells);
                        }
                        PhaseField.UpdateFields(this.LevSet, this.DGLevSet.Current, this.LsTrk, this.ExtensionVelocity.Current, this.GridData, this.Control, this.MultigridSequence);
                        PhaseField.MovePhasefield(iTimestep, dt, Phystime);
                        break;
                    default:
                        throw new ApplicationException();
                }


                // performing underrelaxation
                if (underrelax < 1.0) {
                    this.DGLevSet.Current.Scale(underrelax);
                    this.DGLevSet.Current.Acc((1.0 - underrelax), DGLevSet_oldIter);
                }

                //PlotCurrentState(hack_Phystime, new TimestepNumber(new int[] { hack_TimestepIndex, 1 }), 2);


                #endregion


                // ======================
                // postprocessing  
                // =======================

                if (this.Control.ReInitPeriod > 0 && hack_TimestepIndex % this.Control.ReInitPeriod == 0) {
                    Console.WriteLine("Filtering DG-LevSet");
                    SinglePhaseField FiltLevSet = new SinglePhaseField(DGLevSet.Current.Basis);
                    FiltLevSet.AccLaidBack(1.0, DGLevSet.Current);
                    Filter(FiltLevSet, 2, oldCC);
                    DGLevSet.Current.Clear();
                    DGLevSet.Current.Acc(1.0, FiltLevSet);

                    Console.WriteLine("FastMarchReInit performing FirstOrderReInit");
                    FastMarchReinitSolver = new FastMarchReinit(DGLevSet.Current.Basis);
                    CellMask Accepted = LsTrk.Regions.GetCutCellMask();
                    CellMask ActiveField = LsTrk.Regions.GetNearFieldMask(1);
                    CellMask NegativeField = LsTrk.Regions.GetSpeciesMask("A");
                    FastMarchReinitSolver.FirstOrderReinit(DGLevSet.Current, Accepted, NegativeField, ActiveField);
                }

                #region ensure continuity

                // make level set continuous
                CellMask CC = LsTrk.Regions.GetCutCellMask4LevSet(0);
                CellMask Near1 = LsTrk.Regions.GetNearMask4LevSet(0, 1);
                CellMask PosFF = LsTrk.Regions.GetLevelSetWing(0, +1).VolumeMask;

                ContinuityEnforcer.MakeContinuous(this.DGLevSet.Current, this.LevSet, Near1, PosFF);

                //PlotCurrentState(hack_Phystime, new TimestepNumber(new int[] { hack_TimestepIndex, 2 }), 2);

                if (this.Control.Option_LevelSetEvolution == LevelSetEvolution.FastMarching)
                {
                    CellMask Nearband = Near1.Union(CC);
                    this.DGLevSet.Current.Clear(Nearband);
                    this.DGLevSet.Current.AccLaidBack(1.0, this.LevSet, Nearband);
                    //ContinuityEnforcer.SetFarField(this.DGLevSet.Current, Near1, PosFF);
                }
                
                //PlotCurrentState(hack_Phystime, new TimestepNumber(new int[] { hack_TimestepIndex, 2 }), 2);

                #endregion


                for (int d = 0; d < D; d++)
                    this.XDGvelocity.Velocity[d].UpdateBehaviour = BehaveUnder_LevSetMoovement.AutoExtrapolate;

                if (this.Control.solveCoupledHeatEquation) {
                    this.Temperature.UpdateBehaviour = BehaveUnder_LevSetMoovement.AutoExtrapolate;
                    if (this.Control.conductMode != ConductivityInSpeciesBulk.ConductivityMode.SIP) {
                        for (int d = 0; d < D; d++)
                            this.HeatFlux[d].UpdateBehaviour = BehaveUnder_LevSetMoovement.AutoExtrapolate;
                    }
                }

                //PlotCurrentState(hack_Phystime, new TimestepNumber(new int[] { hack_TimestepIndex, 3 }), 2);


                // ===============
                // tracker update
                // ===============

                this.LsTrk.UpdateTracker(Phystime + dt, incremental: true);

                //PlotCurrentState(hack_Phystime, new TimestepNumber(new int[] { hack_TimestepIndex, 4 }), 2);

                // update near field (in case of adaptive mesh refinement)
                if (this.Control.AdaptiveMeshRefinement && this.Control.Option_LevelSetEvolution == LevelSetEvolution.FastMarching) {
                    Near1 = LsTrk.Regions.GetNearMask4LevSet(0, 1);
                    PosFF = LsTrk.Regions.GetLevelSetWing(0, +1).VolumeMask;
                    ContinuityEnforcer.SetFarField(this.DGLevSet.Current, Near1, PosFF);
                    ContinuityEnforcer.SetFarField(this.LevSet, Near1, PosFF);
                }

                //PlotCurrentState(hack_Phystime, new TimestepNumber(new int[] { hack_TimestepIndex, 5 }), 2);


                // ==================================================================
                // check interface properties (mass conservation, surface changerate)
                // ==================================================================

                if (this.Control.CheckInterfaceProps) {

                    double currentSurfVolume = XNSEUtils.GetSpeciesArea(this.LsTrk, LsTrk.GetSpeciesId("A"));
                    double massChange = ((currentSurfVolume - oldSurfVolume) / oldSurfVolume) * 100;
                    Console.WriteLine("Change of mass = {0}%", massChange);

                    double currentSurfLength = XNSEUtils.GetInterfaceLength(this.LsTrk);
                    double actualSurfChangerate = (currentSurfLength - oldSurfLength) / dt;
                    Console.WriteLine("Interface divergence = {0}", SurfChangerate);
                    Console.WriteLine("actual surface changerate = {0}", actualSurfChangerate);

                }


                // =====================
                // solve coupled system
                // =====================

                //if (this.Control.solveCoupledHeatEquation && m_BDF_coupledTimestepper != null) {
                //    m_BDF_coupledTimestepper.Solve(hack_Phystime, dt, Control.SkipSolveAndEvaluateResidual);
                //    //ComputeHeatflux();
                //}


                // ==================
                // compute residual
                // ==================

                var newCC = LsTrk.Regions.GetCutCellMask();
                LsBkUp.Acc(-1.0, this.LevSet);
                double LevSetResidual = LsBkUp.L2Norm(newCC.Union(oldCC));

                return LevSetResidual;
            }
        }


        private void EnforceVolumeConservation() {
            double spcArea = XNSEUtils.GetSpeciesArea(LsTrk, LsTrk.SpeciesIdS[0]);
            Console.WriteLine("area = {0}", spcArea);
            double InterLength = XNSEUtils.GetInterfaceLength(LsTrk);

            //double cmc = (consvRefArea - spcArea) / InterLength;
            //Console.WriteLine("add constant: {0}", -cmc);
            //this.DGLevSet.Current.AccConstant(-cmc);
            //this.LevSet.AccConstant(-cmc);
        }


        private void Filter(SinglePhaseField FiltrdField, int NoOfSweeps, CellMask CC) {

            Basis patchRecoveryBasis = FiltrdField.Basis;

            L2PatchRecovery l2pr = new L2PatchRecovery(patchRecoveryBasis, patchRecoveryBasis, CC, true);

            SinglePhaseField F_org = FiltrdField.CloneAs();

            for (int pass = 0; pass < NoOfSweeps; pass++) {
                F_org.Clear();
                F_org.Acc(1.0, FiltrdField);
                FiltrdField.Clear();
                l2pr.Perform(FiltrdField, F_org);
            }
        }


        /// <summary>
        ///  Take density-weighted mean value in cut-cells
        /// </summary>
        /// <param name="EvoVelocity"></param>
        /// <returns></returns>
        private ConventionalDGField[] GetMeanVelocityFromXDGField(DGField[] EvoVelocity) {
            int D = EvoVelocity.Length;
            ConventionalDGField[] meanVelocity;

            Debug.Assert(this.XDGvelocity != null);

            meanVelocity = new ConventionalDGField[D];

            double rho_A = this.Control.PhysicalParameters.rho_A, rho_B = this.Control.PhysicalParameters.rho_B;
            double mu_A = this.Control.PhysicalParameters.mu_A, mu_B = this.Control.PhysicalParameters.mu_B;
            CellMask CC = this.LsTrk.Regions.GetCutCellMask4LevSet(0);
            CellMask Neg = this.LsTrk.Regions.GetLevelSetWing(0, -1).VolumeMask;
            CellMask Pos = this.LsTrk.Regions.GetLevelSetWing(0, +1).VolumeMask;
            CellMask posNear = this.LsTrk.Regions.GetNearMask4LevSet(0, 1).Except(Neg);
            CellMask negNear = this.LsTrk.Regions.GetNearMask4LevSet(0, 1).Except(Pos);

            for (int d = 0; d < D; d++) {
                Basis b = this.XDGvelocity.Velocity[d].Basis.NonX_Basis;
                meanVelocity[d] = new SinglePhaseField(b);


                foreach (string spc in this.LsTrk.SpeciesNames) {
                    double rhoSpc;
                    double muSpc;
                    switch (spc) {
                        case "A": rhoSpc = rho_A; muSpc = mu_A; break;
                        case "B": rhoSpc = rho_B; muSpc = mu_B; break;
                        default: throw new NotSupportedException("Unknown species name '" + spc + "'");
                    }

                    double scale = 1.0;
                    switch (this.Control.InterAverage) {
                        case XNSE_Control.InterfaceAveraging.mean: {
                                scale = 0.5;
                                break;
                            }
                        case XNSE_Control.InterfaceAveraging.density: {
                                scale = rhoSpc / (rho_A + rho_B);
                                break;
                            }
                        case XNSE_Control.InterfaceAveraging.viscosity: {
                                scale = muSpc / (mu_A + mu_B);
                                break;
                            }
                    }

                    meanVelocity[d].Acc(scale, ((XDGField)EvoVelocity[d]).GetSpeciesShadowField(spc), CC);
                    switch (spc) {
                        //case "A": meanVelocity[d].Acc(1.0, ((XDGField)EvoVelocity[d]).GetSpeciesShadowField(spc), Neg.Except(CC)); break;
                        case "A": meanVelocity[d].Acc(1.0, ((XDGField)EvoVelocity[d]).GetSpeciesShadowField(spc), negNear); break;
                        case "B": meanVelocity[d].Acc(1.0, ((XDGField)EvoVelocity[d]).GetSpeciesShadowField(spc), posNear); break;
                        default: throw new NotSupportedException("Unknown species name '" + spc + "'");
                    }
                }

            }

            return meanVelocity;
        }



    }
}
