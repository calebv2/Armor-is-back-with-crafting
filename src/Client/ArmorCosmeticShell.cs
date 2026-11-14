using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayerArmorMod
{
    // Fit in ATT's cosmetic bind space so the result keeps the original
    // vertices' skin weights and follows the player through animation.
    internal sealed class ArmorCosmeticShell
    {
        const float Clearance = .004f;
        const float MinimumRadius = .006f;
        readonly Node root;
        readonly Vector3 center;
        readonly Vector3 axis;

        internal ArmorCosmeticShell(List<Triangle> triangles, Vector3 center, Vector3 axis)
        {
            if (triangles.Count > 0) root = new Node(triangles.ToArray(), 0, triangles.Count);
            this.center = center;
            this.axis = axis.normalized;
        }

        internal bool TryTuck(Vector3 point, out Vector3 fitted)
        {
            fitted = point;
            if (root == null) return false;
            Vector3 origin = center + Vector3.Dot(point - center, axis) * axis;
            Vector3 radial = point - origin;
            float radius = radial.magnitude;
            if (radius < MinimumRadius) return false;
            var ray = new Ray(origin, radial / radius);
            float shellDistance = radius + Clearance;
            if (!root.FindSurface(ray, ref shellDistance)) return false;
            float innerRadius = Mathf.Max(MinimumRadius, shellDistance - Clearance);
            if (innerRadius >= radius) return false;
            fitted = ray.GetPoint(innerRadius);
            return true;
        }

        internal static ArmorCosmeticShell Build(ArmorSlot slot, Transform anchor, Renderer[] renderers,
            Dictionary<Transform, int> indices, Matrix4x4[] bindPoses)
        {
            if (anchor == null || !indices.TryGetValue(anchor, out var anchorIndex)) return null;
            var center = bindPoses[anchorIndex].inverse.MultiplyPoint3x4(Vector3.zero);
            // A helmet also covers the crown. Cast from the head center so
            // the fit follows the cap instead of stopping below its top.
            Vector3 axis = slot == ArmorSlot.Head ? Vector3.zero : Vector3.forward;
            if (slot == ArmorSlot.Head) center += Vector3.forward * .055f;
            if (slot == ArmorSlot.Chest || slot == ArmorSlot.Hips) center.y = 0f;
            string joint = null;
            switch (slot)
            {
                case ArmorSlot.LeftShoulder: joint = "elbow_L_jnt"; break;
                case ArmorSlot.RightShoulder: joint = "elbow_R_jnt"; break;
                case ArmorSlot.LeftForearm: joint = "wrist_L_jnt"; break;
                case ArmorSlot.RightForearm: joint = "wrist_R_jnt"; break;
            }
            if (joint != null)
                foreach (var bone in indices)
                    if (bone.Key.name == joint)
                    {
                        var direction = bindPoses[bone.Value].inverse.MultiplyPoint3x4(Vector3.zero) - center;
                        if (direction.sqrMagnitude > .000001f) axis = direction;
                        break;
                    }

            var triangles = new List<Triangle>();
            foreach (var renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (renderer is SkinnedMeshRenderer skin)
                {
                    var mesh = skin.sharedMesh;
                    if (mesh != null && mesh.isReadable)
                        AddSkinned(triangles, skin, mesh, indices, bindPoses);
                    continue;
                }
                var filter = renderer.GetComponent<MeshFilter>();
                var source = filter == null ? null : filter.sharedMesh;
                if (source == null || !source.isReadable) continue;
                Matrix4x4 transform = Matrix4x4.identity;
                bool found = false;
                for (var parent = renderer.transform; parent != null; parent = parent.parent)
                {
                    if (!indices.TryGetValue(parent, out var index)) continue;
                    transform = bindPoses[index].inverse * parent.worldToLocalMatrix * renderer.localToWorldMatrix;
                    found = true;
                    break;
                }
                if (!found) continue;
                var points = source.vertices;
                for (int i = 0; i < points.Length; i++) points[i] = transform.MultiplyPoint3x4(points[i]);
                AddTriangles(triangles, source, points);
            }
            return triangles.Count == 0 ? null : new ArmorCosmeticShell(triangles, center, axis);
        }

        static void AddSkinned(List<Triangle> destination, SkinnedMeshRenderer skin, Mesh mesh,
            Dictionary<Transform, int> indices, Matrix4x4[] bindPoses)
        {
            var sourceBones = skin.bones;
            var sourcePoses = mesh.bindposes;
            var weights = mesh.boneWeights;
            var points = mesh.vertices;
            if (weights.Length != points.Length || sourcePoses.Length != sourceBones.Length) return;
            var transforms = new Matrix4x4[sourceBones.Length];
            var mapped = new bool[sourceBones.Length];
            for (int i = 0; i < sourceBones.Length; i++)
            {
                if (sourceBones[i] == null || !indices.TryGetValue(sourceBones[i], out var target)) continue;
                transforms[i] = bindPoses[target].inverse * sourcePoses[i];
                mapped[i] = true;
            }
            for (int i = 0; i < points.Length; i++)
            {
                var w = weights[i];
                var point = points[i];
                Vector3 result = Vector3.zero;
                if (!AddWeight(point, w.boneIndex0, w.weight0, ref result) ||
                    !AddWeight(point, w.boneIndex1, w.weight1, ref result) ||
                    !AddWeight(point, w.boneIndex2, w.weight2, ref result) ||
                    !AddWeight(point, w.boneIndex3, w.weight3, ref result))
                    return;
                points[i] = result;
            }
            AddTriangles(destination, mesh, points);

            bool AddWeight(Vector3 point, int index, float weight, ref Vector3 sum)
            {
                if (weight <= 0) return true;
                if ((uint)index >= (uint)mapped.Length || !mapped[index]) return false;
                sum += transforms[index].MultiplyPoint3x4(point) * weight;
                return true;
            }
        }

        static void AddTriangles(List<Triangle> destination, Mesh mesh, Vector3[] points)
        {
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                var indices = mesh.GetTriangles(sub);
                for (int i = 0; i < indices.Length; i += 3)
                    destination.Add(new Triangle(points[indices[i]], points[indices[i + 1]], points[indices[i + 2]]));
            }
        }

        internal readonly struct Triangle
        {
            readonly Vector3 start, edgeA, edgeB;
            internal readonly Bounds Bounds;

            internal Triangle(Vector3 a, Vector3 b, Vector3 c)
            {
                start = a;
                edgeA = b - a;
                edgeB = c - a;
                var bounds = new Bounds(a, Vector3.zero);
                bounds.Encapsulate(b);
                bounds.Encapsulate(c);
                Bounds = bounds;
            }

            internal bool FindSurface(Ray ray, ref float closest)
            {
                Vector3 p = Vector3.Cross(ray.direction, edgeB);
                float determinant = Vector3.Dot(edgeA, p);
                if (Mathf.Abs(determinant) < 1e-8f) return false;
                float inverse = 1f / determinant;
                Vector3 offset = ray.origin - start;
                float u = Vector3.Dot(offset, p) * inverse;
                if (u < 0f || u > 1f) return false;
                Vector3 q = Vector3.Cross(offset, edgeA);
                float v = Vector3.Dot(ray.direction, q) * inverse;
                if (v < 0f || u + v > 1f) return false;
                float distance = Vector3.Dot(edgeB, q) * inverse;
                if (distance <= MinimumRadius || distance >= closest) return false;
                closest = distance;
                return true;
            }
        }

        // Subdivide the triangle bounds so a fitting pass need only test the
        // armor faces close to each cosmetic vertex.
        sealed class Node
        {
            readonly Bounds bounds;
            readonly Node left, right;
            readonly Triangle[] triangles;
            readonly int start, count;

            internal Node(Triangle[] triangles, int start, int count)
            {
                bounds = triangles[start].Bounds;
                for (int i = start + 1; i < start + count; i++) bounds.Encapsulate(triangles[i].Bounds);
                bounds.Expand(.0001f);
                if (count <= 12)
                {
                    this.triangles = triangles;
                    this.start = start;
                    this.count = count;
                    return;
                }
                var size = bounds.size;
                int dimension = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
                Array.Sort(triangles, start, count, Comparer<Triangle>.Create((a, b) =>
                    a.Bounds.center[dimension].CompareTo(b.Bounds.center[dimension])));
                int half = count / 2;
                left = new Node(triangles, start, half);
                right = new Node(triangles, start + half, count - half);
            }

            internal bool FindSurface(Ray ray, ref float closest)
            {
                if (!bounds.IntersectRay(ray, out float distance) || distance > closest) return false;
                if (triangles == null)
                    return left.FindSurface(ray, ref closest) | right.FindSurface(ray, ref closest);
                bool hit = false;
                for (int i = start; i < start + count; i++) hit |= triangles[i].FindSurface(ray, ref closest);
                return hit;
            }
        }
    }
}
