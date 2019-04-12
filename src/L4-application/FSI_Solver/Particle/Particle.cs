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
using System.Runtime.Serialization;
using BoSSS.Solution.Control;
using System.Runtime.InteropServices;
using BoSSS.Foundation.XDG;
using BoSSS.Foundation.Quadrature;
using ilPSP;
using ilPSP.Utils;
using BoSSS.Foundation;
using BoSSS.Foundation.Grid;
using System.Diagnostics;
using BoSSS.Foundation.Grid.Classic;
using BoSSS.Foundation.Grid.RefElements;

namespace BoSSS.Application.FSI_Solver
{

    /// <summary>
    /// Particle properties (for disk shape and spherical particles only).
    /// </summary>
    [DataContract]
    [Serializable]
    abstract public class Particle : ICloneable {

        /// <summary>
        /// Empty constructor used during de-serialization
        /// </summary>
        protected Particle()
        {
            // noop
        }
        
        public Particle(int Dim, double[] startPos = null, double startAngl = 0.0) {
            
            SpatialDim = Dim;

            // Particle history
            // =============================   
            for (int i = 0; i < m_HistoryLength; i++) {
                Position.Add(new double[Dim]);
                Angle.Add(new double());
                TranslationalVelocity.Add(new double[Dim]);
                TranslationalAcceleration.Add(new double[Dim]);
                RotationalVelocity.Add(new double());
                RotationalAcceleration.Add(new double());
                HydrodynamicForces.Add(new double[Dim]);
                HydrodynamicTorque.Add(new double());
            }

            #region Initial values
            // ============================= 
            if (startPos == null) {
                startPos = new double[Dim];
            }
            Position[0] = startPos;
            Position[1] = startPos;
            //From degree to radiant
            Angle[0] = StartingAngle = startAngl * 2 * Math.PI / 360;
            Angle[1] = startAngl * 2 * Math.PI / 360;

            //UpdateLevelSetFunction();
            #endregion
        }


        #region Collision parameters
        /// <summary>
        /// Check whether any particles is collided with another particle
        /// </summary>
        public bool[] m_collidedWithParticle;

        /// <summary>
        /// Check whether any particles is collided with the wall
        /// </summary>
        public bool[] m_collidedWithWall;

        public double[][] m_closeInterfacePointTo;

        /// <summary>
        /// Skip calculation of hydrodynamic force and Torque if particles are too close
        /// </summary>
        [DataMember]
        public bool skipForceIntegration = false;
        #endregion

        #region Iteration parameters
        /// <summary>
        /// Number of iterations
        /// </summary>
        [DataMember]
        public int iteration_counter_P = 0;

        /// <summary>
        /// Constant Forces and Torque underrelaxation?
        /// </summary>
        [DataMember]
        public bool AddaptiveUnderrelaxation = false;

        /// <summary>
        /// Defines the order of the underrelaxation factor
        /// </summary>
        [DataMember]
        public int underrelaxationFT_exponent = 0;

        /// <summary>
        /// Underrelaxation factor
        /// </summary>
        [DataMember]
        public double underrelaxation_factor = 1;

        /// <summary>
        /// Set true if you want to delete all values of the Forces anf Torque smaller than convergenceCriterion*1e-2
        /// </summary>
        [DataMember]
        public bool ClearSmallValues = false;
        #endregion

        #region Misc parameters

        /// <summary>
        /// Colored cells of this particle. 0: CellID, 1: Color
        /// </summary>
        public List<int[]> ParticleColoredCells = new List<int[]>();

        /// <summary>
        /// Length of history for time, velocity, position etc.
        /// </summary>
        readonly int m_HistoryLength = 4;
        #endregion

        #region Added dampig parameters
        /// <summary>
        /// Set false if you want to include the effects of added damping
        /// </summary>
        [DataMember]
        public bool neglectAddedDamping = true;

        /// <summary>
        /// Complete added damping tensor, for reference: Banks et.al. 2017
        /// </summary>
        [DataMember]
        public double[,] AddedDampingTensor = new double[6, 6];

        /// <summary>
        /// Scaling parameter for added damping.
        /// </summary>
        private readonly double beta = 1;
        #endregion

        #region Geometric parameters
        /// <summary>
        /// Spatial Dimension of the particle 
        /// </summary>
        [DataMember]
        private readonly int SpatialDim;
        
        /// <summary>
        /// some length scale 
        /// </summary>
        abstract protected double AverageDistance { get; }
        #endregion

        #region Virtual force model parameter
        ///// <summary>
        ///// needed for second velocity model
        ///// </summary>
        //public double C_v = 0.5;

        ///// <summary>
        ///// needed for second velocity model, obsolete?
        ///// </summary>
        //public double velResidual_ConvergenceCriterion = 1e-6;

        ///// <summary>
        ///// needed for second velocity model, obsolete?
        ///// </summary>
        //public double MaxParticleVelIterations = 10000;

        //private int vel_iteration_counter;
        #endregion

        /// <summary>
        /// Density of the particle.
        /// </summary>
        [DataMember]
        public double particleDensity = 1;
        
        /// <summary>
        /// The position (center of mass) of the particle in the current time step.
        /// </summary>
        [DataMember]
        public List<double[]> Position = new List<double[]>();
        
        /// <summary>
        /// The angle (center of mass) of the particle in the current time step.
        /// </summary>
        [DataMember]
        public List<double> Angle = new List<double>();

        ///// <summary>
        /// The angle (center of mass) of the particle at the starting point.
        /// </summary>
        [DataMember]
        private readonly double StartingAngle = new double();
        
        /// <summary>
        /// The translational velocity of the particle in the current time step.
        /// </summary>
        [DataMember]
        public List<double[]> TranslationalVelocity = new List<double[]>();
        
        /// <summary>
        /// The angular velocity of the particle in the current time step.
        /// </summary>
        [DataMember]
        public List<double> RotationalVelocity = new List<double>();
        
        /// <summary>
        /// The translational velocity of the particle in the current time step.
        /// </summary>
        [DataMember]
        public List<double[]> TranslationalAcceleration = new List<double[]>();
        
        /// <summary>
        /// The angular velocity of the particle in the current time step.
        /// </summary>
        [DataMember]
        public List<double> RotationalAcceleration = new List<double>();
        
        /// <summary>
        /// The force acting on the particle in the current time step.
        /// </summary>
        [DataMember]
        public List<double[]> HydrodynamicForces = new List<double[]>();
        
        /// <summary>
        /// The Torque acting on the particle in the current time step.
        /// </summary>
        [DataMember]
        public List<double> HydrodynamicTorque = new List<double>();

        /// <summary>
        /// Level set function describing the particle.
        /// </summary>       
        public abstract double Phi_P(double[] X);

        /// <summary>
        /// Sets the gravity in vertical direction, default is 0.0
        /// </summary>
        [DataMember]
        public double GravityVertical = 0.0;

        /// <summary>
        /// Set true if the particle should be an active particle, i.e. self driven
        /// </summary>
        [DataMember]
        public bool ActiveParticle = false;

        /// <summary>
        /// Convergence criterion for the calculation of the Forces and Torque
        /// </summary>
        [DataMember]
        public double ForceAndTorque_convergence = 1e-8;

        /// <summary>
        /// Active stress on the current particle.
        /// </summary>
        public double ActiveStress = 0;
        
        /// <summary>
        /// Area of the current particle.
        /// </summary>
        abstract protected double Area_P {
            get;
        }

        /// <summary>
        /// Mass of the current particle.
        /// </summary>
        public double Mass_P {
            get {
                double a = Area_P;
                if (a <= 0.0 || double.IsNaN(a) || double.IsInfinity(a))
                    throw new ArithmeticException("Particle volume/area is " + a);
                return Area_P * particleDensity;
            }
        }

        /// <summary>
        /// Circumference of the current particle.
        /// </summary>
        abstract protected double Circumference_P {
            get;
        }

        /// <summary>
        /// Moment of inertia of the current particle.
        /// </summary>
        abstract public double MomentOfInertia_P {
            get;
        }

        [NonSerialized]
        private ParticleAuxillary Aux = new ParticleAuxillary();
        [NonSerialized]
        private ParticleForceIntegration ForceIntegration = new ParticleForceIntegration();
        [NonSerialized]
        private ParticleAddedDamping AddedDamping = new ParticleAddedDamping();
        [NonSerialized]
        private ParticleUnderrelaxation Underrelaxation = new ParticleUnderrelaxation();
        [NonSerialized]
        private ParticleAcceleration Acceleration = new ParticleAcceleration();
        
        /// <summary>
        /// Calculate the new particle position
        /// </summary>
        /// <param name="dt"></param>
        public void CalculateParticlePosition(double dt, double rho_Fluid)
        {
            if (iteration_counter_P == 0)
            {
                Aux.SaveMultidimValueOfLastTimestep(Position);
            }

            if (SpatialDim != 2 && SpatialDim != 3)
                throw new NotSupportedException("Unknown particle dimension: SpatialDim = " + SpatialDim);

            for (int d = 0; d < SpatialDim; d++)
            {
                Position[0][d] = Position[1][d] + TranslationalVelocity[1][d] * dt + (TranslationalAcceleration[1][d] + TranslationalAcceleration[0][d]) * dt.Pow2() / 4;
                if (double.IsNaN(Position[0][d]) || double.IsInfinity(Position[0][d]))
                    throw new ArithmeticException("Error trying to update particle position. Value:  " + Position[0][d]);
            }
        }

        /// <summary>
        /// Calculate the new particle angle
        /// </summary>
        /// <param name="dt"></param>
        public void CalculateParticleAngle(double dt)
        {
            if (iteration_counter_P == 0)
            {
                Aux.SaveValueOfLastTimestep(Angle);
            }

            Angle[0] = Angle[1] + RotationalVelocity[1] * dt + dt.Pow2() * (RotationalAcceleration[1] + RotationalAcceleration[0]) / 4;
            if (double.IsNaN(Angle[0]) || double.IsInfinity(Angle[0]))
                throw new ArithmeticException("Error trying to update particle angle. Value:  " + Angle[0]);
        }

        /// <summary>
        /// Calculate the new acceleration (translational and rotational)
        /// </summary>
        /// <param name="dt"></param>
        public void PredictAcceleration()
        {
            if (iteration_counter_P == 0)
            {
                Aux.SaveMultidimValueOfLastTimestep(TranslationalAcceleration);
                Aux.SaveValueOfLastTimestep(RotationalAcceleration);
                Aux.SaveMultidimValueOfLastTimestep(HydrodynamicForces);
                Aux.SaveValueOfLastTimestep(HydrodynamicTorque);
            }
            for (int d = 0; d < SpatialDim; d++)
            {
                TranslationalAcceleration[0][d] = 2 * TranslationalAcceleration[1][d] - TranslationalAcceleration[2][d];
                
                HydrodynamicForces[0][d] = 2 * HydrodynamicForces[1][d] - HydrodynamicForces[2][d];
                if (Math.Abs(TranslationalAcceleration[0][d]) < 1e-20 || double.IsNaN(TranslationalAcceleration[0][d]))
                    TranslationalAcceleration[0][d] = 0;
                Console.WriteLine("Translational Acceleration[" + d + "] is:    " + TranslationalAcceleration[0][d]);
            }

            RotationalAcceleration[0] = 2 * RotationalAcceleration[1] - RotationalAcceleration[2];
            
            HydrodynamicTorque[0] = 2 * HydrodynamicTorque[1] - HydrodynamicTorque[2];
            if (Math.Abs(RotationalAcceleration[0]) < 1e-20 || double.IsNaN(RotationalAcceleration[0]))
                RotationalAcceleration[0] = 0;
            Console.WriteLine("Rotational Acceleration is:    " + RotationalAcceleration[0]);
        }

        /// <summary>
        /// Calculate the new acceleration (translational and rotational)
        /// </summary>
        /// <param name="dt"></param>
        public void PredictAccelerationWithinIteration()
        {
            for (int d = 0; d < SpatialDim; d++)
            {
                TranslationalAcceleration[0][d] = 2 * TranslationalAcceleration[0][d] - TranslationalAcceleration[1][d];
                HydrodynamicForces[0][d] = 2 * HydrodynamicForces[1][d] - HydrodynamicForces[2][d];
            }

            RotationalAcceleration[0] = 2 * RotationalAcceleration[0] - RotationalAcceleration[1];
            HydrodynamicTorque[0] = 2 * HydrodynamicTorque[1] - HydrodynamicTorque[2];
        }

        /// <summary>
        /// Calculate the new acceleration (translational and rotational)
        /// </summary>
        /// <param name="dt"></param>
        public void CalculateAcceleration(double dt, double fluidDensity, double addedDampingCoeff = 1)
        {
            if (iteration_counter_P == 0)
            {
                Aux.SaveMultidimValueOfLastTimestep(TranslationalAcceleration);
                Aux.SaveValueOfLastTimestep(RotationalAcceleration);
            }
            
            double[,] CoefficientMatrix = Acceleration.CalculateCoefficients(AddedDampingTensor, Mass_P, MomentOfInertia_P, dt);
            double Denominator = Acceleration.CalculateDenominator(CoefficientMatrix);

            TranslationalAcceleration[0] = Acceleration.Translational(CoefficientMatrix, Denominator, HydrodynamicForces[0], HydrodynamicTorque[0]);
            for (int d = 0; d< SpatialDim; d++)
            {
                Console.WriteLine("Translational Acceleration[" + d + "] is:    " + TranslationalAcceleration[0][d]);
                if (Math.Abs(TranslationalAcceleration[0][d]) < 1e-20 || double.IsNaN(TranslationalAcceleration[0][d]))
                    TranslationalAcceleration[0][d] = 0;
            }

            RotationalAcceleration[0] = Acceleration.Rotational(CoefficientMatrix, Denominator, HydrodynamicForces[0], HydrodynamicTorque[0]);
            Console.WriteLine("Rotational Acceleration is:    " + RotationalAcceleration[0]);
            if (Math.Abs(RotationalAcceleration[0]) < 1e-20 || double.IsNaN(RotationalAcceleration[0]))
                RotationalAcceleration[0] = 0;
        }

        /// <summary>
        /// Calculate the new translational velocity of the particle using a Crank Nicolson scheme.
        /// </summary>
        /// <param name="dt">Timestep</param>
        /// <returns></returns>
        public void CalculateTranslationalVelocity(double dt, double FluidDensity, bool includeTranslation)
        {
            if (iteration_counter_P == 0)
            {
                Aux.SaveMultidimValueOfLastTimestep(TranslationalVelocity);
            }

            if (includeTranslation == false)
            {
                for (int d = 0; d < SpatialDim; d++)
                {
                    TranslationalVelocity[0][d] = 0;
                }
            }
            
            for (int d = 0; d < SpatialDim; d++)
            {
                TranslationalVelocity[0][d] = TranslationalVelocity[1][d] + (TranslationalAcceleration[1][d] + TranslationalAcceleration[0][d]) * dt / 2;
                if (double.IsNaN(TranslationalVelocity[0][d]) || double.IsInfinity(TranslationalVelocity[0][d]))
                    throw new ArithmeticException("Error trying to calculate particle velocity Value:  " + TranslationalVelocity[0][d]);
            }
        }

        public void VirtualForceModel(double dt, double fluidDensity)
        {


            double[] temp = new double[2];
            double[] old_temp = new double[2];
            double[] tempForces = new double[2];
            double massDifference = (particleDensity - fluidDensity) * (Area_P);

            #region virtual force model
            // Virtual force model (Schwarz et al. - 2015 A temporal discretization scheme to compute the motion of light particles in viscous flows by an immersed boundary")
            // =============================
            double[] f_vTemp = new double[2];
            double[] f_vNew = new double[2];
            double[] f_vOld = new double[2];
            double[] k_1 = new double[2];
            double[] k_2 = new double[2];
            double[] k_3 = new double[2];
            double[] C_v_mod = new double[2];
            double[] tempForceNew = new double[2];
            double[] tempForceOld = new double[2];
            //C_v_mod[0] = C_v;
            //C_v_mod[1] = C_v;
            double[] c_a = new double[2];
            double[] c_u = new double[2];
            //double vel_iteration_counter = 0;
            double[] test = new double[2];
            // 2nd order Adam Bashford
            //for (int i = 0; i < 2; i++)
            //{
            //    dt = 1e-3;
            //    C_v_mod[i] = 0.1;
            //    c_a[i] = (C_v_mod[i] * FluidDensity) / (particleDensity + C_v_mod[i] * FluidDensity);
            //    c_u[i] = 1 / (Area_P * (particleDensity + C_v_mod[i] * particleDensity));
            //    f_vNew[i] = c_a[i] * (3 * TranslationalVelocity[0][i] - 4 * TranslationalVelocity[1][i] + TranslationalVelocity[2][i]) / (2 * dt);
            //    f_vOld[i] = c_a[i] * (3 * TranslationalVelocity[1][i] - 4 * TranslationalVelocity[2][i] + TranslationalVelocity[3][i]) / (2 * dt);
            //    tempForceNew[i] = (HydrodynamicForces[0][i] + massDifference * gravity[i]) * (c_u[i]) + f_vNew[i];
            //    tempForceOld[i] = (HydrodynamicForces[1][i] + massDifference * gravity[i]) * (c_u[i]) + f_vOld[i];
            //    temp[i] = TranslationalVelocity[0][i] + (3 * tempForceNew[i] - tempForceOld[i]) * dt / 2;
            //}

            // implicit Adams Moulton (modified)
            //for (double velResidual = 1; velResidual > velResidual_ConvergenceCriterion;)
            //{
            //    dt = 1e-3;
            //    for (int i = 0; i < 2; i++)
            //    {
            //        gravity[0] = 0;
            //        if (includeGravity == true)
            //        {
            //            gravity[1] = -9.81;
            //        }
            //        C_v_mod[i] = 300;// * Math.Abs(forces_P[0][i] / (forces_P[0][i] + forces_P[1][i] + 1e-30));
            //        c_a[i] = (C_v_mod[i] * FluidDensity) / (particleDensity + C_v_mod[i] * FluidDensity);
            //        c_u[i] = 1 / (Area_P * (particleDensity + C_v_mod[i] * particleDensity));
            //        f_vTemp[i] = (C_v_mod[i]) / (1 + C_v_mod[i]) * (11 * temp[i] - 18 * TranslationalVelocity[0][i] + 9 * TranslationalVelocity[1][i] - 2 * TranslationalVelocity[2][i]) / (8 * dt);
            //        f_vNew[i] = (C_v_mod[i]) / (1 + C_v_mod[i]) * (11 * TranslationalVelocity[0][i] - 18 * TranslationalVelocity[1][i] + 9 * TranslationalVelocity[2][i] - 2 * TranslationalVelocity[3][i]) / (6 * dt);
            //        f_vOld[i] = (C_v_mod[i]) / (1 + C_v_mod[i]) * (11 * TranslationalVelocity[1][i] - 18 * TranslationalVelocity[2][i] + 9 * TranslationalVelocity[3][i] - 2 * TranslationalVelocity[4][i]) / (6 * dt);
            //        tempForces[i] = (HydrodynamicForces[0][i] + massDifference * gravity[i]) * (c_u[i]) + f_vTemp[i];
            //        tempForceNew[i] = (HydrodynamicForces[1][i] + massDifference * gravity[i]) * (c_u[i]) + f_vNew[i];
            //        tempForceOld[i] = (HydrodynamicForces[2][i] + massDifference * gravity[i]) * (c_u[i]) + f_vOld[i];
            //        old_temp[i] = temp[i];
            //        temp[i] = previous_vel[i] + (1 * tempForces[i] + 4 * tempForceNew[i] + 1 * tempForceOld[i]) * dt / 6;
            //    }
            //    vel_iteration_counter += 1;
            //    if (vel_iteration_counter == MaxParticleVelIterations)
            //    {
            //        throw new ApplicationException("no convergence in particle velocity calculation");
            //    }
            //    velResidual = Math.Sqrt((temp[0] - old_temp[0]).Pow2() + (temp[1] - old_temp[1]).Pow2());

            //    Console.WriteLine("Current velResidual:  " + velResidual);
            //}
            //Console.WriteLine("Number of Iterations for translational velocity calculation:  " + vel_iteration_counter);
            //Console.WriteLine("C_v_mod:  " + C_v_mod[0]);
            #endregion
        }//unused

        /// <summary>
        /// Calculate the new angular velocity of the particle using explicit Euler scheme.
        /// </summary>
        /// <param name="dt">Timestep</param>
        /// <returns></returns>
        public void CalculateAngularVelocity(double dt, bool includeRotation, int noOfSubtimesteps = 1)
        {
            if (iteration_counter_P == 0)
            {
                Aux.SaveValueOfLastTimestep(RotationalVelocity);
            }
            
            if (includeRotation == false)
            {
                RotationalVelocity[0] = 0;
                return;
            }
            
            RotationalVelocity[0] = RotationalVelocity[1] + dt * (RotationalAcceleration[1] + RotationalAcceleration[0]) / 2;
            if (double.IsNaN(RotationalVelocity[0]) || double.IsInfinity(RotationalVelocity[0]))
                throw new ArithmeticException("Error trying to calculate particle angluar velocity. Value:  " + RotationalVelocity[0]);
        }
        
        /// <summary>
        /// clone, not implemented
        /// </summary>
        virtual public object Clone() {
            throw new NotImplementedException("Currently cloning of a particle is not available");
        }

        /// <summary>
        /// Calculate tensors to implement the added damping model (Banks et.al. 2017)
        /// </summary>
        public void CalculateDampingTensor(LevelSetTracker LsTrk, double muA, double rhoA, double dt)
        {
            AddedDampingTensor = AddedDamping.IntegrationOverLevelSet(LsTrk, muA, rhoA, dt, Position[0], CutCells_P(LsTrk));
        }

        /// <summary>
        /// Update in every timestep tensors to implement the added damping model (Banks et.al. 2017)
        /// </summary>
        public void UpdateDampingTensors()
        {
            AddedDampingTensor = AddedDamping.RotateTensor(Angle[0], StartingAngle, AddedDampingTensor);
        }
        
        /// <summary>
        /// Update Forces and Torque acting from fluid onto the particle
        /// </summary>
        /// <param name="U"></param>
        /// <param name="P"></param>
        /// <param name="LsTrk"></param>
        /// <param name="muA"></param>
        public void UpdateForcesAndTorque(VectorField<SinglePhaseField> U, SinglePhaseField P, LevelSetTracker LsTrk, double muA, double dt, double fluidDensity) {

            if (skipForceIntegration) {
                skipForceIntegration = false;
                return;
            }

            if (iteration_counter_P == 0)
            {
                Aux.SaveMultidimValueOfLastTimestep(HydrodynamicForces);
                Aux.SaveValueOfLastTimestep(HydrodynamicTorque);
            }

            int RequiredOrder = U[0].Basis.Degree * 3 + 2;
            Console.WriteLine("Forces coeff: {0}, order = {1}", LsTrk.CutCellQuadratureType, RequiredOrder);
            double[] Forces = new double[SpatialDim];
            SinglePhaseField[] UA = U.ToArray();
            ConventionalDGField pA = null;
            pA = P;
            for (int d = 0; d < SpatialDim; d++)
            {
                void ErrFunc(int CurrentCellID, int Length, NodeSet Ns, MultidimensionalArray result)
                {
                    
                    int NumberOfNodes = result.GetLength(1);
                    MultidimensionalArray Grad_UARes = MultidimensionalArray.Create(Length, NumberOfNodes, SpatialDim, SpatialDim);
                    MultidimensionalArray pARes = MultidimensionalArray.Create(Length, NumberOfNodes);
                    var Normals = LsTrk.DataHistories[0].Current.GetLevelSetNormals(Ns, CurrentCellID, Length);
                    for (int i = 0; i < SpatialDim; i++) {
                        UA[i].EvaluateGradient(CurrentCellID, Length, Ns, Grad_UARes.ExtractSubArrayShallow(-1, -1, i, -1), 0, 1);
                    }
                    pA.Evaluate(CurrentCellID, Length, Ns, pARes);
                    for (int j = 0; j < Length; j++) {
                        for (int k = 0; k < NumberOfNodes; k++) {
                            result[j, k] = ForceIntegration.CalculateStressTensor(Grad_UARes, pARes, Normals, muA, k, j, this.SpatialDim, d);
                        }
                    }
                }
                var SchemeHelper = LsTrk.GetXDGSpaceMetrics(new[] { LsTrk.GetSpeciesId("A") }, RequiredOrder, 1).XQuadSchemeHelper;
                CellQuadratureScheme cqs = SchemeHelper.GetLevelSetquadScheme(0, CutCells_P(LsTrk));
                CellQuadrature.GetQuadrature(new int[] { 1 }, LsTrk.GridDat,
                    cqs.Compile(LsTrk.GridDat, RequiredOrder),
                    delegate (int i0, int Length, QuadRule QR, MultidimensionalArray EvalResult) {
                        ErrFunc(i0, Length, QR.Nodes, EvalResult.ExtractSubArrayShallow(-1, -1, 0));
                    },
                    delegate (int i0, int Length, MultidimensionalArray ResultsOfIntegration) {
                        Forces[d] = ParticleAuxillary.ForceTorqueSummationWithNeumaierArray(Forces[d], ResultsOfIntegration, Length);
                    }
                ).Execute();
            }

            double Torque = 0;
            void ErrFunc2(int j0, int Len, NodeSet Ns, MultidimensionalArray result) {
                int K = result.GetLength(1); // No nof Nodes
                MultidimensionalArray Grad_UARes = MultidimensionalArray.Create(Len, K, SpatialDim, SpatialDim); ;
                MultidimensionalArray pARes = MultidimensionalArray.Create(Len, K);
                // Evaluate tangential velocity to level-set surface
                var Normals = LsTrk.DataHistories[0].Current.GetLevelSetNormals(Ns, j0, Len);
                for (int i = 0; i < SpatialDim; i++) {
                    UA[i].EvaluateGradient(j0, Len, Ns, Grad_UARes.ExtractSubArrayShallow(-1, -1, i, -1), 0, 1);
                }
                pA.Evaluate(j0, Len, Ns, pARes);
                for (int j = 0; j < Len; j++) {
                    MultidimensionalArray tempArray = Ns.CloneAs();
                    LsTrk.GridDat.TransformLocal2Global(Ns, tempArray, j0 + j);
                    for (int k = 0; k < K; k++) {
                        result[j, k] = ForceIntegration.CalculateTorqueFromStressTensor2D(Grad_UARes, pARes, Normals, tempArray, muA, k, j, Position[0]);
                    }
                }
            }
            var SchemeHelper2 = LsTrk.GetXDGSpaceMetrics(new[] { LsTrk.GetSpeciesId("A") }, RequiredOrder, 1).XQuadSchemeHelper;
            CellQuadratureScheme cqs2 = SchemeHelper2.GetLevelSetquadScheme(0, this.CutCells_P(LsTrk));
            CellQuadrature.GetQuadrature(new int[] { 1 }, LsTrk.GridDat,
                cqs2.Compile(LsTrk.GridDat, RequiredOrder),
                delegate (int i0, int Length, QuadRule QR, MultidimensionalArray EvalResult) {
                    ErrFunc2(i0, Length, QR.Nodes, EvalResult.ExtractSubArrayShallow(-1, -1, 0));
                },
                delegate (int i0, int Length, MultidimensionalArray ResultsOfIntegration) {
                    Torque = ParticleAuxillary.ForceTorqueSummationWithNeumaierArray(Torque, ResultsOfIntegration, Length);
                }
            ).Execute();

            // add gravity
            {
                Forces[1] += (particleDensity - fluidDensity) * Area_P * GravityVertical;
            }


            if (Math.Abs(Forces[0]) < 1e-20 || double.IsNaN(Forces[0]))
                Forces[0] = 0;
            if (Math.Abs(Forces[1]) < 1e-20 || double.IsNaN(Forces[1]))
                Forces[1] = 0;
            if (Math.Abs(Torque) < 1e-20 || double.IsNaN(Torque))
                Torque = 0;
            //if (double.IsNaN(Forces[0]) || double.IsInfinity(Forces[0]))
            //    throw new ArithmeticException("Error trying to calculate hydrodynamic forces (x). Value:  " + Forces[0]);
            //if (double.IsNaN(Forces[1]) || double.IsInfinity(Forces[1]))
            //    throw new ArithmeticException("Error trying to calculate hydrodynamic forces (y). Value:  " + Forces[1]);
            //if (double.IsNaN(Torque) || double.IsInfinity(Torque))
            //    throw new ArithmeticException("Error trying to calculate hydrodynamic torque. Value:  " + Torque);

            if (neglectAddedDamping == false) {
                Forces[0] = Forces[0] + beta * dt * (AddedDampingTensor[0, 0] * TranslationalAcceleration[0][0] + AddedDampingTensor[1, 0] * TranslationalAcceleration[0][1] + AddedDampingTensor[0, 2] * RotationalAcceleration[0]);
                Forces[1] = Forces[1] + beta * dt * (AddedDampingTensor[0, 1] * TranslationalAcceleration[0][0] + AddedDampingTensor[1, 1] * TranslationalAcceleration[0][1] + AddedDampingTensor[1, 2] * RotationalAcceleration[0]);
                Torque = Torque + beta * dt * (AddedDampingTensor[2, 0] * TranslationalAcceleration[0][0] + AddedDampingTensor[2, 1] * TranslationalAcceleration[0][1] + AddedDampingTensor[2, 2] * RotationalAcceleration[0]);
            }

            if (iteration_counter_P == 1) {
                Console.WriteLine("First iteration of the current timestep, all relaxation factors are set to 1");
                for (int d = 0; d < SpatialDim; d++) {
                    HydrodynamicForces[0][d] = 0;
                    HydrodynamicForces[0][d] = Forces[d];
                    if (Math.Abs(Forces[d]) < ForceAndTorque_convergence * 1e-2 && ClearSmallValues == true) {
                        Forces[d] = 0;
                    }
                }
                HydrodynamicTorque[0] = 0;
                HydrodynamicTorque[0] = Torque;
                if (Math.Abs(Torque) < ForceAndTorque_convergence * 1e-2 && ClearSmallValues == true) {
                    Torque = 0;
                }
            }

            else if (iteration_counter_P == 1000)
            {
                Console.WriteLine("No convergence after 100 iterations, I will try to restart");
                double ForceSummation = 0;
                for (int d = 0; d < SpatialDim; d++)
                {
                    ForceSummation += HydrodynamicForces[1][d].Pow2(); 
                }
                ForceSummation = Math.Sqrt(ForceSummation);
                HydrodynamicForces[0][0] = ForceSummation * Math.Cos(Angle[1]);
                HydrodynamicForces[0][1] = ForceSummation * Math.Sin(Angle[1]);
                HydrodynamicTorque[0] = 0;
            }

            else
            {
                double[] RelaxatedForceAndTorque = Underrelaxation.RelaxatedForcesAndTorque(Forces, Torque, HydrodynamicForces[0], HydrodynamicTorque[0], ForceAndTorque_convergence, underrelaxation_factor, ClearSmallValues, AddaptiveUnderrelaxation, AverageDistance, iteration_counter_P);
                for (int d = 0; d < this.SpatialDim; d++)
                {
                    HydrodynamicForces[0][d] = RelaxatedForceAndTorque[d];
                }
                HydrodynamicTorque[0] = RelaxatedForceAndTorque[SpatialDim];
            }
            if (double.IsNaN(HydrodynamicForces[0][0]) || double.IsInfinity(HydrodynamicForces[0][0]))
                throw new ArithmeticException("Error trying to calculate hydrodynamic forces (x). Value:  " + HydrodynamicForces[0][0]);
            if (double.IsNaN(HydrodynamicForces[0][1]) || double.IsInfinity(HydrodynamicForces[0][1]))
                throw new ArithmeticException("Error trying to calculate hydrodynamic forces (y). Value:  " + HydrodynamicForces[0][1]);
            if (double.IsNaN(HydrodynamicTorque[0]) || double.IsInfinity(HydrodynamicTorque[0]))
                throw new ArithmeticException("Error trying to calculate hydrodynamic torque. Value:  " + HydrodynamicTorque[0]);
        }

        public double[] CalculateParticleMomentum(double dt)
        {
            double[] temp = new double[SpatialDim + 1];
            for (int d = 0; d < SpatialDim; d++)
            {
                temp[d] = (Mass_P + dt * beta * AddedDampingTensor[d, d]) * TranslationalVelocity[0][d] + AddedDampingTensor[1 - d, d] * TranslationalVelocity[0][1 - d] + AddedDampingTensor[d, 2] * RotationalVelocity[0];
            }
            temp[SpatialDim] = (MomentOfInertia_P + beta * dt * AddedDampingTensor[2, 2] * RotationalVelocity[0]) + beta * dt * AddedDampingTensor[2, 1] * TranslationalVelocity[0][1] + beta * dt * AddedDampingTensor[2, 0] * TranslationalVelocity[0][0];
            return temp;
        }

        public double[] CalculateParticleKineticEnergy(double dt)
        {
            double[] temp = new double[SpatialDim + 1];
            for (int d = 0; d < SpatialDim; d++)
            {
                temp[d] = 0.5 *((Mass_P + dt * beta * AddedDampingTensor[d, d]) * TranslationalVelocity[0][d].Pow2() + AddedDampingTensor[1 - d, d] * TranslationalVelocity[0][1 - d].Pow2() + AddedDampingTensor[d, 2] * RotationalVelocity[0].Pow2());
            }
            temp[SpatialDim] = 0.5 * ((MomentOfInertia_P + beta * dt * AddedDampingTensor[2, 2] * RotationalVelocity[0].Pow2()) + beta * dt * AddedDampingTensor[2, 1] * TranslationalVelocity[0][1].Pow2() + beta * dt * AddedDampingTensor[2, 0] * TranslationalVelocity[0][0].Pow2());
            return temp;
        }
        
        /// <summary>
        /// Calculating the particle reynolds number according to paper Turek and testcase ParticleUnderGravity
        /// </summary>
        abstract public double ComputeParticleRe(double ViscosityFluid);
        
        /// <summary>
        /// get cut cells describing the boundary of this particle
        /// </summary>
        /// <param name="LsTrk"></param>
        /// <returns></returns>
        abstract public CellMask CutCells_P(LevelSetTracker LsTrk);

        /// <summary>
        /// Gives a bool whether the particle contains a certain point or not
        /// </summary>
        /// <param name="point"></param>
        /// <returns></returns>
        abstract public bool Contains(double[] point, LevelSetTracker LsTrk);

        abstract public double[] GetLengthScales();

        virtual public MultidimensionalArray GetSurfacePoints(LevelSetTracker lsTrk, LevelSet levelSet)
        {
            throw new NotImplementedException();
        }
    }
}

