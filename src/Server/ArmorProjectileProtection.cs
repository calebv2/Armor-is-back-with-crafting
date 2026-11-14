using Alta.Impact;

namespace PlayerArmorMod
{
    internal static class ArmorProjectileProtection
    {
        internal static bool IsProjectile(ImpactHit hit)
        {
            var toolHit=hit as ImpactHitWithTool;
            if(toolHit==null)return false;
            var tool=toolHit.Tool;
            var impactor=toolHit.Impactor;
            var pickup=tool!=null?tool.Pickup:impactor==null?null:impactor.Pickup;
            // The attack's source, never the player's struck collider. A held
            // arrow or spear is a melee hit; a released thrown weapon is ranged.
            if(pickup!=null&&pickup.IsInteractedWith)return false;
            if(tool!=null&&(tool.GetComponent<Projectile>()!=null||tool.GetComponent<RaycastProjectile>()!=null||tool.GetComponent<EmbeddableRaycastProjectile>()!=null))return true;
            if(impactor!=null&&(impactor.GetComponentInParent<Projectile>()!=null||impactor.GetComponentInParent<RaycastProjectile>()!=null))return true;
            return pickup!=null&&pickup.LastPlayerInteractor!=null&&pickup.Rigidbody!=null&&pickup.Rigidbody.velocity.sqrMagnitude>1f;
        }
        internal static float Reduction(ImpactHit hit,ArmorPieceSettings balance)
        {
            return balance.projectileDamageReduction.HasValue&&IsProjectile(hit)
                ?balance.projectileDamageReduction.Value:balance.damageReduction;
        }
    }
}
