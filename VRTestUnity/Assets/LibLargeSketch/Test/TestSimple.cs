#if UNITY_EDITOR
using System;
using System.Collections;
using UnityEngine;


namespace TestLibLargeSketch
{
    public class TestSimple : TestClass
    {
        GameObject go;

        public override void Dispose()
        {
            UnityEngine.Object.Destroy(go);
        }

        LargeSketch sketch
        {
            get
            {
                if (go == null)
                    go = Instantiate("large sketch.prefab");
                return go.GetComponent<LargeSketch>();
            }
        }

        int CountTris(int child)
        {
            var mesh = sketch.transform.GetChild(child).GetComponent<MeshFilter>().sharedMesh;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                if (mesh.GetTopology(submesh) == MeshTopology.Triangles)
                    return mesh.GetIndices(submesh).Length / 3;
            return 0;
        }

        int CountStems(int child)
        {
            var mesh = sketch.transform.GetChild(child).GetComponent<MeshFilter>().sharedMesh;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                if (mesh.GetTopology(submesh) == MeshTopology.Lines)
                    return mesh.GetIndices(submesh).Length / 2;
            return 0;
        }

        class MatBuild : LargeSketch.IMaterialBuilder
        {
            public Material mat, mat_alt;
            public Material GetMaterial() { return mat; }
            public Material GetAlternateMaterial() { return mat_alt; }
        }

        MatBuild NewMat(string mat_name, string mat_alt_name = null)
        {
            Material mat_alt = mat_alt_name == null ? null : (Material)LoadObject(mat_alt_name + ".mat");
            return new MatBuild { mat = (Material)LoadObject(mat_name + ".mat"), mat_alt = mat_alt };
        }

        int AddTriangle(MatBuild face_mat, float y, bool stems = false,
                        LargeSketch.Visibility visibility = LargeSketch.Visibility.Regular)
        {
            var builder = sketch.PrepareGeometry(face_mat, visibility: visibility);
            int bn = builder.positions.Count;
            builder.positions.Add(new Vector3(0, y, 0));
            builder.positions.Add(new Vector3(0, y, 1));
            builder.positions.Add(new Vector3(1, y, 0));
            builder.normals.Add(new Vector3(0, 1, 0));
            builder.normals.Add(new Vector3(0, 1, 0));
            builder.normals.Add(new Vector3(0, 1, 0));
            builder.positions.Add(new Vector3(0, y, 0));
            builder.positions.Add(new Vector3(0, y, 1));
            builder.positions.Add(new Vector3(1, y, 0));
            builder.normals.Add(new Vector3(0, -1, 0));
            builder.normals.Add(new Vector3(0, -1, 0));
            builder.normals.Add(new Vector3(0, -1, 0));
            builder.triangles.Add(bn);
            builder.triangles.Add(bn + 1);
            builder.triangles.Add(bn + 2);
            builder.triangles.Add(bn + 3);
            builder.triangles.Add(bn + 5);
            builder.triangles.Add(bn + 4);
            if (stems)
            {
                builder.stems.Add(bn);
                builder.stems.Add(bn + 1);
                builder.stems.Add(bn + 1);
                builder.stems.Add(bn + 2);
                builder.stems.Add(bn + 2);
                builder.stems.Add(bn);
            }
            return builder.id;
        }

        public IEnumerator TestOneTriangle()
        {
            AddTriangle(NewMat("Red"), 0);
            sketch.Flush();
            yield return null;
            Debug.Assert(sketch.transform.childCount == 1);
        }

        public IEnumerable TestTwoTriangles()
        {
            var red = NewMat("Red");
            AddTriangle(red, 1);
            AddTriangle(red, 2);
            sketch.Flush();
            yield return null;
            Debug.Assert(sketch.transform.childCount == 1);
            Debug.Assert(CountTris(0) == 4);
        }

        public IEnumerable TestTwoTimesOneTriangle()
        {
            var red = NewMat("Red");
            AddTriangle(red, 1);
            sketch.Flush();
            yield return null;
            AddTriangle(red, 2);
            sketch.Flush();
            yield return null;
            Debug.Assert(sketch.transform.childCount == 2);    /* == 1: future optimization */
            Debug.Assert(CountTris(0) == 2);
            Debug.Assert(CountTris(1) == 2);
        }

        public IEnumerable TestTwoTrianglesDifferentMat()
        {
            AddTriangle(NewMat("Red"), 1);
            AddTriangle(NewMat("Green"), 2);
            sketch.Flush();
            yield return null;
            Debug.Assert(sketch.transform.childCount == 2);
        }

        public IEnumerable TestManyTriangles()
        {
            var red = NewMat("Red");
            for (int i = 0; i < 6000; i++)
                AddTriangle(red, i / 6000f);    /* 6 vertices, total 36000 */
            sketch.Flush();
            yield return null;
            Debug.Assert(sketch.transform.childCount == 2);
        }

        public IEnumerable TestRemoveAll()
        {
            int id = AddTriangle(NewMat("Red"), 0.5f);
            Debug.Assert(id >= 1);
            sketch.Flush();
            yield return null;

            sketch.RemoveGeometry(id);
            sketch.Flush();
            yield return null;
            Debug.Assert(sketch.transform.childCount == 0);
        }

        public IEnumerable TestRemoveHalf()
        {
            var red = NewMat("Red");
            int id = AddTriangle(red, -0.5f);
            AddTriangle(red, 1);
            sketch.Flush();
            yield return null;

            sketch.RemoveGeometry(id);
            sketch.Flush();
            yield return null;
            Debug.Assert(sketch.transform.childCount == 1);
            Debug.Assert(CountTris(0) == 2);
        }

        public IEnumerable TestHideAll()
        {
            int id = AddTriangle(NewMat("Red"), -0.5f);
            sketch.Flush();
            yield return null;

            Debug.Assert(sketch.GetGeometryVisibility(id) == LargeSketch.Visibility.Regular);
            sketch.ChangeGeometryVisibility(id, LargeSketch.Visibility.Hidden);
            sketch.Flush();
            yield return null;
            Debug.Assert(sketch.transform.childCount == 0);

            Debug.Assert(sketch.GetGeometryVisibility(id) == LargeSketch.Visibility.Hidden);
            sketch.ChangeGeometryVisibility(id, LargeSketch.Visibility.Regular);
            sketch.Flush();
            yield return null;
            Debug.Assert(sketch.transform.childCount == 1);
            Debug.Assert(CountTris(0) == 2);
        }

        public IEnumerator TestTriangleWithStems()
        {
            AddTriangle(NewMat("Red"), 0, stems: true);
            sketch.Flush();
            yield return null;
            Debug.Assert(sketch.transform.childCount == 1);
        }

        public IEnumerable TestRemoveAllStems()
        {
            int id = AddTriangle(NewMat("Red"), 0.5f, stems: true);
            sketch.Flush();
            yield return null;

            sketch.RemoveGeometry(id);
            sketch.Flush();
            yield return null;
            Debug.Assert(sketch.transform.childCount == 0);
        }

        public IEnumerable TestHideAllStems()
        {
            int id = AddTriangle(NewMat("Red"), -0.5f, stems: true);
            sketch.Flush();
            yield return null;

            sketch.ChangeGeometryVisibility(id, LargeSketch.Visibility.Hidden);
            sketch.Flush();
            yield return null;
            Debug.Assert(sketch.transform.childCount == 0);

            sketch.ChangeGeometryVisibility(id, LargeSketch.Visibility.Regular);
            sketch.Flush();
            yield return null;
            Debug.Assert(sketch.transform.childCount == 1);
            Debug.Assert(CountTris(0) == 2);
        }

        public IEnumerable TestStemsOnly()
        {
            var builder = sketch.PrepareGeometryForStems();
            int bn = builder.positions.Count;
            builder.positions.Add(new Vector3(0, 0, 0));
            builder.positions.Add(new Vector3(0, 0, 1));
            builder.stems.Add(bn);
            builder.stems.Add(bn + 1);
            sketch.Flush();
            yield return null;

            Debug.Assert(sketch.transform.childCount == 1);
        }

        public IEnumerable TestChangeStemMaterial()
        {
            AddTriangle(NewMat("Red"), 0.5f, stems: true);
            sketch.Flush();
            yield return null;
            Debug.Assert(CountStems(0) == 3);

            var old_mat = sketch.stemMaterial;
            sketch.stemMaterial = null;
            yield return null;
            Debug.Assert(CountStems(0) == 0);

            sketch.stemMaterial = old_mat;
            yield return null;
            Debug.Assert(CountStems(0) == 3);
        }

        public IEnumerable TestMoveAround()
        {
            var red = NewMat("Red");
            int id = AddTriangle(red, 0);
            sketch.Flush();
            yield return null;
            Debug.Assert(sketch.transform.childCount == 1);

            sketch.RemoveGeometry(id);
            int id2 = AddTriangle(red, 0.5f);
            sketch.Flush();
            yield return null;
            Debug.Assert(sketch.transform.childCount == 1);

            sketch.RemoveGeometry(id2);
            AddTriangle(red, 1);
            sketch.Flush();
            yield return null;
            Debug.Assert(sketch.transform.childCount == 1);
        }

        public IEnumerable TestInitiallyHidden()
        {
            int id = AddTriangle(NewMat("Red"), 0, visibility: LargeSketch.Visibility.Hidden);
            sketch.Flush();
            yield return null;
            Debug.Assert(sketch.transform.childCount == 0);

            sketch.ChangeGeometryVisibility(id, LargeSketch.Visibility.Regular);
            sketch.Flush();
            yield return null;
            Debug.Assert(sketch.transform.childCount == 1);
        }
    }
}
#endif
