using System;
using System.Drawing;
using System.Globalization;
using System.Reflection;

class TrayRingIconTests {
  static int checks;
  static Type renderer;
  static readonly Color Rose=Color.FromArgb(218,145,170);
  static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
  static object Invoke(string name,params object[] args){return renderer.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args);}
  static double? Normalize(double? value){object result=Invoke("Normalize",value);return result==null?(double?)null:(double)result;}
  static string Label(double? value,bool stale){return (string)Invoke("CenterText",value,stale);}
  static Bitmap Render(int size,double? value,bool stale,bool dark,bool update){return (Bitmap)Invoke("Render",size,value,stale,Rose,dark,update);}
  static int RoseArcPixels(Bitmap bitmap) {
    int count=0;double s=bitmap.Width;
    for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++) {
      double dx=(x+.5)/s-.5,dy=(y+.5)/s-.48,r=Math.Sqrt(dx*dx+dy*dy);
      Color c=bitmap.GetPixel(x,y);
      if(r>.32&&r<.40&&c.A>100&&c.R-c.G>50&&c.B-c.G>20)count++;
    }
    return count;
  }
  [STAThread] static int Main() {
    try {
      renderer=typeof(TrayRingIconTests).Assembly.GetType("CodexQuota.TrayRingIcon");
      Check(renderer!=null,"Tray ring renderer is missing");
      Check(Normalize(-12)==0,"Negative quota must clamp to zero");
      Check(Normalize(140)==100,"Excess quota must clamp to 100");
      Check(Normalize(72.625)==72.625,"Fractional quota must remain precise for its arc");
      Check(Normalize(null)==null&&Normalize(Double.NaN)==null&&Normalize(Double.PositiveInfinity)==null&&Normalize(Double.NegativeInfinity)==null,"Unknown and nonfinite quota must stay unknown");
      Check(Label(0,false)=="0"&&Label(100,false)=="100","Boundary labels must be explicit");
      Check(Label(null,false)=="—"&&Label(Double.NaN,false)=="—","Unknown must differ from zero");
      Check(Label(72,true)=="!"&&Label(null,true)=="—","Only stale known quota uses !; unknown must remain —");
      bool dimensions=true,transparent=true;
      foreach(int size in new[]{16,20,24,32,48})foreach(bool dark in new[]{false,true}) {
        foreach(double? value in new double?[]{0,6,72,100,null,Double.NaN})using(var bitmap=Render(size,value,false,dark,false)) {
          dimensions&=bitmap.Width==size&&bitmap.Height==size;
          transparent&=bitmap.GetPixel(0,0).A==0&&bitmap.GetPixel(size-1,size-1).A==0;
        }
      }
      Check(dimensions,"Render dimensions must match all requested tray sizes");
      Check(transparent,"Tray background must remain transparent at every supported size and theme");
      int zero,low,normal,full;
      using(var bitmap=Render(48,0,false,false,false))zero=RoseArcPixels(bitmap);
      using(var bitmap=Render(48,6,false,false,false))low=RoseArcPixels(bitmap);
      using(var bitmap=Render(48,72,false,false,false))normal=RoseArcPixels(bitmap);
      using(var bitmap=Render(48,100,false,false,false))full=RoseArcPixels(bitmap);
      Check(zero==0&&low>zero&&normal>low&&full>normal,"Actual arc coverage must increase from 0 through 6 and 72 to 100");
      using(var unknown=Render(48,null,false,false,false))Check(RoseArcPixels(unknown)==0,"Unknown must not draw a colored quota arc");
      using(var stale=Render(48,72,true,false,false))Check(RoseArcPixels(stale)==0,"Stale quota must be desaturated");
      using(var plain=Render(32,72,false,false,false))using(var updated=Render(32,72,false,false,true)) {
        bool changed=false,centerUnchanged=true;
        for(int y=0;y<32;y++)for(int x=0;x<32;x++) {
          bool same=plain.GetPixel(x,y).ToArgb()==updated.GetPixel(x,y).ToArgb();
          if(x>=10&&x<22&&y>=10&&y<22)centerUnchanged&=same;
          changed|=!same;
        }
        Check(centerUnchanged,"Update indicator must not obscure the central number");
        Check(changed,"Available update must add a visible indicator");
      }
      using(var custom=(Bitmap)Invoke("Render",48,(double?)72,false,Color.FromArgb(50,125,205),false,false))Check(RoseArcPixels(custom)==0,"Custom blue accent must replace the rose quota arc");
      using(var icon=(Icon)Invoke("Create",(double?)72,false,Rose,false,false,new Size(20,20)))Check(icon.Width==20&&icon.Height==20,"Native icon must preserve requested dimensions after HICON cloning");
      using(var icon=(Icon)Invoke("Create",(double?)72,false,Rose,true,true,new Size(24,20)))Check(icon.Width==24&&icon.Height==20,"Non-square system icon sizes must be respected");
      Console.WriteLine("Tray ring icon: "+checks+" checks PASS");return 0;
    } catch(Exception error) { if(error is TargetInvocationException&&error.InnerException!=null)error=error.InnerException;Console.Error.WriteLine("FAIL: "+error.Message);return 1; }
  }
}
