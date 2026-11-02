using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace CodexQuota {
  public class UserPreferences {
    public string Language="zh",AccentHtml="#DA91AA";
    public Color AccentColor {get {try{return ColorTranslator.FromHtml(AccentHtml);}catch{return Color.FromArgb(218,145,170);}}}
    static string Location {get{return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,".data","native-preferences.json");}}
    public static UserPreferences Load() {
      var result=new UserPreferences();
      try {
        if(!File.Exists(Location))return result;
        var data=new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(File.ReadAllText(Location));string value;
        if(data.TryGetValue("language",out value) && (value=="zh" || value=="en"))result.Language=value;
        if(data.TryGetValue("accent",out value) && Regex.IsMatch(value,"^#[0-9a-fA-F]{6}$"))result.AccentHtml=value;
      }catch{}
      return result;
    }
    public void Save() {
      if(Language!="en")Language="zh";
      if(AccentHtml==null || !Regex.IsMatch(AccentHtml,"^#[0-9a-fA-F]{6}$"))AccentHtml="#DA91AA";
      Directory.CreateDirectory(Path.GetDirectoryName(Location));
      string temp=Location+".tmp";
      File.WriteAllText(temp,new JavaScriptSerializer().Serialize(new {language=Language,accent=AccentHtml}));
      if(File.Exists(Location))File.Replace(temp,Location,null);else File.Move(temp,Location);
    }
  }
}
