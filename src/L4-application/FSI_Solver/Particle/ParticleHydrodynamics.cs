﻿/* =======================================================================
Copyright 2019 Technische Universitaet Darmstadt, Fachgebiet fuer Stroemungsdynamik (chair of fluid dynamics)

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

using BoSSS.Foundation.Grid;
using BoSSS.Foundation.XDG;
using ilPSP;
using MPI.Wrappers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace BoSSS.Application.FSI_Solver {
    [Serializable]
    internal class ParticleHydrodynamics {
        internal ParticleHydrodynamics(LevelSetTracker lsTrk) {
            m_LsTrk = lsTrk;
        }
        [DataMember]
        private static readonly int m_Dim = 2;
        [DataMember]
        private readonly List<double[]> m_ForcesAndTorquePreviousIteration = new List<double[]>();
        [DataMember]
        private readonly List<double[]> m_ForcesAndTorqueWithoutRelaxation = new List<double[]>();
        [DataMember]
        private readonly LevelSetTracker m_LsTrk;

        /// <summary>
        /// ...
        /// </summary>
        /// <param name="AllParticles"></param>
        /// <param name="hydrodynamicsIntegration"></param>
        /// <param name="fluidDensity"></param>
        /// <param name="underrelax"></param>
        internal void CalculateHydrodynamics(List<Particle> AllParticles, ParticleHydrodynamicsIntegration hydrodynamicsIntegration, double fluidDensity, bool underrelax) {
            double[] hydrodynamics = new double[m_Dim * AllParticles.Count() + AllParticles.Count()];
            for (int p = 0; p < AllParticles.Count(); p++) {
                Particle currentParticle = AllParticles[p];
                CellMask cutCells = currentParticle.CutCells_P(m_LsTrk);
                int offset = p * (m_Dim + 1);
                double[] tempForces = currentParticle.Motion.CalculateHydrodynamicForces(hydrodynamicsIntegration, fluidDensity, cutCells);
                double tempTorque = currentParticle.Motion.CalculateHydrodynamicTorque(hydrodynamicsIntegration, cutCells);
                for (int d = 0; d < m_Dim; d++) {
                    hydrodynamics[offset + d] = tempForces[d];
                }
                hydrodynamics[offset + m_Dim] = tempTorque;
            }
            for (int p = 0; p < AllParticles.Count(); p++) {
                Particle currentParticle = AllParticles[p];
                int offset = p * (m_Dim + 1);
                if (!currentParticle.IsMaster)
                    continue;
                if (!currentParticle.MasterGhostIDs.IsNullOrEmpty()) {
                    for (int g = 1; g < currentParticle.MasterGhostIDs.Length; g++) {
                        int ghostOffset = (currentParticle.MasterGhostIDs[g] - 1) * (m_Dim + 1);
                        if (currentParticle.MasterGhostIDs[g] < 1)
                            continue;
                        for (int d = 0; d < m_Dim; d++) {
                            hydrodynamics[offset + d] += hydrodynamics[ghostOffset + d];
                            hydrodynamics[ghostOffset + d] = 0;
                        }
                        hydrodynamics[offset + m_Dim] += hydrodynamics[ghostOffset + m_Dim];
                        hydrodynamics[ghostOffset + m_Dim] = 0;
                    }
                }
            }
            double[] relaxatedHydrodynamics = hydrodynamics.CloneAs();
            double omega = AllParticles[0].Motion.omega;
            if (underrelax)
                relaxatedHydrodynamics = HydrodynamicsPostprocessing(hydrodynamics, ref omega);
            AllParticles[0].Motion.omega = omega;
            for (int p = 0; p < AllParticles.Count(); p++) {
                Particle currentParticle = AllParticles[p];
                currentParticle.Motion.UpdateForcesAndTorque(p, relaxatedHydrodynamics);
            }
        }

        /// <summary>
        /// Post-processing of the hydrodynamics. If desired the underrelaxation is applied to the forces and torque.
        /// </summary>
        /// <param name="hydrodynamics"></param>
        private double[] HydrodynamicsPostprocessing(double[] hydrodynamics, ref double omega) {
            double[] relaxatedHydrodynamics;
            m_ForcesAndTorqueWithoutRelaxation.Insert(0, hydrodynamics.CloneAs());
            relaxatedHydrodynamics = m_ForcesAndTorquePreviousIteration.Count > 2
                ? AitkenUnderrelaxation(hydrodynamics, ref omega)
                : StaticUnderrelaxation(hydrodynamics);
            return relaxatedHydrodynamics;
        }

        /// <summary>
        /// ...
        /// </summary>
        /// <param name="AllParticles"></param>
        internal void SaveHydrodynamicOfPreviousIteration(List<Particle> AllParticles) {
            double[] hydrodynamics = new double[(m_Dim + 1) * AllParticles.Count()];
            if(m_ForcesAndTorquePreviousIteration.Count() > 2)
                m_ForcesAndTorquePreviousIteration.RemoveAt(2);
            for (int p = 0; p < AllParticles.Count(); p++) {
                Particle currentParticle = AllParticles[p];
                int offset = p * (m_Dim + 1);
                double[] tempForces = currentParticle.Motion.GetHydrodynamicForces(0);
                double tempTorque = currentParticle.Motion.GetHydrodynamicTorque(0);
                for (int d = 0; d < m_Dim; d++) {
                    hydrodynamics[offset + d] = tempForces[d];
                }
                hydrodynamics[offset + m_Dim] = tempTorque;
            }
            m_ForcesAndTorquePreviousIteration.Insert(0, hydrodynamics.CloneAs());
        }

        /// <summary>
        /// Residual for fully coupled system
        /// </summary>
        /// <param name="iterationCounter"></param>
        internal double CalculateParticleResidual(ref int iterationCounter) {
            double residual = 0;
            double denom = 0;
            if (iterationCounter <= 2)
                residual = double.MaxValue;
            else {
                for (int i = 0; i < m_ForcesAndTorquePreviousIteration[1].Length; i++) {
                    if (m_ForcesAndTorquePreviousIteration[0].Length >= i) {
                        residual += (m_ForcesAndTorquePreviousIteration[0][i] - m_ForcesAndTorquePreviousIteration[1][i]).Pow2();
                        denom += m_ForcesAndTorquePreviousIteration[0][i].Pow2();
                    }
                }
            }
            residual = Math.Sqrt(residual / denom);
            iterationCounter += 1;
            return residual;
        }

        private double[] StaticUnderrelaxation(double[] variable) {
            double[] returnVariable = variable.CloneAs();
            for (int d = 0; d < variable.Length; d++) {
                if (variable[d] == 0)//ghost Particle
                    continue;
                returnVariable[d] = 0.1 * variable[d] + (1 - 0.1) * m_ForcesAndTorquePreviousIteration[1][d];
            }
            return returnVariable;
        }

        private double[] AitkenUnderrelaxation(double[] variable, ref double Omega) {
            double[][] residual = new double[variable.Length][];
            double[] residualDiff = new double[variable.Length];
            double residualScalar = 0;
            for (int i = 0; i < variable.Length; i++) {
                if (variable[i] == 0) {// ghost particle
                    residualDiff[i] = 0;
                    continue;
                }
                residual[i] = new double[] { (variable[i] - m_ForcesAndTorquePreviousIteration[0][i]), (m_ForcesAndTorqueWithoutRelaxation[1][i] - m_ForcesAndTorquePreviousIteration[1][i]) };
                residualDiff[i] = residual[i][0] - residual[i][1];
                residualScalar += residual[i][1] * residualDiff[i];
            }
            Omega = -Omega * residualScalar / residualDiff.L2Norm().Pow2();
            double[] outVar = variable.CloneAs();
            for (int i = 0; i < variable.Length; i++) {
                if (variable[i] == 0)// ghost particle
                    continue;
                outVar[i] = Omega * (variable[i] - m_ForcesAndTorquePreviousIteration[0][i]) + m_ForcesAndTorquePreviousIteration[0][i];
            }
            return outVar;
        }
    }
}
