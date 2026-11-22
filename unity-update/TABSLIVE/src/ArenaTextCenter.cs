using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
[RequireComponent(typeof(Text))]
public sealed class ArenaTextCenter : BaseMeshEffect
{
    readonly List<UIVertex> vertices=new List<UIVertex>();
    public override void ModifyMesh(VertexHelper mesh){
        if(!IsActive())return;var text=graphic as Text;if(!text||text.alignment!=TextAnchor.MiddleCenter||text.GetComponentInParent<InputField>())return;
        mesh.GetUIVertexStream(vertices);if(vertices.Count==0)return;
        float minY=float.MaxValue,maxY=float.MinValue,minX=float.MaxValue,maxX=float.MinValue;
        foreach(var vertex in vertices){minY=Mathf.Min(minY,vertex.position.y);maxY=Mathf.Max(maxY,vertex.position.y);minX=Mathf.Min(minX,vertex.position.x);maxX=Mathf.Max(maxX,vertex.position.x);}
        Vector3 offset=new Vector3(text.rectTransform.rect.center.x-(minX+maxX)*.5f,text.rectTransform.rect.center.y-(minY+maxY)*.5f,0);
        for(int i=0;i<vertices.Count;i++){var vertex=vertices[i];vertex.position+=offset;vertices[i]=vertex;}mesh.Clear();mesh.AddUIVertexTriangleStream(vertices);vertices.Clear();
    }
}
