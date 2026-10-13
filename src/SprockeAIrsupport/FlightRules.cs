using System.Numerics;

namespace SprocketDrones;

public enum DroneKind { Scout, Kamikaze }
public enum FlightMode { Manual, Orbit, Return }

// Engine-independent flight rules; metres, seconds and degrees.
public sealed class FlightState
{
    public DroneKind Kind { get; }
    public FlightMode Mode { get; set; }
    public Vector3 Position;
    public float Heading, Pitch, Bank, Speed, TargetSpeed, AimHeading, AimPitch, Age, Battery;
    public Vector3 OrbitCentre;
    public float OrbitAltitude;
    public bool Armed { get; private set; }
    public float MaxSpeed => Kind == DroneKind.Scout ? 32 : 45;
    public float MinSpeed => Kind == DroneKind.Scout ? 16 : 18;
    public float Radius => Kind == DroneKind.Scout ? 90 : 75;
    public FlightState(DroneKind kind) { Kind = kind; Speed = TargetSpeed = (MinSpeed + MaxSpeed) / 2; Battery=kind==DroneKind.Scout?900:600; Armed=kind==DroneKind.Kamikaze; }
    public void ResetAim() { AimHeading=Heading; AimPitch=Pitch; }
    public void MoveAim(float dx,float dy,float sensitivity)
    {
        if(!float.IsFinite(dx) || !float.IsFinite(dy) || !float.IsFinite(sensitivity)) return;
        sensitivity=Math.Clamp(sensitivity,.01f,1);
        AimHeading=Angle(Heading+Math.Clamp(Angle(AimHeading+Math.Clamp(dx,-400,400)*sensitivity-Heading),-100,100));
        AimPitch=Math.Clamp(AimPitch+Math.Clamp(dy,-400,400)*sensitivity,Kind==DroneKind.Scout?-45:-75,Kind==DroneKind.Scout?45:70);
    }
    public Vector2 MouseSteering => new(Math.Clamp(Angle(AimHeading-Heading)/30,-1,1),Math.Clamp((AimPitch-Pitch)/18,-1,1));
    public void AdjustSpeed(float wheelSteps) { if(float.IsFinite(wheelSteps)) TargetSpeed=Math.Clamp(TargetSpeed+Math.Clamp(wheelSteps,-10,10)*2,MinSpeed,MaxSpeed); }
    public bool ToggleArm()
    {
        if(Kind!=DroneKind.Kamikaze || Mode!=FlightMode.Manual) return false;
        Armed=!Armed; return true;
    }
    public void Disarm() => Armed=false;
    public void ResumeManual(bool armed)
    {
        if(Mode==FlightMode.Return) return;
        Mode=FlightMode.Manual; Armed=Kind==DroneKind.Kamikaze && armed && Battery>=60;
        ResetAim();
    }
    public bool CanDetonate(bool healthy,bool collision) => Kind==DroneKind.Kamikaze && Armed && healthy && collision && Battery>0;
    public Vector3 Forward => new(MathF.Sin(Heading * MathF.PI / 180) * MathF.Cos(Pitch * MathF.PI / 180),
        MathF.Sin(Pitch * MathF.PI / 180), MathF.Cos(Heading * MathF.PI / 180) * MathF.Cos(Pitch * MathF.PI / 180));
    public void BeginOrbit()
    {
        Disarm();
        var right = new Vector3(MathF.Cos(Heading * MathF.PI / 180), 0, -MathF.Sin(Heading * MathF.PI / 180));
        OrbitCentre = Position - right * Radius; OrbitAltitude = Position.Y; Mode = FlightMode.Orbit;
    }
    public void Step(float dt, float turn, float climb, float throttle, Vector3 owner)
    {
        if (!float.IsFinite(dt) || dt <= 0) return;
        // Bound integration steps without discarding elapsed time on slow frames.
        int steps = (int)Math.Min(1000, Math.Ceiling(dt / .05f));
        float step = dt / steps;
        Battery = Math.Max(0, Battery - dt); Age+=dt;
        for (int i = 0; i < steps; i++) Integrate(step, turn, climb, throttle, owner);
    }
    private void Integrate(float dt, float turn, float climb, float throttle, Vector3 owner)
    {
        if (Battery < 60 || Vector3.Distance(Position, owner) > 2500) { Mode = FlightMode.Return; Disarm(); }
        if (Mode != FlightMode.Manual)
        {
            Vector3 aim;
            if (Mode == FlightMode.Orbit)
            {
                var radial = new Vector3(Position.X - OrbitCentre.X, 0, Position.Z - OrbitCentre.Z);
                if (radial.LengthSquared() < 1) radial = Vector3.UnitX * Radius;
                radial = Vector3.Normalize(radial);
                aim = OrbitCentre + (radial + new Vector3(-radial.Z, 0, radial.X) * .45f) * Radius;
                aim.Y = OrbitAltitude;
            }
            else aim = owner + Vector3.UnitY * 45;
            var delta = aim - Position;
            var desired = MathF.Atan2(delta.X, delta.Z) * 180 / MathF.PI;
            turn = Math.Clamp(Angle(desired - Heading) / 30, -1, 1);
            climb = Math.Clamp((delta.Y * .12f - Pitch) / 20, -1, 1);
        }
        Heading = Angle(Heading + Math.Clamp(turn,-1,1) * (Kind == DroneKind.Scout ? 48 : 65) * dt);
        Pitch = Math.Clamp(Pitch + Math.Clamp(climb,-1,1) * 45 * dt, Kind==DroneKind.Scout?-45:-80, Kind==DroneKind.Scout?45:75);
        Bank += (turn * -35 - Bank) * (1 - MathF.Exp(-4 * dt));
        Speed = Math.Clamp(Speed + ((TargetSpeed-Speed)*2+throttle*8) * dt, MinSpeed, MaxSpeed);
        Position += Forward * Speed * dt;
    }
    public static float Angle(float angle) => (angle % 360 + 540) % 360 - 180;
    public static float SegmentDistanceSquared(Vector3 point, Vector3 start, Vector3 end)
    {
        var segment = end - start;
        var t = segment.LengthSquared() < 1e-8f ? 0 : Math.Clamp(Vector3.Dot(point - start, segment) / segment.LengthSquared(), 0, 1);
        return Vector3.DistanceSquared(point, start + segment * t);
    }
    public static float SegmentHitDistance(Vector3 centre, float radius, Vector3 start, Vector3 end)
    {
        var delta=end-start; float length=delta.Length();
        var offset=start-centre; float c=offset.LengthSquared()-radius*radius;
        if(c<=0) return 0;
        if(length<1e-6f) return float.PositiveInfinity;
        float b=Vector3.Dot(offset,delta/length), discriminant=b*b-c;
        if(discriminant<0) return float.PositiveInfinity;
        float distance=-b-MathF.Sqrt(discriminant);
        return distance>=0 && distance<=length ? distance : float.PositiveInfinity;
    }
}

public static class RuntimeRules
{
    public static bool ResetScene(string name) => name is "VehicleControlUI" or "VehicleDesignerUI";
    public static bool BlockAction(string? asset,string? map,string? name,bool escape) =>
        !(asset?.StartsWith("sprocket.drones/",StringComparison.Ordinal)==true) &&
        map is not ("UI" or "Menu" or "Menus" or "Navigation") && !escape && !(name?.Contains("Pause",StringComparison.OrdinalIgnoreCase)==true);
    public static bool ShouldClear(bool combat, bool enabled, bool ownerAlive, bool paused, bool controllerActive, int observedAge) =>
        !combat || !enabled || !ownerAlive || (!paused && (!controllerActive || observedAge > 3));
    public static bool LiveProjectile(int flags) => (flags & 1) != 0 && (flags & 14) == 0;
}
