using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Alta;
using Alta.Crafting;
using Alta.Networking;
using Alta.Pages;
using HarmonyLib;
using UnityEngine;

namespace PlayerArmorMod
{
    // Keep native learning, persistence, book duplication and station docking.
    // Registering a recipe hash alone does not add it to a book's whitelist.
    internal static class ArmorRecipeBookIntegration
    {
        const uint CraftingCollectionHash=57634;
        const int RecipeProvider=2,MaximumCollectionSize=255;
        sealed class References<T>:IEqualityComparer<T> where T:class
        {
            public bool Equals(T a,T b){return ReferenceEquals(a,b);}
            public int GetHashCode(T value){return RuntimeHelpers.GetHashCode(value);}
        }
        sealed class CollectionPlan
        {
            internal PageCollection Collection;
            internal UnityEngine.Object[] Pages;
            internal int[] Initial;
        }
        static readonly Dictionary<GenericCraftingRecipe,ArmorPieceDefinition> approved=
            new Dictionary<GenericCraftingRecipe,ArmorPieceDefinition>(new References<GenericCraftingRecipe>());
        static readonly HashSet<PageCollection> tracked=new HashSet<PageCollection>(new References<PageCollection>());
        static readonly HashSet<Book> initialized=new HashSet<Book>(new References<Book>());
        static readonly FieldInfo pageBases=ArmorNative.Field(typeof(PageCollection),"pageBases"),initialPages=ArmorNative.Field(typeof(PageCollection),"initialPages");
        static readonly FieldInfo bookPages=ArmorNative.Field(typeof(Book),"bookPages");
        static readonly MethodInfo checkGlobalPage=AccessTools.Method(typeof(Book),"CheckGlobalPage");
        static readonly MethodInfo getCurrentPage=AccessTools.Method(typeof(Book),"GetCurrentPage");
        internal static PageCollection WorkshopCollection { get; private set; }

        internal static void CreateWorkshopCollection()
        {
            if(WorkshopCollection!=null)return;
            uint hash=ArmorCraftingPlan.Hash16("PlayerArmor/ArmorRecipeBook/Collection/v1");
            if(HashedGeneralValue<PageCollection>.All.Any(collection=>collection.Hash==hash))
                throw new InvalidOperationException("Armor workshop collection hash collision: "+hash);
            var map=(Dictionary<uint,PageCollection>)ArmorNative.Field(typeof(HashedGeneralValue<PageCollection>),"items").GetValue(null);
            var stock=HashedGeneralValue<PageCollection>.Get(CraftingCollectionHash);
            var collection=ScriptableObject.CreateInstance<PageCollection>();
            collection.name="Armor Recipe Book";
            ArmorNative.Set(collection,"hash",(int)hash);
            ArmorNative.Set(collection,"preferredProvider",RecipeProvider);
            ArmorNative.Set(collection,"pageBases",new UnityEngine.Object[0]);
            ArmorNative.Set(collection,"initialPages",new int[0]);
            if(stock!=null)ArmorNative.Set(collection,"flairs",stock.Flairs);
            map.Add(hash,collection);
            ArmorNative.Field(typeof(HashedGeneralValue<PageCollection>),"itemsByName").SetValue(null,null);
            WorkshopCollection=collection;
        }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(Book),"InitializeAsServer"),
                prefix:new HarmonyMethod(typeof(ArmorRecipeBookIntegration),"BeforeInitializeBook"),
                postfix:new HarmonyMethod(typeof(ArmorRecipeBookIntegration),"AfterInitializeBook"));
            harmony.Patch(AccessTools.Method(typeof(Book),"OnDestroy"),
                postfix:new HarmonyMethod(typeof(ArmorRecipeBookIntegration),"AfterDestroyBook"));
            harmony.Patch(AccessTools.Method(typeof(Book),"Open"),
                prefix:new HarmonyMethod(typeof(ArmorRecipeBookIntegration),nameof(BeforeOpen)));
            harmony.Patch(AccessTools.Method(typeof(Book),"GetPageNumber"),
                prefix:new HarmonyMethod(typeof(ArmorRecipeBookIntegration),nameof(NumberLearnedPage)));
        }

        static GenericCraftingRecipe[] InBookOrder(IEnumerable<KeyValuePair<GenericCraftingRecipe,ArmorPieceDefinition>> entries)
        {
            // Keep archived versions beside their matching armor piece.
            return entries.OrderBy(entry=>entry.Value.Style).ThenBy(entry=>entry.Value.Slot)
                .ThenBy(entry=>entry.Key.Hash).Select(entry=>entry.Key).ToArray();
        }

        static List<CollectionPlan> RegistrationPlans(GenericCraftingRecipe recipe,ArmorPieceDefinition piece)
        {
            if(ReferenceEquals(recipe,null))throw new ArgumentNullException("recipe");
            if(approved.Keys.Any(other=>other.Hash==recipe.Hash))throw new InvalidOperationException("An armor recipe book version already owns hash "+recipe.Hash);
            var next=InBookOrder(approved.Concat(new[]{new KeyValuePair<GenericCraftingRecipe,ArmorPieceDefinition>(recipe,piece)}));
            // Validate every currently loaded collection before changing any of
            // them or admitting the new object to the approved version registry.
            return PlanCollections(next,approved.Keys);
        }
        internal static void ValidateRegistration(GenericCraftingRecipe recipe,ArmorPieceDefinition piece)
        {
            if(!ReferenceEquals(recipe,null)&&approved.ContainsKey(recipe))return;
            RegistrationPlans(recipe,piece);
        }
        internal static void Register(GenericCraftingRecipe recipe,ArmorPieceDefinition piece)
        {
            if(!ReferenceEquals(recipe,null)&&approved.ContainsKey(recipe))return;
            var plans=RegistrationPlans(recipe,piece);
            approved.Add(recipe,piece);Apply(plans);ReplayExistingBooks();
        }
        internal static void Retire(GenericCraftingRecipe recipe)
        {
            if(ReferenceEquals(recipe,null)||!approved.ContainsKey(recipe))return;
            var next=InBookOrder(approved.Where(entry=>!ReferenceEquals(entry.Key,recipe)));
            var plans=PlanCollections(next,approved.Keys);
            approved.Remove(recipe);Apply(plans);
            // Native book/page/render-cache references have their own lifetime.
            // Removing a whitelist entry must never destroy those live objects.
        }
        internal static void Refresh()
        {Apply(PlanCollections(InBookOrder(approved),approved.Keys));ReplayExistingBooks();}
        internal static void Dispose()
        {
            Apply(PlanCollections(new GenericCraftingRecipe[0],approved.Keys));
            approved.Clear();tracked.Clear();initialized.Clear();
        }
        static bool IsCraftingCollection(PageCollection collection)
        {return WorkshopCollection!=null&&collection!=null&&collection.Hash==WorkshopCollection.Hash&&collection.PreferredProvider==RecipeProvider;}
        static void BeforeOpen(Book __instance,ref int ___lastOpenedPage)
        {
            if(NetworkSceneManager.IsServer&&!__instance.IsOpen&&!__instance.IsChangingOpenState&&IsCraftingCollection(__instance.Collection))
                ___lastOpenedPage=0;
        }
        static bool NumberLearnedPage(Book __instance,ref int __result)
        {
            if(!NetworkSceneManager.IsServer||!IsCraftingCollection(__instance.Collection))return true;
            // Native display adds one to this index. Unlearned recipes stay
            // in the collection whitelist but do not occupy numbered pages.
            __result=(int)getCurrentPage.Invoke(__instance,null);
            return false;
        }
        static IEnumerable<PageCollection> Collections()
        {
            tracked.RemoveWhere(collection=>collection==null);
            var found=new HashSet<PageCollection>(new References<PageCollection>());
            foreach(var collection in HashedGeneralValue<PageCollection>.All)
                if(IsCraftingCollection(collection)&&found.Add(collection))yield return collection;
            // Books may hold an instantiated collection sharing the native hash.
            foreach(var collection in Resources.FindObjectsOfTypeAll<PageCollection>())
                if(IsCraftingCollection(collection)&&found.Add(collection))yield return collection;
            foreach(var book in UnityEngine.Object.FindObjectsOfType<Book>(true))
                if(IsCraftingCollection(book.Collection)&&found.Add(book.Collection))yield return book.Collection;
            foreach(var collection in tracked.ToArray())
                if(collection!=null&&IsCraftingCollection(collection)&&found.Add(collection))yield return collection;
        }
        static List<CollectionPlan> PlanCollections(GenericCraftingRecipe[] keep,IEnumerable<GenericCraftingRecipe> remove)
        {return Collections().Select(collection=>PlanCollection(collection,keep,remove)).ToList();}
        static CollectionPlan PlanCollection(PageCollection collection,GenericCraftingRecipe[] keep,IEnumerable<GenericCraftingRecipe> remove)
        {
            var old=collection.PageBases;
            if(old==null)throw new InvalidOperationException("Native crafting collection has no page bases");
            var owned=new HashSet<GenericCraftingRecipe>(remove,new References<GenericCraftingRecipe>());
            foreach(var recipe in keep)owned.Add(recipe);
            var result=new List<UnityEngine.Object>();var indices=new int[old.Length];
            for(int i=0;i<old.Length;i++)
            {
                var recipe=old[i] as GenericCraftingRecipe;
                if(!ReferenceEquals(recipe,null)&&owned.Contains(recipe)){indices[i]=-1;continue;}
                indices[i]=result.Count;result.Add(old[i]);
            }
            if(result.Count+keep.Length>MaximumCollectionSize)
                throw new InvalidOperationException("Armor recipe book exceeds ATT's 255-page collection limit ("+result.Count+" native/other pages + "+keep.Length+" armor recipe versions). Preserve recipe-history.json; no pages or unlocks were removed. Archive paging requires a separate native-compatible design.");
            foreach(var recipe in keep)result.Add(recipe);
            for(int i=0;i<old.Length;i++)if(indices[i]<0)
                for(int n=0;n<keep.Length;n++)if(ReferenceEquals(old[i],keep[n])){indices[i]=result.Count-keep.Length+n;break;}
            var originalInitial=(int[])initialPages.GetValue(collection)??new int[0];
            // Map by old slot, rather than first object occurrence: native or
            // another mod's duplicate entries retain their exact initial page.
            var remapped=new List<int>();
            foreach(int index in originalInitial)
            {
                if(index<0||index>=old.Length)throw new InvalidOperationException("Native crafting collection contains an invalid initial page index");
                if(indices[index]>=0)remapped.Add(indices[index]);
            }
            return new CollectionPlan{Collection=collection,Pages=result.ToArray(),Initial=remapped.ToArray()};
        }
        static void Apply(IEnumerable<CollectionPlan> plans)
        {
            foreach(var plan in plans)
            {
                if(!plan.Collection.PageBases.SequenceEqual(plan.Pages,new References<UnityEngine.Object>()))pageBases.SetValue(plan.Collection,plan.Pages);
                var initial=(int[])initialPages.GetValue(plan.Collection);
                if(initial==null||!initial.SequenceEqual(plan.Initial))initialPages.SetValue(plan.Collection,plan.Initial);
                tracked.Add(plan.Collection);
            }
        }
        static void BeforeInitializeBook(Book __instance)
        {
            if(!IsCraftingCollection(__instance.Collection))return;
            Apply(new[]{PlanCollection(__instance.Collection,InBookOrder(approved),approved.Keys)});
        }
        static void AfterInitializeBook(Book __instance)
        {
            if(IsCraftingCollection(__instance.Collection))initialized.Add(__instance);
        }
        static void AfterDestroyBook(Book __instance)
        {initialized.Remove(__instance);tracked.RemoveWhere(collection=>collection==null);}
        static bool IsInitialized(Book book)
        {return initialized.Contains(book)||(book.Entity!=null&&book.Entity.IsInitialized);}
        static void ReplayExistingBooks()
        {
            if(!NetworkSceneManager.IsServer||GlobalPagesManager.Instance==null)return;
            var learned=GlobalPagesManager.Instance.AddedPages;
            if(learned==null)return;
            foreach(var book in UnityEngine.Object.FindObjectsOfType<Book>(true))
                if(IsCraftingCollection(book.Collection)&&IsInitialized(book))ReplayLearned(book,learned);
            initialized.RemoveWhere(book=>book==null);
        }
        static void ReplayLearned(Book book,IEnumerable<(int,uint)> learned)
        {
            var pages=bookPages.GetValue(book) as IEnumerable<PageData>;
            if(pages==null)return;
            var known=new HashSet<(int,uint)>(learned);
            foreach(var recipe in InBookOrder(approved))
            {
                var key=(RecipeProvider,recipe.Hash);
                if(!known.Contains(key)||pages.Any(page=>page!=null&&page.Provider!=null&&page.Provider.Identifier==RecipeProvider&&ReferenceEquals(page.ProviderData,recipe)))continue;
                // Reuse native sorting, page creation and replication. Never
                // call GlobalPagesManager.AddPage or grant unlearned recipes.
                checkGlobalPage.Invoke(book,new object[]{key});
            }
        }
    }
}
