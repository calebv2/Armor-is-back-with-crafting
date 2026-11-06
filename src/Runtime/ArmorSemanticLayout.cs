using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayerArmorMod
{
    // UV5 is offline authored-part metadata, independent of pose and material.
    // UV3 remains intact for legacy recipes/items that used projected bands.
    internal static class ArmorSemanticLayout
    {
        internal const uint Version=2;
        internal const int Primary=1,Fastener=2;
        internal const float Magic=47f;
        internal static bool HasMetadata(Mesh mesh)
        {
            if(mesh==null)return false;var data=new List<Vector4>();mesh.GetUVs(4,data);
            if(data.Count!=mesh.vertexCount)return false;
            foreach(var value in data)if(!Valid(value))return false;return data.Count>0;
        }
        static bool Valid(Vector4 value)
        {return value.w==Magic&&value.z==Version&&(value.x==Primary||value.x==Fastener)&&value.y>=0&&value.y<32&&value.y==(int)value.y;}
        internal static Vector4[] Read(Mesh mesh)
        {
            var data=new List<Vector4>();mesh.GetUVs(4,data);
            if(data.Count!=mesh.vertexCount)throw new InvalidOperationException("Armor mesh has no authored construction layout");
            foreach(var value in data)if(!Valid(value))throw new InvalidOperationException("Armor construction metadata is invalid");return data.ToArray();
        }
        internal static int Count(Mesh mesh,int role)
        {int count=0;foreach(var value in Read(mesh))if(value.x==role)count=Math.Max(count,(int)value.y+1);return count;}
        internal static bool HasGroup(Mesh mesh,int role,int group)
        {foreach(var value in Read(mesh))if(value.x==role&&value.y==group)return true;return false;}
        internal static Mesh Extract(Mesh source,int role,int group,Vector3[] transformedVertices)
        {
            if(transformedVertices==null||transformedVertices.Length!=source.vertexCount)throw new ArgumentException("Semantic armor vertices do not match the source");
            var data=Read(source);var triangles=source.triangles;var points=new List<Vector3>();var indices=new List<int>();
            for(int i=0;i<triangles.Length;i+=3)
            {
                var label=data[triangles[i]];
                if(label!=data[triangles[i+1]]||label!=data[triangles[i+2]])throw new InvalidOperationException("An armor triangle crosses authored construction labels");
                if(label.x!=role||label.y!=group)continue;
                for(int n=0;n<3;n++){indices.Add(points.Count);points.Add(transformedVertices[triangles[i+n]]);}
            }
            if(points.Count==0)return null;
            var mesh=new Mesh{name="Authored armor "+(role==Primary?"panel ":"fastener ")+(group+1)};mesh.SetVertices(points);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
    }
}
