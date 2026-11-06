using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PlayerArmorMod
{
    // Keep one native, serializable grip (index zero) for old saves, dock memory and
    // late observers. Only the local hit position varies with the particular hand.
    internal static class ArmorSurfaceGrip
    {
        static readonly Dictionary<int,Func<Vector3,Vector3>> templates = new Dictionary<int,Func<Vector3,Vector3>>();
        static readonly Dictionary<Pickup,Func<Vector3,Vector3>> instances = new Dictionary<Pickup,Func<Vector3,Vector3>>();
        static HarmonyLib.Harmony harmony;
        static MethodInfo testGrabPoints;
        static MethodInfo postfix;
        static MethodInfo ensureBestGrab;
        static MethodInfo ensurePrefix;
        static MethodInfo networkStart, networkPrefix, networkFinalizer;
        struct IncomingGrip
        {
            internal Pickup Pickup;
            internal Interactor Hand;
        }
        [ThreadStatic] static IncomingGrip incoming;

        internal static void Install(HarmonyLib.Harmony modHarmony)
        {
            if(harmony!=null)return;
            testGrabPoints=AccessTools.Method(typeof(Pickup),"TestGrabPoints",new[]{typeof(Transform),typeof(HandIndex),typeof(float).MakeByRefType()});
            ensureBestGrab=AccessTools.Method(typeof(Pickup),"EnsureBestGrabFound",new[]{typeof(Interactor)});
            networkStart=AccessTools.Method(typeof(Pickup),"NetworkStartInteraction",new[]{typeof(INetworkInteractArgument),typeof(Interactor),typeof(Action<Interactable>)});
            if(testGrabPoints==null)throw new MissingMethodException(typeof(Pickup).FullName,"TestGrabPoints");
            if(ensureBestGrab==null)throw new MissingMethodException(typeof(Pickup).FullName,"EnsureBestGrabFound");
            if(networkStart==null)throw new MissingMethodException(typeof(Pickup).FullName,"NetworkStartInteraction");
            postfix=AccessTools.Method(typeof(ArmorSurfaceGrip),"AfterTestGrabPoints");
            ensurePrefix=AccessTools.Method(typeof(ArmorSurfaceGrip),"BeforeEnsureBestGrab");
            networkPrefix=AccessTools.Method(typeof(ArmorSurfaceGrip),"BeforeNetworkStart");
            networkFinalizer=AccessTools.Method(typeof(ArmorSurfaceGrip),"AfterNetworkStart");
            modHarmony.Patch(testGrabPoints,postfix:new HarmonyMethod(postfix));
            try
            {
                modHarmony.Patch(ensureBestGrab,prefix:new HarmonyMethod(ensurePrefix));
                modHarmony.Patch(networkStart,prefix:new HarmonyMethod(networkPrefix),finalizer:new HarmonyMethod(networkFinalizer));
            }
            catch
            {
                modHarmony.Unpatch(networkStart,networkPrefix);
                modHarmony.Unpatch(networkStart,networkFinalizer);
                modHarmony.Unpatch(ensureBestGrab,ensurePrefix);
                modHarmony.Unpatch(testGrabPoints,postfix);
                throw;
            }
            harmony=modHarmony;
        }

        static int Key(ArmorStyle style,int slot){return (int)style*ArmorCatalog.SlotCount+slot;}

        internal static void CacheSurface(ArmorPieceDefinition piece,Transform art)
        {
            var filter=art.GetComponentInChildren<MeshFilter>(true);
            var mesh=filter.sharedMesh;
            if(!mesh.isReadable)throw new InvalidOperationException("Unreadable armor grip mesh: "+piece.PrefabName);
            // The pickup factory reuses the proxy group's local pose under the loose
            // root. Include child transforms; the native worn bone is a different frame.
            var meshToPickup=Matrix4x4.TRS(art.localPosition,art.localRotation,art.localScale)*
                art.worldToLocalMatrix*filter.transform.localToWorldMatrix;
            var points=mesh.vertices;
            for(int i=0;i<points.Length;i++)points[i]=meshToPickup.MultiplyPoint3x4(points[i]);
            templates[Key(piece.Style,(int)piece.Slot)]=ArmorCraftingMesh.BuildSurfaceQuery(points,mesh.triangles,piece.PrefabName);
        }

        internal static void Configure(Pickup pickup,ArmorPieceDefinition piece,Bounds bounds)
        {
            var grab=new GrabPoint{rotationMode=RotationMode.Free};
            var surface=templates[Key(piece.Style,(int)piece.Slot)];
            ArmorNative.Set(grab,"position",surface(bounds.center));
            ArmorNative.Set(grab,"isIgnoringAngle",true);
            ArmorNative.Set(pickup,"grabPoints",new[]{grab});
        }

        internal static void Register(Pickup pickup,ArmorStyle style,int slot)
        {
            Func<Vector3,Vector3> surface;
            if(templates.TryGetValue(Key(style,slot),out surface))instances[pickup]=surface;
        }

        internal static void Unregister(Pickup pickup)
        {if(!ReferenceEquals(pickup,null))instances.Remove(pickup);}

        // Native scoring has already enforced enabled/occupied/dock restrictions
        // and computed the Free grip's rotation. Native GrabPointArguments transmits
        // the chosen LastPosition and LastRotation and builds its usual hand joint.
        static void AfterTestGrabPoints(Pickup __instance,Transform interactionPoint,ref float score,bool __result)
        {
            Func<Vector3,Vector3> surface;
            if(!__result || !instances.TryGetValue(__instance,out surface))return;
            var handPosition=__instance.transform.InverseTransformPoint(interactionPoint.position);
            var contact=surface(handPosition);
            __instance.BestGrab.LastPosition=contact;
            score=(contact-handPosition).sqrMagnitude;
        }

        // A dock may remember a previously saved fixed center grip. When the local
        // player actually pulls the armor out, use the current hand and native
        // scoring instead of restoring that old position and rotation.
        static bool BeforeEnsureBestGrab(Pickup __instance,Interactor interactor)
        {
            if(!instances.ContainsKey(__instance))return true;
            // NetworkStartInteraction already applied the sender's selected native
            // GrabPointArguments. A receiving dock must not replace it with its own
            // palm sample (or a previously remembered center) during the callback.
            if(ReferenceEquals(incoming.Pickup,__instance) && ReferenceEquals(incoming.Hand,interactor) && __instance.BestGrab!=null)return false;
            if(!__instance.IsDocked || !interactor.IsLocal)return true;
            float score;
            __instance.AssessScore(interactor,out score);
            return false;
        }

        static bool BeforeNetworkStart(Pickup __instance,INetworkInteractArgument args,Interactor interactor,out IncomingGrip __state)
        {
            __state=incoming;
#if SERVER
            // Native network grabs can proceed after a failed interaction
            // assessment. Stop them before they reach a stowed armor pickup.
            if(__instance.Dock!=null)
            {
                var harness=__instance.Dock.GetComponentInParent<ArmorHarness>();
                if(harness!=null&&harness.Owner!=null&&harness.Owner.IsSpirit)return false;
            }
#endif
            incoming=(args is GrabPointArguments && instances.ContainsKey(__instance)) ?
                new IncomingGrip{Pickup=__instance,Hand=interactor} : default(IncomingGrip);
            return true;
        }

        static void AfterNetworkStart(IncomingGrip __state){incoming=__state;}

        internal static void Dispose()
        {
            if(harmony!=null)
            {
                harmony.Unpatch(networkStart,networkPrefix);
                harmony.Unpatch(networkStart,networkFinalizer);
                harmony.Unpatch(ensureBestGrab,ensurePrefix);
                harmony.Unpatch(testGrabPoints,postfix);
            }
            harmony=null;
            incoming=default(IncomingGrip);
            instances.Clear();
            templates.Clear();
        }
    }

    internal static partial class ArmorCraftingMesh
    {
        // Use the same triangle tree and exact face/edge projection as the workbench.
        // One immutable tree per template; no mesh reads or allocations during a grab.
        internal static Func<Vector3,Vector3> BuildSurfaceQuery(Vector3[] vertices,int[] indices,string name)
        {
            var tree=JigBuildTree(vertices,indices,name);
            return point=>{
                Vector3 closest=Vector3.zero;
                float distance=float.PositiveInfinity;
                ClosestGripPoint(tree,point,ref closest,ref distance);
                return closest;
            };
        }

        static void ClosestGripPoint(JigTree tree,Vector3 point,ref Vector3 closest,ref float bestDistance)
        {
            if(tree.Triangles!=null)
            {
                foreach(var triangle in tree.Triangles)
                {
                    var candidate=JigClosestPoint(point,triangle);
                    float distance=(point-candidate).sqrMagnitude;
                    if(distance<bestDistance){closest=candidate;bestDistance=distance;}
                }
                return;
            }
            var pointBounds=new Bounds(point,Vector3.zero);
            float left=JigBoundsGapSquared(pointBounds,tree.Left.Bounds);
            float right=JigBoundsGapSquared(pointBounds,tree.Right.Bounds);
            var nearer=left<=right?tree.Left:tree.Right;
            var farther=left<=right?tree.Right:tree.Left;
            if(Math.Min(left,right)<bestDistance)ClosestGripPoint(nearer,point,ref closest,ref bestDistance);
            if(Math.Max(left,right)<bestDistance)ClosestGripPoint(farther,point,ref closest,ref bestDistance);
        }
    }
}
