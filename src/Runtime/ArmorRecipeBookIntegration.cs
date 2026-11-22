using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Alta;
using Alta.Crafting;
using Alta.Networking;
using Alta.Pages;
using HarmonyLib;
using UnityEngine;

namespace PlayerArmorMod
{
    internal static class ArmorRecipeBookIntegration
    {
        const uint CraftingCollectionHash = 57634;
        const int RecipeProvider = 2;

        static readonly FieldInfo PageBases = ArmorNative.Field(typeof(PageCollection), "pageBases");
        static readonly FieldInfo BookPages = ArmorNative.Field(typeof(Book), "bookPages");
        static readonly MethodInfo GetCurrentPage = AccessTools.Method(typeof(Book), "GetCurrentPage");
        internal static PageCollection WorkshopCollection { get; private set; }

        internal static void CreateWorkshopCollection()
        {
            if (WorkshopCollection != null) return;
            uint hash = ArmorCraftingPlan.Hash16("PlayerArmor/ArmorRecipeBook/Collection/v1");
            if (HashedGeneralValue<PageCollection>.All.Any(collection => collection.Hash == hash))
                throw new InvalidOperationException("Armor workshop collection hash collision: " + hash);
            var map = (Dictionary<uint, PageCollection>)ArmorNative.Field(typeof(HashedGeneralValue<PageCollection>), "items").GetValue(null);
            var stock = HashedGeneralValue<PageCollection>.Get(CraftingCollectionHash);
            var collection = ScriptableObject.CreateInstance<PageCollection>();
            collection.name = "Armor Recipe Book";
            ArmorNative.Set(collection, "hash", (int)hash);
            ArmorNative.Set(collection, "preferredProvider", RecipeProvider);
            PageBases.SetValue(collection, Array.Empty<UnityEngine.Object>());
            ArmorNative.Set(collection, "initialPages", Array.Empty<int>());
            if (stock != null) ArmorNative.Set(collection, "flairs", ArmorRecipeBookAppearance.CreateFlairs(stock.Flairs));
            map.Add(hash, collection);
            ArmorNative.Field(typeof(HashedGeneralValue<PageCollection>), "itemsByName").SetValue(null, null);
            WorkshopCollection = collection;
        }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(Book), nameof(Book.InitializeAsServer)),
                prefix: new HarmonyMethod(typeof(ArmorRecipeBookIntegration), nameof(BeforeInitializeBook)));
            harmony.Patch(AccessTools.Method(typeof(BookRestrictor), nameof(BookRestrictor.InitializeAsServer)),
                prefix: new HarmonyMethod(typeof(ArmorRecipeBookIntegration), nameof(BeforeInitializeLectern)));
            harmony.Patch(AccessTools.Method(typeof(Book), nameof(Book.SetCollection)),
                prefix: new HarmonyMethod(typeof(ArmorRecipeBookIntegration), nameof(UseWorkshopCollection)));
            harmony.Patch(AccessTools.Method(typeof(Book), "CheckGlobalPage"),
                prefix: new HarmonyMethod(typeof(ArmorRecipeBookIntegration), nameof(ResolveLearnedPage)));
            harmony.Patch(AccessTools.Method(typeof(PageCollection), nameof(PageCollection.IndexOf)),
                prefix: new HarmonyMethod(typeof(ArmorRecipeBookIntegration), nameof(ResolvePageIndex)));
            harmony.Patch(AccessTools.Method(typeof(Book), nameof(Book.Open)),
                prefix: new HarmonyMethod(typeof(ArmorRecipeBookIntegration), nameof(BeforeOpen)));
            harmony.Patch(AccessTools.Method(typeof(Book), "GetPageNumber"),
                prefix: new HarmonyMethod(typeof(ArmorRecipeBookIntegration), nameof(NumberLearnedPage)));
            harmony.Patch(AccessTools.Method(typeof(Book), "SetDisplay"),
                prefix: new HarmonyMethod(typeof(ArmorRecipeBookIntegration), nameof(BeforeDisplay)));
        }

        internal static void Refresh()
        {
            if (WorkshopCollection != null) PageBases.SetValue(WorkshopCollection, ArmorCrafting.CurrentBookRecipes());
        }

        internal static void Dispose()
        {
            if (WorkshopCollection != null) PageBases.SetValue(WorkshopCollection, Array.Empty<UnityEngine.Object>());
            ArmorRecipeBookAppearance.Dispose();
        }

        static bool IsWorkshopBook(Book book) => WorkshopCollection != null && ArmorWorkshopPrefabs.IsBook(book);

        static void BeforeInitializeBook(Book __instance)
        {
            if (!IsWorkshopBook(__instance)) return;
            RefreshClosedBook(__instance);
            ArmorNative.Set(__instance, "lastOpenedPage", 0);
        }

        static void BeforeInitializeLectern(BookRestrictor __instance, ref PageCollection ___collection)
        {
            if (WorkshopCollection != null && ArmorWorkshopPrefabs.HasPrefab(__instance.Entity, ArmorWorkshopPrefabs.RecipeLectern))
            {
                ___collection = WorkshopCollection;
                // The native restrictor only sets its collection on newly spawned
                // books. A saved child may still carry the old crafting collection.
                var existing = __instance.GetComponentInChildren<Book>(true);
                if (existing != null)
                {
                    existing.SetCollection(WorkshopCollection);
                    RefreshClosedBook(existing);
                    ArmorNative.Set(existing, "lastOpenedPage", 0);
                }
            }
        }

        static void UseWorkshopCollection(Book __instance, ref PageCollection collection)
        {
            if (IsWorkshopBook(__instance)) collection = WorkshopCollection;
        }

        // Archived physical pages still have a valid drop target. Keep their
        // own recipe data so an existing workbench project can finish normally.
        static void ResolvePageIndex(PageCollection __instance, ref UnityEngine.Object pageObject)
        {
            if (__instance == WorkshopCollection && pageObject is GenericCraftingRecipe recipe)
                pageObject = ArmorCrafting.CurrentBookRecipe(recipe.Hash) ?? pageObject;
        }

        static bool ResolveLearnedPage(Book __instance, ref (int, uint) __0)
        {
            if (!IsWorkshopBook(__instance)) return true;
            var current = __0.Item1 == RecipeProvider ? ArmorCrafting.CurrentBookRecipe(__0.Item2) : null;
            if (current == null) return false;
            __0 = (RecipeProvider, current.Hash);
            return true;
        }

        internal static bool AdmitPage(Book book, GenericCraftingRecipe recipe, ref PageData page, ref bool accepted)
        {
            var current = ArmorCrafting.CurrentBookRecipe(recipe.Hash);
            if (current == null) return false;
            var pages = (List<PageData>)BookPages.GetValue(book);
            if (pages.Any(existing => ReferenceEquals(existing?.ProviderData, current)))
            {
                // Preserve the native success result for an already learned
                // loose page without inserting or animating a second copy.
                accepted = true;
                return false;
            }
            if (!ReferenceEquals(recipe, current)) page = current.CreatePage(RecipeProvider);
            return true;
        }

        static void BeforeOpen(Book __instance, ref int ___lastOpenedPage)
        {
            if (!NetworkSceneManager.IsServer || !IsWorkshopBook(__instance) || __instance.IsOpen || __instance.IsChangingOpenState) return;
            RefreshClosedBook(__instance);
            ___lastOpenedPage = 0;
        }

        static bool NumberLearnedPage(Book __instance, ref int __result)
        {
            if (!NetworkSceneManager.IsServer || !IsWorkshopBook(__instance)) return true;
            // Native display adds one to the index of this learned page.
            __result = (int)GetCurrentPage.Invoke(__instance, null);
            return false;
        }

        static void BeforeDisplay(Book __instance, Alta.Serialization.Stream stream, DisplayArguments arguments)
        {
            if (!NetworkSceneManager.IsServer || !IsWorkshopBook(__instance) || !stream.IsWriting) return;
            // SetDisplay serializes the number printed on the physical book.
            // Use the visible learned-page index at this native wire boundary.
            arguments.displayPageNumber = (int)GetCurrentPage.Invoke(__instance, null);
        }

        static void RefreshClosedBook(Book book)
        {
            if (book.Collection != WorkshopCollection) book.SetCollection(WorkshopCollection);

            var pages = (List<PageData>)BookPages.GetValue(book);
            var learned = new Dictionary<uint, PageData>();
            var global = GlobalPagesManager.Instance?.AddedPages;
            if (global != null)
                foreach (var key in global)
                {
                    if (key.Item1 != RecipeProvider) continue;
                    var recipe = ArmorCrafting.CurrentBookRecipe(key.Item2);
                    if (recipe != null) learned[recipe.Hash] = null;
                }
            foreach (var page in pages)
            {
                if (!(page?.ProviderData is GenericCraftingRecipe saved)) continue;
                var current = ArmorCrafting.CurrentBookRecipe(saved.Hash);
                if (current == null) continue;
                if (ReferenceEquals(saved, current)) learned[current.Hash] = page;
                else if (!learned.ContainsKey(current.Hash)) learned.Add(current.Hash, null);
            }
            var ordered = new List<PageData>(learned.Count);
            foreach (GenericCraftingRecipe recipe in WorkshopCollection.PageBases)
                if (learned.TryGetValue(recipe.Hash, out var page)) ordered.Add(page ?? recipe.CreatePage(RecipeProvider));
            pages.Clear();
            pages.AddRange(ordered);
        }
    }
}
