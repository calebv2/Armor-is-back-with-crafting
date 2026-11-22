using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace PlayerArmorMod
{
    internal static class ArmorWorkshopCollision
    {
        const float Tolerance = .0001f;

        internal static void Optimize(GameObject house)
        {
            ReplaceFloor(house.transform.Find("ATT architecture/Workshop floor"));
            ReplaceFloor(house.transform.Find("ATT architecture/Town walls, windows and doorway/Covered entrance/ATT entrance boards"));
            CombineMeshes(house);
        }

        static void ReplaceFloor(Transform floor)
        {
            if (floor == null) return;
            var colliders = floor.GetComponentsInChildren<MeshCollider>(true);
            if (colliders.Length == 0 || !Compatible(colliders)) return;
            var bounds = new List<Bounds>(colliders.Length);
            foreach (var collider in colliders)
            {
                var mesh = collider.sharedMesh;
                // Only the native rectangular floor panels qualify. Windows,
                // sloping gables, and shaped timber keep their original faces.
                if (mesh.name != "Floor" || mesh.vertexCount != 24 || mesh.triangles.Length != 36) return;
                var matrix = floor.worldToLocalMatrix * collider.transform.localToWorldMatrix;
                if (Mathf.Abs(matrix.m01) + Mathf.Abs(matrix.m02) + Mathf.Abs(matrix.m10) +
                    Mathf.Abs(matrix.m12) + Mathf.Abs(matrix.m20) + Mathf.Abs(matrix.m21) > Tolerance) return;
                var local = new Bounds(matrix.MultiplyPoint3x4(mesh.bounds.center), Vector3.zero);
                foreach (var vertex in mesh.vertices)
                {
                    for (int axis = 0; axis < 3; axis++)
                        if (Mathf.Abs(vertex[axis] - mesh.bounds.min[axis]) > Tolerance &&
                            Mathf.Abs(vertex[axis] - mesh.bounds.max[axis]) > Tolerance) return;
                    local.Encapsulate(matrix.MultiplyPoint3x4(vertex));
                }
                bounds.Add(local);
            }
            var combined = bounds[0];
            foreach (var part in bounds.Skip(1)) combined.Encapsulate(part);
            float volume = 0;
            for (int i = 0; i < bounds.Count; i++)
            {
                var part = bounds[i];
                if (Mathf.Abs(part.min.y - combined.min.y) > Tolerance ||
                    Mathf.Abs(part.max.y - combined.max.y) > Tolerance) return;
                volume += Volume(part.size);
                for (int j = 0; j < i; j++)
                {
                    var overlap = Vector3.Min(part.max, bounds[j].max) - Vector3.Max(part.min, bounds[j].min);
                    if (overlap.x > Tolerance && overlap.y > Tolerance && overlap.z > Tolerance) return;
                }
            }
            if (Mathf.Abs(volume - Volume(combined.size)) > Tolerance * Volume(combined.size)) return;
            var box = floor.gameObject.AddComponent<BoxCollider>();
            box.center = combined.center;
            box.size = combined.size;
            CopySettings(colliders[0], box);
            foreach (var collider in colliders) Object.DestroyImmediate(collider);
        }

        static float Volume(Vector3 size) { return size.x * size.y * size.z; }

        static bool Compatible(MeshCollider[] colliders)
        {
            var first = colliders[0];
            return colliders.All(collider => collider.enabled && !collider.isTrigger && !collider.convex &&
                collider.attachedRigidbody == null && collider.sharedMesh != null && collider.sharedMesh.isReadable &&
                collider.sharedMesh.subMeshCount == 1 &&
                collider.gameObject.activeSelf && collider.gameObject.layer == first.gameObject.layer &&
                collider.sharedMaterial == first.sharedMaterial && collider.cookingOptions == first.cookingOptions &&
                collider.contactOffset == first.contactOffset);
        }

        static void CopySettings(Collider source, Collider target)
        {
            target.gameObject.layer = source.gameObject.layer;
            target.sharedMaterial = source.sharedMaterial;
            target.contactOffset = source.contactOffset;
        }

        static void CombineMeshes(GameObject house)
        {
            var colliders = house.GetComponentsInChildren<MeshCollider>(true);
            if (colliders.Length < 2 || !Compatible(colliders)) return;
            var mesh = new Mesh { name = "Armor workshop static collision", hideFlags = HideFlags.DontSave };
            GameObject node = null;
            try
            {
                if (colliders.Sum(collider => collider.sharedMesh.vertexCount) > ushort.MaxValue)
                    mesh.indexFormat = IndexFormat.UInt32;
                mesh.CombineMeshes(colliders.Select(collider => new CombineInstance
                {
                    mesh = collider.sharedMesh,
                    transform = house.transform.worldToLocalMatrix * collider.transform.localToWorldMatrix
                }).ToArray(), true, true);
                mesh.RecalculateBounds();
                node = new GameObject("Workshop static collision");
                node.transform.SetParent(house.transform, false);
                var combined = node.AddComponent<MeshCollider>();
                CopySettings(colliders[0], combined);
                combined.cookingOptions = colliders[0].cookingOptions;
                combined.sharedMesh = mesh;
                foreach (var collider in colliders) Object.DestroyImmediate(collider);
            }
            catch
            {
                if (node != null) Object.DestroyImmediate(node);
                Object.DestroyImmediate(mesh);
                throw;
            }
        }
    }
}
