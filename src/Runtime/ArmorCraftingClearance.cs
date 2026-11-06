using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayerArmorMod
{
    internal static partial class ArmorCraftingMesh
    {
        sealed class Candidate{internal Vector3 Point,Normal;internal float Score;}
        static int CompareCandidates(Candidate a,Candidate b)
        {
            int order=Mathf.RoundToInt(b.Score*100000).CompareTo(Mathf.RoundToInt(a.Score*100000));if(order!=0)return order;
            for(int axis=0;axis<3;axis++){order=Mathf.RoundToInt(a.Point[axis]*1000000).CompareTo(Mathf.RoundToInt(b.Point[axis]*1000000));if(order!=0)return order;}
            for(int axis=0;axis<3;axis++){order=Mathf.RoundToInt(a.Normal[axis]*1000000).CompareTo(Mathf.RoundToInt(b.Normal[axis]*1000000));if(order!=0)return order;}return 0;
        }
        internal static int[] LastNailClearanceDegrees=new int[0];

        static Mesh BuildShapeHull(Mesh visual)
        {
            var source=visual.vertices;if(source.Length<3)throw new InvalidOperationException("Armor collision source is empty");
            var input=new List<Vector3>(source);var bounds=visual.bounds;
            // Only degenerate axes receive a 3mm thickness, rather than turning
            // every curved shell and open visor into a solid bounding box.
            for(int axis=0;axis<3;axis++)if(bounds.size[axis]<.003f){int count=input.Count;for(int i=0;i<count;i++){var a=input[i];var b=a;a[axis]=bounds.center[axis]-.0015f;b[axis]=bounds.center[axis]+.0015f;input[i]=a;input.Add(b);}}
            // A tilted planar panel can have nonzero bounds on every world
            // axis. Thicken along its actual missing dimensions as well.
            var start=input[0];var end=start;float span=0;foreach(var point in input)if((point-start).sqrMagnitude>span){span=(point-start).sqrMagnitude;end=point;}
            var lineAxis=end-start;if(lineAxis.sqrMagnitude<1e-12f)lineAxis=Vector3.right;var normalAxis=Vector3.zero;float area=0;
            foreach(var point in input){var cross=Vector3.Cross(lineAxis,point-start);if(cross.sqrMagnitude>area){area=cross.sqrMagnitude;normalAxis=cross.normalized;}}
            if(area<1e-12f){normalAxis=Vector3.Cross(lineAxis.normalized,Mathf.Abs(lineAxis.normalized.y)<.8f?Vector3.up:Vector3.right).normalized;var other=Vector3.Cross(lineAxis.normalized,normalAxis).normalized;int amount=input.Count;for(int i=0;i<amount;i++){var point=input[i];input[i]=point+other*.0015f;input.Add(point-other*.0015f);}}
            float depth=0;foreach(var point in input)depth=Mathf.Max(depth,Mathf.Abs(Vector3.Dot(point-start,normalAxis)));
            if(depth<.0015f){int amount=input.Count;for(int i=0;i<amount;i++){var point=input[i];input[i]=point+normalAxis*.0015f;input.Add(point-normalAxis*.0015f);}}
            // Intersect actual support half-spaces, rather than inflating an
            // inscribed sample hull. The resulting OUTER hull contains every
            // source point while remaining far tighter than its bounds box.
            var directions=new List<Vector3>();for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++)for(int z=-1;z<=1;z++)if(x!=0||y!=0||z!=0)directions.Add(new Vector3(x,y,z).normalized);
            for(int i=0;i<22;i++){float y=1-2*(i+.5f)/22,r=Mathf.Sqrt(1-y*y),angle=i*2.39996323f;directions.Add(new Vector3(Mathf.Cos(angle)*r,y,Mathf.Sin(angle)*r));}
            var box=new Bounds(input[0],Vector3.zero);foreach(var point in input)box.Encapsulate(point);box.Expand(.000004f);var corners=new Vector3[8];for(int i=0;i<8;i++)corners[i]=box.center+Vector3.Scale(box.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
            var polygons=new List<List<Vector3>>();foreach(var face in new[]{new[]{0,4,6,2},new[]{1,3,7,5},new[]{0,1,5,4},new[]{2,6,7,3},new[]{0,2,3,1},new[]{4,5,7,6}}){var polygon=new List<Vector3>();foreach(int index in face)polygon.Add(corners[index]);polygons.Add(polygon);}
            foreach(var normal in directions)
            {
                float offset=float.NegativeInfinity;foreach(var point in input)offset=Mathf.Max(offset,Vector3.Dot(normal,point));offset+=.000001f;
                var next=new List<List<Vector3>>();var cap=new List<Vector3>();var capKeys=new HashSet<Vector3Int>();
                foreach(var polygon in polygons)
                {
                    var clipped=new List<Vector3>();for(int i=0;i<polygon.Count;i++)
                    {
                        var from=polygon[i];var to=polygon[(i+1)%polygon.Count];float da=Vector3.Dot(normal,from)-offset,db=Vector3.Dot(normal,to)-offset;bool keepA=da<=.0000001f,keepB=db<=.0000001f;
                        if(keepA)AddPolygonPoint(clipped,from);
                        if(keepA!=keepB){var cross=Vector3.LerpUnclamped(from,to,da/(da-db));AddPolygonPoint(clipped,cross);if(capKeys.Add(HullKey(cross)))cap.Add(cross);}
                    }
                    if(clipped.Count>1&&(clipped[0]-clipped[clipped.Count-1]).sqrMagnitude<1e-12f)clipped.RemoveAt(clipped.Count-1);if(clipped.Count>=3)next.Add(clipped);
                }
                if(cap.Count>=3){var center=Vector3.zero;foreach(var point in cap)center+=point;center/=cap.Count;var u=Vector3.Cross(Mathf.Abs(normal.y)<.9f?Vector3.up:Vector3.right,normal).normalized;var v=Vector3.Cross(normal,u);cap.Sort((a,b)=>Mathf.Atan2(Vector3.Dot(a-center,v),Vector3.Dot(a-center,u)).CompareTo(Mathf.Atan2(Vector3.Dot(b-center,v),Vector3.Dot(b-center,u))));next.Add(cap);}polygons=next;
            }
            var points=new List<Vector3>();var indices=new List<int>();var map=new Dictionary<Vector3Int,int>();
            foreach(var polygon in polygons)
            {
                var face=new List<int>();foreach(var point in polygon){var key=HullKey(point);int index;if(!map.TryGetValue(key,out index)){index=points.Count;points.Add(point);map.Add(key,index);}if(face.Count==0||face[face.Count-1]!=index)face.Add(index);}if(face.Count>1&&face[0]==face[face.Count-1])face.RemoveAt(face.Count-1);
                for(int i=1;i+1<face.Count;i++)if(Vector3.Cross(points[face[i]]-points[face[0]],points[face[i+1]]-points[face[0]]).sqrMagnitude>1e-18f){indices.Add(face[0]);indices.Add(face[i]);indices.Add(face[i+1]);}
            }
            if(points.Count<4||points.Count>128||indices.Count/3>255)throw new InvalidOperationException("Armor fitted hull exceeds native convex limits");
            var mesh=new Mesh{name="Player Armor fitted convex collision hull",vertices=points.ToArray(),triangles=indices.ToArray()};mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        static Vector3Int HullKey(Vector3 point){return new Vector3Int(Mathf.RoundToInt(point.x*1000000),Mathf.RoundToInt(point.y*1000000),Mathf.RoundToInt(point.z*1000000));}
        static void AddPolygonPoint(List<Vector3> list,Vector3 point){if(list.Count==0||(list[list.Count-1]-point).sqrMagnitude>=1e-12f)list.Add(point);}
        static Vector4[] HullPlanes(Mesh mesh)
        {
            var points=mesh.vertices;var indices=mesh.triangles;var center=Vector3.zero;foreach(var point in points)center+=point;center/=points.Length;var result=new List<Vector4>();
            for(int i=0;i<indices.Length;i+=3)
            {
                var a=points[indices[i]];var n=Vector3.Cross(points[indices[i+1]]-a,points[indices[i+2]]-a).normalized;
                if(n.sqrMagnitude<.5f)continue;if(Vector3.Dot(n,center-a)>0)n=-n;
                // Support clipping welds vertices at micrometre precision. A
                // thin triangulated face can consequently be slightly tilted
                // relative to the rest of its polygon. Its first vertex is
                // not necessarily a support point in that recovered direction:
                // using it would create a false plane cutting the hull itself.
                // Reconstruct an actual support half-space over ALL hull points.
                // This changes neither mesh geometry nor collider padding.
                float d=float.NegativeInfinity;foreach(var point in points)d=Mathf.Max(d,Vector3.Dot(n,point));
                bool duplicate=false;foreach(var p in result)if(Vector3.Dot(n,new Vector3(p.x,p.y,p.z))>.99999f&&Mathf.Abs(d-p.w)<.00001f){duplicate=true;break;}
                if(!duplicate)result.Add(new Vector4(n.x,n.y,n.z,d));
            }
            return result.ToArray();
        }
        static float Outside(Vector4[] planes,Vector3 point)
        {float result=float.NegativeInfinity;foreach(var plane in planes)result=Mathf.Max(result,plane.x*point.x+plane.y*point.y+plane.z*point.z-plane.w);return result;}
        static bool Clear(Vector4[][] hulls,Vector3[] corners)
        {
            foreach(var point in corners)if(point.y<.005f&&Mathf.Abs(point.x)<=.32f&&Mathf.Abs(point.z)<=.32f)return false;
            foreach(var hull in hulls){bool separated=false;foreach(var plane in hull){bool all=true;foreach(var point in corners)if(plane.x*point.x+plane.y*point.y+plane.z*point.z-plane.w<.0003f){all=false;break;}if(all){separated=true;break;}}if(!separated)return false;}return true;
        }
        // Exact native moving/scaled nail branch. The hammer's leading face
        // touches the outward nail face; its full volume extends outward.
        // Default bounds conservatively contain normal, rusty and small heads.
        internal static Vector3[] HammerEnvelope(Vector3 basis,Quaternion root,float progress,float angle,float azimuth,bool smallHammer=false)
        {
            var axis=new Vector3(Mathf.Cos(azimuth),Mathf.Sin(azimuth),0);var deviance=Quaternion.AngleAxis(angle,axis);var rotation=root*deviance;
            var pivot=basis+root*Vector3.forward*.0079f;
            float head=-(.04f*(1-progress)+.02f)+.035f*.36586f-.05f*.36586f;
            var contact=pivot+rotation*Vector3.forward*head;var corners=new Vector3[8];
            float width=smallHammer?.0388448f:.074623f,height=smallHammer?.0367242f:.077103f,depth=smallHammer?.1679911f:.284549f;
            for(int i=0;i<8;i++)corners[i]=contact+rotation*new Vector3((i&1)==0?-width:width,(i&2)==0?-height:height,(i&4)==0?0:-depth);
            return corners;
        }
        static bool Fits(Vector4[][] hulls,Vector3 basis,Quaternion rotation,int angle)
        {
            foreach(float progress in new[]{0f,.5f,.98f})
            {if(!Clear(hulls,HammerEnvelope(basis,rotation,progress,0,0)))return false;if(angle>0)for(int i=0;i<8;i++)if(!Clear(hulls,HammerEnvelope(basis,rotation,progress,angle,i*Mathf.PI*.25f)))return false;}return true;
        }
        internal static float ContainmentError(Mesh source,Mesh hull)
        {var planes=HullPlanes(hull);float worst=0;foreach(var vertex in source.vertices)worst=Mathf.Max(worst,Outside(planes,vertex));return worst;}
        internal static bool NailHasClearance(Mesh[] hulls,Vector3 basis,Quaternion rotation,int angle)
        {var planes=new Vector4[hulls.Length][];for(int i=0;i<hulls.Length;i++)planes[i]=HullPlanes(hulls[i]);return Fits(planes,basis,rotation,angle);}
        internal static void NailPoses(Mesh[] visuals,Mesh[] collisionMeshes,out Vector3[] positions,out Quaternion[] rotations,bool[] requiresNails=null)
        {
            if(visuals==null||collisionMeshes==null||visuals.Length!=collisionMeshes.Length)throw new ArgumentException("Armor assembly collision data differs");
            positions=new Vector3[visuals.Length];rotations=new Quaternion[visuals.Length];LastNailClearanceDegrees=new int[visuals.Length];var hulls=new Vector4[visuals.Length][];for(int i=0;i<hulls.Length;i++)hulls[i]=HullPlanes(collisionMeshes[i]);
            for(int part=0;part<visuals.Length;part++)
            {
                rotations[part]=Quaternion.identity;if(requiresNails!=null&&!requiresNails[part]){LastNailClearanceDegrees[part]=-2;continue;}
                var mesh=visuals[part];var vertices=mesh.vertices;var triangles=mesh.triangles;var candidates=new List<Candidate>();var seen=new HashSet<long>();
                for(int i=0;i<triangles.Length;i+=3)
                {
                    var a=vertices[triangles[i]];var b=vertices[triangles[i+1]];var c=vertices[triangles[i+2]];var normal=Vector3.Cross(b-a,c-a).normalized;if(normal.sqrMagnitude<.5f||normal.y<-.3f)continue;
                    var points=new[]{(a+b+c)/3,a*.96f+(b+c)*.02f,b*.96f+(a+c)*.02f,c*.96f+(a+b)*.02f};
                    foreach(var point in points)
                    {
                        var basis=point+normal*.025f;long key=17;unchecked{foreach(float value in new[]{basis.x*500,basis.y*500,basis.z*500,normal.x*20,normal.y*20,normal.z*20})key=key*397+Mathf.RoundToInt(value);}if(!seen.Add(key))continue;
                        var head=basis+normal*.0184f;float clearance=float.PositiveInfinity;foreach(var hull in hulls)clearance=Mathf.Min(clearance,Outside(hull,head));
                        candidates.Add(new Candidate{Point=basis,Normal=normal,Score=Mathf.Clamp(clearance,-.25f,.08f)*8+normal.y*.08f+(point.y-mesh.bounds.min.y)*.03f});
                    }
                }
                candidates.Sort(CompareCandidates);Candidate best=null;int degree=-1;
                float selectedExtra=0;
                foreach(float extra in new[]{0f,.005f,.01f})
                {
                    foreach(int requested in new[]{45,20,0})
                    {for(int i=0;i<Math.Min(candidates.Count,1024);i++){var candidate=candidates[i];var basis=candidate.Point+candidate.Normal*extra;bool overlaps=false;for(int previous=0;previous<part;previous++)if(LastNailClearanceDegrees[previous]!=-2&&(basis-positions[previous]).sqrMagnitude<.000625f){overlaps=true;break;}if(overlaps)continue;var rotation=Quaternion.FromToRotation(Vector3.forward,-candidate.Normal);if(Fits(hulls,basis,rotation,requested)){best=candidate;degree=requested;selectedExtra=extra;break;}}if(best!=null)break;}
                    if(best!=null)break;
                }
                if(best==null&&candidates.Count>0)best=candidates[0];
                if(best==null){NailPose(mesh,out positions[part],out rotations[part]);LastNailClearanceDegrees[part]=-1;continue;}
                positions[part]=best.Point+best.Normal*selectedExtra;rotations[part]=Quaternion.FromToRotation(Vector3.forward,-best.Normal);LastNailClearanceDegrees[part]=degree;
            }
        }
    }
}
