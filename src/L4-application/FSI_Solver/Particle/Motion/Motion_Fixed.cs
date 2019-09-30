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

using BoSSS.Foundation;
using BoSSS.Foundation.Grid;
using BoSSS.Foundation.XDG;

namespace BoSSS.Application.FSI_Solver {
    public class Motion_Fixed : Motion_Wet {

        /// <summary>
        /// No motion
        /// </summary>
        /// <param name="gravity">
        /// The gravity (volume forces) acting on the particle.
        /// </param>
        /// <param name="density">
        /// The density of the particle.
        /// </param>
        public Motion_Fixed(double[] gravity = null, double density = 0) : base(gravity, density) {
            IncludeRotation = false;
            IncludeTranslation = false;
        }

        /// <summary>
        /// Include rotation?
        /// </summary>
        internal override bool IncludeRotation { get; } = false;

        /// <summary>
        /// Include translation?
        /// </summary>
        internal override bool IncludeTranslation { get; } = false;

        /// <summary>
        /// Calculate the new particle position
        /// </summary>
        /// <param name="dt"></param>
        protected override double[] CalculateParticlePosition(double dt, double collisionTimestep) {
            double[] l_Position = new double[spatialDim];
            for (int d = 0; d < spatialDim; d++) {
                l_Position[d] = GetPosition(1)[d];
            }
            Aux.TestArithmeticException(l_Position, "particle position");
            return l_Position;
        }

        /// <summary>
        /// Calculate the new particle angle
        /// </summary>
        /// <param name="dt"></param>
        protected override double CalculateParticleAngle(double dt, double collisionTimestep = 0) {
            double l_Angle = GetAngle(1);
            Aux.TestArithmeticException(l_Angle, "particle angle");
            return l_Angle;
        }

        /// <summary>
        /// Calculate the new translational velocity of the particle using a Crank Nicolson scheme.
        /// </summary>
        /// <param name="dt">Timestep</param>
        protected override double[] CalculateTranslationalVelocity(double dt) {
            double[] l_TranslationalVelocity = new double[spatialDim];
            for (int d = 0; d < spatialDim; d++) {
                l_TranslationalVelocity[d] = 0;
            }
            Aux.TestArithmeticException(l_TranslationalVelocity, "particle translational velocity");
            return l_TranslationalVelocity;
        }

        /// <summary>
        /// Calculate the new translational velocity of the particle using a Crank Nicolson scheme.
        /// </summary>
        /// <param name="dt">Timestep</param>
        protected override double[] CalculateTranslationalVelocity(double dt, double collisionTimestep) {
            double[] l_TranslationalVelocity = new double[spatialDim];
            for (int d = 0; d < spatialDim; d++) {
                l_TranslationalVelocity[d] = 0;
            }
            Aux.TestArithmeticException(l_TranslationalVelocity, "particle translational velocity");
            return l_TranslationalVelocity;
        }

        /// <summary>
        /// Calculate the new angular velocity of the particle using explicit Euler scheme.
        /// </summary>
        /// <param name="dt">Timestep</param>
        /// <param name="collisionTimestep">The time consumed during the collision procedure</param>
        protected override double CalculateAngularVelocity(double dt) {
            double l_RotationalVelocity = 0;
            Aux.TestArithmeticException(l_RotationalVelocity, "particle rotational velocity");
            return l_RotationalVelocity;
        }

        /// <summary>
        /// Calculate the new angular velocity of the particle using explicit Euler scheme.
        /// </summary>
        /// <param name="dt">Timestep</param>
        /// <param name="collisionTimestep">The time consumed during the collision procedure</param>
        protected override double CalculateAngularVelocity(double dt, double collisionTimestep) {
            double l_RotationalVelocity = 0;
            Aux.TestArithmeticException(l_RotationalVelocity, "particle rotational velocity");
            return l_RotationalVelocity;
        }

        /// <summary>
        /// Calculates the new translational acceleration.
        /// </summary>
        /// <param name="dt"></param>
        protected override double[] CalculateTranslationalAcceleration(double dt = 0) {
            return new double[] { 0, 0 };
        }

        /// <summary>
        /// Calculate the new acceleration (translational and rotational)
        /// </summary>
        /// <param name="dt"></param>
        protected override double CalculateRotationalAcceleration(double dt) {
            return 0;
        }

        /// <summary>
        /// Overrides the calculation of hydrodynamics for fixed particles, so that nothing happens.
        /// </summary>
        public override void UpdateForcesAndTorque(VectorField<SinglePhaseField> U = null, SinglePhaseField P = null, LevelSetTracker levelSetTracker = null, CellMask cutCells = null, double fluidViscosity = 0, double fluidDensity = 0, bool firstIteration = false, double dt = 0) {
            double[] tempForces = new double[spatialDim];
            for (int d = 0; d < spatialDim; d++) {
                tempForces[d] = 0;
            }
            double tempTorque = 0;
            HydrodynamicsPostprocessing(tempForces, tempTorque, true);
        }
    }
}