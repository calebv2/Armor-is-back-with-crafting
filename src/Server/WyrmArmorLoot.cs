using System;
using System.Globalization;
using System.Reflection;
using Alta.Chunks;
using Alta.Intelligence;
using Alta.Networking;
using HarmonyLib;
using UnityEngine;

namespace PlayerArmorMod
{
    internal static class WyrmArmorLoot
    {
        internal const int KillsPerDropOnAverage = 20;
        internal const int FireKillsPerDropOnAverage = 100;
        const uint RegularWyrmHash = 21642;
        const uint RegularTrialWyrmHash = 6004;
        const uint CrystalWyrmHash = 37392;
        const uint CrystalTrialWyrmHash = 48128;
        static readonly System.Random random = new System.Random();
        static readonly MethodInfo death = AccessTools.DeclaredMethod(typeof(Wyrm), "Killed", new[] { typeof(HealthObject) });
        static readonly MethodInfo before = AccessTools.Method(typeof(WyrmArmorLoot), nameof(BeforeDeath));
        static readonly MethodInfo after = AccessTools.Method(typeof(WyrmArmorLoot), nameof(AfterDeath));
        static HarmonyLib.Harmony patchOwner;
        static ArmorPrefabRegistry assets;
        static Func<NetworkEntity, bool> fireWyrmCheck;
        static bool fireIntegrationFailed;

        struct DeathDrop
        {
            internal NetworkPrefab[] Pieces;
            internal Chunk Chunk;
            internal Vector3 Position;
            internal uint Identifier, PrefabHash;
            internal int KillsPerDrop;
        }

        internal static void Install(HarmonyLib.Harmony harmony, ArmorPrefabRegistry registry)
        {
            try
            {
                harmony.Patch(death, prefix: new HarmonyMethod(before), postfix: new HarmonyMethod(after));
                patchOwner = harmony;
                assets = registry;
                string chance = (100d / KillsPerDropOnAverage).ToString("0.##", CultureInfo.InvariantCulture);
                Core.Log("PLAYER_ARMOR_WYRM_LOOT_READY chance=" + chance + "% fireChance=1%");
            }
            catch (Exception error)
            {
                Core.Error("PLAYER_ARMOR_WYRM_LOOT_UNAVAILABLE " + error);
            }
        }

        internal static void Dispose()
        {
            assets = null;
            fireWyrmCheck = null;
            fireIntegrationFailed = false;
            if (patchOwner == null) return;
            patchOwner.Unpatch(death, before);
            patchOwner.Unpatch(death, after);
            patchOwner = null;
        }

        // The original Target.Killed flips IsAlive synchronously. Later
        // death notifications from the same entity cannot roll again.
        static void BeforeDeath(Wyrm __instance, HealthObject __0, out DeathDrop __state)
        {
            __state = default(DeathDrop);
            if (!NetworkSceneManager.IsServer || assets == null || !assets.Ready || !__instance.IsAlive) return;
            // Native Wyrm AI has its own child entity. The health event and
            // creature prefab are on the root, where the world chunk lives.
            var healthEntity = __0 == null ? null : __0.Entity;
            var aiEntity = __instance.Entity;
            var entity = healthEntity == null ? (aiEntity == null ? null : aiEntity.PrefabRoot ?? aiEntity)
                : healthEntity.PrefabRoot ?? healthEntity;
            if (entity == null || entity.Prefab == null)
            {
                Core.Log("PLAYER_ARMOR_WYRM_CHECK skipped=missing_entity_or_prefab");
                return;
            }

            uint hash = entity.Prefab.Hash;
            NetworkPrefab[] pieces;
            int killsPerDrop = KillsPerDropOnAverage;
            switch (hash)
            {
                case RegularWyrmHash:
                case RegularTrialWyrmHash:
                    pieces = assets.WyrmPieces;
                    break;
                case CrystalWyrmHash:
                    pieces = assets.CrystalWyrmPieces;
                    break;
                case CrystalTrialWyrmHash:
                    bool fire = IsFireWyrm(entity);
                    pieces = fire ? assets.FireWyrmPieces : assets.CrystalWyrmPieces;
                    if (fire) killsPerDrop = FireKillsPerDropOnAverage;
                    break;
                default:
                    Core.Log("PLAYER_ARMOR_WYRM_CHECK skipped=unrecognized_prefab prefab=" + hash);
                    return;
            }
            var chunk = entity.Chunk;
            if (chunk == null || entity.IsBeingDestroyed)
            {
                Core.Log("PLAYER_ARMOR_WYRM_CHECK skipped=missing_chunk_or_destroying prefab=" + hash);
                return;
            }
            __state = new DeathDrop { Pieces = pieces, Chunk = chunk,
                Position = entity.transform.position + Vector3.up * .35f, Identifier = entity.Identifier,
                PrefabHash = hash, KillsPerDrop = killsPerDrop };
        }

        static bool IsFireWyrm(NetworkEntity entity)
        {
            if (fireIntegrationFailed) return false;
            try
            {
                if (fireWyrmCheck == null)
                {
                    // The monster shares the native trial prefab with ordinary
                    // Trial Wyrms. Use Extended Televator's exact classifier.
                    var type = AccessTools.TypeByName("Televator200.FireWyrms");
                    if (type == null) return false;
                    var method = AccessTools.DeclaredMethod(type, "IsFireWyrm", new[] { typeof(NetworkEntity) });
                    if (method == null) return false;
                    fireWyrmCheck = (Func<NetworkEntity, bool>)Delegate.CreateDelegate(typeof(Func<NetworkEntity, bool>), method);
                }
                return fireWyrmCheck(entity);
            }
            catch (Exception error)
            {
                fireIntegrationFailed = true;
                Core.Error("PLAYER_ARMOR_FIRE_WYRM_CLASSIFIER_FAILED " + error.GetBaseException().Message);
                return false;
            }
        }

        static void AfterDeath(Wyrm __instance, DeathDrop __state)
        {
            if (__state.Pieces == null) return;
            if (__instance.IsAlive)
            {
                Core.Log("PLAYER_ARMOR_WYRM_CHECK skipped=still_alive_after_death");
                return;
            }
            int slot = RollSlot(random, __state.KillsPerDrop);
            Core.Log("PLAYER_ARMOR_WYRM_ROLL wyrm=" + __state.Identifier + " prefab=" + __state.PrefabHash +
                " odds=1/" + __state.KillsPerDrop + " result=" + (slot < 0 ? "no_drop" : "drop"));
            if (slot < 0) return;
            var prefab = __state.Pieces[slot];
            if (prefab == null) { Core.Error("PLAYER_ARMOR_WYRM_LOOT_FAILED missing armor prefab slot=" + slot); return; }
            try
            {
                var item = SpawnHelper.Spawn(prefab, SpawnData.DontGenerate, __state.Chunk, __state.Position, Quaternion.identity);
                if (item != null) Core.Log("PLAYER_ARMOR_WYRM_LOOT wyrm=" + __state.Identifier + " piece=" + prefab.name);
                else Core.Error("PLAYER_ARMOR_WYRM_LOOT_FAILED spawn returned null for " + prefab.name);
            }
            catch (Exception error)
            {
                Core.Error("PLAYER_ARMOR_WYRM_LOOT_FAILED " + error);
            }
        }

        internal static int RollSlot(System.Random rng, int killsPerDrop = KillsPerDropOnAverage)
        {
            return rng.Next(killsPerDrop) == 0 ? rng.Next(ArmorCatalog.SlotCount) : -1;
        }
    }
}
