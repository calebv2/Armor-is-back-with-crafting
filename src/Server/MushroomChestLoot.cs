using System;
using System.Collections.Generic;
using System.Reflection;
using Alta.Inventory;
using Alta.Loot;
using Alta.Networking;
using HarmonyLib;

namespace PlayerArmorMod
{
    internal static class MushroomChestLoot
    {
        delegate bool SpawnOne(Item item, PickupDock dock, INetworkScene scene, SpawnData data, int quantity);

        static readonly Random random = new Random();
        static readonly MethodInfo fill = AccessTools.Method(typeof(LootHelper), "Spawn",
            new[] { typeof(SpawnData), typeof(IEnumerable<PickupDock>), typeof(float) });
        static readonly MethodInfo fillWithBonus = AccessTools.Method(typeof(MushroomChestLoot), "FillWithBonus");
        static readonly MethodInfo patch = AccessTools.Method(typeof(MushroomChestLoot), "PatchRefill");
        static readonly MethodInfo[] refills = {
            AccessTools.Method(typeof(LootModule), "Respawn", new[] { typeof(int) }),
            AccessTools.Method(typeof(GeneratedLoot), "Respawn", new[] { typeof(int) })
        };
        static HarmonyLib.Harmony patchOwner;
        static ArmorPrefabRegistry assets;
        static SpawnOne spawnOne;

        internal static void Install(HarmonyLib.Harmony harmony, ArmorPrefabRegistry registry)
        {
            try
            {
                var spawn = AccessTools.Method(typeof(LootHelper), "Spawn",
                    new[] { typeof(Item), typeof(PickupDock), typeof(INetworkScene), typeof(SpawnData), typeof(int) });
                spawnOne = (SpawnOne)Delegate.CreateDelegate(typeof(SpawnOne), spawn);
                patchOwner = harmony;
                foreach (var refill in refills)
                    harmony.Patch(refill, transpiler: new HarmonyMethod(patch));
                assets = registry;
                Core.Log("PLAYER_ARMOR_MUSHROOM_CHEST_LOOT_READY");
            }
            catch (Exception error)
            {
                Dispose();
                Core.Error("PLAYER_ARMOR_MUSHROOM_CHEST_LOOT_UNAVAILABLE " + error);
            }
        }

        internal static void Dispose()
        {
            assets = null;
            spawnOne = null;
            if (patchOwner == null) return;
            foreach (var refill in refills)
                if (refill != null) patchOwner.Unpatch(refill, patch);
            patchOwner = null;
        }

        // Both generators can also stock other scenery. Intercept just their
        // successful control path to the normal fill, never a chest-open event.
        static IEnumerable<CodeInstruction> PatchRefill(IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(fill))
                {
                    instruction.operand = fillWithBonus;
                    replacements++;
                }
                yield return instruction;
            }
            if (replacements != 1) throw new InvalidOperationException("Expected one native stock-generation call");
        }

        static bool FillWithBonus(SpawnData data, IEnumerable<PickupDock> targets, float incrementingFailChance)
        {
            bool filled = LootHelper.Spawn(data, targets, incrementingFailChance);
            try
            {
                if (NetworkSceneManager.IsServer && assets != null && assets.Ready)
                    return TryAddArmor(data, targets) || filled;
            }
            catch (Exception error)
            {
                Core.Error("PLAYER_ARMOR_MUSHROOM_CHEST_LOOT_FAILED " + error);
            }
            return filled;
        }

        internal static int RollSlot(float chancePercent, Random rng)
        {
            if (!(chancePercent > 0) || rng.NextDouble() * 100 >= chancePercent) return -1;
            return rng.Next(ArmorCatalog.SlotCount);
        }

        static bool TryAddArmor(SpawnData data, IEnumerable<PickupDock> targets)
        {
            int slot = RollSlot(ArmorSettings.Current.mushroomChestChancePercent, random);
            if (slot < 0) return false;
            var prefab = assets.MushroomPieces[slot];
            var item = prefab.GetComponent<Pickup>().Item;
            var dock = FindOpenChestDock(targets, item);
            if (dock == null) return false;
            // ATT spawns an actual network pickup, docks one immediately
            // and destroys the new entity itself if the final dock rejects it.
            if (!spawnOne(item, dock, NetworkSceneManager.Current, data, 1)) return false;
            Core.Log("PLAYER_ARMOR_MUSHROOM_CHEST_LOOT dock=" + dock.Entity.Identifier + " piece=" + prefab.name);
            return true;
        }

        static PickupDock FindOpenChestDock(IEnumerable<PickupDock> targets, Item item)
        {
            foreach (var dock in targets)
            {
                if (dock == null || dock.IsOwnedByPlayer || dock.QuantityStored != 0 || dock.HasDockedPickup)
                    continue;
                // The cave Structure Chest nests its hinge below the generator.
                // Check the receiving dock's own ancestors, not the scene root.
                var chests = dock.GetComponentsInParent<Chest>(true);
                if (chests.Length == 0) continue;
                var chest = chests[0];
                if (chest.Entity == null || chest.Entity.IsBeingDestroyed)
                    continue;
                var chunk = chest.Entity.Chunk;
                if (chunk != null && chunk.GetComponent<PersonalChunk>() != null) continue;
                if (dock.Settings.IsStackingEnforced || dock.CheckAllowed(item) != PickupDock.AllowedResult.Ok)
                    continue;
                return dock;
            }
            return null;
        }
    }
}
