using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace SprocketDrones;

// Original mesh geometry shared by flying aircraft, fitted models and rendered icons.
internal static class DroneModels
{
    private static readonly Material?[] surfaces=new Material[5];
    // ponytail: finite authored mesh keys live until unload; eviction is only needed for generated shapes.
    private static readonly Dictionary<string,Mesh> meshes=new();
    private static Il2CppReferenceArray<Material>? sharedMaterials;
    internal static Material Surface(int index)
    {
        if(surfaces[index] is {} existing && existing!=null) return existing;
        var shader=Shader.Find("HDRP/Lit") ?? throw new InvalidOperationException("HDRP aircraft shader unavailable");
        var colors=new[]{new Color(.28f,.30f,.24f),new Color(.62f,.63f,.59f),new Color(.055f,.06f,.062f),new Color(.075f,.043f,.030f),new Color(.32f,.34f,.33f)};
        var m=new Material(shader){name="Drone surface "+index,hideFlags=HideFlags.HideAndDontSave};
        m.SetColor("_BaseColor",colors[index]); m.SetFloat("_Metallic",index==4?.7f:index==0?.25f:.03f); m.SetFloat("_Smoothness",index==3?.9f:index==4?.55f:.24f);
        HDMaterial.ValidateMaterial(m); surfaces[index]=m; return m;
    }
    internal static Mesh BuildMesh(Func<PartGeometry> createGeometry,string name)
    {
        if(meshes.TryGetValue(name,out var existing) && existing!=null) return existing;
        var geometry=createGeometry();
        var mesh=new Mesh{name=name,hideFlags=HideFlags.HideAndDontSave};
        try
        {
            mesh.vertices=new Il2CppStructArray<Vector3>(geometry.Vertices.Select(DroneRuntime.ToU).ToArray());
            mesh.uv=new Il2CppStructArray<Vector2>(geometry.Vertices.Select(p=>new Vector2(p.X+.5f,p.Z+.5f)).ToArray());
            mesh.subMeshCount=5;
            var groups=geometry.MaterialTriangles();
            for(int surface=0;surface<5;surface++)
            {
                mesh.SetTriangles(new Il2CppStructArray<int>(groups[surface]),surface);
            }
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); meshes[name]=mesh;return mesh;
        }
        catch { UnityEngine.Object.Destroy(mesh); throw; }
    }
    internal static GameObject Model(Transform parent,string name,Func<PartGeometry> geometry,Vector3 position,string? meshKey=null)
    {
        var go=new GameObject(name); go.transform.SetParent(parent,false); go.transform.localPosition=position;
        var mesh=BuildMesh(geometry,meshKey??name);
        go.AddComponent<MeshFilter>().sharedMesh=mesh;
        sharedMaterials??=new Il2CppReferenceArray<Material>(Enumerable.Range(0,5).Select(Surface).ToArray());
        go.AddComponent<MeshRenderer>().sharedMaterials=sharedMaterials;
        return go;
    }
    internal static (GameObject Model,Transform Propeller,Transform[] Wings) Create(DroneKind kind)
    {
        var root=new GameObject(kind==DroneKind.Scout?"Scout - ZALA inspired":"Kamikaze - Switchblade inspired");
        try
        {
            var wings=new List<Transform>();
            Model(root.transform,kind+" airframe",()=>PartGeometry.Aircraft(kind,false,false),Vector3.zero);
            if(kind==DroneKind.Kamikaze)
            {
                foreach(int side in new[]{-1,1})
                {
                    wings.Add(Model(root.transform,"Front wing "+side,()=>PartGeometry.FlightWing(true,side),new(0,-.035f,.145f)).transform);
                    wings.Add(Model(root.transform,"Rear wing "+side,()=>PartGeometry.FlightWing(false,side),new(0,.012f,-.17f)).transform);
                    wings.Add(Model(root.transform,"Tail fin "+side,PartGeometry.FlightFin,PartToU(PartGeometry.FinPosition(side,1)),"Flight fin").transform);
                }
            }
            else foreach(int side in new[]{-1,1}) wings.Add(Model(root.transform,"Scout wing "+side,()=>PartGeometry.ScoutWing(side),new(0,.025f,-.015f)).transform);
            var prop=Model(root.transform,kind+" pusher propeller",()=>PartGeometry.Propeller(kind),DroneRuntime.ToU(PartGeometry.PropellerPosition(kind)));
            DeployWings(kind,wings.ToArray(),-1);
            return(root,prop.transform,wings.ToArray());
        }
        catch { Release(root); throw; }
    }
    private static Vector3 PartToU(System.Numerics.Vector3 p) => DroneRuntime.ToU(p);
    private static Quaternion PartToU(System.Numerics.Quaternion q) => new(q.X,q.Y,q.Z,q.W);
    internal static void DeployWings(DroneKind kind,Transform[] wings,float launchAge)
    {
        for(int i=0;i<wings.Length;i++)
        {
            int side=i<(kind==DroneKind.Scout?1:3)?-1:1;
            float fraction=LauncherRules.WingDeployment(kind,launchAge,kind==DroneKind.Scout?i:i%3);
            if(kind==DroneKind.Kamikaze && i%3==2) { wings[i].localRotation=PartToU(PartGeometry.FinRotation(fraction)); wings[i].localPosition=PartToU(PartGeometry.FinPosition(side,fraction)); }
            else wings[i].localRotation=PartToU(PartGeometry.WingRotation(kind,side,fraction));
        }
    }
    internal static void Release(GameObject model)
    {
        if(model!=null) {model.SetActive(false);UnityEngine.Object.Destroy(model);}
    }
    internal static void Clear() { foreach(var mesh in meshes.Values) if(mesh!=null) UnityEngine.Object.Destroy(mesh); meshes.Clear();sharedMaterials=null; }
    // Fitted parts share these materials across battle/scene changes; release only on plugin unload.
    internal static void ReleaseMaterials() { for(int i=0;i<surfaces.Length;i++) { if(surfaces[i]!=null) UnityEngine.Object.Destroy(surfaces[i]); surfaces[i]=null; } }
}
