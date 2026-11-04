using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;

namespace CodexQuota {
  // Construct once from the full annual series. Pages share this immutable scale.
  internal sealed class HeatmapScale {
    static readonly Color DefaultAccent=Color.FromArgb(218,145,170);
    static readonly Color Zero=Color.FromArgb(236,232,239);
    static readonly Color[] Palette={
      Color.FromArgb(246,231,240),Color.FromArgb(231,182,208),
      Color.FromArgb(206,125,167),Color.FromArgb(169,68,123),Color.FromArgb(105,32,71)
    };
    readonly long[] upperBounds;
    HeatmapScale(long[] bounds){upperBounds=bounds;}
    internal int BandCount {get{return upperBounds.Length;}}

    internal static HeatmapScale Create(IEnumerable<ChartBucket> annual) {
      var positive=new List<long>();
      if(annual!=null)foreach(var day in annual)
        if(day!=null&&day.Tokens.HasValue&&day.Tokens.Value>0)positive.Add(day.Tokens.Value);
      positive.Sort();
      var bounds=new List<long>();
      if(positive.Count>0)for(int quantile=1;quantile<=5;quantile++) {
        // Nearest-rank 20/40/60/80/100 percentiles include repeated observations.
        int rank=(int)(((long)positive.Count*quantile+4)/5)-1;
        long value=positive[rank];
        if(bounds.Count==0||bounds[bounds.Count-1]!=value)bounds.Add(value);
      }
      return new HeatmapScale(bounds.ToArray());
    }

    // Positive levels are ordinal 1..BandCount, not fixed palette indexes.
    internal int Level(long? tokens) {
      if(!tokens.HasValue||tokens.Value<0)return -1;
      if(tokens.Value==0)return 0;
      for(int index=0;index<upperBounds.Length;index++)if(tokens.Value<=upperBounds[index])return index+1;
      // A late value outside the snapshot saturates into the highest known band.
      // With no positive snapshot, use one middle-strength class until refresh.
      return Math.Max(1,upperBounds.Length);
    }

    internal Color ColorFor(long? tokens,Color accent){return ColorForBand(Level(tokens),accent);}
    internal Color ColorForBand(int band,Color accent) {
      if(band==-1)return Color.Transparent;
      if(band==0)return Zero;
      if(band<1||band>Math.Max(1,BandCount))throw new ArgumentOutOfRangeException("band");
      // Collapsed bands use the full light-to-dark extent; a single class uses
      // the middle color so a lone observed value does not imply extreme usage.
      int paletteIndex=BandCount<=1?2:(int)Math.Round((band-1)*4d/(BandCount-1),MidpointRounding.AwayFromZero);
      Color color=Palette[paletteIndex];
      if((accent.ToArgb()&0xFFFFFF)==(DefaultAccent.ToArgb()&0xFFFFFF))return color;
      double hue=(accent.GetHue()+color.GetHue()-DefaultAccent.GetHue())/360d;
      hue-=Math.Floor(hue);
      double saturation=Math.Min(1,color.GetSaturation()*accent.GetSaturation()/DefaultAccent.GetSaturation());
      return FromHsl(hue,saturation,color.GetBrightness());
    }

    internal string RangeText(int band,bool english) {
      if(band==-1)return english?"No record":"无记录";
      if(band==0)return english?"0 Tokens":"0 Token";
      if(band<1||band>BandCount)throw new ArgumentOutOfRangeException("band");
      if(BandCount==1)return english?"Positive Tokens":"正值 Token";
      long lower=band==1?1:upperBounds[band-2]+1;
      string unit=english?" Tokens":" Token";
      string start=lower.ToString("N0",CultureInfo.InvariantCulture);
      if(band==BandCount)return "≥ "+start+unit;
      long upper=upperBounds[band-1];
      return start+(lower==upper?"":"–"+upper.ToString("N0",CultureInfo.InvariantCulture))+unit;
    }

    static Color FromHsl(double hue,double saturation,double light) {
      if(saturation<=0){int gray=Byte(light);return Color.FromArgb(gray,gray,gray);}
      double q=light<.5?light*(1+saturation):light+saturation-light*saturation,p=2*light-q;
      return Color.FromArgb(Byte(Channel(p,q,hue+1d/3)),Byte(Channel(p,q,hue)),Byte(Channel(p,q,hue-1d/3)));
    }
    static int Byte(double value){return Math.Max(0,Math.Min(255,(int)Math.Round(value*255)));}
    static double Channel(double p,double q,double hue) {
      if(hue<0)hue+=1;if(hue>1)hue-=1;
      if(hue<1d/6)return p+(q-p)*6*hue;
      if(hue<.5)return q;
      if(hue<2d/3)return p+(q-p)*(2d/3-hue)*6;
      return p;
    }
  }
}
