﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BoSSS.Foundation.Grid.Voronoi.Meshing
{
    class BoundaryCutter<T>
        where T :IMesherNode, new()
    {
        MeshIntersecter<T> meshIntersecter;

        BoundaryLineEnumerator boundary;

        CutterState<Edge<T>> state;

        FirstCell firstCell;

        OnEdgeCutter<T> edgeCutter;

        class FirstCell
        {
            public List<BoundaryLine> linesFirstCell = null;

            public Edge<T> firstCellCutRidge = default(Edge<T>);
        }

        public void CutOut(
            IDMesh<T> mesh,
            BoundaryLineEnumerator boundary,
            int firstCellNode_indice)
        {
            Initialize(mesh, boundary, firstCellNode_indice);

            IEnumerator<Edge<T>> edgeEnumerator = FirstCellEdgeEnumerator();
            List<BoundaryLine> lines = new List<BoundaryLine>(10);
            BoundaryLine activeLine = default(BoundaryLine);

            bool firstCut = true;
            while (boundary.MoveNext())
            {
                bool circleCells = true;
                while (circleCells)
                {
                    circleCells = false;
                    activeLine = boundary.Current;
                    ResetState();

                    //Check for intersection of Cell and Line
                    while (edgeEnumerator.MoveNext() && !circleCells)
                    {
                        circleCells = FindIntersection(edgeEnumerator.Current, activeLine);
                    }
                    //Handle intersection
                    if (firstCut)
                    {
                        edgeEnumerator = TryFirstCut(ref circleCells, edgeEnumerator);
                        //Start with empty line set
                        if (state.Case != IntersectionCase.NotIntersecting)
                        {
                            firstCut = false;
                            FinishFirstCut(lines);
                            lines.Clear();
                        }
                    }
                    else
                    {
                        edgeEnumerator = TryCut(ref circleCells, edgeEnumerator, lines);
                        if (state.Case != IntersectionCase.NotIntersecting)
                        {
                            lines.Clear();
                        }
                    }
                }
                lines.Add(activeLine);
            }
            HandleLastCell();
            boundary.Reset();
            meshIntersecter.RemoveOutsideCells();
        }

        void Initialize(
            IDMesh<T> mesh,
            BoundaryLineEnumerator boundary,
            int firstCellNode_indice)
        {
            this.meshIntersecter = new MeshIntersecter<T>(mesh, firstCellNode_indice);
            this.boundary = boundary;
            state = new CutterState<Edge<T>>();
            edgeCutter = new OnEdgeCutter<T>(this.meshIntersecter, boundary);
            firstCell = null;
        }

        IEnumerator<Edge<T>> FirstCellEdgeEnumerator()
        {
            IEnumerator<Edge<T>> ridgeEnum;
            boundary.MoveNext();
            ridgeEnum = meshIntersecter.GetFirst(boundary.Current);
            boundary.Reset();
            return ridgeEnum;
        }

        void ResetState()
        {
            state.Case = IntersectionCase.NotIntersecting;
            state.ActiveEdge = default(Edge<T>);
            state.AlphaCut = default(double);
        }

        IEnumerator<Edge<T>> TryFirstCut(ref bool circleCells, IEnumerator<Edge<T>> ridgeEnum)
        {
            IEnumerator<Edge<T>> runningEnum;
            Edge<T> activeRidge = state.ActiveEdge;

            //First cut ever
            //-----------------------------------------------------------
            switch (state.Case)
            {
                case IntersectionCase.NotIntersecting:
                    ridgeEnum.Reset();
                    break;
                case IntersectionCase.InMiddle:
                    //if intersection was successfull, select next cell
                    activeRidge = meshIntersecter.FirstCut(activeRidge, state.AlphaCut);
                    ridgeEnum = meshIntersecter.GetNeighborFromEdgeNeighbor(activeRidge);
                    break;
                case IntersectionCase.EndOfLine:
                    activeRidge = meshIntersecter.FirstCut(activeRidge, state.AlphaCut);
                    if (boundary.MoveNext())
                    {
                        runningEnum = meshIntersecter.GetConnectedRidgeEnum(activeRidge);
                        ridgeEnum = edgeCutter.CutOut(runningEnum, activeRidge);
                    }
                    else
                    {
                        circleCells = false;
                    }
                    break;
                case IntersectionCase.EndOfRidge:
                    meshIntersecter.VertexCut(activeRidge, state.AlphaCut);
                    runningEnum = meshIntersecter.GetConnectedRidgeEnum(activeRidge);
                    ridgeEnum = edgeCutter.CutOut(runningEnum, activeRidge);
                    break;
                case IntersectionCase.EndOfRidgeAndLine:
                    meshIntersecter.VertexCut(activeRidge, state.AlphaCut);
                    if (boundary.MoveNext())
                    {
                        runningEnum = meshIntersecter.GetConnectedRidgeEnum(activeRidge);
                        ridgeEnum = edgeCutter.CutOut(runningEnum, activeRidge);
                    }
                    else
                    {
                        circleCells = false;
                    }
                    break;
                default:
                    throw new Exception();
            }
            return ridgeEnum;
        }

        void FinishFirstCut(List<BoundaryLine> lines)
        {
            //Information needed for the last cell, when boundary is closed
            firstCell = new FirstCell
            {
                firstCellCutRidge = state.ActiveEdge,
                linesFirstCell = new List<BoundaryLine>(lines)
            };
            if (firstCell.linesFirstCell.Count == 0)
            {
                firstCell.linesFirstCell.Add(boundary.Current);
            }
        }

        IEnumerator<Edge<T>> TryCut(
            ref bool circleCells, 
            IEnumerator<Edge<T>> ridgeEnum, 
            List<BoundaryLine> lines)
        {
            IEnumerator<Edge<T>> runningEnum;
            Edge<T> activeRidge = state.ActiveEdge;
            //All other cuts and subdivisions etc.
            //-----------------------------------------------------------
            switch (state.Case)
            {
                case IntersectionCase.NotIntersecting:
                    ridgeEnum.Reset();
                    break;
                case IntersectionCase.InMiddle:
                    //if intersection was successfull, select next cell
                    activeRidge = meshIntersecter.Subdivide(activeRidge, lines, state.AlphaCut, boundary.LineIndex);
                    ridgeEnum = meshIntersecter.GetNeighborFromEdgeNeighbor(activeRidge);
                    break;
                case IntersectionCase.EndOfLine:
                    activeRidge = meshIntersecter.Subdivide(activeRidge, lines, state.AlphaCut, boundary.LineIndex);
                    if (boundary.MoveNext())
                    {
                        runningEnum = meshIntersecter.GetConnectedRidgeEnum(activeRidge);
                        ridgeEnum = edgeCutter.CutOut(runningEnum, activeRidge);
                    }
                    else
                    {
                        circleCells = false;
                    }
                    break;
                case IntersectionCase.EndOfRidge:
                    activeRidge = meshIntersecter.SubdivideWithoutNewVertex(activeRidge, lines, boundary.LineIndex);
                    runningEnum = meshIntersecter.GetConnectedRidgeEnum(activeRidge);
                    ridgeEnum = edgeCutter.CutOut(runningEnum, activeRidge);
                    break;
                case IntersectionCase.EndOfRidgeAndLine:
                    activeRidge = meshIntersecter.SubdivideWithoutNewVertex(activeRidge, lines, boundary.LineIndex);
                    if (boundary.MoveNext())
                    {
                        runningEnum = meshIntersecter.GetConnectedRidgeEnum(activeRidge);
                        ridgeEnum = edgeCutter.CutOut(runningEnum, activeRidge);
                    }
                    else
                    {
                        circleCells = false;
                    }
                    break;
                default:
                    throw new NotImplementedException();
            }
            return ridgeEnum;
            
        }

        bool FindIntersection(Edge<T> edge, BoundaryLine line)
        {
            state.ActiveEdge = edge;
            bool foundIntersection = LineIntersection.Intersect(edge, line, ref state.Case, out state.AlphaCut);
            return foundIntersection;
        }
        
        void HandleLastCell()
        {
            //Handle last cell
            //-----------------------------------------------------------
            switch (state.Case)
            {
                case IntersectionCase.NotIntersecting:
                    meshIntersecter.CloseMesh(firstCell.linesFirstCell, firstCell.firstCellCutRidge, boundary.LineIndex);
                    break;
                case IntersectionCase.EndOfLine:
                case IntersectionCase.EndOfRidgeAndLine:
                    break;
                case IntersectionCase.EndOfRidge:
                case IntersectionCase.InMiddle:
                default:
                    throw new Exception();
            }
        }
    }
}
