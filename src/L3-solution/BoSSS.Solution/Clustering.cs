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

using BoSSS.Foundation;
using BoSSS.Foundation.Grid;
using BoSSS.Foundation.Grid.Classic;
using ilPSP;
using MPI.Wrappers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace BoSSS.Solution.Utils {

    /// <summary>
    /// Class for a cell clustering that devides the grid into sub-grids
    /// </summary>
    public class Clustering {

        /// <summary>
        /// Information about the grid
        /// </summary>
        private IGridData gridData;

        /// <summary>
        /// The time step constraints that the Clustering is based on
        /// </summary>
        private IList<TimeStepConstraint> timeStepConstraints;

        /// <summary>
        /// Number of clusters
        /// </summary>
        public int NumOfClusters {
            get;
            private set;
        }

        /// <summary>
        /// List of sub-grids
        /// </summary>
        public List<SubGrid> SubGridList {
            get;
            private set;
        }

        /// <summary>
        /// Helper Field needed for the visualization of the sub-grids
        /// </summary>
        public DGField SubGridField {
            get;
            private set;
        }

        /// <summary>
        /// Constructor for the grid clustering
        /// </summary>
        /// <param name="gridData">Information about the grid</param>
        /// <param name="timeStepConstraints">Time step constraings used as cell metric for the clustering</param>
        /// <param name="numOfClusters">Number of clusters</param>
        public Clustering(IGridData gridData, IList<TimeStepConstraint> timeStepConstraints, int numOfClusters) {
            this.gridData = gridData;
            this.timeStepConstraints = timeStepConstraints;
            this.NumOfClusters = numOfClusters;

            this.SubGridField = new SinglePhaseField(new Basis(gridData, 0));
            this.SubGridList = CreateSubGrids(this.NumOfClusters);
        }

        /// <summary>
        /// Creates the sub-grids of the clustering
        /// </summary>     
        /// <param name="numOfClusters">Number of clusters</param>
        /// <returns>A list of sub-grids</returns>
        public List<SubGrid> CreateSubGrids(int numOfClusters) {
            this.NumOfClusters = numOfClusters;
            int numOfCells = gridData.iLogicalCells.NoOfLocalUpdatedCells;

            MultidimensionalArray cellMetric = GetCellMetric();
            MultidimensionalArray means = CreateMeans(cellMetric);

            Kmeans Kmean = new Kmeans(cellMetric.To1DArray(), numOfClusters, means.To1DArray());

            // The corresponding sub-grid IDs
            int[] clustered = Kmean.Cluster();
            int[] clusterCount = Kmean.ClusterCount;

            unsafe {
                int[] globalCC = new int[numOfClusters];
                // send = means[]
                // receive = globalMeans[]
                fixed (int* pSend = &clusterCount[0], pRcv = &globalCC[0]) {
                    csMPI.Raw.Allreduce((IntPtr)(pSend), (IntPtr)(pRcv), numOfClusters, csMPI.Raw._DATATYPE.INT, csMPI.Raw._OP.SUM, csMPI.Raw._COMM.WORLD);
                }
                clusterCount = globalCC;
            }

            int counter = numOfClusters;
            for (int i = 0; i < numOfClusters; i++) {
                if (clusterCount[i] == 0) {
                    System.Console.WriteLine("Sub-grid/Cluster " + (i + 1) + ", with mean value " + Kmean.Means[i] + ", is empty and not used anymore!");
                    counter--;
                }
            }

            SubGridList = new List<SubGrid>(counter);

            // Generating BitArray for all Subgrids, even for those which are empty, i.e ClusterCount == 0
            BitArray[] baMatrix = new BitArray[numOfClusters];
            for (int i = 0; i < numOfClusters; i++) {
                baMatrix[i] = new BitArray(numOfCells);
            }

            // Filling the BitArrays
            this.SubGridField.Clear();
            for (int i = 0; i < numOfCells; i++) {
                if (clustered[i] != -1) { // Happens only in the IBM case for void cells
                    baMatrix[clustered[i]][i] = true;
                    // For Debugging: Visualizes the clusters in a field
                    this.SubGridField.SetMeanValue(i, clustered[i] + 0 * gridData.CellPartitioning.MpiRank);
                }
            }

            // Generating the sub-grids
            int j = 0;
            for (int i = 0; i < numOfClusters; i++) {
                // Generating only the sub-grids which are not empty
                if (clusterCount[i] != 0) {
                    BitArray ba = baMatrix[i];
                    this.SubGridList.Add(new SubGrid(new CellMask(gridData, ba)));
                    j++;
                }
            }
            this.NumOfClusters = counter;

            return SubGridList;
        }

        /// <summary>
        /// Creates an array with an tanh spaced distribution of the mean
        /// values between maximum and minimum value of a given cell metric, 
        /// e.g., minimal distance between two nodes in a cell <see cref="GridData.CellData.h_min"/>
        /// </summary>
        /// <param name="cellMetric">Given cell metric</param>
        /// <returns>Double[] with the length of the number of given sub-grids></returns>
        private MultidimensionalArray CreateMeans(MultidimensionalArray cellMetric) {
            //MultidimensionalArray means = MultidimensionalArray.Create(NumOfSgrd);
            double h_min = cellMetric.Min(d => double.IsNaN(d) ? double.MaxValue : d); // .Where(d => !double.IsNaN(d)).ToArray().Min();
            double h_max = cellMetric.Max();
            Console.WriteLine("Clustering: Create tanh spaced means");
            // Getting global h_min and h_max
            ilPSP.MPICollectiveWatchDog.Watch();
            h_min = h_min.MPIMin();
            h_max = h_max.MPIMax();

            if (h_min == h_max)
                h_max += 0.1 * h_max; // Dirty hack for IBM cases with equidistant grids

            // Tanh Spacing, which yields to more cell cluster for smaller cells
            var means = Grid1D.TanhSpacing(h_min, h_max, NumOfClusters, 4.0, true).Reverse().ToArray();

            // Equidistant spacing, in general not the best choice
            //means = GenericBlas.Linspace(h_min, h_max, NumOfSgrd).Reverse().ToArray();

            return MultidimensionalArray.CreateWrapper(means, NumOfClusters);
        }

        /// <summary>
        /// Checks for changes between two clusterings
        /// </summary>
        /// <param name="oldClustering">A clustering which should be compared to</param>
        /// <returns>True, if clustering has changed. False, if clustering has not changed.</returns>
        public bool CheckForNewClustering(List<SubGrid> oldClustering) {
            bool localResult = false;   // false = no reclustering needed

            if (SubGridList.Count != oldClustering.Count)
                localResult = true;
            else {
                for (int i = 0; i < SubGridList.Count; i++) {
                    if (!SubGridList[i].VolumeMask.Equals(oldClustering[i].VolumeMask)) {
                        localResult = true;
                    }
                }
            }

            bool globalResult;
            unsafe {
                int localResultAsInt = localResult ? 1 : 0;
                int globalResultAsInt;
                csMPI.Raw.Allreduce((IntPtr)(&localResultAsInt), (IntPtr)(&globalResultAsInt), 1, csMPI.Raw._DATATYPE.INT, csMPI.Raw._OP.LOR, csMPI.Raw._COMM.WORLD);
                globalResult = globalResultAsInt == 1 ? true : false;
            }

            return globalResult;
        }

        /// <summary>
        /// Returns a cell metric value in every cell
        /// </summary>
        /// <returns>Cell metric as <see cref="MultidimensionalArray"/></returns>
        public MultidimensionalArray GetCellMetric() {
            MultidimensionalArray cellMetric = MultidimensionalArray.Create(gridData.iLogicalCells.NoOfLocalUpdatedCells);

            // Adapted from Variables.cs --> DerivedVariable CFL
            for (int i = 0; i < gridData.iLogicalCells.NoOfLocalUpdatedCells; i++) {
                cellMetric[i] = this.timeStepConstraints.Min(c => c.GetLocalStepSize(i, 1));
            }

            //int count = 0;
            //foreach (Chunk chunk in CellMask.GetFullMask(gridData)) {
            //    for (int i = 0; i < chunk.Len; i++) {
            //        int cell = i + chunk.i0;
            //        cellMetric[count] = this.timeStepConstraints.Min(c => c.GetLocalStepSize((int)gridData.iLogicalCells.GetGlobalID(cell), 1));
            //        count++;
            //    }
            //}

            return cellMetric;

            //return gridData.iGeomCells.h_min;
        }

        /// <summary>
        /// Updates the clustering variables when they have been changed by another class/method
        /// </summary>
        /// <param name="subGridList">List of clusters</param>
        /// <param name="subGridField">Cluster to be plotted</param>
        /// <param name="numOfClusters">Number of clusters</param>
        public void UpdateClusteringVariables(List<SubGrid> subGridList, DGField subGridField, int numOfClusters) {
            this.SubGridList = subGridList;
            this.SubGridField = subGridField;
            this.NumOfClusters = numOfClusters;
        }
    }
}