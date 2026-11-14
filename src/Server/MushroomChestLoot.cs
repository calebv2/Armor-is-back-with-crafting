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
        const float HelmetChestChancePercent = 1f;
        const string MushroomLootLog = "PLAYER_ARMOR_MUSHROOM_CHEST_LOOT";
        const string NewsboyLootLog = "PLAYER_ARMOR_NEWSBOY_CHEST_LOOT";
        const string BucketLootLog = "PLAYER_ARMOR_BUCKET_CHEST_LOOT";
        const string CauldronLootLog = "PLAYER_ARMOR_CAULDRON_CHEST_LOOT";

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
                Core.Log(MushroomLootLog + "_READY");
                Core.Log(NewsboyLootLog + "_READY");
                Core.Log(BucketLootLog + "_READY");
                Core.Log(CauldronLootLog + "_READY");
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
            if (!NetworkSceneManager.IsServer || assets == null || !assets.Ready) return filled;

            // Each bonus gets its own roll even if another roll already
            // filled a dock. Mushroom keeps first pick of free space.
            filled |= TryAddArmor(data, targets);
            filled |= TryAddHelmet(data, targets, ArmorStyle.NewsboyHelmet, NewsboyLootLog);
            filled |= TryAddHelmet(data, targets, ArmorStyle.BucketHelmet, BucketLootLog);
            filled |= TryAddHelmet(data, targets, ArmorStyle.CauldronHelmet, CauldronLootLog);
            return filled;
        }

        internal static bool RollChance(float chancePercent, Random rng)
        {
            return chancePercent > 0 && rng.NextDouble() * 100 < chancePercent;
        }

        internal static int RollSlot(float chancePercent, Random rng)
        {
            return RollChance(chancePercent, rng) ? rng.Next(ArmorCatalog.SlotCount) : -1;
        }

        static bool TryAddArmor(SpawnData data, IEnumerable<PickupDock> targets)
        {
            int slot = RollSlot(ArmorSettings.Current.mushroomChestChancePercent, random);
            return slot >= 0 && TryAddItem(data, targets, assets.MushroomPieces[slot], MushroomLootLog);
        }

        static bool TryAddHelmet(SpawnData data, IEnumerable<PickupDock> targets, ArmorStyle style, string logPrefix)
        {
            if (!RollChance(HelmetChestChancePercent, random)) return false;
            var prefab = assets.StandaloneHelmets[ArmorCatalog.HelmetIndex(style)];
            return TryAddItem(data, targets, prefab, logPrefix);
        }

        static bool TryAddItem(SpawnData data, IEnumerable<PickupDock> targets, NetworkPrefab prefab, string logPrefix)
        {
            try
            {
                if (prefab == null) return false;
                var pickup = prefab.GetComponent<Pickup>();
                if (pickup == null || pickup.Item == null) return false;
                var item = pickup.Item;
                var dock = FindOpenChestDock(targets, item);
                if (dock == null) return false;
                // ATT spawns a network pickup and destroys it if the dock rejects it.
                if (!spawnOne(item, dock, NetworkSceneManager.Current, data, 1)) return false;
                Core.Log(logPrefix + " dock=" + dock.Entity.Identifier + " piece=" + prefab.name);
                return true;
            }
            catch (Exception error)
            {
                Core.Error(logPrefix + "_FAILED " + error);
                return false;
            }
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
