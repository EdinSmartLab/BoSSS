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
using ilPSP.Utils;
using BoSSS.Foundation;

namespace BoSSS.Solution.NSECommon.Operator.Continuity {
    /// <summary>
    /// velocity jump penalty for the divergence operator, on the level set
    /// </summary>
    public class ActiveDivergenceAtIB : ILevelSetForm {

        public ActiveDivergenceAtIB(int _D, LevelSetTracker lsTrk, Func<double[], double, double[]> getParticleParams) {
            D = _D;
            m_LsTrk = lsTrk;
            m_getParticleParams = getParticleParams;
        }

        private readonly LevelSetTracker m_LsTrk;

        private readonly int D;

        /// <summary>
        /// Describes: 0: velX, 1: velY, 2:rotVel,3:particleradius
        /// </summary>
        private readonly Func<double[], double, double[]> m_getParticleParams;

        /// <summary>
        /// the penalty flux
        /// </summary>
        static double DirichletFlux(double UxN_in, double UxN_out) {
            return (UxN_in - UxN_out);
        }

        public double LevelSetForm(ref CommonParamsLs cp, double[] U_Neg, double[] U_Pos, double[,] Grad_uA, double[,] Grad_uB, double v_Neg, double v_Pos, double[] Grad_vA, double[] Grad_vB) {
            double uAxN = GenericBlas.InnerProd(U_Neg, cp.n);

            var parameters_P = m_getParticleParams(cp.x, cp.time);
            double[] uLevSet = new double[] { parameters_P[0], parameters_P[1] };
            double wLevSet = parameters_P[2];
            double[] RadialNormalVector = new double[] { parameters_P[3], parameters_P[4] };
            double RadialLength = parameters_P[5];

            double[] _uLevSet = new double[D];

            _uLevSet[0] = uLevSet[0] + RadialLength * wLevSet * RadialNormalVector[0];
            _uLevSet[1] = uLevSet[1] + RadialLength * wLevSet * RadialNormalVector[1];

            double uBxN = GenericBlas.InnerProd(_uLevSet, cp.n);

            // transform from species B to A: we call this the "A-fictitious" value
            double uAxN_fict;
            uAxN_fict = uBxN;

            double FlxNeg = -DirichletFlux(uAxN, uAxN_fict); // flux on A-side
            //double FlxPos = 0;

            return FlxNeg * v_Neg;
        }

        public IList<string> ArgumentOrdering {
            get {
                return VariableNames.VelocityVector(this.D);
            }
        }

        public IList<string> ParameterOrdering {
            get {
                return null;
            }
        }

        public int LevelSetIndex {
            get {
                return 0;
            }
        }

        public SpeciesId PositiveSpecies {
            get { return this.m_LsTrk.GetSpeciesId("B"); }
        }

        public SpeciesId NegativeSpecies {
            get { return this.m_LsTrk.GetSpeciesId("A"); }
        }

        public TermActivationFlags LevelSetTerms {
            get {
                return TermActivationFlags.V | TermActivationFlags.UxV;
            }
        }
    }
}