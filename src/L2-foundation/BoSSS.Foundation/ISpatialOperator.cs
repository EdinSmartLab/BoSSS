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

using BoSSS.Foundation.Quadrature;
using BoSSS.Foundation.Quadrature.FluxQuadCommon;
using System;
using System.Collections.Generic;

namespace BoSSS.Foundation {

    /// <summary>
    /// see <see cref="ISpatialOperator.EquationComponents"/>
    /// </summary>
    public interface IEquationComponents : IEnumerable<KeyValuePair<string, IEnumerable<IEquationComponent>>> {

        /// <summary>
        /// returns the collection of equation components for one variable in the 
        /// codomain
        /// </summary>
        /// <param name="EqnName">
        /// a variable in the codomain (<see cref="SpatialOperator.CodomainVar"/>)
        /// </param>
        /// <returns></returns>
        ICollection<IEquationComponent> this[string EqnName] {
            get;
        }
    }

    /// <summary>
    /// A hint for implicit solvers, which linearization of the operator should be used
    /// </summary>
    public enum LinearizationHint {

        /// <summary>
        /// Employ the <see cref="ISpatialOperator.GetJacobiOperator(int)"/>
        /// </summary>
        GetJacobiOperator = 1,

        /// <summary>
        /// Use the ad-hoc matrix builder (default)
        /// </summary>
        AdHoc = 0,

        /// <summary>
        /// compute a finite-differnce Jacobian of the operator
        /// </summary>
        FDJacobi = 2

    }

    /// <summary>
    /// User-defined validation of a solver step, e.g. to prevent the solver to iterate out-of-bounds, 
    /// e.g. to avoid un-physical 'solutions' (e.g. negative density).
    /// ('safeguard' for the solver)
    /// </summary>
    /// <param name="OldSolution">
    /// - on input, the old solution (approximation) 
    /// </param>
    /// <param name="NewSolution">
    /// - on input: new solution (approximation) proposed by the solver
    /// - on exit: optionally, solution (approximation) modified by the user
    /// </param>
    public delegate void SolverSafeguard(DGField[] OldSolution, DGField[] NewSolution);



    /// <summary>
    /// Common interface for spatial operators in the DG and the XDG context
    /// </summary>
    public interface ISpatialOperator {

        /// <summary>
        /// names of (DG-) variables that represent the Co-Domain of this differential operator
        /// These names/strings should not be confused with field identification strings
        /// (<see cref="DGField.Identification"/>), they have nothing to do with that.
        /// </summary>
        IList<string> CodomainVar { get; }


        /// <summary>
        /// names of (DG-) variables that represent the domain of this  differential operator;
        /// These names/strings should not be confused with field identification strings
        /// (<see cref="DGField.Identification"/>), they have nothing to do with that.
        /// </summary>
        IList<string> DomainVar { get; }

        /// <summary>
        /// Notifies the solver that the mean value for a specific value is floating.
        /// An example is e.g. the pressure in the incompressible Navier-Stokes equation with all-walls boundary condition.
        /// - key: the name of some domain variable
        /// - value: false, if the mean value of the solution  is defined, true if the mean value  of the solution is floating (i.e. for some solution u, u + constant is also a solution).
        /// </summary>
        IDictionary<string, bool> FreeMeanValue {
            get;
        }


        /// <summary>
        /// for each variable in <see cref="CodomainVar"/>, a
        /// collection of equation components that define the operator.
        /// </summary>
        IEquationComponents EquationComponents { get; }

        /// <summary>
        /// indicates whether the equation-assembly has been finished (by calling <see cref="Commit"/>)
        /// or not.
        /// </summary>
        bool IsCommited { get; }

        /// <summary>
        /// If set, used to update parameters before evaluation.
        /// Keep in mind: Here, multiple handlers can be added so it is not necessary to 
        /// put the update of all parameter fields for the operator into one big piece of spaghetti code.
        /// Hence, it can be split among different handlers.
        /// </summary>
        /// <remarks>
        /// Alternatively, equation components which implement <see cref="IParameterHandling"/> can be used (<see cref="IParameterHandling.MyParameterUpdate"/>).
        /// </remarks>
        ICollection<DelPartialParameterUpdate> ParameterUpdates { get; }

        /// <summary>
        /// Can be used to implement the allocation of DG fields to store parameter values.
        /// </summary>
        /// <remarks>
        /// Alternatively, equation components which implement <see cref="IParameterHandling"/> can be used (<see cref="IParameterHandling.MyParameterAlloc"/>).
        /// </remarks>
        ICollection<DelParameterFactory> ParameterFactories { get; }

        /// <summary>
        /// names of (DG-) variables which act as parameters; 
        /// Their role is pretty similar to those of the domain variables, and for nonlinear
        /// fluxes, there is no difference.
        /// However, for <em>linear</em> fluxes, they can be used to provide some 
        /// space-depended properties as DG-fields, e.g. for providing boundary conditions
        /// or if the linear operator is some linearization of some nonlinear operator.
        /// </summary>
        IList<string> ParameterVar { get; }

        /// <summary>
        /// finalizes the assembly of the operator;
        /// Can be called only once in the lifetime of this object.
        /// After calling this method, no adding/removing of equation components is possible.
        /// </summary>
        void Commit();


        /// <summary>
        /// Function Mapping from Domain Variable Degrees, Parameter Degrees and CoDomain Variable Degrees to the Quadrature Order
        /// </summary>
        Func<int[], int[], int[], int> QuadOrderFunction {
            get;
            set;
        }

        /// <summary>
        /// An operator which computes the Jacobian matrix of this operator.
        /// All components in this operator need to implement the <see cref="ISupportsJacobianComponent"/> interface in order to support this operation.
        /// </summary>
        ISpatialOperator GetJacobiOperator(int SpatialDimension);

        /// <summary>
        /// A hint for implicit/nonlinear solvers, which linearization of the operator should be used
        /// (<see cref="GetJacobiOperator"/>, <see cref="GetMatrixBuilder"/>, <see cref="GetFDJacobianBuilder"/>),
        /// </summary>
        LinearizationHint LinearizationHint {
            get;
            set;
        }


        /// <summary>
        /// Constructs a new evaluator object for the explicit evaluation of this spatial operator.
        /// </summary>
        /// <returns></returns>
        /// <remarks>
        /// Before this method can be called,
        /// the operator assembly must be finalized by calling <see cref="Commit"/> .
        /// </remarks>
        /// <param name="CodomainVarMap">
        /// used to compute indices into the result vector
        /// </param>
        /// <param name="DomainFields">
        /// domains which are evaluated to compute fluxes, ...
        /// </param>
        /// <param name="ParameterMap">
        /// The parameter variables (of this differential operator);
        /// The number of elements in the list must match the parameter count of the differential operator
        /// (see <see cref="SpatialOperator.ParameterVar"/>);
        /// It is allowed to set an entry to 'null', in this case the values of the parameter field
        /// are assumed to be 0.0;
        /// If the differential operator contains no parameters, this argument can be null;
        /// </param>
        IEvaluatorNonLin GetEvaluatorEx(IList<DGField> DomainFields, IList<DGField> ParameterMap, UnsetteledCoordinateMapping CodomainVarMap);

        /// <summary>
        /// Computes the Jacobian matrix of the operator by finite differences.
        /// </summary>
        IEvaluatorLinear GetFDJacobianBuilder(IList<DGField> DomainFields, IList<DGField> ParameterMap, UnsetteledCoordinateMapping CodomainVarMap);

        /// <summary>
        /// Evaluation of the operator matrix
        /// (only for linear operators or ad-hoc linearizations)
        /// </summary>
        IEvaluatorLinear GetMatrixBuilder(UnsetteledCoordinateMapping DomainVarMap, IList<DGField> ParameterMap, UnsetteledCoordinateMapping CodomainVarMap);


        /// <summary>
        /// Specification of the temporal operator, i.e. the mass matrix.
        /// Null defers to a steady-state system.
        /// Setting is only available before calling <see cref="Commit"/>.
        /// </summary>
        ITemporalOperator TemporalOperator {
            get;
            set;
        }


        /// <summary>
        /// Adaptation of user-defined values when a nonlinear solver moves along a homotopy path.
        /// </summary>
        ICollection<Action<double>> HomotopyUpdate {
            get;
        }

        /// <summary>
        /// Setting (to a different value) fires all <see cref="HomotopyUpdate"/> events
        /// </summary>
        double CurrentHomotopyValue {
            get;
            set;
        }

        /// <summary>
        /// 'safeguard' for  solvers to avoid unphysical solutions (mostly relevant for implicit, nonlinear systems)
        /// </summary>
        SolverSafeguard SolverSafeguard {
            get;
            set;
        }
       
    }

    /// <summary>
    /// <see cref="ISpatialOperator.TemporalOperator"/>
    /// </summary>
    public interface ITemporalOperator {

        /// <summary>
        /// finalizes the assembly of the operator;
        /// Can be called only once in the lifetime of this object.
        /// After calling this method, no adding/removing of equation components is possible.
        /// </summary>
        void Commit();

        /// <summary>
        /// Evaluation of the temporal operator matrix (aka. mass matrix).
        /// </summary>
        IEvaluatorLinear GetMassMatrixBuilder(UnsetteledCoordinateMapping DomainVarMap, IList<DGField> ParameterMap, UnsetteledCoordinateMapping CodomainVarMap);
    }

}