using System;
using System.Reflection;
using CodexQuota;

class PreferencesTests {
  static int checks;
  static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
  static object Get(UserPreferences prefs,string field){var member=typeof(UserPreferences).GetField(field);Check(member!=null,"Preference is available: "+field);return member.GetValue(prefs);}
  static UserPreferences Read(string json){var method=typeof(UserPreferences).GetMethod("FromJson");Check(method!=null,"Preferences JSON parser available");return (UserPreferences)method.Invoke(null,new object[]{json});}
  static string Write(UserPreferences prefs){var method=typeof(UserPreferences).GetMethod("ToJson");Check(method!=null,"Preferences JSON writer available");return (string)method.Invoke(prefs,null);}
  static int Main(){try{Run();Console.WriteLine("Preferences: "+checks+" checks PASS");return 0;}catch(Exception e){Console.Error.WriteLine("FAIL: "+e.Message);return 1;}}
  static void Run(){
    var defaults=new UserPreferences();
    Check((string)Get(defaults,"PanelStyle")=="minimal","New and migrated profiles default to the approved minimal theme");
    Check((string)Get(defaults,"CornerStyle")=="card","New installations default to double-row card");
    Check((int)Get(defaults,"GlassTransparency")==45,"Default glass background transparency is 45 percent");
    var old=Read("{\"language\":\"en\",\"accent\":\"#477ABB\"}");
    Check(old.Language=="en"&&old.AccentHtml=="#477ABB","Old preferences retain language and accent");
    Check((string)Get(old,"PanelStyle")=="minimal","Old JSON gains the minimal theme without losing other settings");
    foreach(string theme in new[]{"minimal","glass"}) {
      var themed=Read("{\"panelStyle\":\""+theme+"\",\"cornerStyle\":\"capsule\",\"glassTransparency\":52}");
      var restored=Read(Write(themed));
      Check((string)Get(restored,"PanelStyle")==theme,"Panel theme survives restart: "+theme);
      Check((string)Get(restored,"CornerStyle")=="capsule"&&(int)Get(restored,"GlassTransparency")==52,"Theme persistence retains corner preferences");
    }
    Check((string)Get(Read("{\"panelStyle\":\"invalid\"}"),"PanelStyle")=="minimal","Unknown panel themes use minimal");
    Check((string)Get(old,"CornerStyle")=="card"&&(int)Get(old,"GlassTransparency")==45,"Old JSON gains corner defaults");
    foreach(string style in new[]{"card","capsule","tray"}) {
      var next=Read("{\"language\":\"en\",\"accent\":\"#AA3366\",\"cornerStyle\":\""+style+"\",\"glassTransparency\":72}");
      var copy=Read(Write(next));
      Check((string)Get(copy,"CornerStyle")==style&&(int)Get(copy,"GlassTransparency")==72,"Mode and alpha survive roundtrip: "+style);
      Check(copy.Language=="en"&&copy.AccentHtml=="#AA3366","Appearance retained through roundtrip");
    }
    var invalid=Read("{\"language\":\"en\",\"accent\":\"invalid\",\"cornerStyle\":\"unknown\",\"glassTransparency\":999}");
    Check(invalid.Language=="en"&&invalid.AccentHtml=="#DA91AA","Invalid color does not discard valid language");
    Check((string)Get(invalid,"CornerStyle")=="card"&&(int)Get(invalid,"GlassTransparency")==80,"Invalid mode defaults; high transparency clamps");
    Check((int)Get(Read("{\"glassTransparency\":-5}"),"GlassTransparency")==10,"Transparency low bound clamps");
    Check((int)Get(Read("{\"glassTransparency\":\"65\"}"),"GlassTransparency")==65,"String numeric values remain compatible");
    Check((int)Get(Read("{\"glassTransparency\":\"invalid\"}"),"GlassTransparency")==45,"Invalid numeric values use defaults");
    Check((string)Get(Read("not json"),"CornerStyle")=="card","Malformed JSON remains usable");
    typeof(UserPreferences).GetField("CornerStyle").SetValue(defaults,null);
    typeof(UserPreferences).GetField("GlassTransparency").SetValue(defaults,200);
    var saved=Read(Write(defaults));
    Check((string)Get(saved,"CornerStyle")=="card"&&(int)Get(saved,"GlassTransparency")==80,"Writes normalize externally assigned values");
  }
}
