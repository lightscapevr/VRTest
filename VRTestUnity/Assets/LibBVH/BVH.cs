using System;
using System.Collections.Generic;
using UnityEngine;


namespace BVH
{
    /* A "Bounding Volume Hierarchy" implementation.  Inspired by this but rewritten from scratch
     * by reading descriptions from other sources:
     * https://www.codeproject.com/Articles/832957/Dynamic-Bounding-Volume-Hiearchy-in-Csharp
     */

    public struct BBox
    {
        /* Similar to UnityEngine.Bounds, but stores the min and the max instead of the center
         * and the size.  None of the logic in this file needs the center and the size, so it
         * avoids pointless conversions back and forth. */
        public readonly Vector3 min, max;
        public BBox(Vector3 min, Vector3 max) { this.min = min; this.max = max; }
        public BBox(Bounds b) { min = b.min; max = b.max; }
        public static explicit operator Bounds(BBox b) { Bounds b1 = new Bounds(); b1.SetMinMax(b.min, b.max); return b1; }

        public bool Contains(Vector3 pt) { return min.x <= pt.x && pt.x <= max.x && min.y <= pt.y && pt.y <= max.y && min.z <= pt.z && pt.z <= max.z; }
        public bool Contains(BBox bbox) { return min.x <= bbox.min.x && bbox.max.x <= max.x && min.y <= bbox.min.y && bbox.max.y <= max.y && min.z <= bbox.min.z && bbox.max.z <= max.z; }
        public bool Touches(BBox bbox) { return min.x <= bbox.max.x && bbox.min.x <= max.x && min.y <= bbox.max.y && bbox.min.y <= max.y && min.z <= bbox.max.z && bbox.min.z <= max.z; }
    }

    public struct BBoxBuilder
    {
        bool any;
        Vector3 min, max;

        public void Add(Vector3 pt)
        {
            if (any)
            {
                min = Vector3.Min(min, pt);
                max = Vector3.Max(max, pt);
            }
            else
            {
                min = pt;
                max = pt;
                any = true;
            }
        }

        public BBox GetBBox()
        {
            return new BBox(min, max);
        }
    }


    public class BVH
    {
        public BVHNodeBase root;

        BVHNode most_recent_node;
        System.Random _rand = new System.Random();
        uint _r;
        int _bits;
        bool _NextRandomBit()
        {
            if (_bits == 0)
            {
                _r = (uint)_rand.Next(1 << 26);
                _bits = 26;
            }
            _bits--;
            bool _result = (_r & 1) != 0;
            _r >>= 1;
            return _result;
        }

        public void AddObject(BVHNodeBase new_object)
        {
            if (root == null)
            {
                new_object.bvh_parent = null;
                root = new_object;
                return;
            }

            // 0. start at 'most_recent_node', and go up as long as its
            // bounding box does not contain new_object's bounding box.
            BVHNodeBase walk = most_recent_node;
            if (walk == null)
                walk = root;
            else
                while (walk != root)
                {
                    if (walk.bbox.Contains(new_object.bbox))
                        break;
                    walk = walk.bvh_parent;
                }

            // 1. first we traverse the node looking for the best leaf
            float newObSAH = SA(new_object);

            while (walk is BVHNode)
            {
                var curNode = (BVHNode)walk;

                // find the best way to add this object.. 3 options..
                // 1. send to left node  (L+N,R)
                // 2. send to right node (L,R+N)
                // 3. merge and pushdown left-and-right node (L+R,N)
                // we tend to avoid option 3 by the 0.3f factor below, because it means
                // that an unknown number of nodes get their depth increased.

                /* first a performance hack which also helps to randomly even out the
                 * two sides in case 'new_object' is between both the bounding box of
                 * 'left' and of 'right'
                 */
                var right = curNode.right;
                bool contains_right = right.bbox.Contains(new_object.bbox);
                var left = curNode.left;
                if (left.bbox.Contains(new_object.bbox))
                {
                    if (contains_right && _NextRandomBit())
                        walk = right;
                    else
                        walk = left;
                    continue;
                }
                else if (contains_right)
                {
                    walk = right;
                    continue;
                }

                float leftSAH = SA(left);
                float rightSAH = SA(right);
                float sendLeftSAH = rightSAH + SA(left, new_object);    // (L+N,R)
                float sendRightSAH = leftSAH + SA(right, new_object);   // (L,R+N)
                float mergedLeftAndRightSAH = SA(left, right) + newObSAH; // (L+R,N)

                if (mergedLeftAndRightSAH < 0.3f * Mathf.Min(sendLeftSAH, sendRightSAH))
                {
                    break;
                }
                else
                {
                    if (sendLeftSAH < sendRightSAH)
                        walk = left;
                    else
                        walk = right;
                }
            }

            // 2. then we add the object and map it to our leaf
            BVHNode parent = walk.bvh_parent;
            most_recent_node = parent;
            var new_node = new BVHNode { bbox = walk.bbox, left = walk, right = new_object, bvh_parent = parent };
            walk.bvh_parent = new_node;
            new_object.bvh_parent = new_node;

            if (parent == null)
            {
                Debug.Assert(walk == root);
                root = new_node;
            }
            else if (parent.left == walk)
                parent.left = new_node;
            else
                parent.right = new_node;

            UpdateBounds(new_node);
        }

        public void RemoveObject(BVHNodeBase old_object)
        {
            most_recent_node = null;

            if (old_object == root)
            {
                root = null;
                return;
            }

            BVHNodeBase keep;
            BVHNode parent = old_object.bvh_parent;
            if (parent.left == old_object)
                keep = parent.right;
            else
                keep = parent.left;

            BVHNode grandparent = parent.bvh_parent;
            keep.bvh_parent = grandparent;
            if (grandparent == null)
                root = keep;
            else
            {
                if (grandparent.left == parent)
                    grandparent.left = keep;
                else
                    grandparent.right = keep;
                UpdateBounds(grandparent);
            }
        }

        float SA(BVHNodeBase node)
        {
            return SA(node.bbox.max - node.bbox.min);
        }

        float SA(Vector3 size)
        {
            return size.x * (size.y + size.z) + size.y * size.z;
        }

        float SA(BVHNodeBase n1, BVHNodeBase n2)    /* return the SA() of the union */
        {
            return SA(Vector3.Max(n1.bbox.max, n2.bbox.max) - Vector3.Min(n1.bbox.min, n2.bbox.min));
        }

        void UpdateBounds(BVHNode node)
        {
            while (true)
            {
                Vector3 min = Vector3.Min(node.left.bbox.min, node.right.bbox.min);
                Vector3 max = Vector3.Max(node.left.bbox.max, node.right.bbox.max);
                if (min != node.bbox.min || max != node.bbox.max)
                {
                    node.bbox = new BBox(min, max);
                    node = node.bvh_parent;
                    if (node == null)
                        break;
                }
                else
                    break;
            }
        }

        struct LocNext : IComparable<LocNext>
        {
            internal readonly float distance;
            internal readonly BVHNodeBase node;

            internal LocNext(float distance, BVHNodeBase node)
            {
                this.distance = distance;
                this.node = node;
            }

            public int CompareTo(LocNext other)
            {
                return distance.CompareTo(other.distance);
            }
        }

        public delegate float DistanceToNode(BVHNodeBase node_base, float current_distance_max);
        public delegate bool SelectNode(BVHNodeBase node_base);

        /// <summary>
        /// Search in the BVH tree using a flexible "locator" delegate, which should return the
        /// "distance" of a BVH Node object.
        /// </summary>
        /// <param name="distance">The maximum distance to look for.</param>
        /// <param name="locator">A delegate.  A typical implementation has got two cases: if
        /// given an instance of a concrete class that the caller knows about, it should return
        /// the distance to that; otherwise, it should return the distance to the bounding box.
        /// The present algorithm assumes that when a bounding box grows, the distance returned
        /// by "locator" decreases or stays constant.</param>
        /// <returns>A instance of the concrete BVHNodeBase subclass.</returns>
        public BVHNodeBase Locate(float distance_max, DistanceToNode locator)
        {
            if (!(root is BVHNode))
            {
                if (root != null)
                {
                    float distance = locator(root, distance_max);
                    if (distance < distance_max)
                        return root;
                }
                return null;
            }
            var heapq = new HeapQ<LocNext>();
            BVHNodeBase base_node = root;

            while (true)
            {
                if (base_node is BVHNode)
                {
                    var node = (BVHNode)base_node;

                    float d_left = locator(node.left, distance_max);
                    if (d_left < distance_max)
                    {
                        if (!(node.left is BVHNode))
                            distance_max = d_left;
                        heapq.Push(new LocNext(d_left, node.left));
                    }

                    float d_right = locator(node.right, distance_max);
                    if (d_right < distance_max)
                    {
                        if (!(node.right is BVHNode))
                            distance_max = d_right;
                        heapq.Push(new LocNext(d_right, node.right));
                    }
                }
                else
                {
                    return base_node;
                }
                if (heapq.Empty)
                    break;
                LocNext next = heapq.Pop();
                base_node = next.node;
            }
            return null;
        }

        public void LocateSelect(SelectNode locator)
        {
            if (!(root is BVHNode))
            {
                if (root != null)
                    locator(root);
                return;
            }
            var stack = new Stack<BVHNode>();
            BVHNode node = (BVHNode)root;

            while (true)
            {
                bool right = locator(node.right);
                bool left = locator(node.left);

                if (left)
                {
                    if (right)
                        stack.Push((BVHNode)node.right);
                    node = (BVHNode)node.left;
                }
                else
                {
                    if (right)
                        node = (BVHNode)node.right;
                    else
                    {
                        if (stack.Count == 0)
                            break;
                        node = stack.Pop();
                    }
                }
            }
        }

#if false
        /// <summary>
        /// Search in the BVH tree using a flexible "locator" object, which should return the
        /// "distance" of a BVH Node object.  Returns a list of results in increasing distance
        /// up to a relative bound and absolute maximum.
        /// </summary>
        /// <param name="distance_max">The maximum distance to look for.</param>
        /// <param name="relative_bound">Stop returning more items after 'relative_bound' times
        /// the first item's distance.</param>
        /// <param name="locator">A delegate, like Locate() but passed also the current 'distance_max'.</param>
        /// <returns>A list of instances of the concrete BVHNodeBase subclass.</returns>
        public List<BVHNodeBase> LocateList(float distance_max, float relative_bound, DistanceToNodeEx locator)
        {
            var output_list = new List<BVHNodeBase>();

            if (!(root is BVHNode))
            {
                if (root != null)
                {
                    float distance = locator(root, distance_max);
                    if (distance < distance_max)
                        output_list.Add(root);
                }
                return output_list;
            }
            var heapq = new HeapQ<LocNext>();
            BVHNodeBase base_node = root;
            float distance_1 = 0;

            while (distance_1 < distance_max)
            {
                if (base_node is BVHNode)
                {
                    var node = (BVHNode)base_node;

                    float d_left = locator(node.left, distance_max);
                    if (d_left < distance_max)
                    {
                        if (!(node.left is BVHNode))
                            distance_max = Mathf.Min(distance_max, d_left * relative_bound);
                        heapq.Push(new LocNext(d_left, node.left));
                    }

                    float d_right = locator(node.right, distance_max);
                    if (d_right < distance_max)
                    {
                        if (!(node.right is BVHNode))
                            distance_max = Mathf.Min(distance_max, d_right * relative_bound);
                        heapq.Push(new LocNext(d_right, node.right));
                    }
                }
                else
                {
                    output_list.Add(base_node);
                }
                if (heapq.Empty)
                    break;
                LocNext next = heapq.Pop();
                distance_1 = next.distance;
                base_node = next.node;
            }
            return output_list;
        }
#endif

        public BVHNodeBase RayCast(float distance_max, Ray ray)
        {
            return Locate(distance_max, (node_base, _) =>
            {
                Bounds b = (Bounds)node_base.bbox;
                float distance;
                if (!b.IntersectRay(ray, out distance))
                    distance = float.PositiveInfinity;
                return distance;
            });
        }

        public BVHNodeBase Closest(float distance_max, Vector3 origin)
        {
            var result = Locate(distance_max * distance_max, (node_base, _) =>
            {
                Bounds b = (Bounds)node_base.bbox;
                return b.SqrDistance(origin);
            });
            return result;
        }

        public BBox? GetBounds()
        {
            if (root == null)
                return null;
            return root.bbox;
        }
    }

    public abstract class BVHNodeBase
    {
        public BBox bbox;
        public BVHNode bvh_parent;
    }

    public class BVHNode : BVHNodeBase
    {
        public BVHNodeBase left, right;
    }
}
