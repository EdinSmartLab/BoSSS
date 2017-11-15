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

using BoSSS.Foundation.Grid;
using BoSSS.Foundation.Grid.Classic;
using BoSSS.Solution.Control;
using ilPSP;
using MPI.Wrappers;
using System;
using System.Diagnostics;

namespace BoSSS.Solution {

    /// <summary>
    /// In an MPI-parallel run, this class can be used to create an MPI
    /// partition of the grid according a given cell cost estimation, so that
    /// the compute load is balanced evenly among the MPI processors.
    /// </summary>
    public class LoadBalancer {

        /// <summary>
        /// A factory used to update
        /// <see cref="CurrentCellCostEstimator"/> if required
        /// </summary>
        private Func<IApplication<AppControl>, int, ICellCostEstimator> cellCostEstimatorFactory;

        /// <summary>
        /// A model that estimates the costs of different cells
        /// </summary>
        public ICellCostEstimator CurrentCellCostEstimator {
            get;
            private set;
        }

        /// <summary>
        /// Indicates whether load balancer has already suggest a new partitioning
        /// before. If this value is true, the load balancer is allowed to
        /// rearrange cells more aggressively
        /// </summary>
        private bool isFirstRepartitioning = true;

        /// <summary>
        /// Constructor.
        /// </summary>
        public LoadBalancer(Func<IApplication<AppControl>, int, ICellCostEstimator> cellCostEstimatorFactory) {
            this.cellCostEstimatorFactory = cellCostEstimatorFactory;
        }

        /// <summary>
        /// Returns a new grid partition based on the performance model.
        /// </summary>
        /// <param name="performanceClassCount"></param>
        /// <param name="cellToPerformanceClassMap"></param>
        /// <param name="Grid"></param>
        /// <param name="TimestepNo"></param>
        /// <param name="gridPartType">Grid partitioning type.</param>
        /// <param name="imbalanceThreshold">
        /// See <see cref="Control.AppControl.DynamicLoadBalancing_ImbalanceThreshold"/>.
        /// </param>
        /// <param name="Period">
        /// See <see cref="Control.AppControl.DynamicLoadBalancing_Period"/>.
        /// </param>
        /// <returns></returns>
        public int[] GetNewPartitioning(IApplication<AppControl> app, int performanceClassCount, int[] cellToPerformanceClassMap, int TimestepNo, GridPartType gridPartType, string PartOptions, double imbalanceThreshold, int Period) {
            // Create new model if number of cell classes has changed
            if (CurrentCellCostEstimator == null
                || CurrentCellCostEstimator.PerformanceClassCount != performanceClassCount) {
                CurrentCellCostEstimator = cellCostEstimatorFactory(app, performanceClassCount);
            }

            CurrentCellCostEstimator.UpdateEstimates(cellToPerformanceClassMap);

            if (app.Grid.Size == 1
                || TimestepNo % Period != 0
                || CurrentCellCostEstimator.ImbalanceEstimate() < imbalanceThreshold) {
                // No new partitioning if timestep not selected or imbalance
                // below threshold
                return null;
            }

            Console.WriteLine(
                "Runtime imbalance ({0:P1}) was above configured threshold ({1:P1}); attempting repartitioning",
                CurrentCellCostEstimator.ImbalanceEstimate(),
                imbalanceThreshold);

            int[] cellCosts = CurrentCellCostEstimator.GetEstimatedCellCosts();
            if (cellCosts == null) {
                return null;
            }

            int[] result;
            switch (gridPartType) {
                case GridPartType.METIS:
                    int.TryParse(PartOptions, out int noOfPartitioningsToChooseFrom);
                    noOfPartitioningsToChooseFrom = Math.Max(1, noOfPartitioningsToChooseFrom);
                    result = app.Grid.ComputePartitionMETIS(cellCosts);
                    isFirstRepartitioning = false;
                    break;

                case GridPartType.ParMETIS:
                    // Do full ParMETIS run on first repartitioning since
                    // initial partitioning may be _really_ bad
                    if (isFirstRepartitioning) {
                        result = app.Grid.ComputePartitionParMETIS(cellCosts);
                        isFirstRepartitioning = false;
                    } else {
                        // Refinement currently deactivate because it behaves
                        // strangely when large numbers of cells should be
                        // repartitioned
                        //result = Grid.ComputePartitionParMETIS(cellCosts, refineCurrentPartitioning: true);
                        result = app.Grid.ComputePartitionParMETIS(cellCosts);
                    }
                    break;

                case GridPartType.Hilbert:
                    return app.Grid.ComputePartitionHilbert(cellCosts);

                case GridPartType.none:
                    result = IndexBasedPartition(cellCosts);
                    break;

                case GridPartType.Predefined:
                    return null;

                default:
                    throw new NotImplementedException();
            }

            if (result.Length == 0) {
                throw new Exception(String.Format(
                    "LoadBalancer computed invalid partitioning; no cells left on rank {0}",
                    app.Grid.MyRank));
            }

            return result;
        }

        static int[] IndexBasedPartition(int[] Cost) {
            int J = Cost.Length;
            int[] AccCost = Cost.CloneAs();

            for (int j = 0; j < J - 1; j++) {
                AccCost[j + 1] += AccCost[j];
            }

            int MpiRank, MpiSize;
            csMPI.Raw.Comm_Rank(csMPI.Raw._COMM.WORLD, out MpiRank);
            csMPI.Raw.Comm_Size(csMPI.Raw._COMM.WORLD, out MpiSize);

            int[] GlobAccCost = new int[MpiSize];
            unsafe {
                fixed (int* pGlobAccCost = GlobAccCost) {
                    int locAccCost = AccCost[J - 1];

                    csMPI.Raw.Allgather(
                        (IntPtr)(&locAccCost), 1, csMPI.Raw._DATATYPE.INT,
                        (IntPtr)pGlobAccCost, 1, csMPI.Raw._DATATYPE.INT,
                        csMPI.Raw._COMM.WORLD);
                }
            }

            for (int proc = 0; proc < MpiSize - 1; proc++) {
                GlobAccCost[proc + 1] += GlobAccCost[proc];
            }

            int glbOffset = MpiRank > 0 ? GlobAccCost[MpiRank - 1] : 0;
            for (int j = 0; j < J; j++) {
                AccCost[j] += glbOffset;
            }


            double TotalCost = GlobAccCost[MpiSize - 1];
            double[] Ranges = new double[MpiSize + 1];
            for (int proc = 1; proc <= MpiSize; proc++) {
                Ranges[proc] = (TotalCost * proc) / MpiSize;
            }
            Ranges[MpiSize] = Math.Max(Ranges[MpiSize], TotalCost);

            int[] R = new int[J];

            int rank = 0;
            for (int j = 0; j < J; j++) {
                Debug.Assert(j == 0 || AccCost[j - 1] < AccCost[j]);
                while (!(Ranges[rank] < AccCost[j] && AccCost[j] <= Ranges[rank + 1]) && (rank < MpiSize - 1)) {
                    rank++;
                }
                Debug.Assert(Ranges[rank] < AccCost[j] && AccCost[j] <= Ranges[rank + 1]);
                R[j] = rank;
            }

            return R;
        }
    }
}
