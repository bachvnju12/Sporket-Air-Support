using Sprocket;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using SprocketKeybinds;
using Sprocket.Vehicles;
using Sprocket.DamageModelling;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Controller = Sprocket.Gameplay.VehicleControl.VehicleController;
using NVector = System.Numerics.Vector3;

namespace SprocketDrones;

internal sealed class Drone : IDisposable
{
    internal readonly FlightState Flight;
    internal readonly GameObject Model;
    internal readonly Transform Propeller;
    internal readonly Transform[] Wings;
    internal readonly VehicleObjectModel Launcher;
    internal readonly NVector HitScale;
    internal bool WingsDeployed,ResumeManual,ResumeArmed;
    internal float Health;
    internal float MaxHealth => DroneCombatRules.MaxHealth(Flight.Kind);
    internal Vector3? Lock;
    internal float GimbalYaw, GimbalPitch = 45, Fov = 35;
    internal bool Sensor;
    internal float LaunchAge,LaunchStarted;
    internal Vector3 LaunchOrigin,LaunchDirection;
    internal Quaternion LaunchRotation;
    internal bool Launching => LaunchAge<LauncherRules.FlightTime;
    internal Drone(DroneKind kind, Vector3 position, float heading,VehicleObjectModel launcher)
    {
        Launcher=launcher;
        Flight = new(kind) { Position = DroneRuntime.ToN(position), Heading = heading, Pitch=8 }; Flight.ResetAim();
        Health=MaxHealth;
        (Model, Propeller,Wings) = DroneModels.Create(kind); Model.transform.position = position;
        Model.transform.localScale=launcher.ModelScale;HitScale=DroneRuntime.ToN(launcher.ModelScale);
        GimbalYaw = heading;
    }
    internal Vector3 Position => DroneRuntime.ToU(Flight.Position);
    public void Dispose() { if (Model != null) DroneModels.Release(Model); }
}

internal static class DroneRuntime
{
    private static Controller? player;
    private static GameTime? clock;
    private static Camera? camera;
    private static IntPtr owner;
    private static int observedFrame = -100;
    private static int combatFrame=-1;
    private static bool combatState;
    private static readonly List<Drone> drones = new();
    private static readonly Il2CppReferenceArray<Collider> overlaps=new(32);
    internal static IReadOnlyList<Drone> Drones => drones;
    private static Drone? selected;
    internal static bool Piloting { get; private set; }
    internal static bool CapturingInput => Piloting || (Ready && Combat() && !Typing() && DroneBindings.LaunchRequested);
    private static float nextScout, nextStrike, markUntil, noticeUntil;
    private static Vector3? marker;
    private static string notice = "";
    private static bool poseHeld;
    private static Vector3 oldPosition;
    private static Quaternion oldRotation;
    private static float oldFov;
    private static bool oldCursorVisible;
    private static CursorLockMode oldCursorLock;
    internal static NVector ToN(Vector3 p) => new(p.x,p.y,p.z);
    internal static Vector3 ToU(NVector p) => new(p.X,p.Y,p.Z);
    internal static bool Owns(Controller p) => player?.Pointer == p.Pointer;
    private static bool Combat()
    {
        if(combatFrame==Time.frameCount) return combatState;
        combatFrame=Time.frameCount;combatState=false;
        for(int i=0;i<SceneManager.sceneCount;i++) if(SceneManager.GetSceneAt(i).name == "VehicleControlUI") {combatState=true;break;}
        return combatState;
    }
    internal static void SceneChanged(string name)
    {
        combatFrame=-1;
        if(!RuntimeRules.ResetScene(name)) return;
        Clear();Plugin.Instance.Log.LogInfo("[Drones] Scene boundary: UAVs cleared and launchers stowed ("+name+")");
    }
    private static bool Simulating => Plugin.Enabled.Value && clock != null && clock.PauseState == PauseState.Unpaused && clock.TimeScale > 0 && clock.DeltaTime > 0;
    private static bool Ready => Simulating && Application.isFocused;
    internal static float PhysicsDelta => clock?.PauseState==PauseState.Unpaused && clock.TimeScale>0 ? clock.FixedDeltaTime : 0;
    private static bool Typing()
    {
        if(Keybinds.IsConfiguring) return true;
        var go=EventSystem.current?.currentSelectedGameObject;
        return go != null && ((go.GetComponent<TMPro.TMP_InputField>()?.isFocused ?? false) || (go.GetComponent<UnityEngine.UI.InputField>()?.isFocused ?? false));
    }
    internal static void Observe(Controller p, GameTime time, Camera view)
    {
        if (!Combat()) return;
        var id = p.ControlledVehicle?.Pointer ?? IntPtr.Zero;
        if (owner != IntPtr.Zero && id != owner) Clear();
        RestorePose(); player=p; clock=time; camera=view; owner=id; observedFrame=Time.frameCount;
    }
    private static void Notify(string message) { notice=message; noticeUntil=Time.unscaledTime+5; Plugin.Instance.Log.LogInfo("[Drones] "+message); }
    internal static void Message(string message) => Notify(message);
    internal static void Tick()
    {
        if(!Combat()) { if(player!=null || drones.Count>0 || Piloting) Clear();return; }
        bool paused=clock!=null && (clock.PauseState!=PauseState.Unpaused || clock.TimeScale<=0);
        if (RuntimeRules.ShouldClear(Combat(),Plugin.Enabled.Value,player!=null && player.ControlledVehicleGameObject!=null,
            paused,player?.isActiveAndEnabled ?? false,Time.frameCount-observedFrame))
        { Clear(); return; }
        if (!Ready || Typing()) { if(Piloting) LeaveView(); }
        if (!Simulating) return;
        bool inputAllowed=Ready && !Typing();
        if(inputAllowed)
        {
            if(DroneBindings.Pressed("deploy-scout")) Launch(DroneKind.Scout);
            else if(DroneBindings.Pressed("deploy-kamikaze")) Launch(DroneKind.Kamikaze);
            else if(DroneBindings.Pressed("toggle-control"))
            { if(Piloting) LeaveView(true); else if(selected != null) EnterView(); }
        }
        var controller=player;
        var ownerObject=controller?.ControlledVehicleGameObject;
        if(controller==null || ownerObject==null) { Clear(); return; }
        var origin=ownerObject.transform.position;
        DroneParts.Animate(clock!.ScaledTime);
        for(int droneIndex=drones.Count-1;droneIndex>=0;droneIndex--)
        {
            var drone=drones[droneIndex];
            if(!DroneParts.Usable(drone.Launcher,drone.Flight.Kind)) { Lose(drone,"UAV link lost: fitted module unavailable"); continue; }
            if(drone.Health<=0 || drone.Flight.Battery<=0) { Lose(drone,drone.Health<=0?"UAV shot down":"UAV battery exhausted"); continue; }
            float flightDelta=clock.DeltaTime;
            if(drone.Launching)
            {
                float previous=drone.LaunchAge;
                // Fitted hardware and airborne UAV use the same game timestamp, including launch-frame hitches.
                drone.LaunchAge=Math.Clamp(clock.ScaledTime-drone.LaunchStarted,0,LauncherRules.FlightTime);
                // Before release, stay in the moving launcher. Once free, the ejection path is fixed in world space.
                if(previous<LauncherRules.ReleaseTime)
                {
                    float mountedAge=Math.Min(drone.LaunchAge,LauncherRules.ReleaseTime);
                    drone.LaunchOrigin=LaunchPosition(drone.Launcher,drone.Flight.Kind,mountedAge);
                    drone.LaunchRotation=LaunchOrientation(drone.Launcher,drone.Flight.Kind,mountedAge);
                    drone.LaunchDirection=drone.LaunchRotation*Vector3.forward;
                    drone.Flight.Heading=drone.LaunchRotation.eulerAngles.y; drone.Flight.Pitch=-FlightState.Angle(drone.LaunchRotation.eulerAngles.x); drone.Flight.ResetAim();
                }
                var afterLaunch=drone.LaunchOrigin+drone.LaunchDirection*LauncherRules.EjectionDistance(drone.LaunchAge);
                // The closed fitted part collider contains the stored UAV; begin collision checks only after it clears the mouth.
                var clearStart=drone.LaunchOrigin+drone.LaunchDirection*LauncherRules.EjectionDistance(Math.Max(previous,LauncherRules.ReleaseTime+.25f));
                if(drone.LaunchAge>LauncherRules.ReleaseTime+.25f && Contact(drone.Flight.Kind,drone.HitScale,drone.LaunchRotation,clearStart,afterLaunch,
                    DroneWeapons.ContactLayerMask,LauncherRules.WingDeployment(drone.Flight.Kind,drone.LaunchAge,drone.Flight.Kind==DroneKind.Scout?1:2)>=1,out var launchPoint,out var launchNormal))
                { ResolveContact(drone,controller,clearStart,afterLaunch,launchPoint,launchNormal);continue; }
                // One aircraft survives storage, deployment and release: no folded replacement at the handoff.
                drone.Flight.Position=ToN(afterLaunch); drone.Model.SetActive(LauncherRules.PayloadVisible(drone.LaunchAge) || drone.LaunchAge>=LauncherRules.ReleaseTime);
                drone.Model.transform.SetPositionAndRotation(afterLaunch,drone.LaunchRotation);
                DroneModels.DeployWings(drone.Flight.Kind,drone.Wings,drone.LaunchAge);
                drone.Propeller.localRotation=Quaternion.Euler(0,0,LauncherRules.PropellerAngle(drone.LaunchAge));
                if(drone.Launching) continue;
                drone.Flight.Heading=drone.LaunchRotation.eulerAngles.y;
                drone.Flight.Pitch=-FlightState.Angle(drone.LaunchRotation.eulerAngles.x);
                drone.Flight.Speed=drone.Flight.TargetSpeed=Math.Clamp(LauncherRules.EjectionSpeed(LauncherRules.FlightTime),drone.Flight.MinSpeed,drone.Flight.MaxSpeed);
                drone.Flight.ResetAim(); flightDelta=Math.Max(0,flightDelta-(LauncherRules.FlightTime-previous));
                if(drone.Flight.Mode==FlightMode.Orbit) drone.Flight.BeginOrbit();
            }
            drone.LaunchAge=clock.ScaledTime-drone.LaunchStarted;
            if(!drone.WingsDeployed) {DroneModels.DeployWings(drone.Flight.Kind,drone.Wings,drone.LaunchAge);drone.WingsDeployed=LauncherRules.WingDeployment(drone.Flight.Kind,drone.LaunchAge,drone.Flight.Kind==DroneKind.Scout?1:2)>=1;}
            float turn=0, climb=0, throttle=0;
            if(Piloting && selected==drone && inputAllowed)
            {
                if(DroneBindings.Pressed("loiter"))
                {
                    if(drone.Flight.Mode==FlightMode.Orbit) { drone.Flight.Mode=FlightMode.Manual; drone.Flight.ResetAim(); if(drone.Flight.Kind==DroneKind.Scout) drone.Sensor=false; }
                    else drone.Flight.BeginOrbit();
                }
                if(DroneBindings.Pressed("return")) { drone.Flight.Disarm(); drone.Flight.Mode=FlightMode.Return; LeaveView(); Notify("Drone disarmed and returning to vehicle"); }
                if(DroneBindings.Pressed("camera"))
                {
                    drone.Sensor=!drone.Sensor;
                    if(drone.Flight.Kind==DroneKind.Scout && drone.Sensor) drone.Flight.BeginOrbit();
                    drone.Flight.ResetAim();
                }
                var mouse=Mouse.current;
                if(Piloting && mouse != null)
                {
                    var delta=mouse.delta.ReadValue();
                    if(drone.Sensor && drone.Flight.Kind==DroneKind.Scout)
                    {
                        float sensitivity=Plugin.MouseSensitivity.Value*drone.Fov/35;
                        if(delta.sqrMagnitude>0) { Unlock(drone); drone.GimbalYaw=FlightState.Angle(drone.GimbalYaw+delta.x*sensitivity); drone.GimbalPitch=Math.Clamp(drone.GimbalPitch-delta.y*sensitivity,-15,89); }
                        drone.Fov=Math.Clamp(drone.Fov-mouse.scroll.ReadValue().y*.035f,3,60);
                    }
                    else
                    {
                        if(delta.sqrMagnitude>.25f && drone.Flight.Mode!=FlightMode.Return)
                        { drone.Flight.MoveAim(delta.x,delta.y,Plugin.MouseSensitivity.Value); drone.Flight.Mode=FlightMode.Manual; }
                        drone.Flight.AdjustSpeed(mouse.scroll.ReadValue().y/120);
                    }
                }
                if(drone.Flight.Mode==FlightMode.Manual) { var steering=drone.Flight.MouseSteering; turn=steering.X; climb=steering.Y; }
                if(Piloting && DroneBindings.Pressed("context"))
                {
                    if(drone.Flight.Kind==DroneKind.Kamikaze)
                    {
                        if(drone.Flight.ToggleArm()) Notify(drone.Flight.Armed?"WARHEAD ARMED - impact consumes this drone":"Warhead disarmed");
                        else Notify("Use mouse flight mode before arming");
                    }
                    else if(drone.Sensor)
                    {
                        if(drone.Lock.HasValue) Unlock(drone);
                        else if(Aim(drone,out var mark)) { drone.Lock=mark; marker=mark; markUntil=clock!.ScaledTime+30; Notify("Point locked and marked for 30 seconds"); }
                    }
                }
            }
            var before=drone.Position;
            drone.Flight.Step(flightDelta,turn,climb,throttle,ToN(origin));
            var after=drone.Position;
            if(drone.Health<=0 || drone.Flight.Battery<=0) { Lose(drone,drone.Health<=0?"UAV shot down":"UAV battery exhausted"); continue; }
            var attitude=Quaternion.Euler(-drone.Flight.Pitch,drone.Flight.Heading,drone.Flight.Bank);
            if(Contact(drone.Flight.Kind,drone.HitScale,attitude,before,after,DroneWeapons.ContactLayerMask,true,out var point,out var normal))
            {
                ResolveContact(drone,controller,before,after,point,normal);continue;
            }
            if(drone.Flight.Mode==FlightMode.Return && Vector3.Distance(after,origin+Vector3.up*45)<30)
            { Lose(drone,"UAV recovered",false); continue; }
            drone.Model.transform.SetPositionAndRotation(after,attitude);
            drone.Propeller.Rotate(0,0,1600*flightDelta,Space.Self);
        }
    }
    private static void ResolveContact(Drone drone,Controller controller,Vector3 before,Vector3 after,Vector3 point,Vector3 normal)
    {
        bool detonating=drone.Flight.CanDetonate(drone.Health>0,true);
        bool launched=detonating && DroneWeapons.Impact(drone,controller,before,after,point,normal);
        Lose(drone,launched?"Kamikaze impact - drone expended":detonating?"Kamikaze impact; native damage unavailable":"Unarmed drone collision");
    }
    // Sweep the body/wing volumes, including native track/terrain hit layers and trigger damage colliders.
    // End-pose overlaps catch bank changes and a target moving into the UAV, which a centre line cannot see.
    internal static bool Contact(DroneKind kind,NVector scale,Quaternion rotation,Vector3 from,Vector3 to,int mask,bool deployed,out Vector3 point,out Vector3 normal)
    {
        point=normal=Vector3.zero;
        var delta=to-from;float distance=delta.magnitude,best=float.PositiveInfinity;
        var direction=distance>1e-6f?delta/distance:rotation*Vector3.forward;
        for(int i=0;i<(deployed?DroneCombatRules.HitBoxCount(kind):1);i++)
        {
            var (centre,half)=DroneCombatRules.HitBox(kind,i);
            var offset=rotation*ToU(centre*scale);
            var size=ToU(half*System.Numerics.Vector3.Abs(scale));
            if(Overlap(from+offset,size,rotation,direction,mask,out var overlapPoint,out var overlapNormal)) {point=overlapPoint;normal=overlapNormal;return true;}
            var hit=new RaycastHit();
            bool swept=distance>1e-6f && Physics.BoxCast(from+offset,size,direction,out hit,rotation,distance,mask,QueryTriggerInteraction.Collide);
            if(swept && hit.distance>1e-5f && hit.distance<best)
            {
                // PhysX can report a zero-distance initial overlap without a usable point/normal.
                if(hit.normal.sqrMagnitude>.1f) {best=hit.distance;point=hit.point;normal=hit.normal;}
            }
            if(float.IsFinite(best)) continue;
            if(distance>1e-6f && Overlap(to+offset,size,rotation,direction,mask,out overlapPoint,out overlapNormal)) {point=overlapPoint;normal=overlapNormal;best=distance;}
        }
        if(VehicleContact(kind,scale,rotation,from,to,deployed,out var armorPoint,out var armorNormal) &&
            (!float.IsFinite(best) || Vector3.Distance(from,armorPoint)<=best+.4f)) {point=armorPoint;normal=armorNormal;return true;}
        return float.IsFinite(best);
    }
    // Vehicle armour/track damage meshes are not necessarily Unity physics colliders.
    // This API returns a blittable distance/normal struct, unlike the unsafe boxed DamageModelHit API.
    internal static bool VehicleContact(DroneKind kind,NVector scale,Quaternion rotation,Vector3 from,Vector3 to,bool deployed,out Vector3 point,out Vector3 normal)
    {
        point=normal=Vector3.zero;
        var models=DamageModel.models;if(models==null) return false;
        var motion=to-from;float length=motion.magnitude;
        var direction=length>1e-6f?motion/length:rotation*Vector3.forward;
        float reach=.32f*Math.Abs(scale.Z),best=float.PositiveInfinity;
        for(int sample=0;sample<(deployed?5:3);sample++)
        {
            float width=(sample>=3?(kind==DroneKind.Scout?.89f:.37f):.075f)*Math.Abs(scale.X);
            var offset=sample==0?Vector3.zero:rotation*Vector3.right*width*(sample%2==0?1:-1);
            var start=from+offset-direction*reach;
            for(int i=0;i<models.Count;i++)
            {
                var model=models[i];if(model==null) continue;
                if(model.Raycast(start,direction,length+2*reach,out DamageModelRaycastHit hit) && float.IsFinite(hit.Distance) && hit.Distance>=0 && hit.Distance<best)
                {best=hit.Distance;point=start+direction*hit.Distance;normal=hit.Normal;}
            }
        }
        return float.IsFinite(best);
    }
    private static bool Overlap(Vector3 centre,Vector3 size,Quaternion rotation,Vector3 direction,int mask,out Vector3 point,out Vector3 normal)
    {
        point=normal=Vector3.zero;
        int count=Physics.OverlapBoxNonAlloc(centre,size,overlaps,rotation,mask,QueryTriggerInteraction.Collide);
        for(int i=0;i<count;i++)
        {
            var collider=overlaps[i];if(collider==null) continue;
            var probe=centre-direction*(size.magnitude+.02f);
            point=collider.ClosestPoint(probe);var outward=probe-point;
            normal=outward.sqrMagnitude>1e-8f?outward.normalized:-direction;return true;
        }
        return false;
    }
    private static Quaternion LaunchOrientation(VehicleObjectModel launcher,DroneKind kind,float age) => launcher.ModelRotation*Quaternion.Euler(-LauncherRules.Elevation(kind,age),0,0);
    private static Vector3 LaunchPosition(VehicleObjectModel launcher,DroneKind kind,float age) => launcher.ModelPosition+launcher.ModelRotation*Vector3.Scale(launcher.ModelScale,ToU(PartGeometry.PayloadPosition(kind,LauncherRules.Elevation(kind,age)))+new Vector3(0,LauncherRules.LiftHeight(age,Plugin.LauncherHeight.Value),0));
    private static void Launch(DroneKind kind)
    {
        if (player?.ControlledVehicleGameObject == null || camera == null) return;
        var launcher=DroneParts.Find(player,kind);
        if(launcher==null) { Notify(kind==DroneKind.Scout?"Fit a Scout drone launcher in Firepower > Drones":"Fit a Kamikaze drone launcher in Firepower > Drones"); return; }
        var existing=drones.FirstOrDefault(d=>d.Flight.Kind==kind);
        if(existing!=null) { if(selected!=existing) LeaveView(); selected=existing; EnterView(); return; }
        float cooldown=kind==DroneKind.Scout?nextScout:nextStrike;
        if(clock!.ScaledTime<cooldown) { Notify($"UAV unavailable for {cooldown-clock.ScaledTime:0}s"); return; }
        var position=LaunchPosition(launcher,kind,0); var rotation=LaunchOrientation(launcher,kind,0);
        var direction=LaunchOrientation(launcher,kind,LauncherRules.ReleaseTime)*Vector3.forward;
        var mouth=LaunchPosition(launcher,kind,LauncherRules.ReleaseTime)+direction*(.65f*Math.Max(.5f,launcher.ModelScale.z));
        if(Physics.CheckSphere(mouth,.12f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore) ||
            Physics.Linecast(mouth,mouth+direction*6,out _,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)) { Notify("Drone launch path obstructed"); return; }
        if(!DroneParts.BeginLaunch(launcher,clock.ScaledTime)) {Notify("Launcher model unavailable; reload the fitted module in designer");return;}
        LeaveView(); selected=new Drone(kind,position,rotation.eulerAngles.y,launcher) {LaunchOrigin=position,LaunchDirection=direction,LaunchRotation=rotation,LaunchStarted=clock.ScaledTime};
        selected.Flight.Pitch=-FlightState.Angle(rotation.eulerAngles.x); selected.Flight.ResetAim();
        selected.Model.SetActive(false); drones.Add(selected); EnterView();
        Notify($"{kind} deployed | Mouse steers, wheel speed | {DroneBindings.Label("toggle-control")} tank");
    }
    internal static void Lose(Drone drone,string reason,bool destroyed=true)
    {
        if(selected==drone) { LeaveView(); selected=null; }
        drones.Remove(drone); drone.Dispose();
        float delay=destroyed?180:15;
        if(drone.Flight.Kind==DroneKind.Scout) nextScout=(clock?.ScaledTime??0)+delay; else nextStrike=(clock?.ScaledTime??0)+delay;
        Notify(reason);
    }
    private static void EnterView()
    {
        if(selected==null || camera==null || Piloting) return;
        if(selected.ResumeManual) selected.Flight.ResumeManual(selected.ResumeArmed);
        selected.ResumeManual=selected.ResumeArmed=false;
        oldCursorVisible=Cursor.visible; oldCursorLock=Cursor.lockState;
        Cursor.visible=false; Cursor.lockState=CursorLockMode.Locked; Piloting=true;
        DroneInput.Update();
        selected.Flight.ResetAim();
        if(player != null)
        {
            player.ResetDriveInputs(); player.Firing=false;
            // Exit the tank scope so ThermalSight and the scope overlay stop affecting the UAV view.
            var scope=player.ScopeControl?.TryCast<Sprocket.Vehicles.ScopeController>();
            if(scope!=null) { scope.SetScopeRequested(false); scope.SetScoped(false); }
        }
    }
    private static void LeaveView(bool resume=false)
    {
        RestorePose();
        if(Piloting) { Cursor.visible=oldCursorVisible; Cursor.lockState=oldCursorLock; }
        Piloting=false;
        DroneInput.Release();
        if(selected!=null)
        {
            selected.ResumeManual=resume && selected.Flight.Mode==FlightMode.Manual;
            selected.ResumeArmed=selected.ResumeManual && selected.Flight.Armed;
            selected.Flight.Disarm();
            if(selected.Flight.Mode==FlightMode.Manual) { if(selected.Launching) selected.Flight.Mode=FlightMode.Orbit; else selected.Flight.BeginOrbit(); }
        }
    }
    private static void RestorePose()
    {
        if(!poseHeld) return;
        if(camera != null) { camera.transform.SetPositionAndRotation(oldPosition,oldRotation); camera.fieldOfView=oldFov; }
        poseHeld=false;
    }
    private static Quaternion SensorRotation(Drone drone) => drone.Lock.HasValue ? Quaternion.LookRotation(drone.Lock.Value-drone.Position) : Quaternion.Euler(drone.GimbalPitch,drone.GimbalYaw,0);
    private static void Unlock(Drone drone)
    {
        if(drone.Lock.HasValue)
        {
            var delta=drone.Lock.Value-drone.Position;
            drone.GimbalYaw=Mathf.Atan2(delta.x,delta.z)*Mathf.Rad2Deg;
            drone.GimbalPitch=Math.Clamp(-Mathf.Atan2(delta.y,new Vector2(delta.x,delta.z).magnitude)*Mathf.Rad2Deg,-15,89);
            drone.Lock=null;
        }
    }
    internal static bool Aim(Drone drone,out Vector3 target)
    {
        if(Physics.Raycast(drone.Position+Vector3.down*.5f,SensorRotation(drone)*Vector3.forward,out var hit,3000,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)) { target=hit.point; return true; }
        target=default; return false;
    }
    internal static bool Visible(Drone drone, Vector3 target)
    {
        var delta=target-drone.Position;
        return !Physics.Raycast(drone.Position,delta.normalized,out _,Math.Max(0,delta.magnitude-2),Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
    }
    internal static void View()
    {
        // Keep the vehicle view during deployment instead of putting the camera inside the closed compartment.
        if(!Piloting || selected==null || selected.LaunchAge<LauncherRules.ReleaseTime || camera==null || !Ready) { RestorePose(); return; }
        RestorePose(); oldPosition=camera.transform.position; oldRotation=camera.transform.rotation; oldFov=camera.fieldOfView; poseHeld=true;
        var drone=selected;
        if(drone.Sensor && drone.Flight.Kind==DroneKind.Scout) { camera.transform.SetPositionAndRotation(drone.Position+Vector3.down*.16f,SensorRotation(drone)); camera.fieldOfView=drone.Fov; }
        else if(drone.Sensor) { camera.transform.SetPositionAndRotation(drone.Position+ToU(drone.Flight.Forward)*.55f,Quaternion.Euler(-drone.Flight.Pitch,drone.Flight.Heading,drone.Flight.Bank*.3f)); camera.fieldOfView=75; }
        else
        {
            var forward=ToU(drone.Flight.Forward); var view=drone.Position-forward*3.5f+Vector3.up*.8f;
            if(Physics.Linecast(drone.Position,view,out var hit,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)) view=hit.point+hit.normal*.3f;
            var aim=Quaternion.Euler(-drone.Flight.AimPitch,drone.Flight.AimHeading,0)*Vector3.forward;
            if(drone.Flight.Mode!=FlightMode.Manual) aim=forward;
            camera.transform.SetPositionAndRotation(view,Quaternion.LookRotation(drone.Position+aim*100-view)); camera.fieldOfView=65;
        }
        float blend=LauncherRules.CameraBlend(drone.LaunchAge);
        camera.transform.SetPositionAndRotation(Vector3.Lerp(oldPosition,camera.transform.position,blend),Quaternion.Slerp(oldRotation,camera.transform.rotation,blend));
        camera.fieldOfView=Mathf.Lerp(oldFov,camera.fieldOfView,blend);
    }
    internal static void RenderHud()
    {
        if(!Combat() || camera==null) { DroneHud.Update(null,false,null,default,null,""); return; }
        var origin=player?.ControlledVehicleGameObject?.transform.position ?? Vector3.zero;
        DroneHud.Update(selected,Piloting,camera,origin,clock!=null && clock.ScaledTime<markUntil?marker:null,
            Time.unscaledTime<noticeUntil?notice:"");
    }
    internal static void Clear()
    {
        DroneParts.ResetAnimation();LeaveView(); foreach(var drone in drones) drone.Dispose(); drones.Clear(); DroneWeapons.Clear();
        selected=null; player=null; clock=null; camera=null; owner=IntPtr.Zero; observedFrame=-100;
        marker=null; nextScout=nextStrike=0; DroneHud.Hide();
    }
}
