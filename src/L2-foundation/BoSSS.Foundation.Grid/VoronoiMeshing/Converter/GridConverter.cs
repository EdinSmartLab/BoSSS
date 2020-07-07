﻿using BoSSS.Foundation.Grid.Classic;
using BoSSS.Foundation.Grid.RefElements;
using BoSSS.Platform;
using BoSSS.Platform.LinAlg;
using ilPSP;
using ilPSP.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace BoSSS.Foundation.Grid.Voronoi.Meshing.Converter
{
    class GridConverter<T>
        where T : IVoronoiNodeCastable
    {
        readonly VoronoiBoundary boundary;

        readonly BoundaryConverter boundaryConverter;

        public GridConverter(VoronoiBoundary boundary, PeriodicMap periodicMap = null)
        {
            this.boundary = boundary;
            boundaryConverter = new BoundaryConverter(boundary, periodicMap);
        }

        public VoronoiGrid ConvertToVoronoiGrid(
            IMesh<T> mesh)
        {
            boundaryConverter.Clear();
            (GridCommons grid, int[][] aggregation) = ExtractGridCommonsAndCellAggregation(mesh.Cells, boundaryConverter);
            VoronoiNodes nodes = ExtractVoronoiNodes(mesh);
            VoronoiGrid voronoiGrid = new VoronoiGrid(grid, aggregation, nodes, boundary);
            voronoiGrid.FirstCornerNodeIndice = mesh.CornerNodeIndice;

            return voronoiGrid;
        }

        static VoronoiNodes ExtractVoronoiNodes(IMesh<T> mesh)
        {
            IList<T> nodeList = mesh.Nodes;
            IList<VoronoiNode> voronoiNodeList = CastAsVoronoiNodes(nodeList);
            VoronoiNodes nodes = new VoronoiNodes(voronoiNodeList);
            return nodes;
        }

        static (GridCommons, int[][]) ExtractGridCommonsAndCellAggregation(
            IEnumerable<MeshCell<T>> cells,
            BoundaryConverter boundaryConverter)
        {
            List<BoSSS.Foundation.Grid.Classic.Cell> cellsGridCommons = new List<BoSSS.Foundation.Grid.Classic.Cell>();
            List<int[]> aggregation = new List<int[]>();

            foreach (MeshCell<T> cell in cells)
            {
                //Convert to BoSSSCell : Triangulate
                Vector[] VoronoiCell = cell.Vertices.Select(voVtx => voVtx.Position).ToArray();
                int[,] iVtxTri = PolygonTesselation.TesselatePolygon(VoronoiCell);
                int[] Agg2Pt = new int[iVtxTri.GetLength(0)];

                bool isBoundaryCell = IsBoundary(cell);

                for (int iTri = 0; iTri < iVtxTri.GetLength(0); iTri++)
                { // loop over triangles of voronoi cell
                    int iV0 = iVtxTri[iTri, 0];
                    int iV1 = iVtxTri[iTri, 1];
                    int iV2 = iVtxTri[iTri, 2];

                    Vector V0 = VoronoiCell[iV0];
                    Vector V1 = VoronoiCell[iV1];
                    Vector V2 = VoronoiCell[iV2];

                    Vector D1 = V1 - V0;
                    Vector D2 = V2 - V0;

                    if (D1.CrossProduct2D(D2) < 0)
                    {
                        int it = iV0;
                        iV0 = iV2;
                        iV2 = it;

                        Vector vt = V0;
                        V0 = V2;
                        V2 = vt;

                        D1 = V1 - V0;
                        D2 = V2 - V0;
                    }

                    Debug.Assert(D1.CrossProduct2D(D2) > 1.0e-8);

                    Cell Cj = new Cell()
                    {
                        GlobalID = cellsGridCommons.Count,
                        Type = CellType.Triangle_3,
                    };
                    Cj.TransformationParams = MultidimensionalArray.Create(3, 2);
                    Cj.TransformationParams.SetRowPt(0, V0);
                    Cj.TransformationParams.SetRowPt(1, V1);
                    Cj.TransformationParams.SetRowPt(2, V2);

                    Agg2Pt[iTri] = cellsGridCommons.Count;
                    cellsGridCommons.Add(Cj);

                    //Save BoundaryInformation
                    if (isBoundaryCell)
                    {
                        List<BoundaryFace> tags = GetBoundaryFacesOf(cell, iV0, iV1, iV2);
                        boundaryConverter.RegisterBoundaries(Cj, tags);
                    }
                    Cj.NodeIndices = new int[]
                    {
                        cell.Vertices[iV0].ID,
                        cell.Vertices[iV1].ID,
                        cell.Vertices[iV2].ID
                    };
                }
                aggregation.Add(Agg2Pt);
            }

            GridCommons grid = new Grid2D(Triangle.Instance)
            {
                Cells = cellsGridCommons.ToArray()
            };
            
            boundaryConverter.RegisterEdgesTo(grid);
            return (grid, aggregation.ToArray());
        }

        static void PrintEdgeTags(List<BoSSS.Foundation.Grid.Classic.Cell> cellsGridCommons)
        {
            using (StreamWriter sw = new StreamWriter("EdgeTags.txt"))
            {
                foreach (Cell cell in cellsGridCommons)
                {
                    for (int i = 0; i < (cell.CellFaceTags?.Length ?? 0); ++i)
                    {
                        CellFaceTag tag = cell.CellFaceTags[i];
                        double x = cell.TransformationParams[(tag.FaceIndex) % 3, 0]
                            + cell.TransformationParams[(tag.FaceIndex + 1) % 3, 0];
                        x /= 2;
                        double y = cell.TransformationParams[(tag.FaceIndex) % 3, 1]
                            + cell.TransformationParams[(tag.FaceIndex + 1) % 3, 1];
                        y /= 2;
                        sw.WriteLine($"{x}, {y}, {tag.EdgeTag}");
                    }
                }
            }
        }

        static List<BoundaryFace> GetBoundaryFacesOf(MeshCell<T> cell, int iV0, int iV1, int iV2)
        {
            //Indices are debug magic. FML
            List<BoundaryFace> tags = new List<BoundaryFace>(3);
            int max = cell.Edges.Length;
            if (iV0 + 1 == iV1 || iV0 - max + 1 == iV1)
            {
                IfIsBoundaryAddEdge2Tags(iV0, 0);
            }
            if (iV1 + 1 == iV2 || iV1 - max + 1 == iV2)
            {
                IfIsBoundaryAddEdge2Tags(iV1, 1);
            }
            if (iV2 + 1 == iV0 || iV2 - max + 1 == iV0)
            {
                IfIsBoundaryAddEdge2Tags(iV2, 2);
            }
            return tags;

            void IfIsBoundaryAddEdge2Tags(int iV, int iFace)
            {
                Edge<T> edge = cell.Edges[iV];
                if (edge.IsBoundary)
                {
                    BoundaryFace tag = new BoundaryFace
                    {
                        Face = iFace,
                        BoundaryEdgeNumber = edge.BoundaryEdgeNumber,
                        ID = edge.Start.ID,
                        NeighborID = edge.Twin.Start.ID
                    };
                    tags.Add(tag);
                }
            }
        }

        static bool IsBoundary(MeshCell<T> cell)
        {
            foreach (Edge<T> edge in cell.Edges)
            {
                if (edge.IsBoundary)
                    return true;
            }
            return false;
        }
        
        static IList<VoronoiNode> CastAsVoronoiNodes(IList<T> nodes)
        {
            IList<VoronoiNode> voronoiNodes = new List<VoronoiNode>(nodes.Count);
            for (int i = 0; i < nodes.Count; ++i)
            {
                voronoiNodes.Add(nodes[i].AsVoronoiNode());
            }
            return voronoiNodes;
        }
    }
}