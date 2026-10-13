using System.Numerics;

namespace SprocketDrones;

public static class DronePartCatalog
{
    public const string ScoutId="sdrone_scout", StrikeId="sdrone_strike";
    public const string ScoutGuid="bd9c840c-0314-4d9e-ae0b-f49125aa2101", StrikeGuid="bd9c840c-0314-4d9e-ae0b-f49125aa2102";
    // Strike identities remain stable so existing blueprints now load the kamikaze launcher.
    public static DroneKind? Kind(string? id) => id==ScoutId?DroneKind.Scout:id==StrikeId?DroneKind.Kamikaze:null;
    public static bool Usable(string? id,DroneKind desired,bool installed,bool released,float health) =>
        Kind(id)==desired && installed && !released && float.IsFinite(health) && health>0;
}

public sealed class PartGeometry
{
    public readonly List<Vector3> Vertices=new();
    public readonly List<int> Triangles=new();
    public readonly List<int> Surfaces=new(); // one material index per triangle
    public int[][] MaterialTriangles()
    {
        var groups=Enumerable.Range(0,5).Select(_=>new List<int>()).ToArray();
        for(int i=0;i<Surfaces.Count;i++) for(int corner=0;corner<3;corner++) groups[Surfaces[i]].Add(Triangles[i*3+corner]);
        return groups.Select(g=>g.ToArray()).ToArray();
    }
    private void Triangle(int a,int b,int c,int surface) { Triangles.AddRange(new[]{a,b,c}); Surfaces.Add(surface); }
    private void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,int surface)
    {
        int n=Vertices.Count; Vertices.AddRange(new[]{a,b,c,d}); Triangle(n,n+1,n+2,surface); Triangle(n,n+2,n+3,surface);
    }
    private void Box(Vector3 centre,Vector3 size,int surface=0,Quaternion? rotation=null)
    {
        var h=size/2; var q=rotation??Quaternion.Identity;
        var v=new[]{new Vector3(-h.X,-h.Y,-h.Z),new Vector3(h.X,-h.Y,-h.Z),new Vector3(h.X,-h.Y,h.Z),new Vector3(-h.X,-h.Y,h.Z),
            new Vector3(-h.X,h.Y,-h.Z),new Vector3(h.X,h.Y,-h.Z),new Vector3(h.X,h.Y,h.Z),new Vector3(-h.X,h.Y,h.Z)}.Select(p=>Vector3.Transform(p,q)+centre).ToArray();
        foreach(var f in new[]{new[]{0,1,2,3},new[]{4,7,6,5},new[]{0,4,5,1},new[]{3,2,6,7},new[]{0,3,7,4},new[]{1,5,6,2}}) Quad(v[f[0]],v[f[1]],v[f[2]],v[f[3]],surface);
    }
    private void Loft(Vector3 centre,(float Z,float X,float Y)[] rings,int surface,Quaternion? rotation=null,int segments=96)
    {
        if(rings.Length>=4)
        {
            var smooth=new List<(float Z,float X,float Y)>();
            float Radius(float a,float b,float c,float d,float t) => Math.Clamp(.5f*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t),Math.Min(b,c),Math.Max(b,c));
            int steps=segments>=64?6:4;
            for(int j=0;j<rings.Length-1;j++) for(int step=0;step<steps;step++)
            {
                float t=step/(float)steps; var a=rings[Math.Max(0,j-1)]; var b=rings[j]; var c=rings[j+1]; var d=rings[Math.Min(rings.Length-1,j+2)];
                smooth.Add((b.Z+(c.Z-b.Z)*t,Radius(a.X,b.X,c.X,d.X,t),Radius(a.Y,b.Y,c.Y,d.Y,t)));
            }
            smooth.Add(rings[^1]); rings=smooth.ToArray();
        }
        int start=Vertices.Count; var q=rotation??Quaternion.Identity;
        foreach(var ring in rings)
        for(int i=0;i<segments;i++)
        { float angle=i*MathF.Tau/segments; Vertices.Add(Vector3.Transform(new(MathF.Cos(angle)*ring.X,MathF.Sin(angle)*ring.Y,ring.Z),q)+centre); }
        for(int j=0;j<rings.Length-1;j++) for(int i=0;i<segments;i++)
        {
            int a=start+j*segments+i,b=start+j*segments+(i+1)%segments,c=b+segments,d=a+segments;
            Triangle(a,b,c,surface); Triangle(a,c,d,surface);
        }
        for(int end=0;end<2;end++)
        {
            int ring=start+(end==0?0:rings.Length-1)*segments,n=Vertices.Count;
            Vertices.Add(Vector3.Transform(new(0,0,rings[end==0?0:rings.Length-1].Z),q)+centre);
            for(int i=0;i<segments;i++) if(end==0) Triangle(n,ring+(i+1)%segments,ring+i,surface); else Triangle(n,ring+i,ring+(i+1)%segments,surface);
        }
    }
    private void Wing(Vector3 centre,float span,float chord,float tipChord,float sweep,int surface=1,Quaternion? rotation=null,int sideOnly=0)
    {
        // Closed airfoil sections, rounded leading edge, thin trailing edge; +Z points forward.
        var profile=new List<Vector2>();
        foreach(int face in new[]{1,-1}) for(int sample=0;sample<=32;sample++)
        {
            float x=(1-MathF.Cos((face==1?sample:32-sample)*MathF.PI/32))/2;
            float thickness=Math.Max(.0008f,.6f*(.2969f*MathF.Sqrt(x)-.126f*x-.3516f*x*x+.2843f*x*x*x-.1036f*x*x*x*x));
            profile.Add(new(.5f-x,face*thickness+.012f*MathF.Sin(MathF.PI*x)));
        }
        int count=profile.Count; var sections=new[]{0f,.04f,.10f,.20f,.35f,.50f,.65f,.80f,.90f,.96f,1f};
        var q=rotation??Quaternion.Identity;
        foreach(int side in new[]{-1,1})
        {
            if(sideOnly!=0 && side!=sideOnly) continue;
            int start=Vertices.Count;
            foreach(float t in sections) foreach(var p in profile)
            {
                float c=chord+(tipChord-chord)*t;
                Vertices.Add(Vector3.Transform(new(side*span*.5f*t,p.Y*c,p.X*c-sweep*t),q)+centre);
            }
            for(int j=0;j<sections.Length-1;j++) for(int i=0;i<count;i++)
            {
                int a=start+j*count+i,b=start+j*count+(i+1)%count,c=b+count,d=a+count;
                if(side>0) { Triangle(a,d,c,surface); Triangle(a,c,b,surface); }
                else { Triangle(a,b,c,surface); Triangle(a,c,d,surface); }
            }
            int end=start+(sections.Length-1)*count;
            for(int i=1;i<count-1;i++)
            {
                if(side>0) { Triangle(start,start+i,start+i+1,surface); Triangle(end,end+i+1,end+i,surface); }
                else { Triangle(start,start+i+1,start+i,surface); Triangle(end,end+i,end+i+1,surface); }
            }
        }
    }
    private void Add(PartGeometry other,Vector3 position,Quaternion? rotation=null)
    {
        int start=Vertices.Count; Vertices.AddRange(other.Vertices.Select(v=>Vector3.Transform(v,rotation??Quaternion.Identity)+position));
        Triangles.AddRange(other.Triangles.Select(i=>i+start)); Surfaces.AddRange(other.Surfaces);
    }
    private void Tube(Vector3 centre,float length,float outer,float inner,int surface,Quaternion? rotation=null,int segments=64)
    {
        int start=Vertices.Count; var q=rotation??Quaternion.Identity;
        foreach(var ring in new[]{(-length/2,outer),(length/2,outer),(length/2,inner),(-length/2,inner)})
        for(int i=0;i<segments;i++)
        { float a=i*MathF.Tau/segments; Vertices.Add(Vector3.Transform(new(MathF.Cos(a)*ring.Item2,MathF.Sin(a)*ring.Item2,ring.Item1),q)+centre); }
        for(int j=0;j<4;j++) for(int i=0;i<segments;i++)
        {
            int a=start+j*segments+i,b=start+j*segments+(i+1)%segments,c=start+(j+1)%4*segments+(i+1)%segments,d=start+(j+1)%4*segments+i;
            Triangle(a,b,c,j==2?2:surface); Triangle(a,c,d,j==2?2:surface);
        }
    }
    private void Rod(Vector3 a,Vector3 b,float radius,int surface=4,int segments=16)
    {
        var delta=b-a; float length=delta.Length(); if(length<1e-6f) return;
        var direction=delta/length; var axis=Vector3.Cross(Vector3.UnitZ,direction);
        var q=axis.LengthSquared()<1e-8f?(direction.Z<0?Quaternion.CreateFromAxisAngle(Vector3.UnitX,MathF.PI):Quaternion.Identity):Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis),MathF.Acos(Math.Clamp(direction.Z,-1,1)));
        Loft((a+b)/2,new[]{(-length/2,radius,radius),(length/2,radius,radius)},surface,q,segments);
    }
    private void Screw(Vector3 centre,Quaternion? rotation=null)
    {
        var q=rotation??Quaternion.CreateFromAxisAngle(Vector3.UnitX,-MathF.PI/2);
        Loft(centre,new[]{(-.002f,.006f,.006f),(0,.009f,.009f),(.004f,.009f,.009f),(.006f,.007f,.007f)},4,q,24);
        Box(centre+Vector3.Transform(new(0,0,.0062f),q),new(.009f,.0015f,.0008f),2,q);
    }
    private void Plate(Vector3 centre,Vector3 size,int surface=0)
    {
        float x=size.X/2,z=size.Z/2,h=size.Y/2,c=Math.Min(x,z)*.14f;
        var outline=new[]{new Vector2(-x+c,-z),new Vector2(x-c,-z),new Vector2(x,-z+c),new Vector2(x,z-c),new Vector2(x-c,z),new Vector2(-x+c,z),new Vector2(-x,z-c),new Vector2(-x,-z+c)};
        for(int i=0;i<8;i++)
        { var a=outline[i]; var b=outline[(i+1)%8]; Quad(centre+new Vector3(a.X,-h,a.Y),centre+new Vector3(a.X,h,a.Y),centre+new Vector3(b.X,h,b.Y),centre+new Vector3(b.X,-h,b.Y),surface); }
        foreach(int side in new[]{-1,1})
        {
            int start=Vertices.Count; Vertices.AddRange(outline.Select(p=>centre+new Vector3(p.X,side*h,p.Y)));
            int middle=Vertices.Count; Vertices.Add(centre+new Vector3(0,side*h,0));
            for(int i=0;i<8;i++) if(side<0) Triangle(middle,start+i,start+(i+1)%8,surface); else Triangle(middle,start+(i+1)%8,start+i,surface);
        }
    }
    private void WingDetail(Vector3 centre,float span,float chord,float tip,float sweep,int sideOnly=0)
    {
        foreach(int side in new[]{-1,1})
        {
            if(sideOnly!=0 && side!=sideOnly) continue;
            Vector3 At(float t) => centre+new Vector3(side*span*.5f*t,.015f*(chord+(tip-chord)*t),-.30f*(chord+(tip-chord)*t)-sweep*t);
            Rod(At(.23f),At(.86f),.0009f,2,12);
            foreach(float t in new[]{.25f,.50f,.78f})
            { var p=At(t); Rod(p+new Vector3(0,.001f,-.009f),p+new Vector3(0,.001f,.009f),.0018f,4,16); }
        }
    }
    public static PartGeometry Propeller(DroneKind kind)
    {
        var mesh=new PartGeometry(); float span=kind==DroneKind.Scout?.28f:.16f;
        mesh.Loft(Vector3.Zero,new[]{(-.015f,.02f,.02f),(.015f,.02f,.02f)},2,segments:16);
        mesh.Wing(Vector3.Zero,span,.028f,.008f,.012f,2,Quaternion.CreateFromAxisAngle(Vector3.UnitX,MathF.PI/2));
        return mesh;
    }
    public static Vector3 PropellerPosition(DroneKind kind) => new(0,0,kind==DroneKind.Scout?-.335f:-.295f);
    public static PartGeometry Aircraft(DroneKind kind,bool propeller=true,bool wings=true,float deployment=1)
    {
        var mesh=new PartGeometry();
        if(kind==DroneKind.Scout)
        {
            // ZALA/War Thunder silhouette: long tailless wing, downward tip fins, underslung sensor, rear propeller.
            mesh.Loft(Vector3.Zero,new[]{(-.32f,.020f,.027f),(-.28f,.055f,.042f),(-.15f,.078f,.053f),(.12f,.084f,.058f),(.26f,.064f,.047f),(.33f,.027f,.025f)},1);
            if(wings) foreach(int side in new[]{-1,1}) mesh.Add(ScoutWing(side),new(0,.025f,-.015f),WingRotation(kind,side,deployment));
            mesh.Loft(new(0,-.095f,.155f),new[]{(-.065f,.018f,.025f),(-.05f,.043f,.049f),(-.02f,.061f,.061f),(.02f,.059f,.055f),(.05f,.043f,.043f),(.065f,.025f,.028f)},1,segments:48);
            mesh.Rod(new(-.065f,-.051f,.15f),new(.065f,-.051f,.15f),.009f,4,24);
            mesh.Tube(new(0,-.097f,.218f),.012f,.028f,.022f,2,segments:48);
            mesh.Loft(new(0,-.097f,.225f),new[]{(-.002f,.022f,.022f),(.002f,.022f,.022f)},3,segments:48);
            foreach(float side in new[]{-.032f,.032f}) foreach(float z in new[]{-.16f,.07f}) mesh.Screw(new(side,.050f,z));
            mesh.Rod(new(0,.052f,-.19f),new(0,.085f,-.21f),.002f,2,16);
            mesh.Plate(new(0,.058f,-.025f),new(.073f,.004f,.145f),1);
            foreach(float x in new[]{-.037f,.037f}) mesh.Rod(new(x,.059f,-.087f),new(x,.059f,.035f),.001f,2,16);
            foreach(int side in new[]{-1,1}) foreach(float z in new[]{-.14f,-.12f,-.10f,-.08f,-.06f}) mesh.Box(new(side*.075f,.016f,z),new(.004f,.017f,.009f),2);
            mesh.Loft(new(0,0,-.306f),new[]{(-.025f,.017f,.018f),(-.019f,.026f,.027f),(.015f,.031f,.033f),(.025f,.026f,.029f)},2,segments:64);
            foreach(float z in new[]{.210f,.217f,.224f}) mesh.Tube(new(0,-.097f,z),.002f,.029f,.023f,4,segments:64);
            foreach(int side in new[]{-1,1})
            {
                mesh.Rod(new(side*.048f,-.046f,.151f),new(side*.048f,-.085f,.151f),.005f,2,24);
                mesh.Screw(new(side*.052f,-.085f,.151f),Quaternion.CreateFromAxisAngle(Vector3.UnitY,side*MathF.PI/2));
                mesh.Plate(new(side*.064f,.045f,-.085f),new(.031f,.008f,.076f),1);
                mesh.Rod(new(side*.067f,.040f,-.135f),new(side*.067f,.040f,-.030f),.001f,2,16);
            }
            mesh.Tube(new(-.025f,-.114f,.212f),.009f,.014f,.010f,2,segments:32);
            mesh.Loft(new(-.025f,-.114f,.218f),new[]{(-.001f,.010f,.010f),(.001f,.010f,.010f)},3,segments:32);
        }
        else
        {
            mesh.Loft(Vector3.Zero,new[]{(-.29f,.032f,.038f),(-.26f,.052f,.055f),(-.21f,.061f,.064f),(.13f,.065f,.068f),(.22f,.062f,.065f),(.27f,.057f,.058f)},1);
            if(wings)
            {
                foreach(int side in new[]{-1,1})
                {
                    mesh.Add(FlightWing(true,side),new(0,-.035f,.145f),WingRotation(kind,side,deployment));
                    mesh.Add(FlightWing(false,side),new(0,.012f,-.17f),WingRotation(kind,side,deployment));
                    mesh.Add(FlightFin(),FinPosition(side,deployment),FinRotation(deployment));
                }
            }
            mesh.Tube(new(0,.012f,.274f),.012f,.043f,.037f,2);
            mesh.Loft(new(0,.012f,.281f),new[]{(-.006f,.036f,.036f),(0,.038f,.038f),(.005f,.029f,.029f),(.010f,.015f,.015f)},3,segments:64);
            mesh.Box(new(0,-.041f,.256f),new(.097f,.04f,.05f),1);
            foreach(float side in new[]{-.027f,.027f})
            {
                mesh.Tube(new(side,-.039f,.285f),.014f,.013f,.0085f,2,segments:40);
                mesh.Loft(new(side,-.039f,.291f),new[]{(-.001f,.0084f,.0084f),(.001f,.0084f,.0084f)},3,segments:40);
                mesh.Rod(new(side,.067f,-.19f),new(side,.067f,-.09f),.0025f,4,16);
            }
            mesh.Box(new(0,.066f,-.01f),new(.077f,.004f,.15f),1);
            foreach(float side in new[]{-.026f,.026f}) foreach(float z in new[]{-.065f,.045f}) mesh.Screw(new(side,.070f,z));
            foreach(int side in new[]{-1,1}) foreach(float z in new[]{-.17f,.145f})
            { mesh.Rod(new(side*.045f,-.03f,z),new(side*.068f,-.03f,z),.005f,4,24); }
            foreach(float z in new[]{-.10f,.07f,.20f})
                mesh.Tube(new(0,0,z),.0018f,.0665f,.0635f,2,segments:64);
            foreach(int side in new[]{-1,1}) foreach(float z in new[]{-.055f,-.035f,-.015f,.005f,.025f}) mesh.Box(new(side*.064f,0,z),new(.003f,.012f,.008f),2);
            foreach(int side in new[]{-1,1}) foreach(float z in new[]{-.17f,.145f})
                mesh.Loft(new(side*.056f,-.026f,z),new[]{(-.010f,.006f,.006f),(-.007f,.009f,.009f),(.007f,.009f,.009f),(.010f,.006f,.006f)},4,Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI/2),32);
            foreach(float x in new[]{-.027f,.027f}) foreach(float y in new[]{-.020f,.044f}) mesh.Screw(new(x,y,.275f),Quaternion.Identity);
        }
        if(propeller) mesh.Add(Propeller(kind),PropellerPosition(kind));
        return mesh;
    }
    public static PartGeometry FlightWing(bool front,int side=0)
    {
        var mesh=new PartGeometry();
        mesh.Wing(Vector3.Zero,front?.76f:.70f,front?.12f:.11f,front?.07f:.065f,front?.018f:.015f,sideOnly:side);
        mesh.WingDetail(Vector3.Zero,front?.76f:.70f,front?.12f:.11f,front?.07f:.065f,front?.018f:.015f,side);
        return mesh;
    }
    public static PartGeometry ScoutWing(int side)
    {
        var mesh=new PartGeometry();
        mesh.Wing(Vector3.Zero,1.8f,.24f,.095f,.09f,sideOnly:side);
        mesh.WingDetail(Vector3.Zero,1.8f,.24f,.095f,.09f,side);
        mesh.Wing(new(side*.89f,-.087f,-.091f),.19f,.115f,.065f,.006f,1,Quaternion.CreateFromAxisAngle(Vector3.UnitZ,side*85*MathF.PI/180));
        return mesh;
    }
    public static Quaternion WingRotation(DroneKind kind,int side,float fraction) => Quaternion.CreateFromAxisAngle(Vector3.UnitY,side*(kind==DroneKind.Scout?85:90)*(1-fraction)*MathF.PI/180);
    public static Quaternion FinRotation(float fraction) => Quaternion.CreateFromAxisAngle(Vector3.UnitX,85*(1-fraction)*MathF.PI/180);
    public static Vector3 FinPosition(int side,float fraction) => new(side*.045f,.018f+.007f*fraction,-.20f);
    public static PartGeometry FlightFin()
    {
        var mesh=new PartGeometry(); mesh.Wing(Vector3.Zero,.30f,.11f,.055f,.01f,1,Quaternion.CreateFromAxisAngle(Vector3.UnitZ,MathF.PI/2),1); return mesh;
    }
    public static readonly Vector3 TubePivot=new(0,.257f,-.135f);
    public static readonly Vector3 ScoutPivot=new(0,.10f,-.30f);
    public static Quaternion TubeRotation(float elevation) => Quaternion.CreateFromAxisAngle(Vector3.UnitX,-elevation*MathF.PI/180);
    public static Vector3 TubePoint(Vector3 local,float elevation=0) => TubePivot+Vector3.Transform(local+new Vector3(0,0,.24f),TubeRotation(elevation));
    // Kamikaze lid coordinates are relative to its moving trunnion frame.
    public static Vector3 LidPivot(DroneKind kind) => kind==DroneKind.Scout?new(-.10f,.205f,.48f):new(-.103f,0,.668f);
    public static Vector3 PayloadPosition(DroneKind kind,float elevation=0) => kind==DroneKind.Scout?ScoutPivot+Vector3.Transform(new Vector3(0,.18f,.48f),TubeRotation(elevation)):TubePoint(new(0,0,.115f),elevation);
    // Explicit movable pieces share their closed pose with icons and native interaction/collision geometry.
    public static PartGeometry Lid(DroneKind kind)
    {
        var mesh=new PartGeometry();
        if(kind==DroneKind.Scout)
        {
            foreach(float z in new[]{-.17f,.16f})
            {
                mesh.Box(new(.10f,.035f,z),new(.17f,.012f,.03f),2);
                foreach(float side in new[]{-.075f,.075f}) mesh.Screw(new(.10f+side,.043f,z));
            }
            mesh.Rod(new(0,0,-.17f),new(0,0,.16f),.008f,4,24);
            foreach(float z in new[]{-.17f,.16f})
            {
                mesh.Box(new(.10f,.026f,z),new(.14f,.008f,.024f),2);
                mesh.Loft(new(0,0,z),new[]{(-.014f,.016f,.016f),(.014f,.016f,.016f)},4,Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI/2),24);
            }
        }
        else
        {
            mesh.Loft(new(.103f,0,0),new[]{(-.01f,.090f,.090f),(0,.104f,.104f),(.014f,.102f,.102f),(.021f,.085f,.085f)},0,segments:64);
            mesh.Tube(new(.103f,0,-.011f),.007f,.094f,.082f,2,segments:64);
            mesh.Box(new(.022f,0,.005f),new(.043f,.054f,.018f),4);
            mesh.Rod(new(0,-.045f,0),new(0,.045f,0),.012f,4,32);
            foreach(float y in new[]{-.035f,.035f}) mesh.Screw(new(.016f,y,.018f),Quaternion.Identity);
            mesh.Box(new(.199f,0,.015f),new(.027f,.036f,.025f),4);
        }
        return mesh;
    }
    private void Cheek(float side)
    {
        // Side plates of the trunnion cradle, extruded in X with a sloped front edge.
        var profile=new[]{new Vector2(-.21f,.085f),new Vector2(.13f,.085f),new Vector2(.045f,.29f),new Vector2(-.04f,.335f),new Vector2(-.15f,.26f)};
        float x=side*.124f; int start=Vertices.Count;
        foreach(int face in new[]{-1,1}) foreach(var p in profile) Vertices.Add(new(x+face*.012f,p.Y,p.X));
        int n=profile.Length;
        for(int i=0;i<n;i++)
        {
            int j=(i+1)%n; Triangle(start+i,start+n+j,start+j,0); Triangle(start+i,start+n+i,start+n+j,0);
        }
        for(int i=1;i<n-1;i++) { Triangle(start,start+i,start+i+1,0); Triangle(start+n,start+n+i+1,start+n+i,0); }
    }
    public static PartGeometry Base(DroneKind kind)
    {
        var mesh=new PartGeometry();
        if(kind==DroneKind.Scout)
        {
            // Open carrier frame leaves room for the catapult drive and does not mask the lift mechanism.
            foreach(int side in new[]{-1,1})
            {
                mesh.Box(new(side*.245f,.026f,-.075f),new(.030f,.052f,1.18f));
                mesh.Box(new(0,.026f,-.075f+side*.575f),new(.48f,.052f,.030f));
                mesh.Box(new(side*.18f,.026f,-.075f),new(.040f,.014f,.56f),4);
                foreach(float z in new[]{-.50f,-.08f,.36f}) mesh.Plate(new(side*.22f,.055f,z),new(.082f,.010f,.10f));
            }
            foreach(float z in new[]{-.30f,.12f}) mesh.Box(new(0,.026f,z),new(.46f,.045f,.048f));
            mesh.Plate(new(-.19f,.055f,.32f),new(.10f,.010f,.18f));
            mesh.Plate(new(.18f,.055f,.25f),new(.11f,.010f,.24f));
            foreach(int side in new[]{-1,1})
            {
                mesh.Plate(new(side*.14f,.056f,-.30f),new(.09f,.014f,.14f),2);
                mesh.Box(new(side*.14f,.081f,-.30f),new(.034f,.054f,.10f));
                mesh.Rod(new(side*.105f,.10f,-.30f),new(side*.175f,.10f,-.30f),.022f,4,32);
                foreach(float z in new[]{-.35f,-.25f}) mesh.Screw(new(side*.14f,.113f,z));
                mesh.Rod(new(side*.11f,.06f,.03f),new(side*.11f,.077f,.09f),.013f,2,24);
            }
            foreach(float x in new[]{-.22f,.22f}) foreach(float z in new[]{-.62f,.46f}) mesh.Screw(new(x,.054f,z));
            // Compact actuator and return spool sit below the rail, clear of the belly camera.
            mesh.Loft(new(.18f,.09f,.25f),new[]{(-.085f,.04f,.04f),(.085f,.04f,.04f)},2,segments:32);
            foreach(float z in new[]{.17f,.33f}) mesh.Tube(new(.18f,.09f,z),.014f,.045f,.036f,4,segments:24);
            mesh.Box(new(-.19f,.075f,.32f),new(.074f,.042f,.14f),2);
            mesh.Rod(new(-.19f,.07f,.23f),new(-.14f,.065f,.10f),.004f,2,16);
        }
        else
        {
            mesh.Plate(new(0,.026f,-.045f),new(.48f,.052f,.58f));
            foreach(float side in new[]{-1f,1f})
            {
                mesh.Box(new(side*.145f,.065f,-.04f),new(.11f,.024f,.40f),2); mesh.Cheek(side);
                mesh.Rod(new(side*.09f,.257f,-.135f),new(side*.15f,.257f,-.135f),.030f,4,32);
                mesh.Loft(new(side*.15f,.257f,-.135f),new[]{(-.007f,.044f,.044f),(.007f,.044f,.044f)},2,Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI/2),32);
                foreach(float z in new[]{-.23f,.10f}) mesh.Screw(new(side*.14f,.115f,z),Quaternion.CreateFromAxisAngle(Vector3.UnitY,side*MathF.PI/2));
            }
            mesh.Box(new(-.184f,.235f,-.135f),new(.055f,.065f,.11f),2);
            mesh.Box(new(.145f,.10f,.19f),new(.15f,.09f,.14f),0);
            mesh.Plate(new(.145f,.148f,.19f),new(.145f,.009f,.133f),2);
            foreach(float x in new[]{-.195f,.195f}) foreach(float z in new[]{-.275f,.185f}) mesh.Screw(new(x,.055f,z));
            foreach(float x in new[]{.09f,.20f}) foreach(float z in new[]{.14f,.24f}) mesh.Screw(new(x,.155f,z));
            mesh.Rod(new(.145f,.11f,.27f),new(.11f,.19f,.18f),.004f,2,20);
            mesh.Rod(new(.11f,.19f,.18f),new(.08f,.205f,-.135f),.004f,2,20);
            foreach(float x in new[]{.115f,.175f})
            {
                mesh.Loft(new(x,.106f,.265f),new[]{(-.008f,.014f,.014f),(-.004f,.017f,.017f),(.009f,.017f,.017f),(.014f,.012f,.012f)},2,segments:32);
                mesh.Rod(new(x,.106f,.280f),new(x,.17f,.185f),.003f,2,24);
            }
        }
        return mesh;
    }
    public static PartGeometry TubeAssembly()
    {
        var mesh=new PartGeometry(); var tilt=Quaternion.Identity;
        Vector3 P(Vector3 v) => v+new Vector3(0,0,.24f);
        mesh.Tube(P(Vector3.Zero),.82f,.094f,.089f,0,tilt,96);
        // Mouth flange, inner guide lip and bolted breech; no open empty cylinder cap.
        mesh.Tube(P(new(0,0,.407f)),.03f,.104f,.088f,4,tilt,96);
        mesh.Tube(P(new(0,0,.382f)),.034f,.095f,.087f,2,tilt,64);
        mesh.Loft(P(new(0,0,-.435f)),new[]{(-.025f,.082f,.082f),(-.012f,.103f,.103f),(.008f,.103f,.103f),(.023f,.094f,.094f)},0,tilt,64);
        foreach(float z in new[]{-.28f,.19f})
        {
            mesh.Tube(P(new(0,0,z)),.033f,.106f,.0935f,2,tilt,64);
            foreach(float side in new[]{-1f,1f}) { mesh.Box(P(new(side*.101f,-.025f,z)),new(.022f,.04f,.044f),4,tilt); mesh.Screw(P(new(side*.113f,-.025f,z)),tilt*Quaternion.CreateFromAxisAngle(Vector3.UnitY,side*MathF.PI/2)); }
        }
        // Longitudinal reinforcement strips and a proper two-point carry grip.
        foreach(float x in new[]{-.061f,.061f}) mesh.Box(P(new(x,-.077f,-.015f)),new(.014f,.016f,.68f),0,tilt);
        mesh.Rod(P(new(.105f,0,-.18f)),P(new(.14f,0,-.18f)),.008f,4,24);
        mesh.Rod(P(new(.105f,0,.10f)),P(new(.14f,0,.10f)),.008f,4,24);
        mesh.Rod(P(new(.14f,0,-.18f)),P(new(.14f,0,.10f)),.013f,2,32);
        foreach(float y in new[]{-.032f,.032f}) mesh.Box(P(new(-.105f,y,.401f)),new(.025f,.025f,.045f),4,tilt);
        mesh.Rod(new(.08f,-.05f,0),P(new(.07f,-.075f,-.30f)),.004f,2,20);
        return mesh;
    }
    public static Vector3 HousingSize(DroneKind kind) => kind==DroneKind.Scout?new(.66f,.44f,1.52f):new(.62f,.44f,1.12f);
    public static Vector3 HousingCentre(DroneKind kind) => new(0,-.215f,kind==DroneKind.Scout?-.07f:.08f);
    public static Vector3 HatchPivot(DroneKind kind,int side=-1) => new(side*HousingSize(kind).X/2,0,HousingCentre(kind).Z);
    public static PartGeometry Housing(DroneKind kind)
    {
        var mesh=new PartGeometry();var size=HousingSize(kind);var c=HousingCentre(kind);
        mesh.Plate(new(0,-.428f,c.Z),new(size.X,.024f,size.Z));
        foreach(int side in new[]{-1,1})
        {
            mesh.Box(c+new Vector3(side*(size.X/2-.008f),0,0),new(.016f,.42f,size.Z),0);
            mesh.Box(c+new Vector3(0,0,side*(size.Z/2-.008f)),new(size.X-.032f,.42f,.016f),0);
            mesh.Box(new(side*(size.X/2-.018f),.003f,c.Z),new(.035f,.018f,size.Z+.024f),4);
            mesh.Box(new(0,.003f,c.Z+side*(size.Z/2-.018f)),new(size.X+.025f,.018f,.035f),4);
        }
        mesh.Box(new(size.X/2+.003f,-.225f,c.Z),new(.009f,.16f,.34f),0);
        foreach(float y in new[]{-.287f,-.163f}) foreach(float z in new[]{c.Z-.14f,c.Z+.14f}) mesh.Screw(new(size.X/2+.009f,y,z),Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI/2));
        foreach(float x in new[]{-size.X/2+.04f,size.X/2-.04f}) foreach(float z in new[]{c.Z-size.Z/2+.05f,c.Z+size.Z/2-.05f}) mesh.Screw(new(x,.013f,z));
        foreach(int side in new[]{-1,1}) foreach(float z in new[]{c.Z-size.Z*.3f,c.Z+size.Z*.3f}) mesh.Rod(new(side*(size.X/2+.008f),.010f,z-.035f),new(side*(size.X/2+.008f),.010f,z+.035f),.012f,4,32);
        foreach(int side in new[]{-1,1})
        {
            mesh.Box(new(side*(kind==DroneKind.Scout?.18f:.16f)+.008f,-.411f,kind==DroneKind.Scout?-.075f:-.045f),new(.044f,.014f,.56f),4);
            foreach(int end in new[]{-1,1}) { var (anchor,_)=HatchStrut(kind,side,end,0);mesh.Box(anchor+new Vector3(side*.004f,-.01f,0),new(.022f,.030f,.060f),4); }
        }
        float anchorZ=LiftDriveAnchor(kind).Z;
        mesh.Plate(new(0,-.411f,anchorZ),new(.075f,.010f,.04f));
        foreach(int side in new[]{-1,1}) mesh.Box(new(side*.027f,-.398f,anchorZ),new(.010f,.024f,.038f),4);
        mesh.Rod(new(-.039f,-.398f,anchorZ),new(.039f,-.398f,anchorZ),.007f,4,24);
        return mesh;
    }
    public static PartGeometry ScoutRail()
    {
        var mesh=new PartGeometry();
        // Twin narrow guides, a linked sled and a clear camera channel; +Z is the launch axis.
        foreach(int side in new[]{-1,1})
        {
            float x=side*.11f;
            mesh.Box(new(x,.040f,.45f),new(.023f,.055f,.96f));
            mesh.Box(new(x,.071f,.45f),new(.028f,.009f,.96f),4);
            mesh.Box(new(x,.021f,.45f),new(.032f,.008f,.96f),4);
            foreach(float z in new[]{.09f,.86f})
            {
                mesh.Plate(new(x,.073f,z),new(.05f,.010f,.058f),2);
                mesh.Screw(new(x,.082f,z));
            }
        }
        mesh.Box(new(0,.105f,.14f),new(.048f,.019f,.057f),2);
        mesh.Tube(new(0,.09f,.135f),.035f,.014f,.006f,4,segments:24);
        mesh.Rod(new(-.11f,.04f,.45f),new(-.10f,.19f,.31f),.010f,4,24);
        mesh.Rod(new(-.11f,.04f,.56f),new(-.10f,.19f,.64f),.010f,4,24);
        foreach(float z in new[]{.31f,.64f}) mesh.Rod(new(-.117f,.205f,z),new(-.083f,.205f,z),.015f,4,24);
        mesh.Box(new(0,.040f,.925f),new(.24f,.034f,.028f),2);
        mesh.Rod(new(.14f,.042f,.06f),new(.14f,.042f,.87f),.003f,2,16);
        return mesh;
    }
    public static PartGeometry ScoutSled()
    {
        var mesh=new PartGeometry();
        foreach(float z in new[]{.29f,.50f})
        {
            mesh.Box(new(0,.092f,z),new(.24f,.018f,.045f));
            // Rubber V saddles carry the fuselage, rather than the camera or propeller.
            foreach(int side in new[]{-1,1})
            {
                var q=Quaternion.CreateFromAxisAngle(Vector3.UnitZ,side*25*MathF.PI/180);
                mesh.Box(new(side*.060f,.112f,z),new(.062f,.024f,.055f),0,q);
                mesh.Box(new(side*.053f,.125f,z),new(.058f,.010f,.048f),2,q);
                mesh.Screw(new(side*.075f,.135f,z));
            }
            foreach(int side in new[]{-1,1})
            {
                float x=side*.11f;
                mesh.Box(new(x,.093f,z),new(.048f,.030f,.065f),2);
                mesh.Rod(new(x-.025f,.088f,z),new(x+.025f,.088f,z),.013f,4,24);
            }
        }
        mesh.Rod(new(0,.09f,.14f),new(0,.09f,.29f),.004f,4,16);
        return mesh;
    }
    public static PartGeometry Hatch(DroneKind kind,int side=-1)
    {
        var mesh=new PartGeometry();var size=HousingSize(kind);
        mesh.Plate(new(-side*size.X/4,.014f,0),new(size.X/2-.009f,.025f,size.Z-.016f));
        foreach(float z in new[]{-size.Z*.20f,size.Z*.20f}) mesh.Box(new(-side*size.X/4,-.006f,z),new(size.X/2-.040f,.016f,.022f),4);
        foreach(float z in new[]{-size.Z*.30f,size.Z*.30f})
        {
            mesh.Box(new(-side*.017f,.018f,z),new(.045f,.014f,.075f),4);
            mesh.Rod(new(0,.010f,z-.035f),new(0,.010f,z+.035f),.012f,4,24);
            mesh.Screw(new(-side*(size.X/2-.038f),.037f,z));
        }
        return mesh;
    }
    public static Vector3 LatchPivot(DroneKind kind,int side,int end) => new(-side*(HousingSize(kind).X/2-.038f),.032f,end*HousingSize(kind).Z*.30f);
    public static PartGeometry HatchLatch()
    {
        var mesh=new PartGeometry();mesh.Box(new(0,0,.009f),new(.025f,.010f,.050f),2);return mesh;
    }
    public static (Vector3 A,Vector3 B) LiftLink(DroneKind kind,int index,float lift)
    {
        int stage=index/4;index%=4;
        float height=(lift+.026f+.404f)/2,z=kind==DroneKind.Scout?-.075f:-.045f;
        float bottom=-.404f+stage*height,y=bottom+height;
        float span=.5f*MathF.Sqrt(.52f*.52f-height*height);
        float x=(index<2?-1:1)*(kind==DroneKind.Scout?.18f:.16f)+(index%2)*.016f;
        float sign=index%2==0?1:-1;
        return(new(x,bottom,z-sign*span),new(x,y,z+sign*span));
    }
    public static PartGeometry LiftArm()
    {
        var mesh=new PartGeometry();
        foreach(int side in new[]{-1,1}) mesh.Box(new(side*.007f,0,.26f),new(.005f,.023f,.50f),4);
        mesh.Box(new(0,-.009f,.26f),new(.014f,.006f,.45f),4);
        var axis=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI/2);
        foreach(float z in new[]{0f,.26f,.52f})
        {
            mesh.Tube(new(0,0,z),.022f,.018f,.007f,4,axis,32);
            mesh.Rod(new(-.019f,0,z),new(.019f,0,z),.006f,4,24);
            foreach(int side in new[]{-1,1})
            {
                mesh.Tube(new(side*.013f,0,z),.004f,.019f,.007f,2,axis,24);
                mesh.Loft(new(side*.019f,0,z),new[]{(-.003f,.010f,.010f),(.003f,.010f,.010f)},4,axis,6);
            }
        }
        return mesh;
    }
    public static Vector3 LiftDriveAnchor(DroneKind kind) => new(0,-.398f,kind==DroneKind.Scout?-.57f:-.458f);
    public static PartGeometry LiftCrossPin(DroneKind kind)
    {
        var mesh=new PartGeometry();float width=kind==DroneKind.Scout?.20f:.18f;
        mesh.Rod(new(-width,0,0),new(width,0,0),.008f,4,32);
        mesh.Tube(Vector3.Zero,.024f,.015f,.008f,2,Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI/2),32);
        return mesh;
    }
    public static (Vector3 A,Vector3 B) HatchStrut(DroneKind kind,int side,int end,float angle)
    {
        var q=Quaternion.CreateFromAxisAngle(Vector3.UnitZ,-side*angle*MathF.PI/180);
        float z=end*HousingSize(kind).Z*.30f;
        return(new(side*(HousingSize(kind).X/2-.025f),-.10f,HousingCentre(kind).Z+z),HatchPivot(kind,side)+Vector3.Transform(new(-side*.13f,-.012f,z),q));
    }
    public static PartGeometry StrutBarrel()
    {
        var mesh=new PartGeometry();mesh.Loft(Vector3.Zero,new[]{(0f,.014f,.014f),(.13f,.014f,.014f)},2,segments:48);
        mesh.Tube(new(0,0,.121f),.013f,.018f,.0135f,4,segments:32);return mesh;
    }
    public static PartGeometry StrutRod()
    {
        var mesh=new PartGeometry();mesh.Rod(Vector3.Zero,Vector3.UnitZ,.006f,4,12);return mesh;
    }
    private static Quaternion LinkRotation(Vector3 direction)
    {
        direction=Vector3.Normalize(direction);var axis=Vector3.Cross(Vector3.UnitZ,direction);
        return axis.LengthSquared()<1e-8f?(direction.Z<0?Quaternion.CreateFromAxisAngle(Vector3.UnitX,MathF.PI):Quaternion.Identity):Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis),MathF.Acos(Math.Clamp(direction.Z,-1,1)));
    }
    public static PartGeometry Mechanism(DroneKind kind,float hatchAngle=0,float lift=LauncherRules.StowedHeight)
    {
        var mesh=new PartGeometry();
        for(int i=0;i<8;i++) { var (a,b)=LiftLink(kind,i,lift);mesh.Add(LiftArm(),a,LinkRotation(b-a)); }
        var drive=LiftLink(kind,0,lift).A;drive.X=0;
        var anchor=LiftDriveAnchor(kind);var driveDirection=Vector3.Normalize(drive-anchor);
        mesh.Add(StrutBarrel(),anchor,LinkRotation(driveDirection));
        mesh.Rod(anchor+driveDirection*.10f,drive,.006f,4,12);
        mesh.Add(LiftCrossPin(kind),drive);
        foreach(int side in new[]{-1,1}) foreach(int end in new[]{-1,1})
        {
            var (a,b)=HatchStrut(kind,side,end,hatchAngle);var direction=Vector3.Normalize(b-a);
            mesh.Add(StrutBarrel(),a,LinkRotation(direction));mesh.Rod(a+direction*.10f,b,.006f,4,12);
        }
        return mesh;
    }
    public static PartGeometry Collision(DroneKind kind)
    {
        // Closed compartment envelope: internal bolts/airframe are visual detail, not collision surfaces.
        var mesh=new PartGeometry();mesh.Box(HousingCentre(kind),HousingSize(kind)+new Vector3(.03f,.06f,.03f));return mesh;
    }
    public static PartGeometry Create(DroneKind kind,float lidAngle=0,bool loaded=true,float elevation=0,float hatchAngle=0,float lift=LauncherRules.StowedHeight,float latchAngle=0,float sledDistance=0)
    {
        var mesh=Housing(kind); var raised=new Vector3(0,lift,0);
        foreach(int side in new[]{-1,1})
        {
            var hatchRotation=Quaternion.CreateFromAxisAngle(Vector3.UnitZ,-side*hatchAngle*MathF.PI/180);
            mesh.Add(Hatch(kind,side),HatchPivot(kind,side),hatchRotation);
            foreach(int end in new[]{-1,1}) mesh.Add(HatchLatch(),HatchPivot(kind,side)+Vector3.Transform(LatchPivot(kind,side,end),hatchRotation),hatchRotation*Quaternion.CreateFromAxisAngle(Vector3.UnitY,latchAngle*MathF.PI/180));
        }
        mesh.Add(Mechanism(kind,hatchAngle,lift),Vector3.Zero);
        mesh.Add(Base(kind),raised);
        var frame=kind==DroneKind.Scout?ScoutPivot:TubePivot;
        mesh.Add(kind==DroneKind.Scout?ScoutRail():TubeAssembly(),frame+raised,TubeRotation(elevation));
        if(kind==DroneKind.Scout) mesh.Add(ScoutSled(),frame+raised+Vector3.Transform(new Vector3(0,0,sledDistance),TubeRotation(elevation)),TubeRotation(elevation));
        var lidRotation=TubeRotation(elevation)*(kind==DroneKind.Scout?Quaternion.CreateFromAxisAngle(Vector3.UnitZ,lidAngle*MathF.PI/180):Quaternion.CreateFromAxisAngle(Vector3.UnitY,-lidAngle*MathF.PI/180));
        var lidPosition=frame+Vector3.Transform(LidPivot(kind),TubeRotation(elevation));
        mesh.Add(Lid(kind),lidPosition+raised,lidRotation);
        if(loaded) mesh.Add(Aircraft(kind,deployment:0),PayloadPosition(kind,elevation)+raised,TubeRotation(elevation));
        return mesh;
    }
}
