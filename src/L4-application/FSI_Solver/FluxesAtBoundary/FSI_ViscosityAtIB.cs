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
using BoSSS.Foundation.XDG;
using System.Diagnostics;
using BoSSS.Foundation;
using ilPSP.Utils;
using BoSSS.Platform.LinAlg;

namespace BoSSS.Solution.NSECommon.Operator.Viscosity {

    public class FSI_ViscosityAtIB : ILevelSetForm {
        public FSI_ViscosityAtIB(int currentDim, int spatialDim, LevelSetTracker levelSetTracker, double penalty, Func<double, int, double> penaltyFunction, double fluidViscosity, Func<Vector, double[]> getParticleParams) {
            m_penalty = penalty;
            m_PenaltyFunc = penaltyFunction;
            m_LsTrk = levelSetTracker;
            muA = fluidViscosity;
            component = currentDim;
            m_GetParticleParams = getParticleParams;
            m_D = spatialDim;
        }

        private readonly int component;
        private readonly int m_D;
        private readonly LevelSetTracker m_LsTrk;

        /// <summary>
        /// Describes: 0: velX, 1: velY, 2: rotVel, 3: particleradius, 4: active_stress, 5: first scaling parameter, 6: second scaling parameter, 7: current angle
        /// </summary>
        private readonly Func<Vector, double[]> m_GetParticleParams;

        /// <summary>
        /// Viscosity in species A
        /// </summary>
        private readonly double muA;
        private readonly double m_penalty;
        private readonly Func<double, int, double> m_PenaltyFunc;

        /// <summary>
        /// default-implementation
        /// </summary>
        public double LevelSetForm(ref CommonParamsLs inp, double[] uA, double[] uB, double[,] Grad_uA, double[,] Grad_uB, double vA, double vB, double[] Grad_vA, double[] Grad_vB) {
            double[] N = inp.n;
            Vector X = new Vector(inp.x);
            double _penalty = m_PenaltyFunc(m_penalty, inp.jCell);
            int D = N.Length;

            // Particle parameters
            // ============================= 
            double[] parameters_P = m_GetParticleParams(X);
            double[] uLevSet = new double[] { parameters_P[0], parameters_P[1] };
            double wLevSet = parameters_P[2];

            double[] RadialVector = new double[] { parameters_P[3], parameters_P[4] };
            double RadialLength = parameters_P[5];

            double active_stress = parameters_P[6];
            double Ang_P = parameters_P[7];
            double[] orientation = new double[] { Math.Cos(Ang_P), Math.Sin(Ang_P) };
            double[] orientationNormal = new double[] { -Math.Sin(Ang_P), Math.Cos(Ang_P) };
            double scaleActiveBoundary = orientation[0] * inp.n[0] + orientation[1] * inp.n[1] > 0 && active_stress != 0 ? 1 : 0;

            Debug.Assert(ArgumentOrdering.Count == D);
            Debug.Assert(Grad_uA.GetLength(0) == this.ArgumentOrdering.Count);
            Debug.Assert(Grad_uB.GetLength(0) == this.ArgumentOrdering.Count);
            Debug.Assert(Grad_uA.GetLength(1) == D);
            Debug.Assert(Grad_uB.GetLength(1) == D);

            // Gradient of u and v 
            // ============================= 
            double Grad_uA_xN = 0, Grad_vA_xN = 0;
            for (int d = 0; d < D; d++) {
                Grad_uA_xN += Grad_uA[component, d] * N[d];
                Grad_vA_xN += Grad_vA[d] * N[d];
            }

            double Ret = 0.0;

            // 3D for IBM_Solver
            // ============================= 
            if (inp.x.Length == 3) {

                Ret -= Grad_uA_xN * (vA);                                                   // consistency term
                Ret -= Grad_vA_xN * (uA[component] - 0) * (1 - scaleActiveBoundary);        // symmetry term
                Ret += _penalty * (uA[component] - 0) * (vA) * (1 - scaleActiveBoundary);   // penalty term

                Debug.Assert(!(double.IsInfinity(Ret) || double.IsNaN(Ret)));
                return Ret * muA;
            }


            // 2D
            // ============================= 
            double[] uAFict = new double[] { uLevSet[0] - RadialLength * wLevSet * RadialVector[1], uLevSet[1] + RadialLength * wLevSet * RadialVector[0] };
            double f_xT;
            if (orientationNormal[0] * inp.n[0] + orientationNormal[1] * inp.n[1] > 0) {
                f_xT = component == 0 ? -active_stress * (inp.n[1]) : active_stress * (inp.n[0]);
            }
            else {
                f_xT = component == 0 ? active_stress * (inp.n[1]) : -active_stress * (inp.n[0]);
            }

            // Dirichlet
            {
                for (int d = 0; d < D; d++) {
                    Ret -= muA * Grad_uA[component, d] * vA * N[d] * (1 - scaleActiveBoundary);
                    Ret -= muA * Grad_vA[d] * (uA[component] - uAFict[component]) * N[d] * (1 - scaleActiveBoundary);
                }
                Ret += muA * (uA[component] - uAFict[component]) * vA * _penalty * (1 - scaleActiveBoundary);
            }
            // Active boundary
            {
                // normal
                for (int dN = 0; dN < D; dN++) {
                    for (int dD = 0; dD < D; dD++) {
                        Ret -= muA * (N[dN] * Grad_uA[dN, dD] * N[dD]) * vA * N[component] * scaleActiveBoundary;   // consistency term
                        Ret -= muA * (Grad_vA[dD] * N[dD]) * (N[dN] * uA[dN] - (N[dN]) * uAFict[dN]) * N[component] * scaleActiveBoundary;     // symmetry term, solid wall
                    }                                                                          
                    Ret += muA * (N[dN] * uA[dN] - (N[dN]) * uAFict[dN]) * N[component] * vA * _penalty * scaleActiveBoundary;
                }
                double[,] P = new double[D, D];
                for (int d1 = 0; d1 < D; d1++) {
                    for (int d2 = 0; d2 < D; d2++) {
                        double nn = 0;
                        if (d1 == d2) {
                            nn = Math.Abs(N[d1] * N[d2]);
                        }
                        if (d1 != d2) {
                            nn = -Math.Abs((N[d1]) * (N[d2]));
                        }
                        if (d1 == d2) {
                            P[d1, d2] = 1 - nn;
                        }
                        else {
                            P[d1, d2] = -nn;
                        }
                    }
                }
                for (int d1 = 0; d1 < D; d1++) {
                    for (int d2 = 0; d2 < D; d2++) {
                        Ret -= (P[d1, d2] * f_xT * scaleActiveBoundary) * (P[d1, component] * vA);// active part
                    }
                }
            }

            Debug.Assert(!(double.IsInfinity(Ret) || double.IsNaN(Ret)));
            return Ret;
        }

        public int LevelSetIndex {
            get { return 0; }
        }

        public IList<string> ArgumentOrdering {
            get { return VariableNames.VelocityVector(this.m_D); }
        }

        public SpeciesId PositiveSpecies {
            get { return m_LsTrk.GetSpeciesId("B"); }
        }

        public SpeciesId NegativeSpecies {
            get { return m_LsTrk.GetSpeciesId("A"); }
        }

        public TermActivationFlags LevelSetTerms {
            get {
                return TermActivationFlags.UxV | TermActivationFlags.UxGradV | TermActivationFlags.GradUxV | TermActivationFlags.V | TermActivationFlags.GradV;
            }
        }

        public IList<string> ParameterOrdering {
            get {
                return null;
            }
        }
    }
}