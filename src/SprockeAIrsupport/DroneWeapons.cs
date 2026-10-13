using HarmonyLib;
using Sprocket.DamageModelling;
using UnityEngine;
using Controller=Sprocket.Gameplay.VehicleControl.VehicleController;

namespace SprocketDrones;

[HarmonyPatch]
internal static class DroneWeapons
{
    private static ProjectileRegister? activeRegister;
    private static IProjectileTypeRegister? types;
    private static IProjectileType? type,jet;
    private static readonly List<int> shots=new();
    private static readonly Il2CppSystem.Guid Guid=new("9d6cf9f5-56a2-45b9-97ce-29a8bf224a73");
    private static readonly Il2CppSystem.Guid JetGuid=new("48e0bc09-d64e-4557-ab7a-b818a845ba73");
    [ThreadStatic] private static int heatProjectile;
    [ThreadStatic] private static bool heatImpact;
    private static readonly HashSet<int> primedJets=new();
    internal static int ContactLayerMask => activeRegister?.physicsLayerMask ?? Physics.DefaultRaycastLayers;
    internal static bool Impact(Drone drone,Controller player,Vector3 before,Vector3 after,Vector3 contactPoint,Vector3 contactNormal)
    {
        if(!drone.Flight.CanDetonate(drone.Health>0,true)) return false;
        try
        {
            var projectiles=activeRegister ?? throw new InvalidOperationException("Native projectile register unavailable");
            var register=IProjectileTypeRegister.Instance ?? throw new InvalidOperationException("Native projectile types unavailable");
            var direction=(after-before).sqrMagnitude>1e-8f?(after-before).normalized:DroneRuntime.ToU(drone.Flight.Forward);
            var source=player.ControlledVehicle.Cast<Il2CppSystem.Object>();
            // Finish and release the HE proxy before another launch can grow the native pool.
            DetonateContact(projectiles,register,source,contactPoint,contactNormal,direction,Math.Max(drone.Flight.Speed,20));
            try { QueueJet(projectiles,register,source,contactPoint,direction); }
            catch(Exception ex) { Plugin.Instance.Log.LogError("[Drones] HEAT failed; contact HE already detonated: "+ex); }
            return true;
        }
        catch(Exception ex) { Plugin.Instance.Log.LogError("[Drones] Kamikaze impact: "+ex); return false; }
    }
    internal static void DetonateContact(ProjectileRegister projectiles,IProjectileTypeRegister register,Il2CppSystem.Object source,Vector3 point,Vector3 normal,Vector3 direction,float speed)
    {
        PrepareTypes(register);
        var launch=new ProjectileLaunchState(point+normal*.02f,direction,direction*speed);
        var launcher=new ProjectileLauncherInfo(0,0);var fire=new ProjectileFireInfo();
        projectiles.Launch(source,ref launch,ref launcher,type!.ID,out fire);
        try
        {
            if(!projectiles.activeIdMap.TryGetValue(fire.ProjectileID,out var index)) throw new InvalidOperationException("Native impact projectile was not created");
            var p=projectiles.pool[index];
            if(p==null || p.Definition?.Guid.ToString()!=Guid.ToString()) throw new InvalidOperationException("Native impact projectile identity mismatch");
            var explosive=p.Definition.Functions.Select(f=>f.TryCast<ExplosiveProjectileFunction>()).FirstOrDefault(f=>f!=null)
                ?? throw new InvalidOperationException("Native HE detonation function missing");
            explosive.context=projectiles.Context;
            explosive.Detonate(ref p,point,normal);
            Plugin.Instance.Log.LogInfo("[Drones] Native HE detonated at kamikaze contact: "+point);
        }
        finally { if(projectiles.activeIdMap.ContainsKey(fire.ProjectileID)) projectiles.Release(fire.ProjectileID); }
    }
    private static float JetSpeed() => PenetrationUtils.ComputeRequiredPenetrationSpeed(DroneCombatRules.JetDiameter*1000,DroneCombatRules.JetMass,Plugin.HeatPenetration.Value,DroneCombatRules.JetConstant);
    internal static void CheckCalibration()
    {
        float budget=Plugin.HeatPenetration.Value;
        float speed=JetSpeed();
        float penetration=PenetrationUtils.ComputePenetration(DroneCombatRules.JetDiameter*1000,DroneCombatRules.JetMass,speed,DroneCombatRules.JetConstant);
        if(!float.IsFinite(speed)||speed<=0 || !float.IsFinite(penetration)||Math.Abs(penetration-budget)>budget*.01f) throw new InvalidOperationException("Native HEAT penetration calibration failed");
        Plugin.Instance.Log.LogInfo($"[Drones] Native HEAT calibration: {budget:0} mm RHA => {speed:0} m/s equivalent jet => {penetration:0} mm RHA");
    }
    private static void PrepareTypes(IProjectileTypeRegister register)
    {
        if(types?.Pointer==register.Pointer && type!=null && jet!=null) return;
        types=register;
        var functions=new Il2CppSystem.Collections.Generic.List<ProjectileFunctionDefinition>();
        functions.Add(new HighExplosiveProjectileFunctionDefinition(0,DroneCombatRules.ContactBlastPower));
        var concrete=register.TryCast<ProjectileTypeRegister>();
        type=concrete?.IsRegistered(Guid)==true ? concrete.GetProjectileDefinition(concrete.GetID(Guid)).Cast<IProjectileType>() :
            register.Create("Kamikaze UAV contact HE",Guid,2,.12f,.55f,1,5,functions.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<ProjectileFunctionDefinition>>());
        var piercing=new Il2CppSystem.Collections.Generic.List<ProjectileFunctionDefinition>();
        piercing.Add(new ArmouredPiercingProjectileFunctionDefinition(DroneCombatRules.JetConstant));
        jet=concrete?.IsRegistered(JetGuid)==true ? concrete.GetProjectileDefinition(concrete.GetID(JetGuid)).Cast<IProjectileType>() :
            register.Create("Kamikaze UAV single HEAT round",JetGuid,DroneCombatRules.JetMass,DroneCombatRules.JetDiameter,.35f,1,.3f,piercing.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<ProjectileFunctionDefinition>>());
    }
    internal static int QueueJet(ProjectileRegister projectileRegister,IProjectileTypeRegister register,Il2CppSystem.Object source,Vector3 point,Vector3 direction)
    {
        PrepareTypes(register);
        direction=direction.normalized;
        var launch=new ProjectileLaunchState(point-direction*.5f,direction,direction*JetSpeed());
        var launcher=new ProjectileLauncherInfo(0,0);var fire=new ProjectileFireInfo();
        projectileRegister.Launch(source,ref launch,ref launcher,jet!.ID,out fire);
        Plugin.Instance.Log.LogInfo($"[Drones] One native HEAT round queued: ID {fire.ProjectileID}, {Plugin.HeatPenetration.Value:0} mm RHA");
        return fire.ProjectileID;
    }
    [HarmonyPrefix,HarmonyPatch(typeof(ArmourPiercingProjectileFunction),nameof(ArmourPiercingProjectileFunction.HitDamageModel))]
    private static void BeginJet(ProjectileInstance projectile,out int __state)
    {
        __state=heatImpact?heatProjectile:-1;
        heatImpact=jet!=null && projectile.Definition?.ID==jet.ID;
        heatProjectile=projectile.ID;
        if(heatImpact) Plugin.Instance.Log.LogInfo("[Drones] HEAT native damage-model hit: "+projectile.ID);
    }
    [HarmonyFinalizer,HarmonyPatch(typeof(ArmourPiercingProjectileFunction),nameof(ArmourPiercingProjectileFunction.HitDamageModel))]
    private static void EndJet(int __state) {heatImpact=__state>=0;heatProjectile=__state;}
    [HarmonyPrefix,HarmonyPriority(Priority.Last),HarmonyPatch(typeof(CompoundStructure),nameof(CompoundStructure.Penetrate))]
    private static void HeatNormalization(ref PenetratorInfo penetrator)
    {
        if(!heatImpact) return;
        var velocity=penetrator.Velocity;
        if(primedJets.Add(heatProjectile))
        {
            velocity=velocity.normalized*JetSpeed();
            Plugin.Instance.Log.LogInfo($"[Drones] HEAT armour entry: ID {heatProjectile}, calibrated {Plugin.HeatPenetration.Value:0} mm RHA; native spall");
        }
        penetrator=new PenetratorInfo(penetrator.Mass,penetrator.Diameter,penetrator.Density,0,penetrator.PenetratorConstant,penetrator.Position,velocity);
    }
    [HarmonyPrefix,HarmonyPriority(Priority.Last),HarmonyPatch(typeof(CompoundStructure),nameof(CompoundStructure.GetAllIntersections))]
    private static void FiniteHeatRay(ref float maxDistance)
    {
        // Infinite native segments miss the BVH on axis-aligned paths (0 * infinity gives NaN).
        // ponytail: 10 m covers one vehicle; longer multi-vehicle jets are not modelled.
        if(heatImpact && !float.IsFinite(maxDistance)) maxDistance=10;
    }
    [HarmonyPrefix,HarmonyPriority(Priority.Last),HarmonyPatch(typeof(ProjectileRegister),nameof(ProjectileRegister.FixedUpdate))]
    private static void PhysicsTick(ProjectileRegister __instance)
    {
        activeRegister=__instance;
        float dt=DroneRuntime.PhysicsDelta; if(dt<=0 || DroneRuntime.Drones.Count==0) return;
        try
        {
            // Snapshot IDs because releasing a hit mutates the native register.
            shots.Clear();foreach(var item in __instance.activeIdMap) shots.Add(item.Key);
            foreach(var id in shots)
            {
                if(!__instance.activeIdMap.TryGetValue(id,out var index)) continue;
                var p=__instance.pool[index];
                if(p==null || !RuntimeRules.LiveProjectile((int)p.flags)) continue;
                var definition=p.Definition;if(definition==null || (type!=null && definition.ID==type.ID) || (jet!=null && definition.ID==jet.ID)) continue;
                Drone? victim=null; float closest=float.PositiveInfinity;
                foreach(var drone in DroneRuntime.Drones)
                {
                    float hit=DroneCombatRules.HitDistance(drone.Flight.Kind,drone.Flight.Position,drone.Flight.Heading,drone.Flight.Pitch,drone.Flight.Bank,
                        DroneRuntime.ToN(p.position),DroneRuntime.ToN(p.position+p.velocity*dt),drone.HitScale);
                    if(hit<closest && drone.Health>0 && drone.LaunchAge>=LauncherRules.ReleaseTime) { closest=hit; victim=drone; }
                }
                if(victim==null) continue;
                if(closest>0 && Physics.Raycast(p.position,p.velocity.normalized,out _,closest,p.layerMask,QueryTriggerInteraction.Ignore)) continue;
                float damage=DroneCombatRules.HitDamage(p.Definition!.Mass,p.velocity.magnitude);
                if(damage<=0) continue;
                __instance.Release(p.ID);
                victim.Health=DroneCombatRules.RemainingHealth(victim.Health,damage);
                Plugin.Instance.Log.LogInfo($"[Drones] {victim.Flight.Kind} hit: -{damage:0} HP, {victim.Health:0}/{victim.MaxHealth:0} HP");
                DroneRuntime.Message(victim.Health<=0?"UAV shot down":"UAV HIT  /  "+$"{victim.Health:0}/{victim.MaxHealth:0} HP");
            }
        }
        catch(Exception ex) { Plugin.Instance.Log.LogWarning("[Drones] Projectile hit check: "+ex.Message); }
    }
    [HarmonyPrefix,HarmonyPatch(typeof(ProjectileRegister),nameof(ProjectileRegister.DestroyAll))]
    private static void DestroyAll(ProjectileRegister __instance)
    { if(activeRegister?.Pointer==__instance.Pointer) Clear(); }
    [HarmonyPostfix,HarmonyPatch(typeof(ProjectileRegister),nameof(ProjectileRegister.Release))]
    private static void Retired(int projectileID) => primedJets.Remove(projectileID);
    internal static void Clear() { types=null; type=jet=null; activeRegister=null; heatImpact=false;heatProjectile=-1;primedJets.Clear();shots.Clear(); }
}
