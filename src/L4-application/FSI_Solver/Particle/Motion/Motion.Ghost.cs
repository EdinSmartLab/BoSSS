﻿/* =======================================================================
Copyright 2020 Technische Universitaet Darmstadt, Fachgebiet fuer Stroemungsdynamik (chair of fluid dynamics)

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
using ilPSP;
using System.Runtime.Serialization;

namespace BoSSS.Application.FSI_Solver {
    public class MotionGhost : Motion {

        /// <summary>
        /// The dry description of motion without hydrodynamics.
        /// </summary>
        /// <param name="gravity">
        /// The gravity (volume forces) acting on the particle.
        /// </param>
        /// <param name="density">
        /// The density of the particle.
        /// </param>
        public MotionGhost(Vector gravity, double density, int MasterID) : base(gravity, density) {
            this.MasterID = MasterID;
        }

        /// <summary>
        /// I'm a ghost! Hui Buh!
        /// </summary>
        internal override bool IsGhost { get; } = true;

        [DataMember]
        readonly private int MasterID;
        [DataMember]
        private Vector Position;
        [DataMember]
        private Vector TranslationalVelocity;
        [DataMember]
        private double Angle;
        [DataMember]
        private double RotationalVelocity;


        internal override int GetMasterID() => MasterID;

        internal override void CopyNewPosition(Vector position, double angle) {
            Position = new Vector(position);
            Angle = angle;
        }

        internal override void CopyNewVelocity(Vector translational, double rotational) {
            TranslationalVelocity = new Vector(translational);
            RotationalVelocity = rotational;
        }

        /// <summary>
        /// Calculate the new translational velocity of the particle using a Crank Nicolson scheme.
        /// </summary>
        /// <param name="dt">Timestep</param>
        protected override Vector CalculateTranslationalVelocity(double dt = 0) {
            Aux.TestArithmeticException(TranslationalVelocity, "particle translational velocity");
            return TranslationalVelocity;
        }

        /// <summary>
        /// Calculates the new translational acceleration.
        /// </summary>
        /// <param name="dt"></param>
        protected override Vector CalculateTranslationalAcceleration(double dt) {
            return new Vector(m_Dim);
        }

        /// <summary>
        /// Calculate the new angular velocity of the particle using explicit Euler scheme.
        /// </summary>
        /// <param name="dt">Timestep</param>
        /// <param name="collisionTimestep">The time consumed during the collision procedure</param>
        protected override double CalculateAngularVelocity(double dt = 0) {
            Aux.TestArithmeticException(RotationalVelocity, "particle rotational velocity");
            return RotationalVelocity;
        }

        /// <summary>
        /// Calculate the new acceleration (translational and rotational)
        /// </summary>
        /// <param name="dt"></param>
        protected override double CalculateRotationalAcceleration(double dt) {
            double l_Acceleration = 0;
            Aux.TestArithmeticException(l_Acceleration, "particle rotational acceleration");
            return l_Acceleration;
        }

        public override object Clone() {
            Motion clonedMotion = new MotionGhost(Gravity, Density, MasterID);
            return clonedMotion;
        }
    }
}
