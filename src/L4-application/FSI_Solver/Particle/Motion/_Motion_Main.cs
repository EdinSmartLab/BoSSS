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
using BoSSS.Foundation.Quadrature;
using BoSSS.Foundation.XDG;
using FSI_Solver;
using ilPSP;
using ilPSP.Utils;
using MPI.Wrappers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace BoSSS.Application.FSI_Solver {
    public class Motion_Wet {

        public Motion_Wet(double[] gravity,
            double density,
            ParticleUnderrelaxationParam underrelaxationParam = null) {
            Gravity = gravity;
            m_UnderrelaxationParam = underrelaxationParam;
            Density = density;

            for (int i = 0; i < historyLength; i++) {
                m_Position.Add(new double[spatialDim]);
                m_Angle.Add(new double());
                m_TranslationalVelocity.Add(new double[spatialDim]);
                m_TranslationalAcceleration.Add(new double[spatialDim]);
                m_RotationalVelocity.Add(new double());
                m_RotationalAcceleration.Add(new double());
                m_HydrodynamicForces.Add(new double[spatialDim]);
                m_HydrodynamicTorque.Add(new double());
            }
        }

        private const int historyLength = 4;
        protected static int spatialDim = 2;
        [NonSerialized]
        readonly internal FSI_Auxillary Aux = new FSI_Auxillary();
        [NonSerialized]
        readonly private ParticleForceIntegration ForceIntegration = new ParticleForceIntegration();
        readonly ParticleUnderrelaxation Underrelaxation = new ParticleUnderrelaxation();
        private readonly ParticleUnderrelaxationParam m_UnderrelaxationParam = null;

        /// <summary>
        /// Gravity (volume force) acting on the particle.
        /// </summary>
        protected double[] Gravity { get; }

        /// <summary>
        /// Density of the particle.
        /// </summary>
        public double Density { get; }

        /// <summary>
        /// The translational velocity of the particle in the current time step.
        /// </summary>
        protected double ParticleArea { get; private set; }

        /// <summary>
        /// The translational velocity of the particle in the current time step.
        /// </summary>
        protected double MomentOfInertia { get; private set; }

        /// <summary>
        /// The maximum lenghtscale of the particle.
        /// </summary>
        protected double MaxParticleLengthScale { get; private set; }

        /// <summary>
        /// Include rotation?
        /// </summary>
        public virtual bool IncludeRotation { get; } = true;

        /// <summary>
        /// Include translation?
        /// </summary>
        public virtual bool IncludeTranslation { get; } = true;

        /// <summary>
        /// Use added damping?, for reference: Banks et.al. 2017
        /// </summary>
        public virtual bool UseAddedDamping { get; } = false;

        /// <summary>
        /// Complete added damping tensor, for reference: Banks et.al. 2017
        /// </summary>
        public virtual double[,] AddedDampingTensor { get; } = new double[6, 6];

        /// <summary>
        /// The position of the particle in the current time step.
        /// </summary>
        public double[] GetPosition(int historyPosition) {// used method instead of property as IReadOnlyList causes exceptions during deserialization
            return m_Position[historyPosition];
        }

        private readonly List<double[]> m_Position = new List<double[]>();

        /// <summary>
        /// The angular velocity of the particle in the current time step.
        /// </summary>
        public double GetAngle(int historyPosition) {
            return m_Angle[historyPosition];
        }

        private readonly List<double> m_Angle = new List<double>();

        /// <summary>
        /// The translational velocity of the particle in the current time step.
        /// </summary>
        public double[] GetTranslationalVelocity(int historyPosition) {
            return m_TranslationalVelocity[historyPosition];
        }

        private readonly List<double[]> m_TranslationalVelocity = new List<double[]>();

        /// <summary>
        /// The angular velocity of the particle in the current time step.
        /// </summary>
        public double GetRotationalVelocity(int historyPosition) {
            return m_RotationalVelocity[historyPosition];
        }

        private readonly List<double> m_RotationalVelocity = new List<double>();

        /// <summary>
        /// The translational velocity of the particle in the current time step.
        /// </summary>
        public double[] GetTranslationalAcceleration(int historyPosition) {
            return m_TranslationalAcceleration[historyPosition];
        }

        private readonly List<double[]> m_TranslationalAcceleration = new List<double[]>();

        /// <summary>
        /// The angular velocity of the particle in the current time step.
        /// </summary>
        public double GetRotationalAcceleration(int historyPosition) {
            return m_RotationalAcceleration[historyPosition];
        }

        private readonly List<double> m_RotationalAcceleration = new List<double>();

        /// <summary>
        /// The force acting on the particle in the current time step.
        /// </summary>
        public double[] GetHydrodynamicForces(int historyPosition) {
            return m_HydrodynamicForces[historyPosition];
        }

        private readonly List<double[]> m_HydrodynamicForces = new List<double[]>();

        /// <summary>
        /// The Torque acting on the particle in the current time step.
        /// </summary>
        public double GetHydrodynamicTorque(int historyPosition) {
            return m_HydrodynamicTorque[historyPosition];
        }

        private readonly List<double> m_HydrodynamicTorque = new List<double>();

        /// <summary>
        /// The force acting on the particle in the current time step.
        /// </summary>
        public double[] GetForcesPreviousIter() {
            return m_ForcesPreviousIter;
        }

        private double[] m_ForcesPreviousIter = new double[spatialDim];

        /// <summary>
        /// The force acting on the particle in the current time step.
        /// </summary>
        public double TorquePreviousIter { get; private set; }

        /// <summary>
        /// The force acting on the particle in the current time step.
        /// </summary>
        private double m_CollisionTimestep = 0;

        /// <summary>
        /// The translational velocity of the particle in the current time step. This list is used by the momentum conservation model.
        /// </summary>
        public double[] GetPreCollisionVelocity() {
            return m_PreCollisionVelocity;
        }

        private double[] m_PreCollisionVelocity = new double[spatialDim];

        /// <summary>
        /// The translational velocity of the particle in the current time step. This list is used by the momentum conservation model.
        /// </summary>
        [DataMember]
        private readonly List<double[]> m_CollisionTranslationalVelocity = new List<double[]>();

        /// <summary>
        /// The angular velocity of the particle in the current time step. This list is used by the momentum conservation model.
        /// </summary>
        [DataMember]
        private readonly List<double> m_CollisionRotationalVelocity = new List<double>();

        /// <summary>
        /// The translational velocity of the particle in the current time step. This list is used by the momentum conservation model.
        /// </summary>
        [DataMember]
        private readonly List<double[]> m_CollisionNormalVector = new List<double[]>();

        /// <summary>
        /// The translational velocity of the particle in the current time step. This list is used by the momentum conservation model.
        /// </summary>
        public double[] GetLastCollisionTangentialVector() {
            return m_CollisionTangentialVector.Last();
        }

        private readonly List<double[]> m_CollisionTangentialVector = new List<double[]>();


        /// <summary>
        /// Saves position and angle of the last timestep
        /// </summary>
        public void SavePositionAndAngleOfPreviousTimestep() {
            Aux.SaveMultidimValueOfLastTimestep(m_Position);
            Aux.SaveValueOfLastTimestep(m_Angle);
        }

        /// <summary>
        /// Saves translational and rotational velocities of the last timestep
        /// </summary>
        public void SaveVelocityOfPreviousTimestep() {
            Aux.SaveMultidimValueOfLastTimestep(m_TranslationalVelocity);
            Aux.SaveValueOfLastTimestep(m_RotationalVelocity);
            Aux.SaveMultidimValueOfLastTimestep(m_TranslationalAcceleration);
            Aux.SaveValueOfLastTimestep(m_RotationalAcceleration);
        }

        /// <summary>
        /// Saves force and torque of the previous timestep
        /// </summary>
        public void SaveHydrodynamicsOfPreviousIteration() {
            m_ForcesPreviousIter = m_HydrodynamicForces[0].CloneAs();
            TorquePreviousIter = m_HydrodynamicTorque[0];
        }

        /// <summary>
        /// Saves force and torque of the previous timestep
        /// </summary>
        public void SaveHydrodynamicsOfPreviousTimestep() {
            Aux.SaveMultidimValueOfLastTimestep(m_HydrodynamicForces);
            Aux.SaveValueOfLastTimestep(m_HydrodynamicTorque);
        }

        public void InitializeParticlePositionAndAngle(double[] positionP, double angleP) {
            for (int i = 0; i < historyLength; i++) {
                m_Position[i] = positionP.CloneAs();
                m_Angle[i] = angleP * 2 * Math.PI / 360;
            }
        }

        public void InitializeParticleVelocity(double[] translationalVelocityP, double rotationalVelocityP) {
            if (translationalVelocityP == null)
                m_TranslationalVelocity[0] = new double[spatialDim];
            else
                m_TranslationalVelocity[0] = translationalVelocityP.CloneAs();

            m_RotationalVelocity[0] = rotationalVelocityP;
        }

        /// <summary>
        /// Init of the particle mass.
        /// </summary>
        /// <param name="mass"></param>
        public void GetParticleArea(double area) => ParticleArea = area;

        /// <summary>
        /// Mass of the current particle.
        /// </summary>
        public double Mass_P {
            get {
                Aux.TestArithmeticException(ParticleArea, "particle area");
                Aux.TestArithmeticException(Density, "particle density");
                return ParticleArea * Density;
            }
        }

        /// <summary>
        /// Init of the moment of inertia.
        /// </summary>
        /// <param name="moment"></param>
        public void GetParticleLengthscale(double lengthscale) => MaxParticleLengthScale = lengthscale;

        /// <summary>
        /// Mass of the current particle.
        /// </summary>
        public double ParticleMass {
            get {
                Aux.TestArithmeticException(ParticleArea, "particle area");
                Aux.TestArithmeticException(Density, "particle density");
                return ParticleArea * Density;
            }
        }
        
        /// <summary>
        /// Init of the moment of inertia.
        /// </summary>
        /// <param name="moment"></param>
        public void GetParticleMomentOfInertia(double moment) {
            MomentOfInertia = moment;
        }

        public void SetCollisionTimestep(double collisionTimestep) {
            m_CollisionTimestep = collisionTimestep;
        }

        /// <summary>
        /// Calls the calculation of the position and angle.
        /// </summary>
        /// <param name="dt"></param>
        public void UpdateParticlePositionAndAngle(double dt) {
            if (m_CollisionTimestep == 0) {
                SavePositionAndAngleOfPreviousTimestep();
                m_Position[0] = CalculateParticlePosition(dt);
                m_Angle[0] = CalculateParticleAngle(dt);
            }
            else {
                if (m_CollisionTimestep < 0)
                    m_CollisionTimestep = 0;
                double[] tempPos = m_Position[0].CloneAs();
                double tempAngle = m_Angle[0];
                SavePositionAndAngleOfPreviousTimestep();
                m_Position[0] = tempPos.CloneAs();
                m_Angle[0] = tempAngle;
                m_Position[0] = CalculateParticlePosition(dt, m_CollisionTimestep);
                m_Angle[0] = CalculateParticleAngle(dt, m_CollisionTimestep);
                if (m_CollisionTimestep > dt) { m_CollisionTimestep -= dt; }
                else m_CollisionTimestep = 0;
            }
        }

        /// <summary>
        /// Calls the calculation of the position and angle.
        /// </summary>
        /// <param name="dt"></param>
        public void CollisionParticlePositionAndAngle(double collisionDynamicTimestep) {
            m_Position[0] = CalculateParticlePosition(collisionDynamicTimestep, collisionProcedure: true);
            m_Angle[0] = CalculateParticleAngle(collisionDynamicTimestep, collisionProcedure: true);
        }

        /// <summary>
        /// Calls the calculation of the velocity.
        /// </summary>
        /// <param name="dt"></param>
        public void UpdateParticleVelocity(double dt) {
            m_TranslationalAcceleration[0] = CalculateTranslationalAcceleration(dt - m_CollisionTimestep);
            m_RotationalAcceleration[0] = CalculateRotationalAcceleration(dt - m_CollisionTimestep);
            if (m_CollisionTimestep == 0) {
                m_TranslationalVelocity[0] = CalculateTranslationalVelocity(dt);
                m_RotationalVelocity[0] = CalculateAngularVelocity(dt);
            }
            else {
                m_TranslationalVelocity[0] = CalculateTranslationalVelocity(dt, m_CollisionTimestep);
                m_RotationalVelocity[0] = CalculateAngularVelocity(dt, m_CollisionTimestep);
            }
        }

        /// <summary>
        /// Calls the calculation of the hydrodynamics
        /// </summary>
        /// <param name="U"></param>
        /// <param name="P"></param>
        /// <param name="LsTrk"></param>
        /// <param name="fluidViscosity"></param>
        public virtual void UpdateForcesAndTorque(VectorField<SinglePhaseField> U, SinglePhaseField P, LevelSetTracker LsTrk, CellMask CutCells_P, double fluidViscosity, double fluidDensity, bool firstIteration, double dt = 0) {
            double[] tempForces = CalculateHydrodynamicForces(U, P, LsTrk, CutCells_P, fluidViscosity, fluidDensity);
            double tempTorque = CalculateHydrodynamicTorque(U, P, LsTrk, CutCells_P, fluidViscosity);
            HydrodynamicsPostprocessing(tempForces, tempTorque, firstIteration);
        }

        public virtual void PredictForceAndTorque(double activeStress, int TimestepInt) {
            if (TimestepInt == 1) {
                m_HydrodynamicForces[0][0] = MaxParticleLengthScale * Math.Cos(m_Angle[0]) * activeStress / 2 + Gravity[0] * Density * ParticleArea / 10;
                m_HydrodynamicForces[0][1] = MaxParticleLengthScale * Math.Sin(m_Angle[0]) * activeStress / 2 + Gravity[1] * Density * ParticleArea / 10;
                m_HydrodynamicTorque[0] = 0;
            }
            else {
                for (int d = 0; d < spatialDim; d++) {
                    m_HydrodynamicForces[0][d] = (m_HydrodynamicForces[1][d] + 4 * m_HydrodynamicForces[2][d] + m_HydrodynamicForces[3][d]) / 6;
                    if (Math.Abs(m_HydrodynamicForces[0][d]) < 1e-20)
                        m_HydrodynamicForces[0][d] = 0;
                }
                m_HydrodynamicTorque[0] = (m_HydrodynamicTorque[1] + 4 * m_HydrodynamicTorque[2] + m_HydrodynamicTorque[3]) / 6;
                if (Math.Abs(m_HydrodynamicTorque[0]) < 1e-20)
                    m_HydrodynamicTorque[0] = 0;
            }
            Aux.TestArithmeticException(m_HydrodynamicForces[0], "hydrodynamic forces");
            Aux.TestArithmeticException(m_HydrodynamicTorque[0], "hydrodynamic torque");
        }

        public virtual void CalculateDampingTensor(Particle particle, LevelSetTracker LsTrk, double muA, double rhoA, double dt) {
            throw new Exception("Added damping tensors should only be used if added damping is active.");
        }

        public virtual void UpdateDampingTensors() {
            throw new Exception("Added damping tensors should only be used if added damping is active.");
        }

        public virtual void ExchangeAddedDampingTensors() {
            throw new Exception("Added damping tensors should only be used if added damping is active.");
        }

        /// <summary>
        /// Calculate the new particle position
        /// </summary>
        /// <param name="dt"></param>
        protected virtual double[] CalculateParticlePosition(double dt) {
            double[] l_Position = new double[spatialDim];
            for (int d = 0; d < spatialDim; d++) {
                l_Position[d] = m_Position[1][d] + (m_TranslationalVelocity[0][d] + 4 * m_TranslationalVelocity[1][d] + m_TranslationalVelocity[2][d]) * dt / 6;
            }
            Aux.TestArithmeticException(l_Position, "particle position");
            return l_Position;
        }

        /// <summary>
        /// Calculate the new particle position
        /// </summary>
        /// <param name="dt"></param>
        protected virtual double[] CalculateParticlePosition(double dt, bool collisionProcedure) {
            double[] l_Position = new double[spatialDim];
            for (int d = 0; d < spatialDim; d++) {
                l_Position[d] = m_Position[0][d] + (m_TranslationalVelocity[0][d] + 4 * m_TranslationalVelocity[1][d] + m_TranslationalVelocity[2][d]) * dt / 6;
            }
            Aux.TestArithmeticException(l_Position, "particle position");
            return l_Position;
        }

        /// <summary>
        /// Calculate the new particle position
        /// </summary>
        /// <param name="dt"></param>
        /// <param name="collisionTimestep">The time consumed during the collision procedure</param>
        protected virtual double[] CalculateParticlePosition(double dt, double collisionTimestep) {
            double[] l_Position = new double[spatialDim];
            for (int d = 0; d < spatialDim; d++) {
                l_Position[d] = m_Position[0][d] + m_TranslationalVelocity[0][d] * (dt - collisionTimestep) / 6;
            }
            Aux.TestArithmeticException(l_Position, "particle position");
            return l_Position;
        }

        /// <summary>
        /// Calculate the new particle angle
        /// </summary>
        /// <param name="dt"></param>
        protected virtual double CalculateParticleAngle(double dt) {
            double l_Angle = m_Angle[1] + (m_RotationalVelocity[0] + 4 * m_RotationalVelocity[1] + m_RotationalVelocity[2]) * dt / 6;
            Aux.TestArithmeticException(l_Angle, "particle angle");
            return l_Angle;
        }

        /// <summary>
        /// Calculate the new particle angle
        /// </summary>
        /// <param name="dt"></param>
        protected virtual double CalculateParticleAngle(double dt, bool collisionProcedure) {
            double l_Angle = m_Angle[0] + (m_RotationalVelocity[0] + 4 * m_RotationalVelocity[1] + m_RotationalVelocity[2]) * dt / 6;
            Aux.TestArithmeticException(l_Angle, "particle angle");
            return l_Angle;
        }

        /// <summary>
        /// Calculate the new particle angle after a collision
        /// </summary>
        /// <param name="dt"></param>
        /// <param name="collisionTimestep">The time consumed during the collision procedure</param>
        protected virtual double CalculateParticleAngle(double dt, double collisionTimestep) {
            double l_Angle = m_Angle[1] + m_RotationalVelocity[0] * (dt - collisionTimestep) / 6;
            Aux.TestArithmeticException(l_Angle, "particle angle");
            return l_Angle;
        }

        /// <summary>
        /// Calculate the new translational velocity of the particle using a Crank Nicolson scheme.
        /// </summary>
        /// <param name="dt">Timestep</param>
        protected virtual double[] CalculateTranslationalVelocity(double dt) {
            double[] l_TranslationalVelocity = new double[spatialDim];
            for (int d = 0; d < spatialDim; d++) {
                l_TranslationalVelocity[d] = m_TranslationalVelocity[1][d] + (m_TranslationalAcceleration[0][d] + 4 * m_TranslationalAcceleration[1][d] + m_TranslationalAcceleration[2][d]) * dt / 6;
            }
            Aux.TestArithmeticException(l_TranslationalVelocity, "particle translational velocity");
            return l_TranslationalVelocity;
        }

        /// <summary>
        /// Calculate the new translational velocity of the particle using a Crank Nicolson scheme.
        /// </summary>
        /// <param name="dt">Timestep</param>
        /// <param name="collisionTimestep">The time consumed during the collision procedure</param>
        protected virtual double[] CalculateTranslationalVelocity(double dt, double collisionTimestep) {
            double[] l_TranslationalVelocity = new double[spatialDim];
            for (int d = 0; d < spatialDim; d++) {
                l_TranslationalVelocity[d] = m_TranslationalVelocity[1][d] + m_TranslationalAcceleration[0][d] * (dt - collisionTimestep) / 6;
            }
            Aux.TestArithmeticException(l_TranslationalVelocity, "particle translational velocity");
            return l_TranslationalVelocity;
        }

        /// <summary>
        /// Calculate the new angular velocity of the particle using explicit Euler scheme.
        /// </summary>
        /// <param name="dt">Timestep</param>
        /// <param name="collisionTimestep">The time consumed during the collision procedure</param>
        protected virtual double CalculateAngularVelocity(double dt) {
            double l_RotationalVelocity = m_RotationalVelocity[1] + (m_RotationalAcceleration[0] + 4 * m_RotationalAcceleration[1] + m_RotationalAcceleration[2]) * dt / 6;
            Aux.TestArithmeticException(l_RotationalVelocity, "particle rotational velocity");
            return l_RotationalVelocity;
        }

        /// <summary>
        /// Calculate the new angular velocity of the particle using explicit Euler scheme.
        /// </summary>
        /// <param name="dt">Timestep</param>
        /// <param name="collisionTimestep">The time consumed during the collision procedure</param>
        protected virtual double CalculateAngularVelocity(double dt, double collisionTimestep) {
            double l_RotationalVelocity = m_RotationalVelocity[1] + m_RotationalAcceleration[0] * (dt - collisionTimestep) / 6;
            Aux.TestArithmeticException(l_RotationalVelocity, "particle rotational velocity");
            return l_RotationalVelocity;
        }

        internal void CalculateNormalAndTangentialVelocity() {
            double[] normalVector = m_CollisionNormalVector.Last();
            double[] TangentialVector = new double[] { -normalVector[1], normalVector[0] };
            double[] Velocity = m_TranslationalVelocity[0];
            m_PreCollisionVelocity = new double[] { Velocity[0] * normalVector[0] + Velocity[1] * normalVector[1], Velocity[0] * TangentialVector[0] + Velocity[1] * TangentialVector[1] };
            Aux.TestArithmeticException(m_PreCollisionVelocity, "particle velocity before collision");
        }

        /// <summary>
        /// Calculate the new acceleration
        /// </summary>
        /// <param name="dt"></param>
        protected virtual double[] CalculateTranslationalAcceleration(double dt) {
            double[] l_Acceleration = new double[spatialDim];
            for (int d = 0; d < spatialDim; d++) {
                l_Acceleration[d] = m_HydrodynamicForces[0][d] / (Density * ParticleArea);
            }
            Aux.TestArithmeticException(l_Acceleration, "particle translational acceleration");
            return l_Acceleration;
        }

        /// <summary>
        /// Calculate the new acceleration (translational and rotational)
        /// </summary>
        /// <param name="dt"></param>
        protected virtual double CalculateRotationalAcceleration(double dt) {
            double l_Acceleration = m_HydrodynamicTorque[0] / MomentOfInertia;
            Aux.TestArithmeticException(l_Acceleration, "particle rotational acceleration");
            return l_Acceleration;
        }
                
        /// <summary>
        /// Update Forces and Torque acting from fluid onto the particle
        /// </summary>
        /// <param name="U"></param>
        /// <param name="P"></param>
        /// <param name="LsTrk"></param>
        /// <param name="muA"></param>
        protected virtual double[] CalculateHydrodynamicForces(VectorField<SinglePhaseField> U, SinglePhaseField P, LevelSetTracker LsTrk, CellMask CutCells_P, double muA, double fluidDensity, double dt = 0) {
            int RequiredOrder = U[0].Basis.Degree * 3 + 2;
            SinglePhaseField[] UA = U.ToArray();
            ConventionalDGField pA = P;
            double[] tempForces = ForcesIntegration(UA, pA, LsTrk, CutCells_P, RequiredOrder, muA);
            Force_MPISum(ref tempForces);
            for (int d = 0; d < spatialDim; d++) {
                tempForces[d] += (Density - fluidDensity) * ParticleArea * Gravity[d];
            }
            return tempForces;
        }

        protected double[] ForcesIntegration(SinglePhaseField[] UA, ConventionalDGField pA, LevelSetTracker LsTrk, CellMask CutCells_P, int RequiredOrder, double FluidViscosity) {
            double[] tempForces = new double[spatialDim];
            double[] IntegrationForces = tempForces.CloneAs();
            for (int d = 0; d < spatialDim; d++) {
                void ErrFunc(int CurrentCellID, int Length, NodeSet Ns, MultidimensionalArray result) {

                    int K = result.GetLength(1);
                    MultidimensionalArray Grad_UARes = MultidimensionalArray.Create(Length, K, spatialDim, spatialDim);
                    MultidimensionalArray pARes = MultidimensionalArray.Create(Length, K);
                    MultidimensionalArray Normals = LsTrk.DataHistories[0].Current.GetLevelSetNormals(Ns, CurrentCellID, Length);
                    for (int i = 0; i < spatialDim; i++) {
                        UA[i].EvaluateGradient(CurrentCellID, Length, Ns, Grad_UARes.ExtractSubArrayShallow(-1, -1, i, -1), 0, 1);
                    }
                    pA.Evaluate(CurrentCellID, Length, Ns, pARes);
                    for (int j = 0; j < Length; j++) {
                        for (int k = 0; k < K; k++) {
                            result[j, k] = ForceIntegration.CalculateStressTensor(Grad_UARes, pARes, Normals, FluidViscosity, k, j, spatialDim, d);
                        }
                    }
                }
                var SchemeHelper = LsTrk.GetXDGSpaceMetrics(new[] { LsTrk.GetSpeciesId("A") }, RequiredOrder, 1).XQuadSchemeHelper;
                CellQuadratureScheme cqs = SchemeHelper.GetLevelSetquadScheme(0, CutCells_P);
                CellQuadrature.GetQuadrature(new int[] { 1 }, LsTrk.GridDat, cqs.Compile(LsTrk.GridDat, RequiredOrder),
                    delegate (int i0, int Length, QuadRule QR, MultidimensionalArray EvalResult) {
                        ErrFunc(i0, Length, QR.Nodes, EvalResult.ExtractSubArrayShallow(-1, -1, 0));
                    },
                    delegate (int i0, int Length, MultidimensionalArray ResultsOfIntegration) {
                        IntegrationForces[d] = ForceTorqueSummationWithNeumaierArray(IntegrationForces[d], ResultsOfIntegration, Length);
                    }
                ).Execute();
            }
            return tempForces = IntegrationForces.CloneAs();
        }

        protected void Force_MPISum(ref double[] forces) {
            double[] stateBuffer = forces.CloneAs();
            double[] globalStateBuffer = stateBuffer.MPISum();
            for (int d = 0; d < spatialDim; d++) {
                forces[d] = globalStateBuffer[d];
            }
        }

        protected virtual double CalculateHydrodynamicTorque(VectorField<SinglePhaseField> U, SinglePhaseField P, LevelSetTracker LsTrk, CellMask CutCells_P, double muA, double dt = 0) {
            int RequiredOrder = U[0].Basis.Degree * 3 + 2;
            SinglePhaseField[] UA = U.ToArray();
            ConventionalDGField pA = P;
            double tempTorque = TorqueIntegration(UA, pA, LsTrk, CutCells_P, RequiredOrder, muA);
            Torque_MPISum(ref tempTorque);
            return tempTorque;
        }

        protected double TorqueIntegration(SinglePhaseField[] UA, ConventionalDGField pA, LevelSetTracker LsTrk, CellMask CutCells_P, int RequiredOrder, double FluidViscosity) {
            double tempTorque = new double();
            void ErrFunc2(int j0, int Len, NodeSet Ns, MultidimensionalArray result) {
                int K = result.GetLength(1);
                MultidimensionalArray Grad_UARes = MultidimensionalArray.Create(Len, K, spatialDim, spatialDim); ;
                MultidimensionalArray pARes = MultidimensionalArray.Create(Len, K);
                MultidimensionalArray Normals = LsTrk.DataHistories[0].Current.GetLevelSetNormals(Ns, j0, Len);
                for (int i = 0; i < spatialDim; i++) {
                    UA[i].EvaluateGradient(j0, Len, Ns, Grad_UARes.ExtractSubArrayShallow(-1, -1, i, -1), 0, 1);
                }
                pA.Evaluate(j0, Len, Ns, pARes);
                for (int j = 0; j < Len; j++) {
                    MultidimensionalArray Ns_Global = Ns.CloneAs();
                    LsTrk.GridDat.TransformLocal2Global(Ns, Ns_Global, j0 + j);
                    for (int k = 0; k < K; k++) {
                        result[j, k] = ForceIntegration.CalculateTorqueFromStressTensor2D(Grad_UARes, pARes, Normals, Ns_Global, FluidViscosity, k, j, m_Position[0]);
                    }
                }
            }
            var SchemeHelper2 = LsTrk.GetXDGSpaceMetrics(new[] { LsTrk.GetSpeciesId("A") }, RequiredOrder, 1).XQuadSchemeHelper;
            CellQuadratureScheme cqs2 = SchemeHelper2.GetLevelSetquadScheme(0, CutCells_P);
            CellQuadrature.GetQuadrature(new int[] { 1 }, LsTrk.GridDat, cqs2.Compile(LsTrk.GridDat, RequiredOrder),
                delegate (int i0, int Length, QuadRule QR, MultidimensionalArray EvalResult) {
                    ErrFunc2(i0, Length, QR.Nodes, EvalResult.ExtractSubArrayShallow(-1, -1, 0));
                },
                delegate (int i0, int Length, MultidimensionalArray ResultsOfIntegration) {
                    tempTorque = ForceTorqueSummationWithNeumaierArray(tempTorque, ResultsOfIntegration, Length);
                }
            ).Execute();
            return tempTorque;
        }

        protected void Torque_MPISum(ref double torque) {
            double stateBuffer = torque;
            double globalStateBuffer = stateBuffer.MPISum();
            torque = globalStateBuffer;
        }

        protected virtual void HydrodynamicsPostprocessing(double[] tempForces, double tempTorque, bool firstIteration) {
            if (m_UnderrelaxationParam != null && !firstIteration) {
                double forceAndTorqueConvergence = m_UnderrelaxationParam.hydroDynConvergenceLimit;
                double underrelaxationFactor = m_UnderrelaxationParam.underrelaxationFactor;
                bool useAddaptiveUnderrelaxation = m_UnderrelaxationParam.useAddaptiveUnderrelaxation;
                Underrelaxation.CalculateAverageForces(tempForces, tempTorque, MaxParticleLengthScale, out double averagedForces);
                Underrelaxation.Forces(ref tempForces, m_ForcesPreviousIter, forceAndTorqueConvergence, underrelaxationFactor, useAddaptiveUnderrelaxation, averagedForces);
                Underrelaxation.Torque(ref tempTorque, TorquePreviousIter, forceAndTorqueConvergence, underrelaxationFactor, useAddaptiveUnderrelaxation, averagedForces);
            }
            for (int d = 0; d < spatialDim; d++) {
                m_HydrodynamicForces[0][d] = tempForces[d];
            }
            m_HydrodynamicTorque[0] = tempTorque;
            Aux.TestArithmeticException(m_HydrodynamicForces[0], "hydrodynamic forces");
            Aux.TestArithmeticException(m_HydrodynamicTorque[0], "hydrodynamic torque");
        }

        /// <summary>
        /// This method performs the Neumaier algorithm form the sum of the entries of an array.
        /// </summary>
        /// <param name="ResultVariable">
        /// The variable where  the sum will be saved.
        /// </param>
        /// <param name="Summands">
        /// The array of summands
        /// </param>
        /// <param name="Length">
        /// The number of summands.
        /// </param>
        private double ForceTorqueSummationWithNeumaierArray(double ResultVariable, MultidimensionalArray Summands, double Length) {
            double sum = ResultVariable;
            double naiveSum;
            double c = 0.0;
            for (int i = 0; i < Length; i++) {
                naiveSum = sum + Summands[i, 0];
                if (Math.Abs(sum) >= Math.Abs(Summands[i, 0])) {
                    c += (sum - naiveSum) + Summands[i, 0];
                }
                else {
                    c += (Summands[i, 0] - naiveSum) + sum;
                }
                sum = naiveSum;
            }
            return sum + c;
        }

        /// <summary>
        /// Calculating the particle Reynolds number
        /// </summary>
        public double ComputeParticleRe(double ViscosityFluid) {
            return m_TranslationalVelocity[0].L2Norm() * MaxParticleLengthScale / ViscosityFluid;
        }

        /// <summary>
        /// Calculating the particle Stokes number
        /// </summary>
        public double ComputeParticleSt(double ViscosityFluid, double DensityFluid) {
            return ComputeParticleRe(ViscosityFluid) * Density / (9 * DensityFluid);
        }

        /// <summary>
        /// Calculating the particle momentum
        /// </summary>
        public double[] CalculateParticleMomentum() {
            double[] temp = new double[spatialDim + 1];
            for (int d = 0; d < spatialDim; d++) {
                temp[d] = Mass_P * m_TranslationalVelocity[0][d];
            }
            temp[spatialDim] = MomentOfInertia * m_RotationalVelocity[0];
            return temp;
        }

        /// <summary>
        /// Calculating the particle kinetic energy
        /// </summary>
        public double[] CalculateParticleKineticEnergy() {
            double[] temp = new double[spatialDim + 1];
            for (int d = 0; d < spatialDim; d++) {
                temp[d] = 0.5 * Mass_P * m_TranslationalVelocity[0][d].Pow2();
            }
            temp[spatialDim] = 0.5 * MomentOfInertia * m_RotationalVelocity[0].Pow2();
            return temp;
        }

        /// <summary>
        /// Deletes the complete history of the translational velocity
        /// </summary>
        public void ClearParticleHistoryTranslation() {
            for (int i = 0; i < m_TranslationalVelocity.Count; i++) {
                for (int d = 0; d < spatialDim; d++) {
                    m_TranslationalVelocity[i][d] = 0;
                    m_TranslationalAcceleration[i][d] = 0;
                }
            }
        }

        /// <summary>
        /// Deletes the complete history of the rotational velocity
        /// </summary>
        public void ClearParticleHistoryRotational() {
            for (int i = 0; i < m_RotationalVelocity.Count; i++) {
                m_RotationalAcceleration[i] = 0;
                m_RotationalVelocity[i] = 0;
            }
        }

        public void SetCollisionVectors(double[] normalVector, double[] tangentialVector) {
            m_CollisionNormalVector.Add(normalVector);
            m_CollisionTangentialVector.Add(tangentialVector);
        }

        public void SetCollisionVelocities(double normalVelocity, double tangentialVelocity, double rotationalVelocity) {
            m_CollisionTranslationalVelocity.Add(new double[] { normalVelocity, tangentialVelocity });
            m_CollisionRotationalVelocity.Add(rotationalVelocity);
        }

        /// <summary>
        /// Collision post-processing. Sums up the results of the multiple binary collisions of one timestep
        /// </summary>
        public void PostProcessCollisionTranslation() {
            if (m_CollisionTranslationalVelocity.Count >= 1) {
                double[] Normal = new double[spatialDim];
                double[] Tangential = new double[spatialDim];
                for (int t = 0; t < m_CollisionTranslationalVelocity.Count; t++) {
                    for (int d = 0; d < spatialDim; d++) {
                        Normal[d] += m_CollisionNormalVector[t][d];
                        Tangential[d] += m_CollisionTangentialVector[t][d];
                    }
                }

                Normal.ScaleV(1 / Math.Sqrt(Normal[0].Pow2() + Normal[1].Pow2()));
                Tangential.ScaleV(1 / Math.Sqrt(Tangential[0].Pow2() + Tangential[1].Pow2()));
                double temp_NormalVel = 0;
                double temp_TangentialVel = 0;
                for (int t = 0; t < m_CollisionTranslationalVelocity.Count; t++) {
                    double cos = new double();
                    for (int d = 0; d < spatialDim; d++) {
                        cos += Normal[d] * m_CollisionNormalVector[t][d];
                    }
                    double sin = cos == 1 ? 0 : m_CollisionNormalVector[t][0] > Normal[0] ? Math.Sqrt(1 + 1e-15 - cos.Pow2()) : -Math.Sqrt(1 + 1e-15 - cos.Pow2());
                    temp_NormalVel += m_CollisionTranslationalVelocity[t][0] * cos - m_CollisionTranslationalVelocity[t][1] * sin;
                    temp_TangentialVel += m_CollisionTranslationalVelocity[t][0] * sin + m_CollisionTranslationalVelocity[t][1] * cos;

                }
                temp_NormalVel /= m_CollisionTranslationalVelocity.Count;
                temp_TangentialVel /=m_CollisionTranslationalVelocity.Count;

                ClearParticleHistoryTranslation();
                for (int d = 0; d < spatialDim; d++) {
                    m_TranslationalVelocity[0][d] = Normal[d] * temp_NormalVel + Tangential[d] * temp_TangentialVel;
                }
                m_CollisionTranslationalVelocity.Clear();
                m_CollisionNormalVector.Clear();
                m_CollisionTangentialVector.Clear();
            }
        }

        /// <summary>
        /// Collision post-processing. Sums up the results for the angular velocity of the multiple binary collisions of one timestep
        /// </summary>
        public void PostProcessCollisionRotation() {
            if (m_CollisionRotationalVelocity.Count >= 1) {
                ClearParticleHistoryRotational();
                m_RotationalVelocity[0] = m_CollisionRotationalVelocity.Sum() / m_CollisionRotationalVelocity.Count;
                m_CollisionRotationalVelocity.Clear();
                if (double.IsNaN(m_RotationalVelocity[0]) || double.IsInfinity(m_RotationalVelocity[0]))
                    throw new ArithmeticException("Error trying to update particle angular velocity during collision post-processing. The angular velocity is:  " + m_RotationalVelocity[0]);
            }
        }

        public double[] BuildSendArray() {
            double[] dataSend = new double[13];
            dataSend[0] = m_RotationalVelocity[0];
            dataSend[1] = m_TranslationalVelocity[0][0];
            dataSend[2] = m_TranslationalVelocity[0][1];
            dataSend[3] = m_Angle[0];
            dataSend[4] = m_Position[0][0];
            dataSend[5] = m_Position[0][1];
            dataSend[6] = m_CollisionTimestep;
            dataSend[7] = m_RotationalVelocity[1];
            dataSend[8] = m_TranslationalVelocity[1][0];
            dataSend[9] = m_TranslationalVelocity[1][1];
            dataSend[10] = m_Angle[1];
            dataSend[11] = m_Position[1][0];
            dataSend[12] = m_Position[1][1];
            return dataSend;
        }

        public void WriteReceiveArray(double[] dataReceive, int Offset) {
            m_RotationalVelocity[0] = dataReceive[0 + Offset];
            m_TranslationalVelocity[0][0] = dataReceive[1 + Offset];
            m_TranslationalVelocity[0][1] = dataReceive[2 + Offset];
            m_Angle[0] = dataReceive[3 + Offset];
            m_Position[0][0] = dataReceive[4 + Offset];
            m_Position[0][1] = dataReceive[5 + Offset];
            m_CollisionTimestep = dataReceive[6 + Offset];
            m_RotationalVelocity[1] = dataReceive[7 + Offset];
            m_TranslationalVelocity[1][0] = dataReceive[8 + Offset];
            m_TranslationalVelocity[1][1] = dataReceive[9 + Offset];
            m_Angle[1] = dataReceive[10 + Offset];
            m_Position[1][0] = dataReceive[11 + Offset];
            m_Position[1][1] = dataReceive[12 + Offset];
        }
    }
}
