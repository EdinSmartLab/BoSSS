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

using System;
using System.Runtime.Serialization;
using ilPSP;
using System.Linq;
using ilPSP.Utils;

namespace BoSSS.Application.FSI_Solver {
    [DataContract]
    [Serializable]
    public class Particle_Rectangle : Particle {
        /// <summary>
        /// Empty constructor used during de-serialization
        /// </summary>
        private Particle_Rectangle() : base() {

        }

        /// <summary>
        /// Constructor for an ellipsoid.
        /// </summary>
        /// <param name="motionInit">
        /// Initializes the motion parameters of the particle (which model to use, whether it is a dry simulation etc.)
        /// </param>
        /// <param name="length">
        /// The length of the horizontal halfaxis.
        /// </param>
        /// <param name="thickness">
        /// The length of the vertical halfaxis.
        /// </param>
        /// <param name="startPos">
        /// The initial position.
        /// </param>
        /// <param name="startAngl">
        /// The inital anlge.
        /// </param>
        /// <param name="activeStress">
        /// The active stress excerted on the fluid by the particle. Zero for passive particles.
        /// </param>
        /// <param name="startTransVelocity">
        /// The inital translational velocity.
        /// </param>
        /// <param name="startRotVelocity">
        /// The inital rotational velocity.
        /// </param>
        public Particle_Rectangle(ParticleMotionInit motionInit, double length = 4, double thickness = 1, double[] startPos = null, double startAngl = 0, double activeStress = 0, double[] startTransVelocity = null, double startRotVelocity = 0) : base(motionInit, startPos, startAngl, activeStress, startTransVelocity, startRotVelocity) {
            m_Length = length;
            m_Thickness = thickness;
            Aux.TestArithmeticException(length, "Particle length");
            Aux.TestArithmeticException(thickness, "Particle thickness");

            Motion.GetParticleLengthscale(GetLengthScales().Max());
            Motion.GetParticleMinimalLengthscale(GetLengthScales().Max());
            Motion.GetParticleArea(Area);
            Motion.GetParticleMomentOfInertia(MomentOfInertia);
        }

        [DataMember]
        private readonly double m_Length;
        [DataMember]
        private readonly double m_Thickness;

        /// <summary>
        /// Circumference of an elliptic particle. Approximated with Ramanujan.
        /// </summary>
        public override double Circumference => 2 * m_Length + 2 * m_Thickness;

        /// <summary>
        /// Moment of inertia of an elliptic particle.
        /// </summary>
        override public double MomentOfInertia => (Mass_P * (m_Length.Pow2() + m_Thickness.Pow2())) / 12;

        /// <summary>
        /// Area occupied by the particle.
        /// </summary>
        public override double Area => m_Length * m_Thickness;

        /// <summary>
        /// Level set function of the particle.
        /// </summary>
        /// <param name="X">
        /// The current point.
        /// </param>
        public override double LevelSetFunction(double[] X) {
            double angle = Motion.GetAngle(0);
            double[] position = Motion.GetPosition(0);
            double[] tempX = X.CloneAs();
            tempX[0] = X[0] * Math.Cos(angle) + X[1] * Math.Sin(angle);
            tempX[1] = X[0] * Math.Sin(angle) + X[1] * Math.Cos(angle);
            double r = -Math.Max(Math.Abs(tempX[0] - position[0]) - m_Length, Math.Abs(tempX[1] - position[1]) - m_Thickness);
            if (double.IsNaN(r) || double.IsInfinity(r))
                throw new ArithmeticException();
            return r;
        }

        /// <summary>
        /// Returns true if a point is withing the particle.
        /// </summary>
        /// <param name="point">
        /// The point to be tested.
        /// </param>
        /// <param name="minTolerance">
        /// Minimum tolerance length.
        /// </param>
        /// <param name="maxTolerance">
        /// Maximal tolerance length. Equal to h_min if not specified.
        /// </param>
        /// <param name="WithoutTolerance">
        /// No tolerance.
        /// </param>
        public override bool Contains(double[] point, double minTolerance, double maxTolerance = 0, bool WithoutTolerance = false) {
            double angle = Motion.GetAngle(0);
            double[] position = Motion.GetPosition(0);
            if (maxTolerance == 0)
                maxTolerance = minTolerance;
            double a = !WithoutTolerance ? m_Length + Math.Sqrt(maxTolerance.Pow2() + minTolerance.Pow2()) : m_Length;
            double b = !WithoutTolerance ? m_Thickness + Math.Sqrt(maxTolerance.Pow2() + minTolerance.Pow2()) : m_Thickness;
            double[] tempX = point.CloneAs();
            tempX[0] = point[0] * Math.Cos(angle) - point[1] * Math.Sin(angle);
            tempX[1] = point[0] * Math.Sin(angle) + point[1] * Math.Cos(angle);
            if (Math.Abs(tempX[0] - position[0]) < a && Math.Abs(tempX[1] - position[1]) < b)
                return true;
            else
               return false;
        }

        /// <summary>
        /// Returns the support point of the particle in the direction specified by a vector.
        /// </summary>
        /// <param name="vector">
        /// A vector. 
        /// </param>
        override public double[] GetSupportPoint(double[] vector, int SubParticleID) {
            Aux.TestArithmeticException(vector, "vector in calc of support point");
            if (vector.L2Norm() == 0)
                throw new ArithmeticException("The given vector has no length");
            
            double[] supportPoint = vector.CloneAs();
            double angle = Motion.GetAngle(0);
            double[] position = Motion.GetPosition(0);
            double[] rotVector = vector.CloneAs();
            rotVector[0] = vector[0] * Math.Cos(angle) - vector[1] * Math.Sin(angle);
            rotVector[1] = vector[0] * Math.Sin(angle) + vector[1] * Math.Cos(angle);
            double[] length = position.CloneAs();
            length[0] = m_Length * Math.Cos(angle) - m_Thickness * Math.Sin(angle);
            length[1] = m_Length * Math.Sin(angle) + m_Thickness * Math.Cos(angle);
            for(int d = 0; d < position.Length; d++) {
                supportPoint[d] = Math.Sign(rotVector[d]) * length[d] + position[d];
            }
            return supportPoint;
        }

        /// <summary>
        /// Returns the legnthscales of a particle.
        /// </summary>
        override public double[] GetLengthScales() {
            return new double[] { m_Length, m_Thickness };
        }
    }
}
