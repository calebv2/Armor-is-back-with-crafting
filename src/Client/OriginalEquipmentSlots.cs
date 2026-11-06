using System;
using UnityEngine;

namespace PlayerArmorMod.Original
{
    // Ported from ATTMain0.0.0.1 CharacterPartSlot / EquipmentPartPickup.
    // Method names and category -> enter/exit -> release -> attach flow retained.
    // Compatibility substitutions: stable ArmorSlot replaces the removed category
    // registry, proximity replaces TriggerShape, server intent replaces destroying
    // a customization shell. Neither native hand flags nor item transforms change.
    internal sealed class EquipmentSlot
    {
        internal readonly ArmorSlot Category;
        internal Transform Point;
        internal bool Occupied;
        internal bool IsDummy;
        internal Action<EquipmentPartPickup> Attach;
        internal EquipmentSlot(ArmorSlot category){Category=category;}
        internal bool CheckIfValid(ArmorSlot category){return category==Category;}
        internal void CheckEnteredObject(EquipmentPartPickup pickup)
        {if(pickup!=null&&CheckIfValid(pickup.Category)&&!Occupied)pickup.Slot=this;}
        internal void CheckExitedObject(EquipmentPartPickup pickup)
        {if(pickup!=null&&CheckIfValid(pickup.Category)&&pickup.Slot==this)pickup.Slot=null;}
        internal void AttachCharacterPart(EquipmentPartPickup pickup)
        {if(pickup!=null&&pickup.Slot==this&&CheckIfValid(pickup.Category)&&!Occupied&&Attach!=null)Attach(pickup);}
    }

    internal sealed class EquipmentPartPickup : IDisposable
    {
        internal readonly ArmorPickup Armor;
        internal readonly Pickup Pickup;
        internal readonly ArmorSlot Category;
        internal EquipmentSlot Slot;
        internal bool IsInCustomizationMode {get;private set;}
        internal bool Submitted;
        internal float ReleaseDeadline;
        internal EquipmentPartPickup(ArmorPickup armor)
        {Armor=armor;Pickup=armor.GetComponent<Pickup>();Category=(ArmorSlot)armor.Slot;}
        // Original EquipmentPartPickup.SetToCustomizationMode event lifetime.
        internal void SetToCustomizationMode(bool isInCustomizationMode)
        {
            if(isInCustomizationMode!=IsInCustomizationMode)
            {
                IsInCustomizationMode=isInCustomizationMode;
                if(Pickup!=null)
                {
                    if(isInCustomizationMode)Pickup.InteractionEnded+=ItemLetGoInCustomizationMode;
                    else Pickup.InteractionEnded-=ItemLetGoInCustomizationMode;
                }
            }
        }
        internal void ItemLetGoInCustomizationMode(Interactable interactable,Interactor interactor)
        {
            if(!IsInCustomizationMode||Submitted||Pickup==null)return;
            if(!interactable.IsInteractedWith&&interactable.Interactors.Count==0&&Slot!=null)ApplyToSlot();
        }
        void ApplyToSlot(){Slot.AttachCharacterPart(this);}
        public void Dispose(){SetToCustomizationMode(false);Slot=null;}
    }
}
