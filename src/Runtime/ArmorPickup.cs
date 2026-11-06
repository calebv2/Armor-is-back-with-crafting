using Alta.Inventory;
using Alta.Networking;
using UnityEngine;

namespace PlayerArmorMod
{
    // Identification and presentation only. Native Pickup/Docker own grabbing,
    // world physics, network messages, docking, and the saved inventory item.
    internal sealed partial class ArmorPickup : NetworkEntityBehaviour
    {
        public int Slot;
        public ArmorStyle Style;
        public Transform Art;
        internal float Weight { get; set; }
        Renderer[] renderers;
#if CLIENT
        bool? shown;
        ArmorMaterialAppearance materialAppearance;
        ArmorMaterialState materialState;
        uint materialLayout=1;
        uint materialRevision=uint.MaxValue;
        uint[] materialHashes=new uint[0];
#endif
        Pickup pickup;
        public override void Initialize()
        {
            base.Initialize();pickup=GetComponent<Pickup>();
            renderers=Art==null?new Renderer[0]:Art.GetComponentsInChildren<Renderer>(true);
            if(pickup!=null)
            {
                ArmorSurfaceGrip.Register(pickup,Style,Slot);
                pickup.InteractionFilter+=FilterWearer;
            }
            ArmorDurability.Register(this);
            if (Weight <= 0) ArmorWeight.Refresh(this, 1f);
        }
        void FilterWearer(Interactor hand,Interactable item,ref bool allowed)
        {
            if(!allowed||pickup==null||pickup.Dock==null)return;
            var harness=pickup.Dock.GetComponentInParent<ArmorHarness>();if(harness==null)return;
            if(harness.Owner==null||harness.Owner.IsSpirit||hand==null||hand.Controller==null||hand.Controller.PlayerController!=harness.Owner)allowed=false;
        }
        void LateUpdate()
        {
#if CLIENT
            if(pickup==null)pickup=GetComponent<Pickup>();
            if(ArmorCatalog.UsesCraftMaterials(Style)&&Art!=null)
            {
                if(renderers==null)renderers=Art.GetComponentsInChildren<Renderer>(true);
                if(materialAppearance==null)materialAppearance=new ArmorMaterialAppearance(renderers,Style,ArmorMaterialPalette.Resolve);
                if(materialState==null){var resolved=GetComponent<ArmorMaterialState>();if(!object.ReferenceEquals(materialState,resolved)){materialState=resolved;materialRevision=uint.MaxValue;materialHashes=new uint[0];materialAppearance.Restore();}}
                if(materialState!=null&&materialRevision!=materialState.Revision){materialRevision=materialState.Revision;materialHashes=materialState.PrimaryMaterialHashes;materialLayout=materialState.LayoutVersion;}
                materialAppearance.Update(materialHashes,materialRevision,materialLayout,ArmorConstructionPlan.PrimaryCount(Style,(ArmorSlot)Slot),ArmorCatalog.TintsFasteners(Style));
            }
            bool worn=pickup!=null&&pickup.Dock!=null&&pickup.Dock.GetComponentInParent<ArmorHarness>()!=null;
            bool visible=!worn;
            if(shown==visible)return;shown=visible;
            if(renderers==null)renderers=Art==null?new Renderer[0]:Art.GetComponentsInChildren<Renderer>(true);
            foreach(var renderer in renderers)if(renderer!=null)renderer.enabled=visible;
#endif
        }
        void OnDestroy()
        {
#if CLIENT
            if(materialAppearance!=null)materialAppearance.Dispose();materialAppearance=null;
#endif
            ArmorSurfaceGrip.Unregister(pickup);
            if(pickup!=null)pickup.InteractionFilter-=FilterWearer;ArmorDurability.Unregister(this);
        }
    }
}
