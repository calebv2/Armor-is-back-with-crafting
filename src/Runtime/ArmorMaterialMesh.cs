using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PlayerArmorMod
{
    // UV3 contains a canonical, rest-pose section coordinate written by the
    // offline bake. Animation and a pickup's local rotation never change it.
    internal static class ArmorMaterialMesh
    {
        internal const float CoordinateVersion=46f;
        internal static int PrimaryCount(int slot)
        {if(slot==0)return 3;if(slot==6)return 4;if(slot>=1&&slot<=5)return 2;throw new ArgumentOutOfRangeException("slot");}
        internal static Color? SampleColdColor(Material material)
        {
            if(material==null)return null;
            // ATT MetalNonForging stores its cold albedo in A/B. The packed
            // texture green channel blends them; RGB is not a diffuse texture.
            // Its serialized _Color value is orphaned and not a shader property.
            bool packedMetal=material.HasProperty("_ColorA")&&material.HasProperty("_ColorB");
            Color first=packedMetal?ShaderColor(material,"_ColorA"):(material.HasProperty("_Color")?ShaderColor(material,"_Color"):Color.white);
            Color second=packedMetal?ShaderColor(material,"_ColorB"):first;
            if(!Finite(first.r)||!Finite(first.g)||!Finite(first.b)||!Finite(second.r)||!Finite(second.g)||!Finite(second.b))return null;
            var texture=material.mainTexture;
            if(texture==null)return ColdAlbedo(first,second,Color.white,packedMetal);
            RenderTexture previous=RenderTexture.active;var target=RenderTexture.GetTemporary(8,8,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);Texture2D read=null;
            try
            {
                // GPU texture sampling honors the source texture's sRGB flag,
                // matching its green-channel value in the native shader. The
                // readback itself stays linear; do not decode it a second time.
                Graphics.Blit(texture,target);RenderTexture.active=target;read=new Texture2D(8,8,TextureFormat.RGBA32,false,true);read.ReadPixels(new Rect(0,0,8,8),0,0);read.Apply();
                var pixels=read.GetPixels();var average=Color.clear;
                foreach(var pixel in pixels)average+=ColdAlbedo(first,second,pixel,packedMetal);
                average/=pixels.Length;return new Color(average.r,average.g,average.b,1);
            }
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);if(read!=null){if(Application.isPlaying)UnityEngine.Object.Destroy(read);else UnityEngine.Object.DestroyImmediate(read);}}
        }
        internal static Color ShaderColor(Material material,string property)
        {
            var color=material.GetColor(property);var shader=material.shader;int index=shader==null?-1:shader.FindPropertyIndex(property);
            // Unity uploads Color properties in linear space for a linear
            // renderer, whereas GetColor returns their serialized sRGB values.
            if(QualitySettings.activeColorSpace==ColorSpace.Linear&&index>=0&&shader.GetPropertyType(index)==ShaderPropertyType.Color)color=color.linear;
            return color;
        }
        static Color ColdAlbedo(Color first,Color second,Color sample,bool packedMetal)
        {
            var value=packedMetal?Color.Lerp(first,second,Mathf.Clamp01(sample.g)):first*sample;
            return new Color(Mathf.Clamp01(value.r),Mathf.Clamp01(value.g),Mathf.Clamp01(value.b),1);
        }
        static bool Finite(float value){return !float.IsNaN(value)&&!float.IsInfinity(value);}
        internal static float Luminance(Color value){return value.r*.2126f+value.g*.7152f+value.b*.0722f;}
        internal static bool HasCoordinates(Mesh mesh)
        {
            if(mesh==null)return false;
            var uv=new List<Vector2>();mesh.GetUVs(2,uv);
            if(uv.Count!=mesh.vertexCount)return false;
            for(int i=0;i<uv.Count;i++)if(Math.Abs(uv[i].y-CoordinateVersion)>.001f||float.IsNaN(uv[i].x)||uv[i].x<-.001f||uv[i].x>1.001f)return false;
            return true;
        }
        internal static Mesh Build(Mesh source,Color[] palette,uint layoutVersion=1,int primaryCount=0,bool tintFasteners=true,
            ArmorStyle style=ArmorStyle.Metal)
        {
            if(source==null||palette==null||palette.Length<1||palette.Length>32||(layoutVersion==2?!ArmorSemanticLayout.HasMetadata(source):!HasCoordinates(source)))throw new ArgumentException("Armor mesh has no canonical material coordinates");
            var points=source.vertices;var normals=source.normals;var tangents=source.tangents;var weights=source.boneWeights;
            var coordinates=new List<Vector2>();source.GetUVs(2,coordinates);
            // UV6 marks authored soft hide that keeps its own texture/tint.
            // This is independent of UV5 construction/material-region identity.
            var neutralHide=new List<Vector2>();source.GetUVs(5,neutralHide);
            if(neutralHide.Count!=0&&neutralHide.Count!=points.Length)throw new ArgumentException("Armor soft-hide metadata has an incomplete vertex count");
            var authored=layoutVersion==2?ArmorSemanticLayout.Read(source):null;
            var originals=new List<int>();var colors=new List<Color>();var groups=new int[source.subMeshCount][];
            for(int sub=0;sub<source.subMeshCount;sub++)
            {
                if(source.GetTopology(sub)!=MeshTopology.Triangles)throw new ArgumentException("Armor material sections require triangle geometry");
                var indices=source.GetTriangles(sub);var next=new int[indices.Length];
                for(int i=0;i<indices.Length;i+=3)
                {
                    float value=authored==null?(coordinates[indices[i]].x+coordinates[indices[i+1]].x+coordinates[indices[i+2]].x)/3:0;
                    int section=Mathf.Clamp(Mathf.FloorToInt(value*palette.Length),0,palette.Length-1);
                    Color color=palette[section];
                    if(authored!=null)
                    {
                        var part=authored[indices[i]];
                        if(part!=authored[indices[i+1]]||part!=authored[indices[i+2]])throw new ArgumentException("Authored armor triangle crosses construction parts");
                        if(part.x==ArmorSemanticLayout.Primary)
                        {
                            section=(int)part.y;
                            if(section>=palette.Length)throw new ArgumentException("Material palette has fewer entries than authored parts");
                            color=palette[section];
                        }
                        else
                        {
                            // New semantic items append closure materials after
                            // their primary plates. Old profiles have only the
                            // plates; reuse an existing metal instead of guessing
                            // an ingredient that was never saved on that item.
                            section=primaryCount+(int)part.y;
                            color=Color.white;
                            if(tintFasteners&&primaryCount>0&&(style==ArmorStyle.Metal||style==ArmorStyle.Forgeguard))
                                color=section<palette.Length?palette[section]:palette[0];
                        }
                    }
                    if(neutralHide.Count!=0)
                    {
                        bool first=NeutralHide(neutralHide[indices[i]]),second=NeutralHide(neutralHide[indices[i+1]]),third=NeutralHide(neutralHide[indices[i+2]]);
                        if(first!=second||second!=third)throw new ArgumentException("Armor triangle crosses soft-hide tint boundaries");
                        if(first)color=Color.white;
                    }
                    for(int n=0;n<3;n++){next[i+n]=originals.Count;originals.Add(indices[i+n]);colors.Add(color);}
                }
                groups[sub]=next;
            }
            var result=new Mesh{name=source.name+" (material sections)",hideFlags=HideFlags.DontSave};
            if(originals.Count>65535)result.indexFormat=IndexFormat.UInt32;
            var vertices=new Vector3[originals.Count];var mappedNormals=normals.Length==points.Length?new Vector3[originals.Count]:null;
            var mappedTangents=tangents.Length==points.Length?new Vector4[originals.Count]:null;var mappedWeights=weights.Length==points.Length?new BoneWeight[originals.Count]:null;
            for(int i=0;i<originals.Count;i++)
            {int index=originals[i];vertices[i]=points[index];if(mappedNormals!=null)mappedNormals[i]=normals[index];if(mappedTangents!=null)mappedTangents[i]=tangents[index];if(mappedWeights!=null)mappedWeights[i]=weights[index];}
            result.vertices=vertices;if(mappedNormals!=null)result.normals=mappedNormals;if(mappedTangents!=null)result.tangents=mappedTangents;
            if(mappedWeights!=null)result.boneWeights=mappedWeights;result.bindposes=source.bindposes;
            for(int channel=0;channel<8;channel++)
            {
                var input=new List<Vector4>();source.GetUVs(channel,input);if(input.Count!=points.Length)continue;
                var output=new List<Vector4>(originals.Count);foreach(int index in originals)output.Add(input[index]);result.SetUVs(channel,output);
            }
            result.colors=colors.ToArray();result.subMeshCount=groups.Length;
            for(int sub=0;sub<groups.Length;sub++)result.SetTriangles(groups[sub],sub,false);
            for(int shape=0;shape<source.blendShapeCount;shape++)for(int frame=0;frame<source.GetBlendShapeFrameCount(shape);frame++)
            {
                var dv=new Vector3[points.Length];var dn=new Vector3[points.Length];var dt=new Vector3[points.Length];source.GetBlendShapeFrameVertices(shape,frame,dv,dn,dt);
                var ov=new Vector3[originals.Count];var on=new Vector3[originals.Count];var ot=new Vector3[originals.Count];
                for(int i=0;i<originals.Count;i++){ov[i]=dv[originals[i]];on[i]=dn[originals[i]];ot[i]=dt[originals[i]];}
                result.AddBlendShapeFrame(source.GetBlendShapeName(shape),source.GetBlendShapeFrameWeight(shape,frame),ov,on,ot);
            }
            result.bounds=source.bounds;return result;
        }
        // Buckle, ring and rivet islands in the original Leather atlas.
        internal static bool Hardware(Vector2 uv)
        {
            return InAtlas(uv,.208f,.648f,.298f,.738f) || InAtlas(uv,.400f,.573f,.464f,.612f)
                || InAtlas(uv,.452f,.368f,.625f,.539f);
        }
        static bool InAtlas(Vector2 uv,float u0,float v0,float u1,float v1)
        {return uv.x>=u0&&uv.x<=u1&&uv.y>=v0&&uv.y<=v1;}
        internal static bool NeutralHide(Vector2 value){return value.x==1&&value.y==373;}
    }
}
