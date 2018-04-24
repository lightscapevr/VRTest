using System;
using System.Collections.Generic;
using UnityEngine;
using BaroqueUI;


public class ColorPicker : MonoBehaviour
{
    public Renderer sphere;

    const float CENTRAL_HOLE = 0.3f;


    private void Start()
    {
        mesh = new Mesh();
        UpdateForPoint(new Vector3(0, 0.6f, 0));
        PrepareTrianglesOnMesh();

        var ht = Controller.HoverTracker(this);
        ht.onTriggerDrag += Ht_onTriggerDrag;
        ht.onTriggerUp += Ht_onTriggerUp;
    }

    private void Ht_onTriggerDrag(Controller controller)
    {
        var local_pt = transform.InverseTransformPoint(controller.position);
        UpdateForPoint(local_pt);
    }

    private void Ht_onTriggerUp(Controller controller)
    {
        var local_pt = transform.InverseTransformPoint(controller.position);
        UpdateForPoint(local_pt, up: true);
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

        /* can't use SetColor(), because we don't want the gamma correction */
        sphere.material.SetVector("_Color1", new Vector4(col.r, col.g, col.b, 1));

        sphere.transform.localPosition = local_pt;
        PrepareMesh(local_pt, lineto_pt);
    }

#if false
    void PrepareTexture()
    {
        const int TEX_SIZE = 128;

        var pixels = new Color[TEX_SIZE * TEX_SIZE];
        for (int j = 0; j < TEX_SIZE; j++)
            for (int i = 0; i < TEX_SIZE; i++)
                pixels[j * TEX_SIZE + i] = Color.HSVToRGB(i / (float)TEX_SIZE, 1, j / (float)(TEX_SIZE - 1));

        var tex0 = new Texture2D(TEX_SIZE, TEX_SIZE);
        tex0.wrapModeU = TextureWrapMode.Repeat;
        tex0.wrapModeV = TextureWrapMode.Clamp;
        tex0.SetPixels(pixels);
        tex0.Apply(updateMipmaps: true, makeNoLongerReadable: true);

        for (int j = 0; j < TEX_SIZE; j++)
            for (int i = 0; i < TEX_SIZE; i++)
                pixels[j * TEX_SIZE + i] = Color.HSVToRGB(i / (float)TEX_SIZE, j / (float)(TEX_SIZE - 1), 1);

        var tex2 = new Texture2D(TEX_SIZE, TEX_SIZE);
        tex2.wrapModeU = TextureWrapMode.Repeat;
        tex2.wrapModeV = TextureWrapMode.Clamp;
        tex2.SetPixels(pixels);
        tex2.Apply(updateMipmaps: true, makeNoLongerReadable: true);

        var renderer = GetComponent<Renderer>();
        var matlist = renderer.materials;   /* make a copy */
        matlist[0].SetTexture("_MainTex", tex0);
        matlist[2].SetTexture("_MainTex", tex2);
        renderer.materials = matlist;
    }
#endif

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
        mesh.SetColors(colors);
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

        GetComponent<MeshFilter>().sharedMesh = mesh;
    }
}
