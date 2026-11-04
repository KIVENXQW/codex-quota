using System;
using System.Collections.Generic;
using System.Drawing;
using CodexQuota;

class HeatmapScaleTests {
  static int checks;
  static readonly Color Rose=Color.FromArgb(218,145,170);
  static void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;}
  static List<ChartBucket> Days(params long?[] tokens){var result=new List<ChartBucket>();foreach(var n in tokens)result.Add(new ChartBucket{Tokens=n});return result;}
  static void Levels(HeatmapScale scale,long?[] values,int[] wanted,string message){for(int i=0;i<values.Length;i++)if(scale.Level(values[i])!=wanted[i])throw new Exception(message+" at "+i);checks++;}
  static int Main(){try {
    var varied=HeatmapScale.Create(Days(1000000,10000000,40000000,100000000,400000000));
    Check(varied.BandCount==5,"Millions through hundreds of millions must retain five distinct positive bands");
    Levels(varied,new long?[]{1000000,10000000,40000000,100000000,400000000},new[]{1,2,3,4,5},"Balanced positive samples must not all collapse toward the lightest color");
    Check(varied.ColorFor(1000000,Rose).ToArgb()==Color.FromArgb(246,231,240).ToArgb()&&varied.ColorFor(400000000,Rose).ToArgb()==Color.FromArgb(105,32,71).ToArgb(),"The known low/high activity days must use the approved rose extremes");
    var repeated=HeatmapScale.Create(Days(1000000,1000000,1000000,1000000,1000000,1000000,10000000,100000000,200000000,400000000));
    Check(repeated.BandCount==3,"Repeated percentile boundaries must merge rather than invent empty bands");
    Levels(repeated,new long?[]{1000000,1000000,10000000,100000000,200000000,400000000},new[]{1,1,2,2,3,3},"Identical counts must have one stable class and boundary values stay in their lower class");
    Check(repeated.ColorFor(100000000,Rose).ToArgb()==Color.FromArgb(206,125,167).ToArgb(),"Three observed bands must use the middle color for the middle band");
    var single=HeatmapScale.Create(Days(42,42,42,null,0));
    Check(single.BandCount==1&&single.Level(42)==1&&single.ColorFor(42,Rose).ToArgb()==Color.FromArgb(206,125,167).ToArgb(),"A single positive value must remain one neutral-strength positive class");
    var sparse=HeatmapScale.Create(Days(5,9));
    Check(sparse.BandCount==2&&sparse.ColorFor(5,Rose).ToArgb()==Color.FromArgb(246,231,240).ToArgb()&&sparse.ColorFor(9,Rose).ToArgb()==Color.FromArgb(105,32,71).ToArgb(),"Two distinct positive values must cover both ends of the palette");
    Levels(single,new long?[]{null,-1,0,42},new[]{-1,-1,0,1},"Unknown and zero must never be treated as low positive use");
    Check(single.ColorFor(null,Rose).A==0&&single.ColorFor(0,Rose).ToArgb()==Color.FromArgb(236,232,239).ToArgb(),"Missing days are transparent for an outline; recorded zero has its own gray fill");
    var empty=HeatmapScale.Create(Days(null,0,-2));
    Check(empty.BandCount==0,"An empty positive population must not manufacture a positive legend");
    var outlier=HeatmapScale.Create(Days(1000000,2000000,3000000,4000000,5000000,6000000,7000000,8000000,9000000,Int64.MaxValue));
    Levels(outlier,new long?[]{1000000,2000000,3000000,4000000,5000000,6000000,7000000,8000000,9000000,Int64.MaxValue},new[]{1,1,2,2,3,3,4,4,5,5},"An extreme peak must not erase the distinctions between ordinary active days");
    // Both pages use this single annual object; neither page contributes a new scale.
    var annual=HeatmapScale.Create(Days(10,20,30,40,50,60,70,80,90,100));
    Levels(annual,new long?[]{10,30,50,70,90},new[]{1,2,3,4,5},"Page one uses annual thresholds");
    Levels(annual,new long?[]{20,40,60,80,100},new[]{1,2,3,4,5},"Page two retains annual thresholds rather than renormalizing");
    Check(annual.RangeText(2,false).Contains("21")&&annual.RangeText(2,false).Contains("40")&&annual.RangeText(5,true).Contains("81"),"Accessible ranges must express inclusive integer boundaries without overlap");
    Check(outlier.RangeText(5,true).Contains("8,000,001"),"The open final range must avoid overflowing the largest token value");
    Check(varied.ColorFor(40000000,Color.RoyalBlue).ToArgb()!=varied.ColorFor(40000000,Rose).ToArgb()&&varied.ColorFor(null,Color.RoyalBlue).A==0&&varied.ColorFor(0,Color.RoyalBlue).ToArgb()==Color.FromArgb(236,232,239).ToArgb(),"Custom accent changes positive colors while preserving missing and zero semantics");
    double previous=1;bool ordered=true;for(int band=1;band<=5;band++){double light=varied.ColorForBand(band,Color.RoyalBlue).GetBrightness();if(light>=previous)ordered=false;previous=light;}
    Check(ordered,"A custom color must preserve a strictly increasing darkness scale");
    Console.WriteLine("Heatmap scale: "+checks+" checks PASS");return 0;
  }catch(Exception error){Console.Error.WriteLine("FAIL: "+error.Message);return 1;}}
}
