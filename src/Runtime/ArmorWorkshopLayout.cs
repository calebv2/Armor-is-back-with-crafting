using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Alta.Pages;
using Alta.Networking;
using ATT.Saving;
using MelonLoader;
using UnityEngine;

namespace PlayerArmorMod
{
    // The Unity transforms supply poses. The scene owns the actual native
    // equipment, with each piece saved as a child of this house.
    public sealed class ArmorWorkshopLayout : NetworkEntityBehaviour
    {
        const int BenchCount = 3;
        const int StandCount = 4;
        const int LecternIndex = BenchCount + StandCount;
        const int TableIndex = LecternIndex + 1;
        const int LockboxIndex = TableIndex + 1;
        const int FurnitureCount = LockboxIndex + 1;
        public Transform[] PlacementMarkers;
        public NetworkPrefab[] FurniturePrefabs;
        bool queued;

        internal void Configure(NetworkPrefab bench, NetworkPrefab stand, NetworkPrefab lectern, NetworkPrefab table, NetworkPrefab lockbox)
        {
            if (bench == null || stand == null || lectern == null || table == null || lockbox == null)
                throw new InvalidOperationException("All five workshop furniture prefabs must be available");
            var placements = transform.Find("Furniture Templates");
            if (placements == null || placements.childCount != FurnitureCount)
                throw new InvalidOperationException("Expected ten workshop furniture templates");
            PlacementMarkers = new Transform[FurnitureCount];
            FurniturePrefabs = new NetworkPrefab[FurnitureCount];
            for (int i = 0; i < BenchCount; i++)
                Assign(i, placements, "Armor Workbench " + (i + 1), bench);
            for (int i = 0; i < StandCount; i++)
                Assign(i + BenchCount, placements, "Armor Dummy " + (i + 1), stand);
            Assign(LecternIndex, placements, "Armor Recipe Lectern", lectern);
            Assign(TableIndex, placements, "Armor Shop Table", table);
            Assign(LockboxIndex, placements, "Armor Shop Lockbox", lockbox);
        }

        void Assign(int index, Transform placements, string name, NetworkPrefab prefab)
        {
            var marker = placements.Find(name);
            if (marker == null || marker.GetComponentsInChildren<Component>(true).Length != 1)
                throw new InvalidOperationException("Invalid workshop marker " + name);
            PlacementMarkers[index] = marker;
            FurniturePrefabs[index] = prefab;
        }

        int[] FindMissingFurniture(IEnumerable<NetworkEntity> children)
        {
            var remaining = children.Where(child => child != null && !child.IsBeingDestroyed).ToList();
            var open = new List<int>(FurnitureCount);
            for (int i = 0; i < FurnitureCount; i++)
            {
                // Reserve furniture already at its marker first. This keeps
                // a newly filled slot clear of an existing stand or bench.
                var prefab = FurniturePrefabs[i];
                var position = PlacementMarkers[i].position;
                int match = remaining.FindIndex(child => ArmorWorkshopPrefabs.HasPrefab(child, prefab) &&
                    (child.transform.position - position).sqrMagnitude <= .25f);
                if (match >= 0) remaining.RemoveAt(match);
                else open.Add(i);
            }

            var missing = new List<int>(open.Count);
            foreach (int i in open)
            {
                // The workshop still owns furniture moved off its marker.
                // Count each saved child once, regardless of where it sits.
                var prefab = FurniturePrefabs[i];
                int match = remaining.FindIndex(child => ArmorWorkshopPrefabs.HasPrefab(child, prefab));
                if (match >= 0) remaining.RemoveAt(match);
                else missing.Add(i);
            }
            return missing.ToArray();
        }

        public override void InitializeAsServer(SpawnData data)
        {
            base.InitializeAsServer(data);
            if (!Core.IsServer || queued) return;
            queued = true;
            MelonCoroutines.Start(PlaceAfterChunking(SpawnData.ShouldGenerate(data)));
        }

        IEnumerator PlaceAfterChunking(bool furnish)
        {
            float deadline = Time.unscaledTime + 20f;
            do { yield return null; }
            while (this != null && Entity != null && !Entity.IsBeingDestroyed && Entity.Chunk == null && Time.unscaledTime < deadline);
            if (this == null || Entity == null || Entity.IsBeingDestroyed) yield break;
            if (Entity.Chunk == null)
            {
                Core.Error("PLAYER_ARMOR_WORKSHOP_FURNISH_FAILED house has no world chunk");
                yield break;
            }
            int[] missing = FindMissingFurniture(Entity.Children);
            if (missing.Length == 0) yield break;
            var oldBook = furnish || !missing.Contains(LecternIndex) ? null : Entity.Children.FirstOrDefault(child =>
                ArmorWorkshopPrefabs.HasPrefab(child, ArmorWorkshopPrefabs.RecipeBook) &&
                child.CommonPickup != null && !child.CommonPickup.IsDocked);
            if (oldBook != null)
            {
                while (oldBook != null && oldBook.CommonPickup.IsInteractedWith)
                {
                    yield return null;
                    if (this == null || Entity == null || Entity.IsBeingDestroyed) yield break;
                }
                missing = FindMissingFurniture(Entity.Children);
                if (missing.Length == 0) yield break;
            }
            if (oldBook != null && (!missing.Contains(LecternIndex) || oldBook.IsBeingDestroyed ||
                oldBook.ChunkingParent != Entity || oldBook.CommonPickup.IsDocked)) oldBook = null;
            var previousParent = oldBook == null ? null : oldBook.transform.parent;
            var previousPosition = oldBook == null ? Vector3.zero : oldBook.transform.position;
            var previousRotation = oldBook == null ? Quaternion.identity : oldBook.transform.rotation;
            var placed = new List<NetworkEntity>(PlacementMarkers.Length);
            try
            {
                foreach (int i in missing)
                {
                    var marker = PlacementMarkers[i];
                    var entity = SpawnHelper.Spawn(FurniturePrefabs[i], SpawnData.DontGenerate, Entity, SaveMode.Child,
                        created =>
                        {
                            placed.Add(created);
                            created.transform.SetPositionAndRotation(marker.position, marker.rotation);
                            // The native restrictor reuses an existing Book transform child.
                            if (i == LecternIndex && oldBook != null) oldBook.transform.SetParent(created.transform, true);
                        });
                    if (entity == null || entity.IsBeingDestroyed) throw new InvalidOperationException("Could not place " + marker.name);
                    if (i == LecternIndex)
                    {
                        var recipeBook = entity.Children.Select(child => child == null ? null : child.GetComponent<Book>())
                            .SingleOrDefault(ArmorWorkshopPrefabs.IsBook);
                        if (recipeBook == null || recipeBook.Collection != ArmorRecipeBookIntegration.WorkshopCollection)
                            throw new InvalidOperationException("Workshop lectern did not spawn its armor recipe book");
                    }
                }
                if (placed.Count > 0)
                    Core.Log((furnish ? "PLAYER_ARMOR_WORKSHOP_FURNISHED" : "PLAYER_ARMOR_WORKSHOP_FURNITURE_UPGRADED") +
                        " house=" + Entity.Identifier + " added=" + placed.Count + " slots=" + FurnitureCount);
            }
            catch (Exception error)
            {
                if (oldBook != null && !oldBook.IsBeingDestroyed)
                {
                    oldBook.transform.SetParent(previousParent, true);
                    oldBook.transform.SetPositionAndRotation(previousPosition, previousRotation);
                }
                foreach (var child in placed)
                {
                    if (child == null || child.IsBeingDestroyed) continue;
                    foreach (var nested in child.Children.ToArray())
                        if (nested != null && nested != oldBook && ArmorWorkshopPrefabs.IsBook(nested.GetComponent<Book>())) nested.SceneDestroy();
                    child.SceneDestroy();
                }
                if (oldBook != null && !oldBook.IsBeingDestroyed)
                {
                    oldBook.ChunkingParent = Entity;
                    oldBook.Prefab.SaveMode = SaveMode.Child;
                    oldBook.CommonPickup.BaseSaveMode = SaveMode.Child;
                    oldBook.CommonPickup.IsChunkingParentStatic = true;
                }
                Core.Error("PLAYER_ARMOR_WORKSHOP_FURNISH_FAILED " + error);
            }
        }
    }
}
