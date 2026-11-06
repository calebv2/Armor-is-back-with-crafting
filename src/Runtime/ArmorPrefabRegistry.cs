using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Alta;
using Alta.Inventory;
using Alta.Networking;
using MelonLoader;
using SyncLib.Items;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PlayerArmorMod
{
    internal sealed class ArmorPrefabRegistry
    {
        internal GameObject VisualTemplate {get;private set;}
        internal GameObject WyrmTemplate {get;private set;}
        internal NetworkPrefab Harness {get;private set;}
        internal readonly NetworkPrefab[] Pieces=new NetworkPrefab[ArmorCatalog.SlotCount];
        internal readonly NetworkPrefab[] LeatherPieces=new NetworkPrefab[ArmorCatalog.SlotCount];
        internal readonly NetworkPrefab[] WoodlandPieces=new NetworkPrefab[ArmorCatalog.SlotCount];
        internal readonly NetworkPrefab[] HebiosPieces=new NetworkPrefab[ArmorCatalog.SlotCount];
        internal readonly NetworkPrefab[] MushroomPieces=new NetworkPrefab[ArmorCatalog.SlotCount];
        internal readonly NetworkPrefab[] WyrmPieces=new NetworkPrefab[ArmorCatalog.SlotCount];
        internal NetworkPrefab[] PiecesFor(ArmorStyle style)
        {switch(style){case ArmorStyle.Metal:return Pieces;case ArmorStyle.Leather:return LeatherPieces;case ArmorStyle.Woodland:return WoodlandPieces;case ArmorStyle.Hebios:return HebiosPieces;case ArmorStyle.Mushroom:return MushroomPieces;case ArmorStyle.Wyrm:return WyrmPieces;default:throw new ArgumentOutOfRangeException("style");}}
        internal bool Ready {get;private set;}
        readonly MelonMod mod;bool loading,attempted;
        internal ArmorPrefabRegistry(MelonMod owner){mod=owner;}
        internal void Subscribe(){NetworkPrefabRegistry.RegisterPrefabs+=Register;}
        internal void Preload(){if(loading)return;loading=true;MelonCoroutines.Start(Load());}
        IEnumerator Load()
        {
            AssetBundleCreateRequest request;
            try
            {
                using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("PlayerArmor.Model"))
                using(var output=new MemoryStream())
                {
                    if(stream==null)throw new InvalidOperationException("Embedded player armor bundle is missing");
                    stream.CopyTo(output);request=AssetBundle.LoadFromMemoryAsync(output.ToArray());
                }
            }
            catch(Exception error){Core.Error("PLAYER_ARMOR_ASSET_FAILED "+error);yield break;}
            while(!request.isDone)yield return null;
            var bundle=request.assetBundle;if(bundle==null){Core.Error("PLAYER_ARMOR_ASSET_FAILED empty bundle");yield break;}
            var load=bundle.LoadAssetAsync<GameObject>("assets/ironarmor/prefabs/att_playerironarmor_visuals.prefab");
            while(!load.isDone)yield return null;
            VisualTemplate=load.asset as GameObject;bundle.Unload(false);
            if(VisualTemplate==null){Core.Error("PLAYER_ARMOR_ASSET_FAILED wearable geometry missing");yield break;}
            try
            {
                using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("PlayerArmor.Wyrm"))
                {
                    if(stream==null)throw new InvalidDataException("Missing Wyrm art bundle");
                    var wyrmBundle=AssetBundle.LoadFromStream(stream);
                    if(wyrmBundle==null)throw new InvalidDataException("Invalid Wyrm art bundle");
                    try { WyrmTemplate=wyrmBundle.LoadAsset<GameObject>("assets/originalarmor/wyrm/att_wyrmarmor_visuals.prefab"); }
                    finally { wyrmBundle.Unload(false); }
                }
                if(WyrmTemplate==null)throw new InvalidDataException("Missing Wyrm art prefab");
            }
            catch(Exception error){Core.Error("PLAYER_ARMOR_WYRM_ART_FAILED "+error);yield break;}
#if CLIENT
            ArmorStereo.Configure(VisualTemplate);
            ArmorStereo.Configure(WyrmTemplate);
#endif
            Core.Log("PLAYER_ARMOR_ART_READY six armor sets, 42 items, seven body slots");
        }
        void Register()
        {
            if(attempted)return;attempted=true;
            try
            {
                if(VisualTemplate==null)throw new InvalidOperationException("Armor preload did not finish before SyncLib registration");
                ArmorCraftingJigPlans.VerifyEmbedded();
                ArmorRecipeBookIntegration.CreateWorkshopCollection();
                // Fail before registering any network identity if a pickup proxy
                // was exported incorrectly. Worn skins use native character
                // bones and are never valid loose-pickup physics geometry.
                foreach(var definition in ArmorCatalog.AllPieces)ValidatePickupArt(definition);
                var staging=new GameObject("Player Armor templates - not placed objects");staging.SetActive(false);Object.DontDestroyOnLoad(staging);
                // A fixed native donor supplies the native pickup/interaction implementation.
                var donor=PrefabManager.AllPrefabs.FirstOrDefault(p=>p!=null&&p.Hash==61648u&&p.GetComponent<Pickup>()!=null);
                if(donor==null)throw new InvalidOperationException("Native Gold Coin pickup donor #61648 is unavailable");
                ArmorDonorLayout.Validate(donor);
                Core.Log("PLAYER_ARMOR_DONOR_READY hash="+donor.Hash+"; native child grip/heat objects will be removed from armor copies");
                var items=new Item[ArmorCatalog.SlotCount];
                foreach(var definition in ArmorCatalog.Pieces)
                {
                    items[(int)definition.Slot]=RegisterPiece(staging,donor,definition,Pieces);
                }
                // Preserve the original eight registrations: seven metal items,
                // then their saved harness. New leather items are appended.
                var harnessTemplate=ArmorHarnessTemplate.Build(staging,items);
                Harness=mod.RegisterCustomPrefab(harnessTemplate,ArmorCatalog.HarnessName);
                if(Harness==null)throw new InvalidOperationException("SyncLib refused player armor harness");
                Harness.SaveMode=ATT.Saving.SaveMode.Child;Harness.DisableSpawnThroughCommands();ArmorNative.Bind(harnessTemplate);
                var leatherItems=new Item[ArmorCatalog.SlotCount];
                foreach(var definition in ArmorCatalog.LeatherPieces)
                    leatherItems[(int)definition.Slot]=RegisterPiece(staging,donor,definition,LeatherPieces);
                var harness=harnessTemplate.GetComponent<ArmorHarness>();harness.LeatherItems=leatherItems;
                for(int i=0;i<ArmorCatalog.SlotCount;i++)harness.Docks[i].Settings.IncludedItems.Add(leatherItems[i]);
#if SERVER
                ArmorSettings.FreezeRecipes();
#endif
                // Original fourteen recipe pages precede every expansion item.
                // Preserve all historical prefab registration offsets on a new
                // SyncLib registry as well as existing saved name mappings.
                ArmorCrafting.RegisterBatch(Core.Instance,staging,this,ArmorCatalog.LegacyPieces,false);
                foreach(var style in new[]{ArmorStyle.Woodland,ArmorStyle.Hebios})
                {
                    var familyItems=new Item[ArmorCatalog.SlotCount];
                    foreach(var definition in ArmorCatalog.ForStyle(style))familyItems[(int)definition.Slot]=RegisterPiece(staging,donor,definition,PiecesFor(style));
                    if(style==ArmorStyle.Woodland)harness.WoodlandItems=familyItems;else harness.HebiosItems=familyItems;
                    for(int i=0;i<ArmorCatalog.SlotCount;i++)harness.Docks[i].Settings.IncludedItems.Add(familyItems[i]);
                }
                ArmorCrafting.RegisterBatch(Core.Instance,staging,this,ArmorCatalog.ExpansionPieces.Where(d=>!ArmorCatalog.IsLootOnly(d.Style)),false);
                // Every historical item AND page registration keeps its offset.
                // The fifth family's seven items and pages are appended only
                // after the complete four-family registration sequence above.
                var mushroomItems=new Item[ArmorCatalog.SlotCount];
                foreach(var definition in ArmorCatalog.MushroomPieces)
                    mushroomItems[(int)definition.Slot]=RegisterPiece(staging,donor,definition,MushroomPieces);
                harness.MushroomItems=mushroomItems;
                for(int i=0;i<ArmorCatalog.SlotCount;i++)harness.Docks[i].Settings.IncludedItems.Add(mushroomItems[i]);
                ArmorCrafting.RegisterBatch(Core.Instance,staging,this,ArmorCatalog.MushroomPieces,true);
                NetworkPrefab stand = null;
                try { stand = ArmorStandPrefab.Register(mod, staging, this); }
                catch(Exception error) { Core.Error("PLAYER_ARMOR_STAND_REGISTRATION_FAILED " + error); }
                var wyrmItems=new Item[ArmorCatalog.SlotCount];
                foreach(var definition in ArmorCatalog.WyrmPieces)
                    wyrmItems[(int)definition.Slot]=RegisterPiece(staging,donor,definition,WyrmPieces);
                harness.WyrmItems=wyrmItems;
                for(int i=0;i<ArmorCatalog.SlotCount;i++)harness.Docks[i].Settings.IncludedItems.Add(wyrmItems[i]);
                if(stand!=null)ArmorStandDocks.AddWyrm(stand,WyrmPieces);
                NetworkPrefab[] workshop=new NetworkPrefab[0];
                try { workshop=ArmorWorkshopPrefabs.Register(mod,staging,stand); }
                catch(Exception error) { Core.Error("PLAYER_ARMOR_WORKSHOP_REGISTRATION_FAILED "+error); }
                Ready=true;Core.Log("PLAYER_ARMOR_REGISTERED harness="+Harness.Hash+" sockets=7 styles=metal,leather,woodland,hebios,mushroom,wyrm protection=native durability=native");
#if SERVER
                try
                {
                    string path=Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory,"PlayerArmor");Directory.CreateDirectory(path);
                    var names=ArmorCatalog.AllPieces.Select(d=>d.PrefabName+" #"+PiecesFor(d.Style)[(int)d.Slot].Hash+" ("+d.Style+" "+d.Slot+(ArmorCatalog.IsChestOnly(d.Style)?", world chest loot":d.Style==ArmorStyle.Wyrm?", Wyrm kill loot":"")+")").ToList();
                    if(stand!=null)names.Add(ArmorStandPrefab.Name+" #"+stand.Hash+" (Armor stand)");
                    names.AddRange(workshop.Select(prefab=>prefab.name+" #"+prefab.Hash+" (Armor workshop)"));
                    File.WriteAllLines(Path.Combine(path,"prefabs.txt"),names.ToArray());
                }
                catch(Exception error){Core.Error("PLAYER_ARMOR_PREFAB_LIST_WRITE_FAILED "+error.GetBaseException().Message);}
#endif
            }
            catch(Exception error){Ready=false;Core.Error("PLAYER_ARMOR_REGISTRATION_FAILED "+error);}
        }
        Item RegisterPiece(GameObject staging,NetworkPrefab donor,ArmorPieceDefinition definition,NetworkPrefab[] destination)
        {
            var template=BuildPickup(staging,donor,definition);
            var prefab=mod.RegisterCustomPrefab(template,definition.PrefabName);
            if(prefab==null)throw new InvalidOperationException("SyncLib refused "+definition.PrefabName);
            if(definition.Style==ArmorStyle.Wyrm)ArmorNative.Set(prefab,"canBeSpawnedThroughCommand",true);
            if(ArmorCatalog.IsChestOnly(definition.Style))prefab.DisableSpawnThroughCommands();
            if(template.GetComponent<NetworkRigidbody>()==null)throw new InvalidOperationException("SyncLib did not add armor NetworkRigidbody");
            prefab.SaveMode=ATT.Saving.SaveMode.Base;
            var pickup=template.GetComponent<Pickup>();
            ArmorNative.Set(pickup,"networkRigidbody",template.GetComponent<NetworkRigidbody>());
            var item=CreateItem(definition,prefab);pickup.Item=item;
            ArmorNative.Bind(template);destination[(int)definition.Slot]=prefab;
            Core.Log("PLAYER_ARMOR_PIECE_REGISTERED name="+definition.PrefabName+" hash="+prefab.Hash+" style="+definition.Style);
            return item;
        }
        GameObject BuildPickup(GameObject staging,NetworkPrefab donor,ArmorPieceDefinition definition)
        {
            var root=Object.Instantiate(donor.gameObject,staging.transform,false);root.name=definition.PrefabName;
            ArmorDonorLayout.Normalize(root);
            root.transform.localPosition=Vector3.zero;root.transform.localRotation=Quaternion.identity;root.transform.localScale=Vector3.one;
            // Keep the donor's native entity/interaction classes. Remove its
            // coin-specific gameplay and replace geometry and cached colliders.
            foreach(var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                if(!(behaviour is NetworkPrefab)&&!(behaviour is NetworkEntity)&&!(behaviour is Pickup)&&!(behaviour is ColliderGroup))Object.DestroyImmediate(behaviour);
            foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))Object.DestroyImmediate(renderer);
            foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true))Object.DestroyImmediate(filter);
            foreach(var collider in root.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(collider);
            foreach(var lod in root.GetComponentsInChildren<LODGroup>(true))Object.DestroyImmediate(lod);
            var group=PickupArt(definition);
            var art=Object.Instantiate(group.gameObject,root.transform,false);art.name="Player armor art";
            art.transform.localPosition=group.localPosition;art.transform.localRotation=group.localRotation;art.transform.localScale=group.localScale;
            var bounds=ArmorPickupBounds.Validate(ArmorNative.LocalBounds(root),definition.PrefabName);
            var colliderNew=root.AddComponent<BoxCollider>();colliderNew.center=bounds.center;colliderNew.size=Vector3.Max(bounds.size,Vector3.one*.025f);
            var colliders=root.GetComponent<ColliderGroup>();if(colliders==null)colliders=root.AddComponent<ColliderGroup>();
            colliders.Components.Clear();colliders.Components.Add(colliderNew);
            var pickup=root.GetComponent<Pickup>();
            root.transform.localPosition=Vector3.zero;root.transform.localRotation=Quaternion.identity;root.transform.localScale=Vector3.one;
            ArmorNative.Set(pickup,"childrenColliders",new Collider[]{colliderNew});ArmorNative.Set(pickup,"rigidbody",root.GetComponent<Rigidbody>());
            ArmorNative.Set(pickup,"grabPointsScale",1f);ArmorNative.Set(pickup,"isTightGrab",false);
            ArmorNative.Set(pickup,"isSwingable",false);ArmorNative.Set(pickup,"blockLevel",BlockLevel.CantBlock);
            // Donor UnityEvents can retain persistent listeners to removed coin
            // children. Keep native event containers, with no inherited listeners.
            ArmorNative.Set(pickup,"docked",new PickupDockedEvent());ArmorNative.Set(pickup,"undocked",new PickupDockedEvent());
            ArmorNative.Set(pickup,"breakEvent",new PickupEvent());ArmorNative.Set(pickup,"subDocks",null);ArmorNative.Set(pickup,"durabilityModule",null);
            ArmorNative.Set(pickup,"isLoggingOwnerChanged",false);ArmorNative.Set(pickup,"isNextTreatedAsDocked",false);
            ArmorNative.Set(pickup,"isEnvironmentalJoint",false);ArmorNative.Set(pickup,"isChunkingParentStatic",false);
            ArmorNative.Set(pickup,"craftingPart",null);ArmorNative.Set(pickup,"interfereWithDockingObjects",new MonoBehaviour[0]);
            ArmorSurfaceGrip.Configure(pickup,definition,bounds);
            var body=root.GetComponent<Rigidbody>();body.mass=ArmorWeightPolicy.BaseWeight(definition.Style,definition.Slot);body.isKinematic=false;body.useGravity=true;
            body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            body.ResetCenterOfMass();body.ResetInertiaTensor();
            var marker=root.AddComponent<ArmorPickup>();marker.Slot=(int)definition.Slot;marker.Style=definition.Style;marker.Art=art.transform;
            ArmorDurability.AddNative(root,marker);
            ArmorMaterialState.AddNative(root,marker);
            ArmorCraftRecord.AddNative(root);
#if SERVER
            // Headless pickups need their native collider and saved item only.
            // Keep worn character skins out of every server-side loose entity.
            Object.DestroyImmediate(art);marker.Art=null;
#endif
            Core.Log("PLAYER_ARMOR_PICKUP_BOUNDS name="+definition.PrefabName+" center="+bounds.center.ToString("F3")+" size="+bounds.size.ToString("F3"));
            root.SetActive(true); // staging stays inactive; spawned instances must be enabled.
            return root;
        }
        Transform PickupArt(ArmorPieceDefinition definition)
        {
            string leaf=definition.ArtRootName.Substring(definition.ArtRootName.LastIndexOf('/')+1);
            string path="Pickups/"+definition.Style+"/"+leaf;
            var template=definition.Style==ArmorStyle.Wyrm?WyrmTemplate:VisualTemplate;
            var group=template==null?null:template.transform.Find(path);
            if(group==null)throw new InvalidOperationException("Missing compact static armor pickup art "+path+"; rebuild the armor bundle");
            return group;
        }
        void ValidatePickupArt(ArmorPieceDefinition definition)
        {
            var group=PickupArt(definition);
            if(group.localPosition.sqrMagnitude>.000001f||Quaternion.Angle(group.localRotation,Quaternion.identity)>.001f||(group.localScale-Vector3.one).sqrMagnitude>.000001f)
                throw new InvalidOperationException("Armor pickup proxy must have identity transform: "+definition.PrefabName);
            if(group.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length!=0||group.GetComponentsInChildren<MonoBehaviour>(true).Length!=0||group.GetComponentsInChildren<Collider>(true).Length!=0||group.GetComponentsInChildren<Rigidbody>(true).Length!=0)
                throw new InvalidOperationException("Armor pickup proxy must contain static geometry only: "+definition.PrefabName);
            if(group.GetComponentsInChildren<MeshRenderer>(true).Length!=1||group.GetComponentsInChildren<MeshFilter>(true).Length!=1)
                throw new InvalidOperationException("Armor pickup proxy must contain exactly one static mesh: "+definition.PrefabName);
            ArmorPickupBounds.Validate(ArmorNative.LocalBounds(group.gameObject),definition.PrefabName);
            ArmorSurfaceGrip.CacheSurface(definition,group);
        }
        static Item CreateItem(ArmorPieceDefinition definition,NetworkPrefab prefab)
        {
            var item=ScriptableObject.CreateInstance<Item>();item.name=definition.PrefabName;
            ArmorNative.Set(item,"hash",(int)prefab.Hash);item.Prefab=prefab;
            ArmorNative.Set(item,"isStackable",false);ArmorNative.Set(item,"isStackableWhenCrafted",false);
            ArmorNative.Set(item,"dockedStackSize",1);ArmorNative.Set(item,"size",2);
            ArmorNative.Set(item,"weight",ArmorWeightPolicy.BaseWeight(definition.Style,definition.Slot));
            ArmorNative.Set(item,"isUsingShrunkenVisual",false);ArmorNative.Set(item,"isScalingOnGrab",false);ArmorNative.Set(item,"storeScale",Vector3.one);
            ArmorNative.Set(item,"normalPositioning",new Item.DockPositioning(Vector3.zero,Vector3.zero));
            ArmorNative.Set(item,"scaledPositioning",new Item.DockPositioning(Vector3.zero,Vector3.zero));
            ArmorNative.Set(item,"customPositions",new Item.CustomDockPositioning[0]);ArmorNative.Set(item,"customPositionsWhenCrafted",new Item.CustomDockPositioning[0]);
            var localCollider=prefab.GetComponent<BoxCollider>();
            ArmorStorage.ConfigureItem(item,new Bounds(localCollider.center,localCollider.size));
            var map=(Dictionary<uint,Item>)ArmorNative.Field(typeof(HashedGeneralValue<Item>),"items").GetValue(null);
            if(map.ContainsKey(prefab.Hash))throw new InvalidOperationException("Armor Item hash already belongs to an item: "+prefab.Hash);
            map.Add(prefab.Hash,item);
            ArmorNative.Field(typeof(HashedGeneralValue<Item>),"itemsByName").SetValue(null,null);
            return item;
        }
    }
}
