using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayerArmorMod
{
    internal static partial class ArmorCraftingMesh
    {
        internal static int JigPoseCandidateCount(ArmorStyle style,ArmorSlot slot)
        {
            if(style==ArmorStyle.Leather&&slot==ArmorSlot.LeftForearm)return 8;
            if(style==ArmorStyle.Metal&&slot==ArmorSlot.Hips)return 17;
            return 1;
        }
        internal static void PrepareJigMeshes(Mesh[] meshes,bool[] primary,ArmorStyle style,ArmorSlot slot)
        {PrepareJigMeshes(meshes,primary,style,slot,style==ArmorStyle.Metal&&slot==ArmorSlot.Hips?9:0);}

        // Called only for newly owned layout-3 workbench meshes, before their
        // collision hulls. Wearable/pickup assets and semantic identities never
        // pass through this function. Candidate overload is for geometry QA.
        internal static void PrepareJigMeshes(Mesh[] meshes,bool[] primary,ArmorStyle style,ArmorSlot slot,int candidate)
        {
            if(meshes==null||primary==null||meshes.Length==0||meshes.Length!=primary.Length)throw new ArgumentException("Invalid armor jig pose meshes");
            if(candidate<0||candidate>=JigPoseCandidateCount(style,slot))throw new ArgumentOutOfRangeException("candidate");
            foreach(var mesh in meshes)if(mesh==null)throw new ArgumentException("Armor jig pose has a missing owned mesh");
            if(style==ArmorStyle.Leather&&slot==ArmorSlot.LeftForearm)
            {
                // Strap centres locate the forearm's actual longitudinal axis,
                // independently of native left/right bone coordinate frames.
                var straps=new List<int>();for(int i=0;i<meshes.Length;i++)if(!primary[i])straps.Add(i);
                if(straps.Count!=2)throw new InvalidOperationException("Leather bracer jig requires its two whole authored straps");
                var axis=meshes[straps[1]].bounds.center-meshes[straps[0]].bounds.center;
                if(axis.sqrMagnitude<.000001f)throw new InvalidOperationException("Leather bracer jig strap axis is degenerate");
                var angles=new[]{180f,90f,270f,45f,135f,225f,315f,0f};
                JigRotateAndSeat(meshes,Quaternion.AngleAxis(angles[candidate],axis.normalized));
            }
            else if(style==ArmorStyle.Metal&&slot==ArmorSlot.Hips)
            {
                var panels=new List<int>();for(int i=0;i<meshes.Length;i++)if(primary[i])panels.Add(i);
                if(panels.Count!=4||meshes.Length!=5)throw new InvalidOperationException("Metal hip jig requires four complete panels and its original closure group");
                // Native tassets form two independent hinged wings. Close each
                // upper panel onto its own lower panel while clamped. The lower
                // panels and their two closure islands retain their exact fit;
                // no invented belt joins the left and right sides.
                JigCloseHinge(meshes[panels[0]],meshes[panels[1]]);
                JigCloseHinge(meshes[panels[2]],meshes[panels[3]]);
                if(candidate>=9){JigFaceUpWings(meshes,primary,panels,candidate-9);return;}
                var rotations=new[]{Quaternion.Euler(-90,0,0),Quaternion.Euler(90,0,0),Quaternion.identity,
                    Quaternion.Euler(180,0,0),Quaternion.Euler(0,0,90),Quaternion.Euler(0,0,-90),
                    Quaternion.Euler(0,0,180),Quaternion.Euler(-45,0,0),Quaternion.Euler(45,0,0)};
                JigRotateAndSeat(meshes,rotations[candidate]);
            }
        }
        sealed class JigWingPose
        {internal Quaternion Rotation;internal Vector3 Pivot,Offset;internal Bounds Bounds;}
        static void JigFaceUpWings(Mesh[] meshes,bool[] primary,List<int> panels,int variant)
        {
            int closure=-1;for(int i=0;i<primary.Length;i++)if(!primary[i])closure=i;
            var points=meshes[closure].vertices;var islands=JigPhysicalIslands(meshes[closure]);
            if(islands.Count!=2)throw new InvalidOperationException("Native Metal Hips closure must retain its two intact physical islands");
            var centers=new Vector3[2];for(int i=0;i<2;i++){foreach(int vertex in islands[i])centers[i]+=points[vertex];centers[i]/=islands[i].Count;}
            var bounds=new Bounds[2];for(int wing=0;wing<2;wing++){bounds[wing]=meshes[panels[wing*2]].bounds;bounds[wing].Encapsulate(meshes[panels[wing*2+1]].bounds);}
            float direct=(centers[0]-bounds[0].center).sqrMagnitude+(centers[1]-bounds[1].center).sqrMagnitude;
            float crossed=(centers[0]-bounds[1].center).sqrMagnitude+(centers[1]-bounds[0].center).sqrMagnitude;
            var assignment=direct<=crossed?new[]{0,1}:new[]{1,0};var poses=new JigWingPose[2];var center=(bounds[0].center+bounds[1].center)*.5f;
            for(int wing=0;wing<2;wing++)
            {
                var upper=meshes[panels[wing*2]];var lower=meshes[panels[wing*2+1]];
                var normal=JigOuterPlateNormal((variant&1)==0?lower:upper,bounds[wing].center-center);
                bool flip=variant<4?variant>=2:variant<6?wing==0:wing==1;if(flip)normal=-normal;
                var rotation=Quaternion.FromToRotation(normal,Vector3.up);var heading=rotation*(lower.bounds.center-upper.bounds.center);heading.y=0;
                if(heading.sqrMagnitude>1e-10f)rotation=Quaternion.AngleAxis(-Mathf.Atan2(heading.x,heading.z)*Mathf.Rad2Deg,Vector3.up)*rotation;
                var pose=new JigWingPose{Pivot=bounds[wing].center,Rotation=rotation};bool first=true;
                foreach(int index in new[]{panels[wing*2],panels[wing*2+1]})foreach(var point in meshes[index].vertices)JigPoseBounds(pose,point,ref first);
                for(int island=0;island<2;island++)if(assignment[island]==wing)foreach(int vertex in islands[island])JigPoseBounds(pose,points[vertex],ref first);
                poses[wing]=pose;
            }
            // Separate the two supported wings by a real hammer-head corridor.
            // The standard native head extends about75mm to either side of a
            // strike; a25mm gap lets the other wing obstruct an otherwise good
            // joint. Final uniform seating still obeys the55cm bench footprint.
            const float aisle=.180f;float leftWidth=poses[0].Bounds.size.x,rightWidth=poses[1].Bounds.size.x;
            for(int wing=0;wing<2;wing++)
            {
                var pose=poses[wing];float x=wing==0?-(rightWidth+aisle)*.5f:(leftWidth+aisle)*.5f;
                pose.Offset=new Vector3(x-pose.Bounds.center.x,.03f-pose.Bounds.min.y,-pose.Bounds.center.z);
                JigApplyWing(meshes[panels[wing*2]],pose);JigApplyWing(meshes[panels[wing*2+1]],pose);
            }
            // Transform complete connected closure islands, never a spatial
            // cut through triangles or a new bridging belt. Their original fit
            // to the associated lower panel remains exact throughout the pose.
            var normals=meshes[closure].normals;var tangents=meshes[closure].tangents;
            for(int island=0;island<2;island++)
            {
                var pose=poses[assignment[island]];foreach(int vertex in islands[island])
                {
                    points[vertex]=pose.Rotation*(points[vertex]-pose.Pivot)+pose.Offset;
                    if(normals.Length==points.Length)normals[vertex]=pose.Rotation*normals[vertex];
                    if(tangents.Length==points.Length){var t=tangents[vertex];var xyz=pose.Rotation*new Vector3(t.x,t.y,t.z);tangents[vertex]=new Vector4(xyz.x,xyz.y,xyz.z,t.w);}
                }
            }
            meshes[closure].vertices=points;if(normals.Length==points.Length)meshes[closure].normals=normals;if(tangents.Length==points.Length)meshes[closure].tangents=tangents;meshes[closure].RecalculateBounds();
            JigRotateAndSeat(meshes,Quaternion.identity);
        }
        static void JigPoseBounds(JigWingPose pose,Vector3 point,ref bool first)
        {point=pose.Rotation*(point-pose.Pivot);if(first){pose.Bounds=new Bounds(point,Vector3.zero);first=false;}else pose.Bounds.Encapsulate(point);}
        static Vector3 JigOuterPlateNormal(Mesh mesh,Vector3 outward)
        {
            outward.Normalize();var points=mesh.vertices;var triangles=mesh.triangles;float best=-1;var result=Vector3.up;
            for(int i=0;i<triangles.Length;i+=3)
            {
                var cross=Vector3.Cross(points[triangles[i+1]]-points[triangles[i]],points[triangles[i+2]]-points[triangles[i]]);float area=cross.magnitude;if(area<1e-10f)continue;
                var normal=cross/area;float score=area*(.1f+Mathf.Max(0,Vector3.Dot(normal,outward)));
                if(score>best){best=score;result=normal;}
            }
            return result;
        }
        static void JigApplyWing(Mesh mesh,JigWingPose pose)
        {
            var points=mesh.vertices;for(int i=0;i<points.Length;i++)points[i]=pose.Rotation*(points[i]-pose.Pivot)+pose.Offset;mesh.vertices=points;
            var normals=mesh.normals;if(normals.Length==points.Length){for(int i=0;i<normals.Length;i++)normals[i]=pose.Rotation*normals[i];mesh.normals=normals;}
            var tangents=mesh.tangents;if(tangents.Length==points.Length){for(int i=0;i<tangents.Length;i++){var t=tangents[i];var xyz=pose.Rotation*new Vector3(t.x,t.y,t.z);tangents[i]=new Vector4(xyz.x,xyz.y,xyz.z,t.w);}mesh.tangents=tangents;}mesh.RecalculateBounds();
        }
        static int JigIslandRoot(int[] parent,int index)
        {while(parent[index]!=index){parent[index]=parent[parent[index]];index=parent[index];}return index;}
        static List<List<int>> JigPhysicalIslands(Mesh mesh)
        {
            var points=mesh.vertices;var triangles=mesh.triangles;var parent=new int[points.Length];var map=new Dictionary<Vector3Int,int>();
            for(int i=0;i<points.Length;i++){int existing;var key=HullKey(points[i]);if(map.TryGetValue(key,out existing))parent[i]=existing;else{map.Add(key,i);parent[i]=i;}}
            for(int i=0;i<triangles.Length;i+=3)for(int n=1;n<3;n++){int a=JigIslandRoot(parent,triangles[i]),b=JigIslandRoot(parent,triangles[i+n]);parent[b]=a;}
            var groups=new Dictionary<int,List<int>>();for(int i=0;i<points.Length;i++){int root=JigIslandRoot(parent,i);List<int> group;if(!groups.TryGetValue(root,out group))groups.Add(root,group=new List<int>());group.Add(i);}
            var result=new List<List<int>>(groups.Values);result.Sort((a,b)=>a[0].CompareTo(b[0]));return result;
        }
        static void JigCloseHinge(Mesh upper,Mesh lower)
        {
            Vector3 from=Vector3.zero,to=Vector3.zero;float distance=float.PositiveInfinity;
            JigNearestSurfaces(JigBuildTree(upper),JigBuildTree(lower),ref distance,ref from,ref to);
            if(float.IsPositiveInfinity(distance))throw new InvalidOperationException("Metal tasset hinge has no real source surfaces");
            float gap=Mathf.Sqrt(distance);if(gap<=.002f)return;
            if(gap>.025f)throw new InvalidOperationException("Metal tasset hinge exceeds its 25mm crafting-pose travel");
            // Six millimetres of real plate overlap, rather than leaving a
            // tangential air gap and pretending that a rivet spans it.
            var movement=(to-from)/gap*(gap+.006f);var points=upper.vertices;
            for(int i=0;i<points.Length;i++)points[i]+=movement;upper.vertices=points;upper.RecalculateBounds();
        }
        static void JigNearestSurfaces(JigTree a,JigTree b,ref float best,ref Vector3 from,ref Vector3 to)
        {
            if(JigBoundsGapSquared(a.Bounds,b.Bounds)>=best)return;
            if(a.Triangles==null){JigNearestSurfaces(a.Left,b,ref best,ref from,ref to);JigNearestSurfaces(a.Right,b,ref best,ref from,ref to);return;}
            if(b.Triangles==null){JigNearestSurfaces(a,b.Left,ref best,ref from,ref to);JigNearestSurfaces(a,b.Right,ref best,ref from,ref to);return;}
            foreach(var first in a.Triangles)foreach(var second in b.Triangles)
            {
                if(JigBoundsGapSquared(first.Bounds,second.Bounds)>=best)continue;
                Vector3 point,support;JigClosestTriangles(first,second,out point,out support);float distance=(point-support).sqrMagnitude;
                if(distance<best){best=distance;from=point;to=support;}
            }
        }
        static void JigRotateAndSeat(Mesh[] meshes,Quaternion rotation)
        {
            var bounds=meshes[0].bounds;for(int i=1;i<meshes.Length;i++)bounds.Encapsulate(meshes[i].bounds);var pivot=bounds.center;
            bool first=true;var posed=new Bounds();
            foreach(var mesh in meshes)
            {
                var points=mesh.vertices;for(int i=0;i<points.Length;i++){points[i]=rotation*(points[i]-pivot);if(first){posed=new Bounds(points[i],Vector3.zero);first=false;}else posed.Encapsulate(points[i]);}mesh.vertices=points;
                var normals=mesh.normals;if(normals.Length==points.Length){for(int i=0;i<normals.Length;i++)normals[i]=rotation*normals[i];mesh.normals=normals;}
                var tangents=mesh.tangents;if(tangents.Length==points.Length){for(int i=0;i<tangents.Length;i++){var t=tangents[i];var xyz=rotation*new Vector3(t.x,t.y,t.z);tangents[i]=new Vector4(xyz.x,xyz.y,xyz.z,t.w);}mesh.tangents=tangents;}
            }
            float scale=Mathf.Min(1,Mathf.Min(.55f/Mathf.Max(.001f,Mathf.Max(posed.size.x,posed.size.z)),.45f/Mathf.Max(.001f,posed.size.y)));
            var offset=new Vector3(-posed.center.x,-posed.min.y,-posed.center.z);
            foreach(var mesh in meshes){var points=mesh.vertices;for(int i=0;i<points.Length;i++)points[i]=(points[i]+offset)*scale+Vector3.up*.03f;mesh.vertices=points;mesh.RecalculateBounds();}
        }
    }
}
