using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayerArmorMod
{
    internal static partial class ArmorCraftingMesh
    {
        // Preserve the actual three-dimensional armor shape. Pickup proxies are
        // baked in their native slot-anchor space, not ordinary Unity Y-up space.
        // Ingredient identities keep the old projected-X triangle ordering so a
        // saved project does not exchange one ingredient's geometry for another.
        internal static Mesh[] Partition(Mesh source,int count,ArmorSlot slot)
        {
            if(source==null||count<1||count>ArmorCraftingPlan.MaximumPieces)throw new ArgumentException("Invalid armor recipe mesh/count");
            Vector3[] ordering=Project(source),vertices=Assembly(source,slot);int[] triangles=source.triangles;
            if(triangles.Length/3<count)throw new InvalidOperationException("Armor art has fewer triangles than requested ingredients");
            var sorted=new List<int>();for(int i=0;i<triangles.Length;i+=3)sorted.Add(i);
            sorted.Sort((a,b)=>Center(ordering,triangles,a).x.CompareTo(Center(ordering,triangles,b).x));
            var meshes=new Mesh[count];
            for(int part=0;part<count;part++)
            {
                int start=part*sorted.Count/count,end=(part+1)*sorted.Count/count;
                var points=new List<Vector3>();var indices=new List<int>();
                for(int i=start;i<end;i++)for(int corner=0;corner<3;corner++)
                {indices.Add(points.Count);points.Add(vertices[triangles[sorted[i]+corner]]);}
                var mesh=new Mesh{name="Player Armor workbench piece "+(part+1)};
                mesh.SetVertices(points);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();meshes[part]=mesh;
            }
            return meshes;
        }
        static Vector3 Center(Vector3[] vertices,int[] triangles,int offset)
        {return (vertices[triangles[offset]]+vertices[triangles[offset+1]]+vertices[triangles[offset+2]])/3;}
        internal static Quaternion WorkbenchRotation(ArmorSlot slot)
        {
            // World rotations of the exact canonical native anchors used by
            // BuildOriginalArmor.BakePickupMesh, from Assets/CloneReview/Data/rig.json.
            // Only rotation is needed: center and table clearance are set below.
            switch(slot)
            {
                case ArmorSlot.Head:return new Quaternion(-.537651474f,.459272133f,.537651474f,-.459272133f);
                case ArmorSlot.Chest:return new Quaternion(-.5f,.5f,.5f,-.5f);
                case ArmorSlot.LeftShoulder:return new Quaternion(-.096687676f,.692694402f,-.714357044f,.022802926f);
                case ArmorSlot.RightShoulder:return new Quaternion(.022802926f,.714357044f,.692694402f,.096687676f);
                case ArmorSlot.LeftForearm:return new Quaternion(-.112814096f,.664783163f,-.738396240f,-.010359464f);
                case ArmorSlot.RightForearm:return new Quaternion(-.010359464f,.738396240f,.664783163f,.112814096f);
                case ArmorSlot.Hips:return new Quaternion(0,.689180018f,.724590162f,0);
                default:throw new ArgumentOutOfRangeException("slot");
            }
        }
        internal static Vector3[] Assembly(Mesh source,ArmorSlot slot)
        {
            var rotation=WorkbenchRotation(slot);var points=source.vertices;
            if(points.Length==0)throw new InvalidOperationException("Armor recipe source mesh is empty");
            var center=source.bounds.center;
            for(int i=0;i<points.Length;i++)points[i]=rotation*(points[i]-center);
            var bounds=new Bounds(points[0],Vector3.zero);
            for(int i=1;i<points.Length;i++)bounds.Encapsulate(points[i]);
            float largest=Math.Max(bounds.size.x,Math.Max(bounds.size.y,bounds.size.z));
            if(largest<.001f||float.IsNaN(largest)||float.IsInfinity(largest))throw new InvalidOperationException("Armor recipe source mesh has invalid bounds");
            float footprint=Math.Max(bounds.size.x,bounds.size.z);
            float scale=Math.Min(.55f/Math.Max(footprint,.001f),.45f/Math.Max(bounds.size.y,.001f));
            var offset=new Vector3(-bounds.center.x,-bounds.min.y,-bounds.center.z);
            for(int i=0;i<points.Length;i++)points[i]=(points[i]+offset)*scale+Vector3.up*.03f;
            return points;
        }
        internal static void NailPose(Mesh mesh,out Vector3 position,out Quaternion rotation)
        {
            // Use an exposed triangle near a collision-hull edge. A curved or
            // hollow piece can have no surface at its bounds center; a recessed
            // triangle's normal can also leave its nail inside the convex hull.
            var vertices=mesh.vertices;var triangles=mesh.triangles;
            var bounds=mesh.bounds;var half=Vector3.Max(bounds.extents,Vector3.one*.005f);
            Vector3[] directions={Vector3.up,Vector3.right,Vector3.left,Vector3.forward,Vector3.back,Vector3.down};
            float best=float.NegativeInfinity;Vector3 point=Vector3.zero,axis=Vector3.up;
            for(int i=0;i<triangles.Length;i+=3)
            {
                var a=vertices[triangles[i]];var b=vertices[triangles[i+1]];var c=vertices[triangles[i+2]];
                var cross=Vector3.Cross(b-a,c-a);float area=cross.magnitude;
                if(area<1e-9f)continue;
                var normal=cross/area;
                foreach(var direction in directions)
                {
                    float alignment=Vector3.Dot(normal,direction);if(alignment<.05f)continue;
                    Vector3 top=a,other=b,last=c;
                    if(Vector3.Dot(b,direction)>Vector3.Dot(top,direction)){top=b;other=a;last=c;}
                    if(Vector3.Dot(c,direction)>Vector3.Dot(top,direction)){top=c;other=a;last=b;}
                    float hullEdge=Vector3.Dot(bounds.center,direction)+Vector3.Dot(half,new Vector3(Math.Abs(direction.x),Math.Abs(direction.y),Math.Abs(direction.z)));
                    float edge=Vector3.Dot(top,direction);if(hullEdge-edge>.0051f)continue;
                    // Move slightly inside that real triangle (at most 1mm
                    // below its extreme) so ray/impact hits avoid exact edges.
                    float inset=Vector3.Dot(top*2-other-last,direction);
                    float mix=Math.Min(.02f,.001f/Math.Max(inset,.001f));
                    var candidate=top*(1-2*mix)+(other+last)*mix;
                    float height=(candidate.y-bounds.min.y)/Math.Max(bounds.size.y,.001f);
                    float score=direction.y*.35f+alignment*.6f+height*.05f;
                    if(score>best){best=score;point=candidate;axis=direction;}
                }
            }
            if(float.IsNegativeInfinity(best))throw new InvalidOperationException("Armor recipe piece has no surface for a nail");
            position=point+axis*.015f;
            // Native nail forward points into the workpiece (90 degrees X on a
            // horizontal top). Follow its chosen surface instead of forcing Y.
            rotation=Quaternion.FromToRotation(Vector3.forward,-axis);
        }
        internal static Mesh CollisionProxy(Mesh visual)
        {
            return BuildShapeHull(visual);
        }
        static Vector3[] Project(Mesh source)
        {
            // Legacy two-dimensional projection remains only for recipe glyphs
            // and stable partition ordering, never for the assembly's vertices.
            var bounds=source.bounds;float[] size={bounds.size.x,bounds.size.y,bounds.size.z};
            int depth=0;if(size[1]<size[depth])depth=1;if(size[2]<size[depth])depth=2;
            int horizontal=(depth+1)%3,vertical=(depth+2)%3;
            if(size[horizontal]<size[vertical]){int swap=horizontal;horizontal=vertical;vertical=swap;}
            float scale=.55f/Math.Max(size[horizontal],.01f);
            float depthScale=Math.Min(scale,.08f/Math.Max(size[depth],.001f)),height=.03f+size[depth]*depthScale*.5f;
            int[] order={horizontal,depth,vertical};int inversions=0;for(int a=0;a<3;a++)for(int b=a+1;b<3;b++)if(order[a]>order[b])inversions++;
            float handedness=inversions%2==0?1:-1;
            var points=source.vertices;
            for(int i=0;i<points.Length;i++)
            {
                var p=points[i]-bounds.center;
                points[i]=new Vector3(p[horizontal]*scale*handedness,p[depth]*depthScale+height,p[vertical]*scale);
            }
            return points;
        }
        internal static Sprite Icon(Mesh source)
        {
            const int size=128;var points=Project(source);var indices=source.triangles;
            var pixels=new Color32[size*size];
            Func<Vector3,Vector2> screen=p=>new Vector2(size*.5f+p.x/.62f*size,size*.5f+p.z/.62f*size);
            for(int t=0;t<indices.Length;t+=3)
            {
                Vector2 a=screen(points[indices[t]]),b=screen(points[indices[t+1]]),c=screen(points[indices[t+2]]);
                float area=Cross(b-a,c-a);if(Math.Abs(area)<.001f)continue;
                int minX=Mathf.Clamp(Mathf.FloorToInt(Math.Min(a.x,Math.Min(b.x,c.x))),0,size-1),maxX=Mathf.Clamp(Mathf.CeilToInt(Math.Max(a.x,Math.Max(b.x,c.x))),0,size-1);
                int minY=Mathf.Clamp(Mathf.FloorToInt(Math.Min(a.y,Math.Min(b.y,c.y))),0,size-1),maxY=Mathf.Clamp(Mathf.CeilToInt(Math.Max(a.y,Math.Max(b.y,c.y))),0,size-1);
                for(int y=minY;y<=maxY;y++)for(int x=minX;x<=maxX;x++)
                {
                    var p=new Vector2(x+.5f,y+.5f);float u=Cross(b-a,p-a)/area,v=Cross(c-b,p-b)/area,w=Cross(a-c,p-c)/area;
                    if(u>=0&&v>=0&&w>=0)pixels[y*size+x]=new Color32(30,22,15,255);
                }
            }
            var texture=new Texture2D(size,size,TextureFormat.RGBA32,false){name="Player Armor recipe glyph",hideFlags=HideFlags.DontSave};
            texture.SetPixels32(pixels);texture.Apply();
            var sprite=Sprite.Create(texture,new Rect(0,0,size,size),new Vector2(.5f,.5f),100);
            sprite.name="Player Armor glyph - "+source.name;return sprite;
        }
        static float Cross(Vector2 a,Vector2 b){return a.x*b.y-a.y*b.x;}
    }
}
