using System.Numerics;

namespace SprocketDrones;

// Gameplay health/HEAT values, independent of real weapon performance.
public static class DroneCombatRules
{
    // One full-calibre native round supplies the game's normal armour/spall simulation.
    public const float JetMass=2f, JetDiameter=.080f;
    public const float DefaultPenetration=480,ContactBlastPower=12;
    public const ushort JetConstant=2400;
    public static float MaxHealth(DroneKind kind) => kind==DroneKind.Scout?100:80;
    public static float HitDamage(float mass,float speed)
    {
        if(!float.IsFinite(mass)||mass<=0||!float.IsFinite(speed)||speed<=0) return 0;
        return Math.Clamp(.5f*mass*speed*speed/120,5,200);
    }
    public static float RemainingHealth(float health,float damage) =>
        float.IsFinite(health)&&float.IsFinite(damage)&&damage>0?Math.Max(0,health-damage):health;
    public static int HitBoxCount(DroneKind kind) => kind==DroneKind.Scout?4:5;
    public static (Vector3 Centre,Vector3 Half) HitBox(DroneKind kind,int index) => index==0?(Vector3.Zero,new(.085f,.085f,.32f)):
        kind==DroneKind.Scout ? index==1?(Vector3.Zero,new(.90f,.035f,.14f)):(new((index==2?-1:1)*.89f,-.062f,-.106f),new(.02f,.10f,.06f)):
        index<3?(new(0,0,index==1?-.17f:.145f),new(.38f,.035f,.065f)):(new((index==3?-1:1)*.045f,.10f,-.20f),new(.009f,.08f,.055f));
    public static float HitDistance(DroneKind kind,Vector3 position,float heading,float pitch,float bank,Vector3 from,Vector3 to,Vector3? scale=null)
    {
        // A narrow body and wing volume follow the aircraft attitude; empty space around the wings is not a hit.
        var inverse=Quaternion.Inverse(Quaternion.CreateFromYawPitchRoll(heading*MathF.PI/180,-pitch*MathF.PI/180,bank*MathF.PI/180));
        var a=Vector3.Transform(from-position,inverse); var b=Vector3.Transform(to-position,inverse);
        if(scale is {} s) { a/=s; b/=s; }
        float best=float.PositiveInfinity;
        for(int i=0;i<HitBoxCount(kind);i++)
        {
            var (centre,half)=HitBox(kind,i); best=Math.Min(best,BoxHit(a-centre,b-centre,half));
        }
        return float.IsFinite(best)?best*Vector3.Distance(from,to):float.PositiveInfinity;
    }
    private static float BoxHit(Vector3 a,Vector3 b,Vector3 half)
    {
        float enter=0,exit=1;
        bool Slab(float start,float end,float h)
        {
            float delta=end-start;
            if(Math.Abs(delta)<1e-7f) return Math.Abs(start)<=h;
            float first=(-h-start)/delta,last=(h-start)/delta;
            if(first>last) (first,last)=(last,first);
            enter=Math.Max(enter,first); exit=Math.Min(exit,last);
            return enter<=exit;
        }
        return Slab(a.X,b.X,half.X)&&Slab(a.Y,b.Y,half.Y)&&Slab(a.Z,b.Z,half.Z)?enter:float.PositiveInfinity;
    }
}
