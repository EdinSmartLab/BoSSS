﻿using BoSSS.Foundation.Grid;
using BoSSS.Foundation.Grid.Aggregation;
using BoSSS.Foundation.Quadrature;
using ilPSP;
using ilPSP.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BoSSS.Foundation
{
    public class AggregationGridCurvedInjector
    {
        public static MultidimensionalArray[] AggregateCurvedCells(AggregationGridData _agGrd, Basis _maxDgBasis, int _ilevel)
        {
            AggregationGridData agGrd = _agGrd;
            int ilevel = _ilevel;
            Basis dgBasis = _maxDgBasis;

            // check type of underlying reference elements
            foreach (var refel in agGrd.AncestorGrid.iGeomCells.RefElements){
                if (refel.GetType() != typeof(BoSSS.Foundation.Grid.RefElements.Square)) { throw new NotSupportedException("currently only square elements are supported"); }
            }

            // get dimensions of Injection operator
            int Jagg = agGrd.iLogicalCells.NoOfLocalUpdatedCells;
            int Np = dgBasis.Length;

            MultidimensionalArray[] InjectorCoarse = new MultidimensionalArray[Jagg];

            for (int i=0; i < Jagg; i++)
            {   
                // get parts of current aggregation cell
                int[] parts = agGrd.iLogicalCells.AggregateCellToParts[i];

                // get a list of all inner edges
                int[] iedges;
                List<int> l_iedges = new List<int>();

                for (int j = 0; j < agGrd.iGeomEdges.Count; j++){
                    // check for outer edges and if both cells on the edge belong to the current aggregation cell     
                    if (agGrd.iGeomEdges.LogicalCellIndices[j, 0] == agGrd.iGeomEdges.LogicalCellIndices[j, 1] && agGrd.iGeomEdges.LogicalCellIndices[j, 0] == i) {                                           
                        l_iedges.Add(j);                        
                    }                    
                }
                // convert to array
                iedges = l_iedges.ToArray();

                InjectorCoarse[i] = MultidimensionalArray.Create(parts.Length, Np, Np);

                // special case for the first basis function
                InjectorCoarse[i].ExtractSubArrayShallow(-1, 0, 0).Acc(1, GetCoefficients0Degree(agGrd, dgBasis, parts));

                // compute the columns in the Injection operator for subsequent basis functions
                // SO FAR DUMMY FUNCTION
                for (int n = 1; n < Np; n++)
                {
                    InjectorCoarse[i].ExtractSubArrayShallow(new int[] { 0, 0, n},new int[] { parts.Length-1, n, -1 }).Acc(1, GetCoefficients(agGrd, dgBasis, InjectorCoarse[i], parts, iedges, n));
                }

            }

            return InjectorCoarse;
        }

        // computes the injector coefficients for the first basis functions
        private static MultidimensionalArray GetCoefficients0Degree(AggregationGridData _agGrd, Basis _maxDgBasis, int[] parts)
        { 
            var Injector0Degree =  MultidimensionalArray.Create(parts.Length);

            // calculate total aggregation cell volume
            double aggVol = 0;
            foreach(int i in parts)
            {
                aggVol += _agGrd.AncestorGrid.Cells.GetCellVolume(i);
            }
                
            // For the first basis functions with constant value this breaks down to a simple scaling
            double scale = 1/Math.Sqrt(aggVol);
                       
            int count = 0;
            foreach (int i in parts)
            {
                Injector0Degree[count] = Math.Sqrt(_agGrd.AncestorGrid.Cells.GetCellVolume(i)) * scale;
                count++;
            }

            return Injector0Degree;
        }

        // computes the injector coefficients higher order basis functions
        private static MultidimensionalArray GetCoefficients(AggregationGridData _agGrd, Basis _maxDgBasis, MultidimensionalArray _inj, int[] parts, int[] iedges, int basisIndex)
        {
            int partCount = parts.Length;
            int varCount = partCount * (basisIndex + 1);
            int currentDegree = _maxDgBasis.Polynomials[0][basisIndex].AbsoluteDegree;
            var InjectorNDegree = MultidimensionalArray.Create(partCount, basisIndex + 1);

            // equation matrix
            // basisIndex + 1 equations for othogonality
            // (currentdegree + 1) * iedges.Length equations for continuity along inneredges
            // (basisIndex + 1) * iedges.Length equations for edge-normal gradient jump minimization (smootheness)
            MultidimensionalArray aggEq = MultidimensionalArray.Create(basisIndex + 1 + (currentDegree + 1 + basisIndex * 2) * iedges.Length,varCount);

            // rhs
            MultidimensionalArray aggRhs = MultidimensionalArray.Create(basisIndex + 1 + (currentDegree + 1 + basisIndex * 2) * iedges.Length);
            
            // assemble system
            MultidimensionalArray aggSys = MultidimensionalArray.Create(basisIndex + 1 + (currentDegree + 1 + basisIndex * 2) * iedges.Length, varCount + 1);
            aggEq = aggSys.ExtractSubArrayShallow(new int[] { 0, 0 }, new int[] { basisIndex + 1 + (currentDegree + 1 + basisIndex * 2) * iedges.Length - 1, varCount - 1 });
            aggRhs = aggSys.ExtractSubArrayShallow(-1, varCount);

            // construct the system
            // row[basisIndex] is a placeholder for the normality condition, to ensure full rank of the system
            aggEq[basisIndex, basisIndex] = 1;
            aggRhs[basisIndex] = 1;

            // orthogonality
            for (int row = 0; row < basisIndex; row++)
            {
                for (int i = 0; i < partCount; i++)
                {
                    for (int column = 0; column <= row; column++)
                    {
                        aggEq[row, i * (basisIndex + 1) + column] = _inj[i,row-column,row];
                    }
                }
            }

            // continuity
            // TODO harder part: Select currentDegree + 1 points per shared edge and ensure continuity there
            // 1. per edge construct currentDegree + 1 points
            // 2. evaluate the basisIndex -basisfunction on the adjacent cells for these points
            // 3. write the values in aggEq
            int rowOffset = basisIndex + 1;
            for (int row = 0; row < iedges.Length; row++)
            {

                // FOR THE WHOLE SEGMENT, SUPER CLUNKY RIGHT NOW NEEDS TO BE WORKED OVER IF CODE IS FUNCTIONAL
                // select current edge
                int edge = iedges[row];

                // get the cells for the edge
                int cell_i = _agGrd.iGeomEdges.CellIndices[iedges[row], 0];
                int cell_j = _agGrd.iGeomEdges.CellIndices[iedges[row], 1];

                // get indices of left and right hand cell on the respective edge
                int index_i = Array.IndexOf(parts, cell_i);
                int index_j = Array.IndexOf(parts, cell_j);

                // get reference edge
                int ref_edge_i = _agGrd.iGeomEdges.Edge2CellTrafoIndex[iedges[row], 0];
                int ref_edge_j = _agGrd.iGeomEdges.Edge2CellTrafoIndex[iedges[row], 1];

                // get reference edge vertex indices
                // WILL NOT WORK FaceToVertexIndices IS NOT CORRECT AS OF RN
                List<int> ref_vert_i = new List<int>();
                for (int v=0; v < _agGrd.iGeomCells.GetRefElement(cell_i).FaceToVertexIndices.GetLength(1); v++)
                {
                    ref_vert_i.Add(_agGrd.iGeomCells.GetRefElement(cell_i).FaceToVertexIndices[ref_edge_i,v]);
                }
                List<int> ref_vert_j = new List<int>();
                for (int v = 0; v < _agGrd.iGeomCells.GetRefElement(cell_j).FaceToVertexIndices.GetLength(1); v++)
                {
                    ref_vert_j.Add(_agGrd.iGeomCells.GetRefElement(cell_j).FaceToVertexIndices[ref_edge_j, v]);
                }

                // get a list with all vertices on that edge
                MultidimensionalArray ref_vert_coord_i = MultidimensionalArray.Create(currentDegree + 1, _maxDgBasis.GridDat.SpatialDimension);
                foreach (var v in ref_vert_i)
                {
                    ref_vert_coord_i.ExtractSubArrayShallow(ref_vert_i.IndexOf(v), -1).Acc(1.0, _agGrd.iGeomCells.GetRefElement(cell_i).Vertices.ExtractSubArrayShallow(v, -1));
                }

                MultidimensionalArray ref_vert_coord_j = MultidimensionalArray.Create(currentDegree + 1, _maxDgBasis.GridDat.SpatialDimension);
                foreach (var v in ref_vert_j)
                {
                    ref_vert_coord_j.ExtractSubArrayShallow(ref_vert_j.IndexOf(v), -1).Acc(1.0, _agGrd.iGeomCells.GetRefElement(cell_j).Vertices.ExtractSubArrayShallow(v, -1));
                }

                // create sequence of sampling points through linear combination of vertex coordinates
                int pointIndex = 0;
                while (ref_vert_i.Count + pointIndex < currentDegree + 1 || ref_vert_j.Count + pointIndex < currentDegree + 1) {
                    ref_vert_coord_i.ExtractSubArrayShallow(ref_vert_i.Count + pointIndex, -1).Acc(0.5, ref_vert_coord_i.ExtractSubArrayShallow(pointIndex, -1) + ref_vert_coord_i.ExtractSubArrayShallow(pointIndex + 1, -1));

                    ref_vert_coord_j.ExtractSubArrayShallow(ref_vert_j.Count + pointIndex, -1).Acc(0.5, ref_vert_coord_j.ExtractSubArrayShallow(pointIndex, -1) + ref_vert_coord_j.ExtractSubArrayShallow(pointIndex + 1, -1));

                    pointIndex++;
                }

                // Collect points in a NodeSet
                NodeSet vert_coord_i = new NodeSet(_agGrd.iGeomCells.GetRefElement(cell_i), ref_vert_coord_i);
                NodeSet vert_coord_j = new NodeSet(_agGrd.iGeomCells.GetRefElement(cell_i), ref_vert_coord_i);

                for (int i = 0; i < (currentDegree + 1); i++)
                {
                    // DUMMY, TODO IMPLEMENTATION OF EVALUATION ON SAMPLING POINT ON THE EDGE IN BOTH CELLS
                    // It is missing the transformation from reference to physical cell
                    int polyLength = _maxDgBasis.Polynomials[0].Where(poly => poly.AbsoluteDegree <= currentDegree).Count();

                    MultidimensionalArray value_i = MultidimensionalArray.Create(currentDegree + 1, polyLength);
                    value_i.Multiply(1.0, _agGrd.ChefBasis.BasisValues.GetValues(vert_coord_i, currentDegree), _maxDgBasis.GridDat.ChefBasis.OrthonormalizationTrafo.GetValue_Cell(cell_i, 1, currentDegree).ExtractSubArrayShallow(new int[] { 0, 0, 0 }, new int[] { -1, polyLength - 1, polyLength - 1 }), 0.0, "ij", "ik", "kj");
                    
                    MultidimensionalArray value_j = MultidimensionalArray.Create(currentDegree + 1, polyLength);
                    value_j.Multiply(1.0, _agGrd.ChefBasis.BasisValues.GetValues(vert_coord_j, currentDegree), _maxDgBasis.GridDat.ChefBasis.OrthonormalizationTrafo.GetValue_Cell(cell_j, 1, currentDegree).ExtractSubArrayShallow(new int[] { 0, 0, 0 }, new int[] { -1, polyLength - 1, polyLength - 1 }), 0.0, "ij", "ik", "kj");

                    for (int column = 0; column < basisIndex + 1; column++)
                    {

                        aggEq[row * (currentDegree + 1) + i + rowOffset, index_i * (basisIndex + 1) + column] = value_i[i, column];
                        aggEq[row * (currentDegree + 1) + i + rowOffset, index_j * (basisIndex + 1) + column] = -value_j[i, column];
                    }
                }
            }

            // smootheness
            // TODO hardest part: Evaluate the gradients on the edge in normal direction then compute the L2-norm of the jump and enforce a minimization condition
            // 1. compute the gradient parts for the relevant (0-basisIndex) basis functions in normal direction on each edge
            // 2. compute the Gram determinant for the edge
            // 3. integrate the basisfunction couplings (originating from the L2-norm) over the edge (Caching would be highly advisable, because the same integrals will be needed for the next basis function to aggregate)
            // 4. write values in aggEq
            rowOffset = basisIndex + 1 + (currentDegree + 1) * iedges.Length;

            // iterate over edges
            for (int row = 0; row < iedges.Length; row++)
            {
                // FOR THE WHOLE SEGMENT, SUPER CLUNKY RIGHT NOW NEEDS TO BE WORKED OVER IF CODE IS FUNCTIONAL
                // select current edge
                int edge = iedges[row];

                // get the cells for the edge
                int cell_i = _agGrd.iGeomEdges.CellIndices[iedges[row], 0];
                int cell_j = _agGrd.iGeomEdges.CellIndices[iedges[row], 1];

                // get indices of left and right hand cell on the respective edge
                int index_i = Array.IndexOf(parts, cell_i);
                int index_j = Array.IndexOf(parts, cell_j);

                // create array containing the edge-normal gradient coupling integrals
                MultidimensionalArray gradient = MultidimensionalArray.Create(2, basisIndex, 2, basisIndex);
                //gradient = GetEdgeGradient(_maxDgBasis, _agGrd, edge, cell_i, cell_j, basisIndex);

                // compute the gradient coupled integrals
                var Context = _maxDgBasis.GridDat;
                var edgeMask = new EdgeMask(Context, new int[] { edge }, MaskType.Geometrical);

                EdgeQuadrature.GetQuadrature(new int[4] { 2, basisIndex, 2, basisIndex }, Context,
                    (new EdgeQuadratureScheme(true, edgeMask)).Compile(Context, _maxDgBasis.Degree * 2), // integrate over target cell
                    delegate (int i0, int Length, QuadRule QR, MultidimensionalArray _EvalResult)
                    {
                        NodeSet nodes_ref_edge = QR.Nodes;
                        //Debug.Assert(Length == 1);

                        MultidimensionalArray edge_normals = Context.iGeomEdges.NormalsCache.GetNormals_Edge(nodes_ref_edge, edge, 1);
                        Tuple<MultidimensionalArray, MultidimensionalArray> edge_gradient = _maxDgBasis.EdgeEvalGradient(nodes_ref_edge, edge, 1);
                        MultidimensionalArray edge_normal_gradient = MultidimensionalArray.Create(2, nodes_ref_edge.Length, basisIndex);

                        // compute the gradients in normal direction at the quadnodes reverse the normal for one cell
                        edge_normal_gradient.ExtractSubArrayShallow(0, -1, -1).Multiply(1.0, edge_gradient.Item1.ExtractSubArrayShallow(new int[] { 0, 0, 1, 0 }, new int[] { -1, nodes_ref_edge.Length - 1, basisIndex, Context.SpatialDimension - 1 }), edge_normals.ExtractSubArrayShallow(0, -1, -1), 0.0, "ik", "ikn", "in");
                        edge_normal_gradient.ExtractSubArrayShallow(1, -1, -1).Multiply(-1.0, edge_gradient.Item2.ExtractSubArrayShallow(new int[] { 0, 0, 1, 0 }, new int[] { -1, nodes_ref_edge.Length - 1, basisIndex, Context.SpatialDimension - 1 }), edge_normals.ExtractSubArrayShallow(0, -1, -1), 0.0, "ik", "ikn", "in");

                        // couple the gradients at each node (out "kinjm" "node_k,cell_i,polynom_n,cell_j,polynom_m")
                        var EvalResult = _EvalResult.ExtractSubArrayShallow(0, -1, -1, -1, -1, -1);
                        EvalResult.Multiply(1.0, edge_normal_gradient, edge_normal_gradient, 0.0, "kinjm", "ikn", "jkm");

                    },
                    /*_SaveIntegrationResults:*/ delegate (int i0, int Length, MultidimensionalArray ResultsOfIntegration)
                                                 {
                                                     Debug.Assert(Length == 1);

                                                     var res = ResultsOfIntegration.ExtractSubArrayShallow(0, -1, -1, -1, -1);
                                                     gradient.Clear();
                                                     gradient.Acc(1.0, res);
                                                 }).Execute();


               
                // iterate over both cells (leading coeffcient)
                for (int i = 0; i <= 1; i++)
                {

                    // iterate over all conditions for the current cell
                    for (int k = 1; k < basisIndex + 1; k++)
                    {

                        // iterate over the variables
                        for (int column = 1; column < basisIndex + 1; column++)
                        {
                            aggEq[row * (basisIndex * 2) + (k - 1) + i * basisIndex + rowOffset, index_i * (basisIndex + 1) + column] = gradient[i, k - 1, 0, column - 1];
                            aggEq[row * (basisIndex * 2) + (k - 1) + i * basisIndex + rowOffset, index_j * (basisIndex + 1) + column] = gradient[i, k - 1, 1, column - 1];
                        }
                    }
                }
            }




            // bring augmented system in reduced row echelon form
            //(var aggSol, var pivots, var cols, int rankAug) = aggSys.ReducedRowEchelonForm();
            double[] sol = new double[varCount];
            aggEq.LeastSquareSolve(sol, aggRhs.To1DArray());

            //// check consistency
            //if (rankAug != varCount) {
            //    int rank = aggEq.ReducedRowEchelonForm().Item4;
            //    // TODO handling of the infinite solutions case, select some solution with minimum L2-norm or whatever
            //    if (rank == rankAug)
            //    {
            //        throw new ArgumentException($"Error while computing the injection operator for curved cells and basis function {basisIndex}: Infinite Solutions");
            //    }
            //    else if (rank < rankAug)
            //    {
            //        throw new ArgumentException($"Error while computing the injection operator for curved cells and basis function {basisIndex}: No Solution"); 
            //    }
            //    else
            //    {
            //        throw new ArgumentException($"Error while computing the injection operator for curved cells and basis function {basisIndex}");
            //    }                    
            //}

            // create solution vector
            //MultidimensionalArray vector_InjectorNDegree = aggSol.ExtractSubArrayShallow(-1, varCount);

            // compute the diagonal entry of the Injector for the first cell in parts and then solve the dependencies to the other cells
            // normality condition
            double scale = 1 / sol.L2Norm();
            sol.ScaleV(scale);
            MultidimensionalArray vector_InjectorNDegree = MultidimensionalArray.CreateWrapper(sol, varCount);
            //vector_InjectorNDegree.Scale(scale);

            // restructure to Injector operator form
            for (int i = 0; i < parts.Length; i++)
            {
                InjectorNDegree.ExtractSubArrayShallow(i, -1).Acc(1.0, vector_InjectorNDegree.ExtractSubArrayShallow(new int[] { i * (basisIndex + 1) },new int[] {(i+1) * (basisIndex + 1) - 1 }));
            }

            return InjectorNDegree;
        }


        private static MultidimensionalArray GetEdgeGradient(Basis _dgBasis, AggregationGridData _aggGrd, int _edge, int _cell_i, int _cell_j, int _basisIndex)
        {
            MultidimensionalArray gradientsInt = MultidimensionalArray.Create(2, _basisIndex, 2, _basisIndex);
            MultidimensionalArray gradients = MultidimensionalArray.Create(2,_basisIndex + 1);

            for (int i = 1; i < _basisIndex + 1; i++)
            {
                //// compute the inverse of the transposed jacobian of the transformation for cell i
                //multidimensionalarray jac = _agggrd.jacobian.getvalue_cell(_agggrd.ancestorgrid.cells.getrefelement(_cell_i).vertices, _cell_i, 1).transpose().invertto();

                //// build the gradient of the i-th basis function
                //multidimensionalarray grad = multidimensionalarray.create(_dgbasis.griddat.spatialdimension);

                //// extract the normal in reference coordinates for the given edge on cell i
                //multidimensionalarray norm = _agggrd.ancestorgrid.cells.getrefelement(_cell_i).facenormals.extractsubarrayshallow(_agggrd.igeomedges.edge2celltrafoindex[_edge, 0], -1);

                //// compute (jac * grad, norm)
                //grad.multiply(1.0, jac, grad, 0.0, "i", "ij", "j");

                //MultidimensionalArray dummy_node = _maxDgBasis.GridDat.GlobalNodes.GetValue_Cell(vert_coord_i, cell_i, 1);
                //MultidimensionalArray Dummy = _maxDgBasis.CellEvalGradient(new NodeSet(_agGrd.iGeomCells.GetRefElement(cell_i), dummy_node.ExtractSubArrayShallow(new int[] { 0, 0, 0 }, new int[] { -1, 1, 1 })), cell_i, 1);

                // repeat for cell j
                //gradients[0, i - 1] = norm.InnerProduct(grad);

                //gradients.ExtractSubArrayShallow(0,-1).Multiply(1.0, jac, grad, "","","")
                //gradients[1, i];
            }

            return gradientsInt;
        }


    }
}
