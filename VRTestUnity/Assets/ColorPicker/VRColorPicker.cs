using System;
using System.Collections.Generic;
using UnityEngine;


public class VRColorPicker : MonoBehaviour
{
    /* Public API: read and write the color in the gamma color space.  The gamma space
     * is the one in which colors you pick from the editor are usually sent; they are
     * transformed into linear space for the GPU, like texture colors are.  (This is
     * all assuming default settings.)  If you want linear colors, use the Linear
     * version of these functions.
     *
     * Note that rendering in the gamma color space gives nicer and "more expected"
     * color ranges.  That's why conversion is applied inside the fragment part of
     * VertexColMask32.shader: the computed *gamma* color is a linear interpolation
     * between pixels, which is turned into *linear* at each pixel.  This is different
     * from doing a linear interpolation on *linear* colors directly (gives a much
     * more white-ish result).
     */
    public Color GetGammaColor() { return col_gamma; }
    public void SetGammaColor(Color col_gamma) { ApplyColor(col_gamma); }
    public Color GetLinearColor() { return col_gamma.linear; }
    public void SetLinearColor(Color col_linear) { ApplyColor(col_linear.gamma); }

    /* There is no built-in interaction.  You need to invoke these methods at the
     * appropriate place for your own UI system.  The names start with "Mouse" but
     * it is usually about some kind of VR controller.
     */
    public void MouseOver(IEnumerable<Vector3> world_positions) { DoHover(world_positions); }
    public void MouseDrag(Vector3 world_position) { DoDrag(world_position); }
    public void MouseRelease() { DoRelease(); }


    /****************************************************************************/


    public Renderer sphere;
    public Material sphereSilhouette, sphereSilhouetteNearby;

    const float CENTRAL_HOLE = 0.3f;
    Color col_gamma = Color.white;

    private void Start()
    {
        mesh = new Mesh();
        ApplyColor(col_gamma);
        PrepareTrianglesOnMesh();
        GetComponent<MeshFilter>().sharedMesh = mesh;
    }

    void DoHover(IEnumerable<Vector3> world_positions)
    {
        Vector3 local_target = Col2Vector3(col_gamma);
        Material mat = sphereSilhouette;
        foreach (var pos in world_positions)
        {
            Vector3 local_pos = transform.InverseTransformPoint(pos);
            if (Vector3.Distance(local_pos, local_target) < 0.25f)
                mat = sphereSilhouetteNearby;
        }
        SetSphereSilhouette(mat);
    }

    void SetSphereSilhouette(Material mat)
    {
        var mats = sphere.sharedMaterials;
        mats[1] = mat;
        sphere.sharedMaterials = mats;
    }

    void DoDrag(Vector3 world_position)
    {
        var local_pt = transform.InverseTransformPoint(world_position);
        UpdateForPoint(local_pt);
        SetSphereSilhouette(sphereSilhouette);
    }

    void DoRelease()
    {
        ApplyColor(col_gamma);
    }

    static Vector3 Col2Vector3(Color col)
    {
        float h, s, v;
        Color.RGBToHSV(col, out h, out s, out v);
        h *= Mathf.PI * 2;
        s *= Mathf.Sqrt(1 - (1 - v) * (1 - v));
        if (s > 0f)
            s += CENTRAL_HOLE;
        return new Vector3(Mathf.Cos(h) * s, v, Mathf.Sin(h) * s);
    }

    void UpdateForPoint(Vector3 local_pt, bool up = false)
    {
        Vector3 v3 = local_pt - Vector3.up;     /* center at the origin */
        Vector2 v2 = new Vector2(v3.x, v3.z);
        float mag = v2.magnitude;
        if (mag > CENTRAL_HOLE)
            v2 *= ((mag - CENTRAL_HOLE) / mag);
        else
            v2 = Vector2.zero;
        v3.x = v2.x;
        v3.z = v2.y;
        if (v3.y <= 0f && v3.magnitude >= 1f)
            v3.Normalize();
        float v = Mathf.Clamp(v3.y + 1, 0f, 1f);
        float h = Mathf.Atan2(v3.z, v3.x);
        float s = Mathf.Clamp(new Vector2(v3.x, v3.z).magnitude, 0f, 1f);

        h = (h / (Mathf.PI * 2) + 2f) % 1f;
        if (v < 1e-5)
            s = 0;
        else
            s /= Mathf.Sqrt(1 - (1 - v) * (1 - v));
        Color col = Color.HSVToRGB(h, s, v);

        Vector3 lineto_pt = Col2Vector3(col);
        if (Vector3.Distance(local_pt, lineto_pt) > 0.08f)
        {
            const int RES = 4;

            col = Color.white;
            float distance_min_2 = float.PositiveInfinity;
            for (int r = 0; r <= RES; r++)
            {
                Color check_col = new Color(r / (float)RES, 0, 0);
                for (int g = 0; g <= RES; g++)
                {
                    check_col.g = g / (float)RES;
                    for (int b = 0; b <= RES; b++)
                    {
                        if (r == 0 || r == RES || g == 0 || g == RES || b == 0 || b == RES ||
                            ((r == g) && (g == b)))
                        {
                            check_col.b = b / (float)RES;
                            float distance_2 = (local_pt - Col2Vector3(check_col)).sqrMagnitude;
                            if (distance_2 < distance_min_2)
                            {
                                distance_min_2 = distance_2;
                                col = check_col;
                            }
                        }
                    }
                }
            }
            lineto_pt = Col2Vector3(col);
            if (up)
                local_pt = lineto_pt;
        }
        else
            local_pt = lineto_pt;

        ApplyColor(col, local_pt, lineto_pt);
    }

    void ApplyColor(Color col)
    {
        Vector3 pt = Col2Vector3(col);
        ApplyColor(col, pt, pt);
    }

    void ApplyColor(Color col, Vector3 local_pt, Vector3 lineto_pt)
    {
        /* use SetColor(), which asks Unity to do the sRGB-to-linear correction */
        col_gamma = col;
        sphere.material.SetColor("_Color1", col);

        sphere.transform.localPosition = local_pt;
        if (mesh != null)
            PrepareMesh(local_pt, lineto_pt);
    }


    const int MESH_WIDTH = 60;
    const int MESH_HEIGHT = 15;
    Mesh mesh;

    Vector3 GetPoint(int i, int j, float height = 1)
    {
        float frac = Mathf.Min(j / (float)MESH_HEIGHT, height);
        float breadth = Mathf.Sqrt(1 - (1 - frac) * (1 - frac));
        breadth += CENTRAL_HOLE;

        float alpha = i * (Mathf.PI * 2 / MESH_WIDTH);
        return new Vector3(Mathf.Cos(alpha) * breadth, frac, Mathf.Sin(alpha) * breadth);
    }

    const int middle_idx = 2 * MESH_WIDTH * (MESH_HEIGHT + 1) + 2;

    void PrepareMesh(Vector3 local_pt, Vector3 lineto_pt)
    {
        float height = lineto_pt.y;
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var colors = new List<Color>();

        for (int j = 0; j <= MESH_HEIGHT; j++)
        {
            for (int i = 0; i < MESH_WIDTH; i++)
            {
                var pt = GetPoint(i, j);
                vertices.Add(pt);
                normals.Add(-pt);
                colors.Add(Color.HSVToRGB(i / (float)MESH_WIDTH, 1, pt.y));
                pt = GetPoint(i, j, height);
                vertices.Add(pt);
                normals.Add(pt);
                colors.Add(Color.HSVToRGB(i / (float)MESH_WIDTH, 1, pt.y));
            }
        }

        vertices.Add(local_pt); vertices.Add(lineto_pt);
        normals.Add(Vector3.up); normals.Add(Vector3.up);
        colors.Add(Color.black); colors.Add(Color.black);

        Debug.Assert(middle_idx == vertices.Count);
        Debug.Assert(middle_idx == normals.Count);
        Debug.Assert(middle_idx == colors.Count);

        for (int i = 0; i < MESH_WIDTH; i++)
        {
            float angle = i * (Mathf.PI * 2 / MESH_WIDTH);
            vertices.Add(new Vector3(Mathf.Cos(angle) * CENTRAL_HOLE, height, Mathf.Sin(angle) * CENTRAL_HOLE));
            normals.Add(Vector3.up);
            colors.Add(Color.HSVToRGB(0, 0, height));
        }

        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetColors(colors);     /* no sRGB-to-linear conversion is applied here: sent as sRGB */
    }

    void PrepareTrianglesOnMesh()
    {
        mesh.subMeshCount = 5;
        Func<int, List<int>> make_triangles = (delta) =>
        {
            var triangles = new List<int>();
            for (int j = 0; j < MESH_HEIGHT; j++)
                for (int i = 0; i < MESH_WIDTH; i++)
                {
                    int dx = 2;
                    int dy = 2 * MESH_WIDTH;
                    int b = delta + j * dy + i * dx;
                    if (i == MESH_WIDTH - 1) dx *= (1 - MESH_WIDTH);
                    if (delta == 1) { int tmp = dx; dx = dy; dy = tmp; }
                    triangles.Add(b);
                    triangles.Add(b + dx);
                    triangles.Add(b + dy);
                    triangles.Add(b + dx);
                    triangles.Add(b + dx + dy);
                    triangles.Add(b + dy);
                }
            if (delta == 0)
            {
                int prev_i0 = MESH_WIDTH - 1;
                for (int i0 = 0; i0 < MESH_WIDTH; i0++)
                {
                    triangles.Add(1 + 2 * prev_i0);
                    triangles.Add(1 + 2 * i0);
                    triangles.Add(middle_idx + prev_i0);
                    triangles.Add(middle_idx + prev_i0);
                    triangles.Add(1 + 2 * i0);
                    triangles.Add(middle_idx + i0);
                    prev_i0 = i0;
                }
            }
            return triangles;
        };
        mesh.SetTriangles(make_triangles(0), 0);
        mesh.SetTriangles(make_triangles(1), 1);

        var triangles2 = new List<int>();
        int b0 = 1 + 2 * MESH_HEIGHT * MESH_WIDTH;
        int prev_i = MESH_WIDTH - 1;
        for (int i = 0; i < MESH_WIDTH; i++)
        {
            triangles2.Add(b0 + 2 * prev_i);
            triangles2.Add(middle_idx + prev_i);
            triangles2.Add(b0 + 2 * i);
            triangles2.Add(middle_idx + prev_i);
            triangles2.Add(middle_idx + i);
            triangles2.Add(b0 + 2 * i);
            prev_i = i;
        }
        mesh.SetTriangles(triangles2, 2);

        var stems = new List<int>();
        prev_i = MESH_WIDTH - 1;
        for (int i = 0; i < MESH_WIDTH; i++)
        {
            stems.Add(b0 + 2 * prev_i);
            stems.Add(b0 + 2 * i);
            stems.Add(b0 + 2 * prev_i - 1);
            stems.Add(b0 + 2 * i - 1);
            stems.Add(middle_idx + prev_i);
            stems.Add(middle_idx + i);
            prev_i = i;
        }
        for (int i = 0; i < MESH_WIDTH; i += MESH_WIDTH / 6)
        {
            stems.Add(b0 + 2 * i);
            stems.Add(middle_idx + i);
            int b1 = 2 * i;
            for (int j = 0; j < MESH_HEIGHT; j++)
            {
                stems.Add(b1);
                b1 += 2 * MESH_WIDTH;
                stems.Add(b1);
            }
        }
        mesh.SetIndices(stems.ToArray(), MeshTopology.Lines, 3);

        mesh.SetIndices(new int[] { middle_idx - 2, middle_idx - 1 }, MeshTopology.Lines, 4);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (GetComponent<MeshFilter>().sharedMesh != null)
            return;
        if (mesh == null)
        {
            mesh = new Mesh();
            PrepareMesh(Vector3.up, Vector3.up);
            PrepareTrianglesOnMesh();
        }
        Gizmos.DrawMesh(mesh, 0, transform.position, transform.rotation, transform.lossyScale);
        Gizmos.DrawMesh(mesh, 1, transform.position, transform.rotation, transform.lossyScale);
        Gizmos.DrawMesh(mesh, 3, transform.position, transform.rotation, transform.lossyScale);
    }
#endif
}
