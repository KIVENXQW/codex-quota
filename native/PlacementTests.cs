using System;
using System.Drawing;
using CodexQuota;
class PlacementTests {
  static void Check(Point actual,Point expected,string name){if(actual!=expected)throw new Exception(name+": "+actual);}
  static void Main(){
    var badge=new Size(40,40);var avatar=new Point(26,46);
    Check(BadgePlacement.Calculate(new Rectangle(0,0,1920,1080),badge,1,avatar),new Point(6,986),"maximized avatar");
    Check(BadgePlacement.Calculate(new Rectangle(278,60,1395,790),badge,1,avatar),new Point(284,756),"resize follows bottom immediately");
    Check(BadgePlacement.Calculate(new Rectangle(0,0,800,600),badge,1,null),new Point(6,510),"unknown avatar fallback");
    Console.WriteLine("Native placement: 3 checks PASS");
  }
}
