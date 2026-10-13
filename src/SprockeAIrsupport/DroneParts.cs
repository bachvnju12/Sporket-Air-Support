using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AssetManagement;
using UnityEngine;
using Controller=Sprocket.Gameplay.VehicleControl.VehicleController;

namespace SprocketDrones;

[HarmonyPatch]
internal static class DroneParts
{
    private sealed record PartVisual(VehicleObjectModel Source,IntPtr ModelId,Mesh Mesh,Mesh Collision,GameObject Root,Transform Tray,Transform Pivot,Transform Lid,DroneKind Kind)
    {
        internal float Started=-1;
        internal float LastPose=-2;
        internal Transform[] Links=Array.Empty<Transform>(),Barrels=Array.Empty<Transform>(),Rods=Array.Empty<Transform>(),Latches=Array.Empty<Transform>(),Doors=Array.Empty<Transform>();
        internal Renderer[] Renderers=Array.Empty<Renderer>();
        internal Transform? Sled,DriveBarrel,DriveRod,DriveJoint;
    }
    private static readonly Dictionary<IntPtr,PartVisual> meshes=new();
    private static readonly VehicleMaterial?[] surfaces=new VehicleMaterial[5];
    internal static bool Usable(VehicleObjectModel? part,DroneKind kind)
    {
        if(part==null || part.VehicleObject==null || part.VehicleObject.Released || !part.IsInstalled) return false;
        return DronePartCatalog.Usable(part.ComponentID,kind,true,false,part.HealthFraction);
    }
    internal static VehicleObjectModel? Find(Controller controller,DroneKind kind)
    {
        var reader=controller.ControlledVehicle?.VehicleStructure?.ObjectReader;
        if(reader==null) return null;
        var models=new Il2CppSystem.Collections.Generic.List<VehicleObjectModel>();
        reader.GetAll(models,false);
        foreach(var part in models) if(Usable(part,kind)) return part;
        return null;
    }
    [HarmonyPrefix,HarmonyPatch(typeof(VehicleObjectModel),nameof(VehicleObjectModel.Build))]
    private static void Building(VehicleObjectModel __instance)
        => PrepareModel(__instance);
    [HarmonyPrefix,HarmonyPatch(typeof(VehicleObjectModel),nameof(VehicleObjectModel.Initiate))]
    private static void Initiating(VehicleObjectModel __instance) => PrepareModel(__instance);
    [HarmonyPostfix,HarmonyPatch(typeof(VehicleObjectModel),nameof(VehicleObjectModel.Initiate))]
    private static void Initiated(VehicleObjectModel __instance)
    {
        if(DronePartCatalog.Kind(__instance.ComponentID)==null || __instance.Model==null) return;
        if(!meshes.TryGetValue(__instance.Pointer,out var visual) || visual.Root==null || visual.ModelId!=__instance.Model.Pointer) Loaded(__instance);
    }
    [HarmonyPrefix,HarmonyPatch(typeof(VehicleObjectModel),nameof(VehicleObjectModel.RequestModelLoad))]
    private static void Requesting(VehicleObjectModel __instance) => PrepareModel(__instance);
    // Harmony treats a method named Prepare as its own initialization callback.
    private static void PrepareModel(VehicleObjectModel model)
    {
        if(DronePartCatalog.Kind(model.ComponentID)==null) return;
        model.staticRenderer=false;
        model.rendererFlags &= ~VehicleRendererFlags.Static;
        model.modelLoadRequest?.ClearBake();
    }
    [HarmonyPostfix,HarmonyPatch(typeof(VehicleObjectModel),nameof(VehicleObjectModel.OnModelLoaded))]
    private static void Loaded(VehicleObjectModel __instance)
    {
        var kind=DronePartCatalog.Kind(__instance.ComponentID);
        if(kind==null || __instance.Model==null) return;
        try
        {
            meshes.TryGetValue(__instance.Pointer,out var previous);
            bool samePart=previous!=null && previous.Source.VehicleObject!=null && __instance.VehicleObject!=null && previous.Source.VehicleObject.Pointer==__instance.VehicleObject.Pointer;
            float started=samePart?previous!.Started:-1,pose=samePart?previous!.LastPose:-1;
            ReleaseMesh(__instance.Pointer);
            var mesh=DroneModels.BuildMesh(()=>PartGeometry.Housing(kind.Value),__instance.ComponentID+" instance");
            // Removable UAVs are excluded from the permanent hardware collider.
            var collision=DroneModels.BuildMesh(()=>PartGeometry.Collision(kind.Value),__instance.ComponentID+" collision");
            // Native designer/combat models may clone children from a pooled model with a different part pointer.
            for(int i=__instance.Model.transform.childCount-1;i>=0;i--) { var child=__instance.Model.transform.GetChild(i);if(child.name=="Drone launcher moving parts") DroneModels.Release(child.gameObject); }
            // Native model pose is authoritative; its pooled transform need not be the mesh's mounting frame.
            var root=new GameObject("Drone launcher moving parts");
            // Register ownership before child creation, so partial construction is also cleaned up.
            var visual=new PartVisual(__instance,__instance.Model.Pointer,mesh,collision,root,root.transform,root.transform,root.transform,kind.Value){Started=started}; meshes[__instance.Pointer]=visual;
            visual.Doors=new Transform[2];
            for(int i=0;i<2;i++) { int side=i==0?-1:1;visual.Doors[i]=DroneModels.Model(root.transform,kind+" compartment door "+side,()=>PartGeometry.Hatch(kind.Value,side),DroneRuntime.ToU(PartGeometry.HatchPivot(kind.Value,side))).transform; }
            visual.Links=Enumerable.Range(0,8).Select(i=>DroneModels.Model(root.transform,"Lift linkage "+i,PartGeometry.LiftArm,Vector3.zero,"Lift arm").transform).ToArray();
            visual.DriveBarrel=DroneModels.Model(root.transform,"Lift actuator barrel",PartGeometry.StrutBarrel,DroneRuntime.ToU(PartGeometry.LiftDriveAnchor(kind.Value)),"Gas spring barrel").transform;
            visual.DriveRod=DroneModels.Model(root.transform,"Lift actuator piston",PartGeometry.StrutRod,DroneRuntime.ToU(PartGeometry.LiftDriveAnchor(kind.Value))+Vector3.forward*.10f,"Gas spring rod").transform;
            visual.DriveJoint=DroneModels.Model(root.transform,"Lift cross shaft",()=>PartGeometry.LiftCrossPin(kind.Value),Vector3.zero).transform;
            visual.Barrels=new Transform[4];visual.Rods=new Transform[4];visual.Latches=new Transform[4];
            for(int i=0;i<4;i++)
            {
                visual.Barrels[i]=DroneModels.Model(root.transform,"Hatch gas spring "+i,PartGeometry.StrutBarrel,Vector3.zero,"Gas spring barrel").transform;
                visual.Rods[i]=DroneModels.Model(root.transform,"Hatch piston rod "+i,PartGeometry.StrutRod,Vector3.zero,"Gas spring rod").transform;
                visual.Latches[i]=DroneModels.Model(visual.Doors[i/2],"Hatch locking handle "+i,PartGeometry.HatchLatch,DroneRuntime.ToU(PartGeometry.LatchPivot(kind.Value,i<2?-1:1,i%2==0?-1:1)),"Locking handle").transform;
            }
            var tray=new GameObject("Launcher lift").transform;tray.SetParent(root.transform,false);
            DroneModels.Model(tray,kind+" launcher platform",()=>PartGeometry.Base(kind.Value),Vector3.zero);
            var pivot=new GameObject(kind==DroneKind.Scout?"Scout rail trunnion":"Tube trunnion").transform;
            pivot.SetParent(tray,false);pivot.localPosition=DroneRuntime.ToU(kind==DroneKind.Scout?PartGeometry.ScoutPivot:PartGeometry.TubePivot);
            if(kind==DroneKind.Scout)
            {
                DroneModels.Model(pivot,"Scout catapult rail",PartGeometry.ScoutRail,Vector3.zero);
                visual.Sled=DroneModels.Model(pivot,"Scout launch sled",PartGeometry.ScoutSled,Vector3.zero).transform;
            }
            else DroneModels.Model(pivot,"Launch canister",PartGeometry.TubeAssembly,Vector3.zero);
            var lid=DroneModels.Model(pivot,kind+" launcher lid",()=>PartGeometry.Lid(kind.Value),DroneRuntime.ToU(PartGeometry.LidPivot(kind.Value)));
            visual=visual with {Tray=tray,Pivot=pivot,Lid=lid.transform}; meshes[__instance.Pointer]=visual;
            visual.Renderers=root.GetComponentsInChildren<Renderer>(true).ToArray();
            foreach(var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer=__instance.Model.Layer;
            SetPose(visual,pose);
            SyncMount(visual);
            // Warm shared flight meshes during module loading, before the launch frame.
            var warm=DroneModels.Create(kind.Value);DroneModels.Release(warm.Model);
            __instance.MarkDynamic();
            __instance.Model.SetMesh(mesh); __instance.Model.SetShadowProxy(mesh);
            __instance.Model.SetInteractionMesh(collision); __instance.Model.SetCollisionMesh(collision);
            var native=__instance.Model;
            var paint=native.Materials!=null && native.Materials.Length>0?native.Materials[0]:native.Material;
            for(int i=0;i<5;i++) surfaces[i]??=new VehicleMaterial(DroneModels.Surface(i)){Flags=VehicleMaterialFlags.Unpainted|VehicleMaterialFlags.DisableGrime|VehicleMaterialFlags.CustomShader};
            native.SetMaterials(new Il2CppReferenceArray<VehicleMaterial>(new[]{paint??surfaces[0]!,surfaces[1]!,surfaces[2]!,surfaces[3]!,surfaces[4]!}));
            SyncPaint(visual);
        }
        catch(Exception ex) { ReleaseMesh(__instance.Pointer); Plugin.Instance.Log.LogError("[Drones] Part model load failed: "+ex); }
    }
    [HarmonyPostfix,HarmonyPatch(typeof(VehicleObjectModel),nameof(VehicleObjectModel.Release))]
    private static void Released(VehicleObjectModel __instance) => ReleaseMesh(__instance.Pointer);
    [HarmonyPostfix,HarmonyPatch(typeof(VehicleRendererGroup),nameof(VehicleRendererGroup.SetMaterial))]
    private static void PaintedSingle(VehicleRendererGroup __instance) => Painted(__instance);
    [HarmonyPostfix,HarmonyPatch(typeof(VehicleRendererGroup),nameof(VehicleRendererGroup.SetMaterials))]
    private static void Painted(VehicleRendererGroup __instance)
    {
        foreach(var item in meshes.Values) if(item.ModelId==__instance.Pointer && item.Root!=null)
            try {SyncPaint(item);} catch(Exception ex) {Plugin.Instance.Log.LogWarning("[Drones] Launcher paint sync: "+ex.Message);}
    }
    private static void SyncPaint(PartVisual item)
    {
        var native=item.Source.Model;if(native==null) return;
        var paint=native.Materials!=null && native.Materials.Length>0?native.Materials[0]:native.Material;
        if(paint!=null) ApplyPaint(item.Renderers,paint);
    }
    internal static void ApplyPaint(Renderer[] renderers,VehicleMaterial paint)
    {
        for(int i=1;i<5;i++) surfaces[i]??=new VehicleMaterial(DroneModels.Surface(i)){Flags=VehicleMaterialFlags.Unpainted|VehicleMaterialFlags.DisableGrime|VehicleMaterialFlags.CustomShader};
        var materials=new Il2CppReferenceArray<VehicleMaterial>(new[]{paint,surfaces[1]!,surfaces[2]!,surfaces[3]!,surfaces[4]!});
        foreach(var renderer in renderers) if(renderer!=null) VehicleMaterial.ApplyToRenderer(renderer,materials);
    }
    private static void SetPose(PartVisual item,float elapsed)
    {
        float angle=LauncherRules.LidAngle(elapsed),hatch=LauncherRules.HatchAngle(elapsed),height=LauncherRules.LiftHeight(elapsed,Plugin.LauncherHeight.Value);
        for(int i=0;i<item.Doors.Length;i++) item.Doors[i].localRotation=Quaternion.Euler(0,0,(i==0?1:-1)*hatch);
        item.Tray.localPosition=new(0,height,0);
        item.Pivot.localRotation=Quaternion.Euler(-LauncherRules.Elevation(item.Kind,elapsed),0,0);
        if(item.Sled!=null) item.Sled.localPosition=new(0,0,LauncherRules.SledDistance(elapsed));
        var drive=PartGeometry.LiftLink(item.Kind,0,height).A;drive.X=0;
        if(item.DriveJoint!=null) item.DriveJoint.localPosition=DroneRuntime.ToU(drive);
        var anchor=PartGeometry.LiftDriveAnchor(item.Kind);var driveDirection=DroneRuntime.ToU(drive-anchor);var driveRotation=Quaternion.LookRotation(driveDirection);
        if(item.DriveBarrel!=null) item.DriveBarrel.localRotation=driveRotation;
        if(item.DriveRod!=null)
        {
            item.DriveRod.localPosition=DroneRuntime.ToU(anchor)+driveDirection.normalized*.10f;
            item.DriveRod.localRotation=driveRotation;item.DriveRod.localScale=new(1,1,driveDirection.magnitude-.10f);
        }
        item.Lid.localRotation=item.Kind==DroneKind.Scout?Quaternion.Euler(0,0,angle):Quaternion.Euler(0,-angle,0);
        for(int i=0;i<item.Links.Length;i++)
        {
            var (a,b)=PartGeometry.LiftLink(item.Kind,i,height);
            item.Links[i].localPosition=DroneRuntime.ToU(a);item.Links[i].localRotation=Quaternion.LookRotation(DroneRuntime.ToU(b-a));
        }
        for(int i=0;i<item.Barrels.Length;i++)
        {
            var (a,b)=PartGeometry.HatchStrut(item.Kind,i<2?-1:1,i%2==0?-1:1,hatch);
            var direction=DroneRuntime.ToU(b-a).normalized;var rotation=Quaternion.LookRotation(direction);
            item.Barrels[i].localPosition=DroneRuntime.ToU(a);item.Barrels[i].localRotation=rotation;
            item.Rods[i].localPosition=DroneRuntime.ToU(a)+direction*.10f;item.Rods[i].localRotation=rotation;
            item.Rods[i].localScale=new(1,1,(b-a).Length()-.10f);
            item.Latches[i].localRotation=Quaternion.Euler(0,LauncherRules.LatchAngle(elapsed),0);
        }
        item.LastPose=LauncherRules.PoseAge(elapsed);
    }
    internal static bool BeginLaunch(VehicleObjectModel launcher,float now)
    {
        if(launcher.Model==null) return false;
        if(!meshes.TryGetValue(launcher.Pointer,out var item) || item.Root==null || item.ModelId!=launcher.Model.Pointer) Loaded(launcher);
        if(!meshes.TryGetValue(launcher.Pointer,out item) || item.Root==null) return false;
        item.Started=now;
        SetPose(item,0);
        return true;
    }
    private static void SyncMount(PartVisual item)
    {
        var source=item.Source;
        bool visible=source.VehicleObject!=null && !source.VehicleObject.Released && source.Model!=null && source.Model.Pointer==item.ModelId && source.Model.gameObject.activeInHierarchy && source.visible;
        if(item.Root.activeSelf!=visible) item.Root.SetActive(visible);
        if(!visible) return;
        item.Root.transform.SetPositionAndRotation(source.ModelPosition,source.ModelRotation);
        item.Root.transform.localScale=source.ModelScale;
    }
    // Also follows editor rotation, mirroring, scaling and turret motion while the mechanism is idle.
    internal static void FollowMounts()
    {
        foreach(var item in meshes.Values) if(item.Root!=null) SyncMount(item);
    }
    internal static void Animate(float now)
    {
        foreach(var item in meshes.Values)
        {
            if(item.Root==null) continue;
            float elapsed=item.Started<0?-1:now-item.Started;
            float pose=LauncherRules.PoseAge(elapsed);
            if(item.LastPose!=pose) SetPose(item,pose);
        }
    }
    internal static void ResetAnimation()
    {
        foreach(var id in meshes.Keys.ToArray())
        {
            var item=meshes[id];item.Started=-1;
            try { if(item.Root==null) {ReleaseMesh(id);continue;} SetPose(item,-1); }
            catch(Exception ex) {ReleaseMesh(id);Plugin.Instance.Log.LogWarning("[Drones] Retired stale launcher visual: "+ex.Message);}
        }
    }
    private static void ReleaseMesh(IntPtr id)
    {
        if(!meshes.Remove(id,out var item)) return;
        DroneModels.Release(item.Root);
    }
    internal static void Clear() { foreach(var id in meshes.Keys.ToArray()) ReleaseMesh(id); Array.Clear(surfaces,0,surfaces.Length); }
}
