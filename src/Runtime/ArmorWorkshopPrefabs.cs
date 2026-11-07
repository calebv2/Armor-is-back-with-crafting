using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Alta.Crafting;
using Alta.Networking;
using Alta.Pages;
using ATT.Saving;
using MelonLoader;
using SyncLib.Items;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PlayerArmorMod
{
    internal static class ArmorWorkshopPrefabs
    {
        internal const string HouseName = "ATT_ArmorWorkshop";
        internal const string BenchName = "ATT_ArmorCraftingTable";
        internal const string BookName = "ATT_ArmorRecipeBook";
        internal const string LecternName = "ATT_ArmorRecipeLectern";
        const uint CraftingDeckDonor = 28396;
        const uint BookDonor = 44174;
        const uint LecternDonor = 59986;
        const uint ShopTable = 8520;
        const uint ShopLockbox = 6974;
        internal static NetworkPrefab Bench { get; private set; }
        internal static NetworkPrefab RecipeBook { get; private set; }
        internal static NetworkPrefab RecipeLectern { get; private set; }
        internal static bool Ready { get; private set; }

        internal static bool IsBench(GenericCraftingStation station)
        {
            return station != null && HasPrefab(station.Entity, Bench);
        }

        internal static bool IsBook(Book book)
        {
            return book != null && HasPrefab(book.Entity, RecipeBook);
        }

        internal static bool HasPrefab(NetworkEntity entity, NetworkPrefab template)
        {
            var prefab = entity == null ? null : entity.PrefabRoot != null ? entity.PrefabRoot.Prefab : entity.Prefab;
            return template != null && prefab != null && prefab.Hash == template.Hash;
        }

        internal static NetworkPrefab[] Register(MelonMod mod, GameObject staging, NetworkPrefab stand)
        {
            var stationDonor = NativeDonor<GenericCraftingStation>(CraftingDeckDonor);
            var bookDonor = NativeDonor<Book>(BookDonor);
            var lecternDonor = NativeDonor<BookRestrictor>(LecternDonor);
            var table = PrefabManager.GetPrefab(ShopTable);
            var lockbox = PrefabManager.GetPrefab(ShopLockbox);
            if (table == null || lockbox == null)
                throw new InvalidOperationException("The native shop table or lockbox is unavailable");
            var house = LoadHouse(staging.transform);
            var station = Object.Instantiate(stationDonor.gameObject, staging.transform, false);
            var book = Object.Instantiate(bookDonor.gameObject, staging.transform, false);
            // SyncLib adds a root NetworkRigidbody for Rigidbody prefabs. Let
            // it rebuild that binding on the portable native book copy.
            var bookNetworkBody = book.GetComponent<NetworkRigidbody>();
            if (bookNetworkBody != null) Object.DestroyImmediate(bookNetworkBody);
            ArmorNative.Set(book.GetComponentInChildren<Book>(true), "collection", ArmorRecipeBookIntegration.WorkshopCollection);
            Bench = RegisterOne(mod, station, BenchName);
            RecipeBook = RegisterOne(mod, book, BookName);
            var body = book.GetComponent<NetworkRigidbody>();
            var bookEntity = book.GetComponent<NetworkEntity>();
            ArmorNative.Set(body, "entity", bookEntity);
            ArmorNative.Set(body, "cachedTransform", body.transform);
            body.UpdateRigidbody();
            var bookPickup = book.GetComponent<Pickup>();
            ArmorNative.Set(bookPickup, "networkRigidbody", body);
            // Reading the book should not move its save ownership to the player's hand.
            bookPickup.IsChunkingParentStatic = true;
            var bookBehaviours = bookEntity.GetComponents<MonoBehaviour>().Where(b => b is INetworkEntityBehaviour).ToArray();
            ArmorNative.Set(bookEntity, "networkEntityBehaviours", bookBehaviours);
            var lectern = Object.Instantiate(lecternDonor.gameObject, staging.transform, false);
            BindLectern(lectern.GetComponentInChildren<BookRestrictor>(true), book.GetComponent<Book>());
            var lecternContent = lectern.GetComponent<AdditionalStaticContent>();
            if (lecternContent == null) throw new InvalidOperationException("Native lectern is missing its static art and collision");
            ArmorNative.Set(lecternContent, "needsToInstantiate", true);
            // World lecterns use baked map collision. A spawned lectern needs
            // its native book rest on the authoritative physics scene as well.
            if(Core.IsServer)lecternContent.InstantiateContent(lectern.transform);
            var entity = house.AddComponent<NetworkEntity>();
            var prefab = house.AddComponent<NetworkPrefab>();
            ArmorNative.Set(prefab, "entity", entity);
            house.AddComponent<ArmorWorkshopLayout>();
            ArmorNative.Bind(house);
            var registeredHouse = RegisterOne(mod, house, HouseName);
            // Append the new prefab after the existing workshop identities.
            RecipeLectern = RegisterOne(mod, lectern, LecternName);
            registeredHouse.GetComponent<ArmorWorkshopLayout>().Configure(Bench, stand, RecipeLectern, table, lockbox);
            Ready = true;
            return new[] { registeredHouse, Bench, RecipeBook, RecipeLectern };
        }

        internal static void BindLectern(BookRestrictor lectern, Book book)
        {
            if (lectern == null || book == null || ArmorRecipeBookIntegration.WorkshopCollection == null)
                throw new InvalidOperationException("Armor lectern requires its native restrictor and dedicated recipe book");
            ArmorNative.Set(lectern, "collection", ArmorRecipeBookIntegration.WorkshopCollection);
            ArmorNative.Set(lectern, "bookPrefab", book);
        }

        static NetworkPrefab NativeDonor<T>(uint hash) where T : Component
        {
            var prefab = PrefabManager.GetPrefab(hash);
            if (prefab == null || prefab.GetComponentsInChildren<T>(true).Length != 1 || prefab.GetComponent<NetworkEntity>() == null)
                throw new InvalidOperationException("Missing native workshop donor " + hash + " for " + typeof(T).Name);
            return prefab;
        }

        static NetworkPrefab RegisterOne(MelonMod mod, GameObject template, string name)
        {
            template.name = name;
            template.SetActive(true);
            var registered = mod.RegisterCustomPrefab(template, name);
            if (registered == null) throw new InvalidOperationException("SyncLib refused " + name);
            registered.SaveMode = SaveMode.Base;
            ArmorNative.Set(registered, "canBeSpawnedThroughCommand", true);
            Core.Log("PLAYER_ARMOR_WORKSHOP_PREFAB name=" + name + " hash=" + registered.Hash + " embedded=" + registered.Embedded.Count);
            return registered;
        }

        static GameObject LoadHouse(Transform parent)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PlayerArmor.Workshop"))
            {
                if (stream == null) throw new InvalidDataException("Armor workshop bundle is missing");
                var bundle = AssetBundle.LoadFromStream(stream);
                if (bundle == null) throw new InvalidDataException("Armor workshop bundle could not load");
                try
                {
                    var path = bundle.GetAllAssetNames().Single(p => p.EndsWith("/att_armorworkshop.prefab", StringComparison.OrdinalIgnoreCase));
                    var model = bundle.LoadAsset<GameObject>(path);
                    if (model == null) throw new InvalidDataException("Armor workshop shell is missing");
                    var house = Object.Instantiate(model, parent, false);
                    int ground = LayerMask.NameToLayer("Ground");
                    if (ground < 0) throw new InvalidOperationException("Native Ground layer is unavailable");
                    foreach (var collider in house.GetComponentsInChildren<Collider>(true)) collider.gameObject.layer = ground;
                    if(Core.IsServer)
                    {
                        foreach (var renderer in house.GetComponentsInChildren<Renderer>(true)) Object.DestroyImmediate(renderer);
                        foreach (var filter in house.GetComponentsInChildren<MeshFilter>(true)) Object.DestroyImmediate(filter);
                    }
                    else ArmorStereo.Configure(house);
                    return house;
                }
                finally { bundle.Unload(false); }
            }
        }
    }
}
