using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace CodexQuota {
  public class UserPreferences {
    public string Language="zh",AccentHtml="#DA91AA";
    public string CornerStyle="card";
    public int GlassTransparency=45;
    public Color AccentColor {get {try{return ColorTranslator.FromHtml(AccentHtml);}catch{return Color.FromArgb(218,145,170);}}}
    static string Location {get{return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,".data","native-preferences.json");}}
    public static UserPreferences Load() {
      var result=new UserPreferences();
      try {
        if(!File.Exists(Location))return result;
        return FromJson(File.ReadAllText(Location));
      }catch{}
      return result;
    }
    public static UserPreferences FromJson(string json) {
      var result=new UserPreferences();
      try {
        var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(json);
        if(data==null)return result;
        object value;
        if(data.TryGetValue("language",out value) && value is string)result.Language=(string)value;
        if(data.TryGetValue("accent",out value) && value is string)result.AccentHtml=(string)value;
        if(data.TryGetValue("cornerStyle",out value) && value is string)result.CornerStyle=(string)value;
        int transparency;
        if(data.TryGetValue("glassTransparency",out value) && Int32.TryParse(Convert.ToString(value,CultureInfo.InvariantCulture),NumberStyles.Integer,CultureInfo.InvariantCulture,out transparency))result.GlassTransparency=transparency;
        result.Normalize();
      }catch{}
      return result;
    }
    public void Normalize() {
      if(Language!="en")Language="zh";
      if(AccentHtml==null || !Regex.IsMatch(AccentHtml,"^#[0-9a-fA-F]{6}$"))AccentHtml="#DA91AA";
      if(CornerStyle!="card" && CornerStyle!="capsule" && CornerStyle!="tray")CornerStyle="card";
      GlassTransparency=Math.Max(10,Math.Min(80,GlassTransparency));
    }
    public string ToJson() {
      Normalize();
      return new JavaScriptSerializer().Serialize(new {language=Language,accent=AccentHtml,cornerStyle=CornerStyle,glassTransparency=GlassTransparency});
    }
    public void Save() {
      Directory.CreateDirectory(Path.GetDirectoryName(Location));
      string temp=Location+".tmp";
      File.WriteAllText(temp,ToJson());
      if(File.Exists(Location))File.Replace(temp,Location,null);else File.Move(temp,Location);
    }
  }
}
