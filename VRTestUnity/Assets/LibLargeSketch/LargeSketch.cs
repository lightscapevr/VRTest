using System;
using System.Collections.Generic;
using UnityEngine;


/// <summary>
/// Displays a sketch consisting of triangles and stems.  Stems are edges, or line
/// segments, which may be drawn around the polygons or float on their own.  This
/// is optimized to handle very large sketches and supports relatively quick updates
/// (but not each-and-every-frame updates).
/// </summary>
/// <remark>
/// Despite what the API may suggest, this is optimized to be efficient for quite a
/// large amount of geometry.  This class is meant to be general and a bit low-level,
/// in that it receives triangles only; it does not handle the triangulation of more
/// general polygons.
///
/// Additions and removals of geometry might or might not be visible before Flush() is
/// called.  Disabling the whole GameObject during initial construction might be a way
/// to avoid partial results from showing up, if that is a problem.
///
/// All public methods can be called from a different thread than the main one, but
/// should not be called from multiple threads in parallel for the same LargeSketch
/// instance.  Use a lock if that could be the case.
///
/// If you need to minimize Z-fighting between different materials, one solution is to use
/// a material that comes with a Z offset and use a different offset for different
/// materials.  Also, the rendering of stems (edges) is sometimes hidden behind the
/// material of the triangles they are an edge of.  One solution to that is again Z offsets:
/// if the Z offset in the stem materials is always greater than the Z offset of the face
/// materials (e.g. because the latter is negative), the edges will be drawn on top.
///
/// Note that you can run into Unity limitations if the X, Y or Z values of 'positions'
/// are greater than a few thousand.  For example, in Unity 2017.3, if you use very large
/// values and then try to correct it by setting a very small scale on the GameObject,
/// it might render completely black.  More fundamentally, even if the scale is reasonable,
/// if all values are centered around a point that is many kilometers away from the origin,
/// then you'll run into precision issues in the 'float' type.  In theory, you should check
/// and fix both cases by storing internally values as a 3D 'double', and
/// scaling/offsetting them when converting to a Vector3.  (VR-Sketch-4 stores them as
/// 'double', but doesn't so far apply scaling/offsetting.)
/// </remark>

public class LargeSketch : MonoBehaviour
{
    [Tooltip("The prefab used to make meshes.")]
    public GameObject largeSketchMeshPrefab;

    [Tooltip("The material to use for stems.  Set to null to hide all stems.  Can be changed at runtime.")]
    public Material stemMaterial;

    [Tooltip("The alternate material to use for stems.  For 'Visibility.Alternate'.")]
    public Material stemAlternateMaterial;

    /// <summary>
    /// The Materials can only be manipulated in the main Unity thread, so we use opaque
    /// objects with the MaterialBuilder interface and we convert them to regular Material
    /// objects in the main thread only.  The second function is called if we use
    /// Visibility.Alternate.
    /// </summary>
    public interface IMaterialBuilder
    {
        Material GetMaterial();
        Material GetAlternateMaterial();
    }

    /// <summary>
    /// Interface that you can put on MonoBehaviours attached to the largeSketchMeshPrefabs.
    /// It lets you know when a specific largeSketchMesh was modified.
    /// </summary>
    public interface IMeshModified
    {
        void Modified();
    }

    /// <summary>
    /// Calls GetMaterial() again on all MaterialBuilder, to update the sketch.
    /// </summary>
    public void InvalidateBuildMaterials()
    {
        rebuild_materials = true;
    }

    /// <summary>
    /// Prepare new geometry with the given Material for the faces.
    /// 
    /// This method returns a GeometryBuilder on which you can add vertices and then create
    /// triangles or stems between them.  You could try to reuse previous vertices on the
    /// same GeometryBuilder, but it's not something VR-Sketch-4 does.
    /// 
    /// The returned GeometryBuilder structure also contains a unique id for this geometry,
    /// which can be saved and passed to ChangeMaterials() or RemoveGeometry().
    /// 
    /// Note that performance does NOT require you to draw many triangles inside a single
    /// PrepareGeometry() call.  Typically, every two-sided polygon is done with two calls to
    /// PrepareGeometry(), corresponding to the two sides.  The edges are added as stems
    /// during one of the two calls, not during extra calls, to reuse the vertices.  If the
    /// two sides have the same material then they can be combined into a single call to
    /// further reuse the vertices.  But apart from easy vertex reuse, there is no point in
    /// combining calls.
    /// 
    /// If you call with 'texture: true', the GeometryBuilder will have a non-null 'uvs' list
    /// into which you have to add UV coordinates for every vertex.
    /// </summary>
    public GeometryBuilder PrepareGeometry(IMaterialBuilder face_mat, bool texture = false, Visibility visibility = Visibility.Regular)
    {
        MeshBuilder builder;
        var builders = texture ? mesh_builders_with_texture : mesh_builders;
        if (!builders.TryGetValue(face_mat, out builder) || TryFinish(builder))
        {
            builder = new MeshBuilder(this, face_mat, texture ? 2 : 1);
            builders[face_mat] = builder;
        }
        return new GeometryBuilder
        {
            id = NextGeomId(builder, visibility),
            positions = builder.positions,
            normals = builder.normals,
            uvs = builder.uvs,
            triangles = builder.triangles,
            stems = builder.stems,
        };
    }

    /// <summary>
    /// Same as PrepareGeometry(), but returns a StemsGeometryBuilder, which does not have
    /// the 'normals' or 'triangles' list.  For the free-floating stems in the model.
    ///
    /// Note that there is no PrepareGeometryXxx() method specifically for drawing
    /// triangles but not stems.  Just use 'PrepareGeometry(face_mat)' and never add
    /// any stem; this is basically just as efficient.
    /// </summary>
    public StemGeometryBuilder PrepareGeometryForStems(Visibility visibility = Visibility.Regular)
    {
        MeshBuilder builder = mesh_builder_for_stems;
        if (builder == null || TryFinish(builder))
        {
            builder = new MeshBuilder(this, null, 0);
            mesh_builder_for_stems = builder;
        }
        return new StemGeometryBuilder
        {
            id = NextGeomId(builder, visibility),
            positions = builder.positions,
            stems = builder.stems,
        };
    }


    public struct GeometryBuilder
    {
        /// <summary> Unique id > 0. </summary>
        public int id;

        /// <summary>
        /// List of vertices.  After PrepareGeometry(), you add your vertex positions here.
        /// </summary>
        public List<Vector3> positions;

        /// <summary>
        /// List of normals.  After PrepareGeometry(), you add your vertex normals here.
        /// This list must always have the same length as 'positions'.
        /// </summary>
        public List<Vector3> normals;

        /// <summary>
        /// Integer list of triangles.  Put three numbers for each triangle, which are
        /// indexes in the 'positions' and 'normals' lists.
        /// </summary>
        public List<int> triangles;

        /// <summary>
        /// Integer list of stems.  Put two numbers for each stem, which are indexes
        /// in the 'positions' list.
        /// </summary>
        public List<int> stems;

        /// <summary>
        /// List of UV coordinates, only from PrepareGeometryWithTexture().  This list must
        /// always have the same length as 'positions' and 'normals'.
        /// </summary>
        public List<Vector2> uvs;
    }


    public struct StemGeometryBuilder
    {
        /// <summary> Unique id > 0. </summary>
        public int id;
        /// <summary> See GeometryBuilder.positions </summary>
        public List<Vector3> positions;
        /// <summary> See GeometryBuilder.stems </summary>
        public List<int> stems;
    }


    /// <summary>
    /// Removes the geometry added after the corresponding PrepareGeometry() call.
    /// 
    /// After calling RemoveGeometry() the id is invalid and should not be used any more.
    /// It could be reused by future PrepareGeometry() calls (even before the next
    /// Flush(), in multithreaded situations).
    /// </summary>
    public void RemoveGeometry(int id)
    {
        update_renderer_gindex.Add(geometries[id].gindex & ~GINDEX_MASK);
        geometries[id].gindex = GINDEX_DESTROYED;
    }

    public enum Visibility
    {
        Regular = (int)GINDEX_REG,
        Hidden = (int)GINDEX_HIDDEN,
        Alternate = (int)GINDEX_ALT,
    };

    /// <summary>
    /// Temporarily change the visibility of the geometry added after the corresponding
    /// PrepareGeometry() call.
    /// 
    /// The visibility can be Regular (default), Hidden, or Alternate (uses the alternate
    /// materials for surface and stems).
    /// </summary>
    public void ChangeGeometryVisibility(int id, Visibility visibility)
    {
        uint gindex = geometries[id].gindex & ~GINDEX_MASK;
        update_renderer_gindex.Add(gindex);
        geometries[id].gindex = gindex | (uint)visibility;
    }


    /// <summary>
    /// Return the current visibility.
    /// </summary>
    public Visibility GetGeometryVisibility(int id)
    {
        uint result = geometries[id].gindex & GINDEX_MASK;
        return (Visibility)result;
    }


    public delegate void OnReady();

    /// <summary>
    /// Schedule flushing of the pending additions, removals, and shows/hides.
    /// The actual construction or updating of meshes will occur during the next
    /// LateUpdate().
    /// 
    /// If 'onReady' is given, then this will be called in the main thread after
    /// the construction is complete.
    /// </summary>
    public void Flush(OnReady onReady = null)
    {
        foreach (var builder in mesh_builders.Values)
            Finish(builder);
        mesh_builders.Clear();

        foreach (var builder in mesh_builders_with_texture.Values)
            Finish(builder);
        mesh_builders_with_texture.Clear();

        if (mesh_builder_for_stems != null)
        {
            Finish(mesh_builder_for_stems);
            mesh_builder_for_stems = null;
        }

        if (update_renderer_gindex.Count > 0)
        {
            var lst = new List<uint>(update_renderer_gindex);
            EnqueueUpdater(new RendererUpdater(lst));
            update_renderer_gindex.Clear();
        }

        if (onReady != null)
            EnqueueUpdater(new ReadyUpdater(onReady));
    }


    /***************************** IMPLEMENTATION *****************************/


    interface IMeshUpdater
    {
        void MainThreadUpdate(LargeSketch sketch, object tag);
    }

    class MeshBuilder : IMeshUpdater
    {
        internal IMaterialBuilder face_mat;
        internal List<Vector3> positions;
        internal List<Vector3> normals;
        internal List<Vector2> uvs;
        internal List<int> triangles;
        internal List<int> stems;

        internal List<int> geom_ids;
        internal int current_geom_id = -1;
        internal int renderer_index;
        internal int[] triangles_final, stems_final;

        internal MeshBuilder(LargeSketch sketch, IMaterialBuilder face_mat, int level)
        {
            this.face_mat = face_mat;

            positions = new List<Vector3>();
            if (level >= 1) normals = new List<Vector3>();
            if (level >= 2) uvs = new List<Vector2>();
            triangles = new List<int>();    // if level == 0, should remain empty
            stems = new List<int>();

            geom_ids = new List<int>();
            renderer_index = sketch.NewRendererIndex();
        }

        public void MainThreadUpdate(LargeSketch sketch, object tag)
        {
            var mgos = sketch.mgos;
            int index = renderer_index;
            while (!(index < mgos.Count))
                mgos.Add(null);
            Debug.Assert(mgos[index] == null);
            mgos[index] = new MeshGameObject(sketch, this, tag);
        }
    }

    class RendererUpdater : IMeshUpdater
    {
        List<uint> renderers;

        internal RendererUpdater(List<uint> renderers)
        {
            this.renderers = renderers;
        }

        public void MainThreadUpdate(LargeSketch sketch, object tag)
        {
            foreach (var gindex in renderers)
            {
                Debug.Assert((gindex & GINDEX_MASK) == 0);

                int renderer_index = (int)(gindex >> GINDEX_SHIFT);
                var mgo = sketch.mgos[renderer_index];
                if (mgo == null)
                    continue;
                mgo.UpdateRendering(tag);

                if (mgo.IsDefinitelyEmpty())
                {
                    mgo.DestroyMesh();
                    sketch.mgos[renderer_index] = null;
                    sketch.AddFreeRendererIndex(renderer_index);
                }
            }
        }
    }

    class ReadyUpdater : IMeshUpdater
    {
        OnReady onReady;

        internal ReadyUpdater(OnReady onReady)
        {
            this.onReady = onReady;
        }

        public void MainThreadUpdate(LargeSketch sketch, object tag)
        {
            onReady();
        }
    }


    const uint GINDEX_REG = 0;
    const uint GINDEX_FREE = 1;
    const uint GINDEX_HIDDEN = 2;
    const uint GINDEX_ALT = 3;

    const uint GINDEX_MASK = 0x03;
    const int GINDEX_SHIFT = 2;

    const uint GINDEX_DESTROYED = (0xffffffff << GINDEX_SHIFT) | GINDEX_HIDDEN;

    struct Geom
    {
        internal uint gindex;
        internal ushort triangle_start, triangle_stop;
        internal ushort stem_start, stem_stop;

        /* Each of the ranges stores information in two 16-bit values: the 'start' 16 bits are
         * the index of the starting point (counted as whole triangles/stems, not as indices
         * inside the 'triangles' or 'stems' list); and the 'stop' 16 bits are the first value
         * after the range.  If stops are 0xFFFF, then it means actually that the range goes up
         * to the end, which may be below or above 65535.  The start is always below 65535.
         */
    }

    Dictionary<IMaterialBuilder, MeshBuilder> mesh_builders = new Dictionary<IMaterialBuilder, MeshBuilder>();
    Dictionary<IMaterialBuilder, MeshBuilder> mesh_builders_with_texture = new Dictionary<IMaterialBuilder, MeshBuilder>();
    MeshBuilder mesh_builder_for_stems = null;
    HashSet<uint> update_renderer_gindex = new HashSet<uint>();
    Queue<IMeshUpdater> mesh_builders_ready = new Queue<IMeshUpdater>();
    volatile bool mesh_builders_ready_flag;
    bool rebuild_materials;

    Geom[] geometries;
    int geom_free_head = 0;
    int geom_free_head_mainthread = 0;
    object geometries_lock = new object();  // protects the 'geometries' and 'geom_free_head_mainthread' fields


    int NextGeomId(MeshBuilder builder, Visibility visibility)
    {
        int result = geom_free_head;
        if (result <= 0)
            result = FillMoreGeoms();
        geom_free_head = (int)(geometries[result].gindex >> GINDEX_SHIFT);

        geometries[result].gindex = (((uint)builder.renderer_index) << GINDEX_SHIFT) | (uint)visibility;
        geometries[result].triangle_start = (ushort)((uint)builder.triangles.Count / 3);
        geometries[result].stem_start = (ushort)((uint)builder.stems.Count / 2);
        builder.geom_ids.Add(result);
        builder.current_geom_id = result;
        return result;
    }

    int FillMoreGeoms()
    {
        lock (geometries_lock)
        {
            if (geom_free_head_mainthread != 0)
            {
                geom_free_head = geom_free_head_mainthread;
                geom_free_head_mainthread = 0;
            }
            else
            {
                int cnt;
                if (geometries == null)
                {
                    cnt = 1;    /* entry 0 is never used */
                    geometries = new Geom[48];
                }
                else
                {
                    cnt = geometries.Length;
                    Array.Resize(ref geometries, cnt * 3 / 2);
                }
                for (int i = geometries.Length - 1; i >= cnt; i--)
                {
                    geometries[i].gindex = (((uint)geom_free_head) << GINDEX_SHIFT) | GINDEX_FREE;
                    geom_free_head = i;
                }
            }
        }
        Debug.Assert(geom_free_head > 0);
        return geom_free_head;
    }

    bool TryFinish(MeshBuilder builder)
    {
        uint n1 = (uint)builder.positions.Count;
        uint n2 = (uint)builder.triangles.Count / 3;
        uint n3 = (uint)builder.stems.Count / 2;
        if ((n1 | n2 | n3) > 0x7FFF)
        {
            Finish(builder);
            return true;
        }
        else
        {
            int id = builder.current_geom_id;
            builder.current_geom_id = -1;
            geometries[id].triangle_stop = (ushort)n2;
            geometries[id].stem_stop = (ushort)n3;
            return false;
        }
    }

    void Finish(MeshBuilder builder)
    {
        int id = builder.current_geom_id;
        builder.current_geom_id = -1;
        geometries[id].triangle_stop = 0xFFFF;
        geometries[id].stem_stop = 0xFFFF;

        Debug.Assert(builder.normals == null || builder.positions.Count == builder.normals.Count);
        Debug.Assert(builder.uvs == null || builder.positions.Count == builder.uvs.Count);
        Debug.Assert((uint)builder.triangles.Count % 3 == 0);
        Debug.Assert((uint)builder.stems.Count % 2 == 0);

        builder.triangles_final = builder.triangles.ToArray(); builder.triangles = null;
        builder.stems_final = builder.stems.ToArray(); builder.stems = null;

        EnqueueUpdater(builder);
    }

    void EnqueueUpdater(IMeshUpdater updater)
    {
        lock (mesh_builders_ready)
            mesh_builders_ready.Enqueue(updater);
        mesh_builders_ready_flag = true;
    }


    Stack<int> mgos_free_list = new Stack<int>();
    int mgos_allocated_count = 0;

    int NewRendererIndex()
    {
        int result;
        lock (mgos_free_list)
        {
            if (mgos_free_list.Count > 0)
                result = mgos_free_list.Pop();
            else
                result = mgos_allocated_count++;
        }
        return result;
    }

    void AddFreeRendererIndex(int renderer_index)
    {
        lock (mgos_free_list)
            mgos_free_list.Push(renderer_index);
    }


    /***************************** Main thread gameobject parts *****************************/
    /***             Everything below is accessed only by the main thread.                ***/


    List<MeshGameObject> mgos = new List<MeshGameObject>();


    class MeshGameObject
    {
        LargeSketch sketch;
        Mesh mesh;
        MeshRenderer renderer;
        List<int> geom_ids;
        IMaterialBuilder face_mat_builder;
        Material stem_mat, stem_alt_mat, face_mat, face_alt_mat;
        int[] all_triangles, all_stems;
        object current_tag;


        internal MeshGameObject(LargeSketch sketch, MeshBuilder builder, object tag)
        {
            this.sketch = sketch;
            geom_ids = builder.geom_ids;
            face_mat_builder = builder.face_mat;
            all_triangles = builder.triangles_final;
            all_stems = builder.stems_final;

            mesh = new Mesh();

            if (builder.positions.Count >= 0xFFE0)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(builder.positions);

            if (builder.normals != null)
                mesh.SetNormals(builder.normals);

            if (builder.uvs != null)
                mesh.SetUVs(0, builder.uvs);

            RefreshRenderer(tag);
        }

        void RefreshRenderer(object tag)
        {
            List<int> triangles = new List<int>(capacity: all_triangles.Length);
            List<int> stems = new List<int>(capacity: all_stems.Length);
            List<int> alt_triangles = new List<int>();
            List<int> alt_stems = new List<int>();

            lock (sketch.geometries_lock)
            {
                var geometries = sketch.geometries;

                for (int i = geom_ids.Count - 1; i >= 0; --i)
                {
                    int geom_id = geom_ids[i];
                    Geom geom = geometries[geom_id];
                    /* Concurrent writes to 'geom' could occur, but only the 'gindex' field, and
                     * only to add/remove the GINDEX_HIDDEN flag or to change it to GINDEX_DESTROYED.
                     * If that occurs, this MeshGameObject will be scheduled for another update
                     * at the next Flush() anyway.
                     */
                    List<int> triangles1, stems1;
                    uint gmask = geom.gindex & GINDEX_MASK;

                    if (gmask == GINDEX_REG)
                    {
                        /* Geometry is visible */
                        triangles1 = triangles;
                        stems1 = stems;
                    }
                    else if (gmask == GINDEX_ALT)
                    {
                        triangles1 = alt_triangles;
                        stems1 = alt_stems;
                    }
                    else
                    {
                        if (geom.gindex == GINDEX_DESTROYED)
                        {
                            /* Geom was destroyed */
                            int last = geom_ids.Count - 1;
                            geom_ids[i] = geom_ids[last];
                            geom_ids.RemoveAt(last);

                            geometries[geom_id].gindex =
                                (((uint)sketch.geom_free_head_mainthread) << GINDEX_SHIFT) | GINDEX_FREE;
                            sketch.geom_free_head_mainthread = geom_id;
                        }
                        continue;
                    }

                    int start, stop;
                    start = geom.triangle_start * 3;
                    stop = geom.triangle_stop * 3;
                    if (stop == 0xFFFF * 3)
                        stop = all_triangles.Length;
                    for (int j = start; j < stop; j++)
                        triangles1.Add(all_triangles[j]);

                    start = geom.stem_start * 2;
                    stop = geom.stem_stop * 2;
                    if (stop == 0xFFFF * 2)
                        stop = all_stems.Length;
                    for (int j = start; j < stop; j++)
                        stems1.Add(all_stems[j]);
                }
            }
            geom_ids.TrimExcess();

            bool any_triangle_1 = triangles.Count > 0;
            bool any_triangle_2 = alt_triangles.Count > 0;
            bool any_stem_1 = (stems.Count > 0 && sketch.stemMaterial != null);
            bool any_stem_2 = (alt_stems.Count > 0 && sketch.stemAlternateMaterial != null);
            stem_mat = sketch.stemMaterial;
            stem_alt_mat = sketch.stemAlternateMaterial;
            face_mat = null;
            face_alt_mat = null;

            if (any_triangle_1 || any_stem_1 || any_triangle_2 || any_stem_2)
            {
                mesh.subMeshCount = (any_triangle_1 ? 1 : 0) + (any_stem_1 ? 1 : 0) +
                                    (any_triangle_2 ? 1 : 0) + (any_stem_2 ? 1 : 0);
                Material[] mats = new Material[mesh.subMeshCount];
                int submesh = 0;

                if (any_triangle_1)
                {
                    mesh.SetTriangles(triangles, submesh, calculateBounds: false);
                    face_mat = face_mat_builder.GetMaterial();
                    mats[submesh] = face_mat;
                    submesh++;
                }
                if (any_stem_1)
                {
                    mesh.SetIndices(stems.ToArray(), MeshTopology.Lines, submesh, calculateBounds: false);
                    mats[submesh] = stem_mat;
                    submesh++;
                }
                if (any_triangle_2)
                {
                    mesh.SetTriangles(alt_triangles, submesh, calculateBounds: false);
                    face_alt_mat = face_mat_builder.GetAlternateMaterial();
                    mats[submesh] = face_alt_mat;
                    submesh++;
                }
                if (any_stem_2)
                {
                    mesh.SetIndices(alt_stems.ToArray(), MeshTopology.Lines, submesh, calculateBounds: false);
                    mats[submesh] = stem_alt_mat;
                    submesh++;
                }

                mesh.RecalculateBounds();

                if (renderer == null)
                {
                    var go = Instantiate(sketch.largeSketchMeshPrefab, sketch.transform);
                    go.GetComponent<MeshFilter>().sharedMesh = mesh;
                    renderer = go.GetComponent<MeshRenderer>();
                }
                renderer.sharedMaterials = mats;

                foreach (var script in renderer.GetComponents<IMeshModified>())
                    script.Modified();
            }
            else
            {
                if (renderer != null)
                {
                    Destroy(renderer.gameObject);
                    renderer = null;
                }
            }
            current_tag = tag;
        }

        internal bool IsDefinitelyEmpty()
        {
            return geom_ids.Count == 0;
        }

        internal void DestroyMesh()
        {
            Destroy(mesh);
        }

        internal void UpdateIfMaterialChanged(object tag)
        {
            /* face_mat and face_alt_mat are copies of the face_mat_builder.GetXxxMaterial(),
             * or null if we didn't actually render any such face.  If we didn't then it doesn't
             * matter if it changed.
             * 
             * On the other hand, stem_mat and stem_alt_mat are usually null because
             * sketch.stemXxxMaterial was null previously.  So even if they are null,
             * if sketch.stemXxxMaterial is no longer null then we need to refresh.
             */
            if (sketch.stemMaterial == stem_mat &&
                sketch.stemAlternateMaterial == stem_alt_mat &&
                (face_mat == null || face_mat_builder.GetMaterial() == face_mat) &&
                (face_alt_mat == null || face_mat_builder.GetAlternateMaterial() == face_alt_mat))
                return;

            RefreshRenderer(tag);
        }

        internal void UpdateRendering(object tag)
        {
            /* only refresh if 'tag' differs, i.e. if we didn't already refresh during this
             * call to LateUpdate() */
            if (tag != current_tag)
                RefreshRenderer(tag);
        }

        internal void _ExtractMeshGeometry(MeshExtractor extractor, Geom geom, int renderer_index)
        {
            var vertices = extractor.vertices[renderer_index];
            if (vertices == null)
                vertices = extractor.vertices[renderer_index] = mesh.vertices;

            var normals = extractor.normals[renderer_index];
            if (normals == null)
                normals = extractor.normals[renderer_index] = mesh.normals;

            int tstart, tstop;
            tstart = geom.triangle_start * 3;
            tstop = geom.triangle_stop * 3;
            if (tstop == 0xFFFF * 3)
                tstop = all_triangles.Length;

            int sstart, sstop;
            sstart = geom.stem_start * 2;
            sstop = geom.stem_stop * 2;
            if (sstop == 0xFFFF * 2)
                sstop = all_stems.Length;

            /* xxx this assumes that each independent geom_id was built from its
             * own set of new vertices, without reusing any of the previous vertices;
             * otherwise, we're going to get large ranges in min,max and a lot of
             * duplication if we're extracting a mesh with many geom_ids */
            int min = 0x7fffffff, max = -1;
            for (int j = tstart; j < tstop; j++)
            {
                if (all_triangles[j] < min) min = all_triangles[j];
                if (all_triangles[j] > max) max = all_triangles[j];
            }
            for (int j = sstart; j < sstop; j++)
            {
                if (all_stems[j] < min) min = all_stems[j];
                if (all_stems[j] > max) max = all_stems[j];
            }

            int diff = extractor.out_vertices.Count - min;
            for (int i = min; i <= max; i++)
            {
                extractor.out_vertices.Add(vertices[i]);
                extractor.out_normals.Add(i < normals.Length ? normals[i] : Vector3.zero);
            }
            for (int j = tstart; j < tstop; j++)
                extractor.out_triangles.Add(all_triangles[j] + diff);
            for (int j = sstart; j < sstop; j++)
                extractor.out_stems.Add(all_stems[j] + diff);
        }
    }


    Material old_stem_mat, old_stem_alt_mat;

    public void CallRegularUpdate()
    {
        if (old_stem_mat != stemMaterial || old_stem_alt_mat != stemAlternateMaterial)
            rebuild_materials = true;

        if (!mesh_builders_ready_flag && !rebuild_materials)
            return;

        List<IMeshUpdater> updaters;

        lock (mesh_builders_ready)
        {
            updaters = new List<IMeshUpdater>(mesh_builders_ready);
            mesh_builders_ready.Clear();
            mesh_builders_ready_flag = false;
        }

        object tag = new object();
        foreach (var updater in updaters)
            updater.MainThreadUpdate(this, tag);

        if (rebuild_materials)
        {
            foreach (var mgo in mgos)
                if (mgo != null)
                    mgo.UpdateIfMaterialChanged(tag);
            old_stem_mat = stemMaterial;
            old_stem_alt_mat = stemAlternateMaterial;
            rebuild_materials = false;
        }
    }


    /***************************** LargeSketchUpdater object *****************************/
    /***  A separate GameObject just to ensure LateUpdate() runs even if the original  ***/
    /***  LargeSketch is disabled                                                      ***/


    class LargeSketchUpdater : MonoBehaviour
    {
        internal LargeSketch sketch;

        private void LateUpdate()
        {
            sketch.CallRegularUpdate();
        }
    }
    LargeSketchUpdater updater;

    private void Awake()
    {
        var go = new GameObject(gameObject.name + " (updater)");
        updater = go.AddComponent<LargeSketchUpdater>();
        updater.sketch = this;
    }

    private void OnDestroy()
    {
        if (updater != null)
        {
            DestroyImmediate(updater.gameObject);
            updater = null;
        }
    }


    /********************* Extracting geometries into a regular Mesh *********************/

    class MeshExtractor
    {
        internal Vector3[][] vertices, normals;   /* caches */
        internal List<Vector3> out_vertices = new List<Vector3>();
        internal List<Vector3> out_normals = new List<Vector3>();
        internal List<int> out_triangles = new List<int>();
        internal List<int> out_stems = new List<int>();
    }

    public Mesh ExtractMesh(IEnumerable<int> geom_ids)
    {
        /* Returns a single Mesh containing all geometry from the geom_ids (except the UV
         * coordinates).  The mesh consists of two submeshes: the triangles, and the stems.
         * We fill them with dummies if they are empty, to simplify the callers.
         */
        CallRegularUpdate();

        lock (geometries_lock)
        {
            var extractor = new MeshExtractor();
            extractor.vertices = new Vector3[mgos.Count][];
            extractor.normals = new Vector3[mgos.Count][];

            foreach (var geom_id in geom_ids)
            {
                Geom geom = geometries[geom_id];
                uint gmask = geom.gindex & GINDEX_MASK;
                if (gmask != GINDEX_FREE)
                {
                    int renderer_index = (int)(geom.gindex >> GINDEX_SHIFT);
                    var mgo = mgos[renderer_index];
                    if (mgo != null)
                        mgo._ExtractMeshGeometry(extractor, geom, renderer_index);
                }
            }

            if (extractor.out_vertices.Count == 0)
            {
                extractor.out_vertices.Add(Vector3.zero);
                extractor.out_normals.Add(Vector3.zero);
            }
            if (extractor.out_triangles.Count == 0)
            {
                extractor.out_triangles.Add(0);
                extractor.out_triangles.Add(0);
                extractor.out_triangles.Add(0);
            }
            if (extractor.out_stems.Count == 0)
            {
                extractor.out_stems.Add(0);
                extractor.out_stems.Add(0);
            }

            var mesh = new Mesh();
            if (extractor.out_vertices.Count >= 0xFFE0)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(extractor.out_vertices);
            mesh.SetNormals(extractor.out_normals);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(extractor.out_triangles, 0, calculateBounds: false);
            mesh.SetIndices(extractor.out_stems.ToArray(), MeshTopology.Lines, 1, calculateBounds: false);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
