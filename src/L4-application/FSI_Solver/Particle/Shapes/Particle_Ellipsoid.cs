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
    public class Particle_Ellipsoid : Particle {
        /// <summary>
        /// Empty constructor used during de-serialization
        /// </summary>
        private Particle_Ellipsoid() : base() {

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
        public Particle_Ellipsoid(ParticleMotionInit motionInit, double length = 4, double thickness = 1, double[] startPos = null, double startAngl = 0, double activeStress = 0, double[] startTransVelocity = null, double startRotVelocity = 0) : base(motionInit, startPos, startAngl, activeStress, startTransVelocity, startRotVelocity) {
            m_Length = length;
            m_Thickness = thickness;
            Aux.TestArithmeticException(length, "Particle length");
            Aux.TestArithmeticException(thickness, "Particle thickness");

            Motion.GetParticleLengthscale(GetLengthScales().Max());
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
        protected override double Circumference => Math.PI * ((m_Length + m_Thickness) + (3 * (m_Length - m_Thickness).Pow2()) / (10 * (m_Length + m_Thickness) + Math.Sqrt(m_Length.Pow2() + 14 * m_Length * m_Thickness + m_Thickness.Pow2())));

        /// <summary>
        /// Moment of inertia of an elliptic particle.
        /// </summary>
        override public double MomentOfInertia => (1 / 4.0) * (Mass_P * (m_Length * m_Length + m_Thickness * m_Thickness));

        /// <summary>
        /// Area occupied by the particle.
        /// </summary>
        public override double Area => m_Length * m_Thickness * Math.PI;

        /// <summary>
        /// Level set function of the particle.
        /// </summary>
        /// <param name="X">
        /// The current point.
        /// </param>
        public override double LevelSetFunction(double[] X) {
            double angle = -Motion.GetAngle(0);
            double[] position = Motion.GetPosition(0);
            double r = -(((X[0] - position[0]) * Math.Cos(angle) - (X[1] - position[1]) * Math.Sin(angle)) / m_Length).Pow2()
                       - (((X[0] - position[0]) * Math.Sin(angle) + (X[1] - position[1]) * Math.Cos(angle)) / m_Thickness).Pow2()
                       + 1.0;
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
            double radiusTolerance = 1;
            double a = !WithoutTolerance ? m_Length + Math.Sqrt(maxTolerance.Pow2() + minTolerance.Pow2()) : m_Length;
            double b = !WithoutTolerance ? m_Thickness + Math.Sqrt(maxTolerance.Pow2() + minTolerance.Pow2()) : m_Thickness;
            double Ellipse = ((point[0] - position[0]) * Math.Cos(angle) + (point[1] - position[1]) * Math.Sin(angle)).Pow2() / a.Pow2() + (-(point[0] - position[0]) * Math.Sin(angle) + (point[1] - position[1]) * Math.Cos(angle)).Pow2() / b.Pow2();
            return Ellipse < radiusTolerance;
        }

        /// <summary>
        /// Returns an array with points on the surface of the particle.
        /// </summary>
        /// <param name="hMin">
        /// Minimal cell length. Used to specify the number of surface points.
        /// </param>
        override public MultidimensionalArray GetSurfacePoints(double hMin) {
            if (spatialDim != 2)
                throw new NotImplementedException("Only two dimensions are supported.");
            double angle = Motion.GetAngle(0);
            double[] position = Motion.GetPosition(0);
            int NoOfSurfacePoints = Convert.ToInt32(5 * Circumference / hMin);
            MultidimensionalArray SurfacePoints = MultidimensionalArray.Create(NoOfSubParticles, NoOfSurfacePoints, spatialDim);
            double[] InfinitisemalAngle = GenericBlas.Linspace(0, Math.PI * 2, NoOfSurfacePoints + 1);
            if (Math.Abs(10 * Circumference / hMin + 1) >= int.MaxValue)
                throw new ArithmeticException("Error trying to calculate the number of surface points, overflow");
            for (int j = 0; j < NoOfSurfacePoints; j++) {
                double temp0 = Math.Cos(InfinitisemalAngle[j]) * m_Length;
                double temp1 = Math.Sin(InfinitisemalAngle[j]) * m_Thickness;
                SurfacePoints[0, j, 0] = (temp0 * Math.Cos(angle) - temp1 * Math.Sin(angle)) + position[0];
                SurfacePoints[0, j, 1] = (temp0 * Math.Sin(angle) + temp1 * Math.Cos(angle)) + position[1];

            }
            return SurfacePoints;
        }

        /// <summary>
        /// Returns the support point of the particle in the direction specified by a vector.
        /// </summary>
        /// <param name="vector">
        /// A vector. 
        /// </param>
        override public double[] GetSupportPoint(double[] vector) {
            Aux.TestArithmeticException(vector, "vector in calc of support point");
            if (vector.L2Norm() == 0)
                throw new ArithmeticException("The given vector has no length");

            double[] SupportPoint = new double[spatialDim];
            double angle = Motion.GetAngle(0);
            double[] position = Motion.GetPosition(0);

            double[,] rotMatrix = new double[2, 2];
            rotMatrix[0, 0] = m_Length * Math.Cos(angle);
            rotMatrix[0, 1] = -m_Thickness * Math.Sin(angle);
            rotMatrix[1, 0] = m_Length * Math.Sin(angle);
            rotMatrix[1, 1] = m_Thickness * Math.Cos(angle);
            double[,] transposeRotMatrix = rotMatrix.CloneAs();
            transposeRotMatrix[0, 1] = rotMatrix[1, 0];
            transposeRotMatrix[1, 0] = rotMatrix[0, 1];

            double[] rotVector = new double[2];
            for (int i = 0; i < 2; i++) {
                for (int j = 0; j < 2; j++) {
                    rotVector[i] += transposeRotMatrix[i, j] * vector[j];
                }
            }
            rotVector.ScaleV(rotVector.L2Norm());

            for (int i = 0; i < 2; i++) {
                for (int j = 0; j < 2; j++) {
                    SupportPoint[i] += rotMatrix[i, j] * rotVector[j];
                }
                SupportPoint[i] += position[i];
            }
            return SupportPoint;
        }

        /// <summary>
        /// Returns the legnthscales of a particle.
        /// </summary>
        override public double[] GetLengthScales() {
            return new double[] { m_Length, m_Thickness };
        }
    }
}

