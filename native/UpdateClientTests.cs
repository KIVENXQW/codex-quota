using System;
using System.IO;
using System.Web.Script.Serialization;
using CodexQuota;
class UpdateClientTests {
  static void Check(bool ok,string text){if(!ok)throw new Exception(text);}
  static string Json(string tag,string url,bool preview){return new JavaScriptSerializer().Serialize(new {tag_name=tag,body="Fixes",draft=false,prerelease=preview,assets=new[]{new {name="CodexQuota-win-x64.zip",browser_download_url=url,digest="sha256:"+new string('a',64)}}});}
  static void Main() {
    string url="https://github.com/owner/repo/releases/download/v2.2.0/CodexQuota-win-x64.zip";
    var good=UpdateClient.ParseRelease(Json("v2.2.0",url,false),"owner/repo","CodexQuota-win-x64.zip","2.1.0");
    Check(good.Version=="2.2.0"&&good.Sha256==new string('a',64),"new stable release metadata");
    Check(UpdateClient.ParseRelease(Json("v2.1.0",url,false),"owner/repo","CodexQuota-win-x64.zip","2.1.0")==null,"same version skipped");
    Check(UpdateClient.ParseRelease(Json("v2.2.0",url,true),"owner/repo","CodexQuota-win-x64.zip","2.1.0")==null,"preview release skipped");
    bool rejected=false;try{UpdateClient.ParseRelease(Json("v2.2.0","https://github.com/other/repo/releases/download/v2.2.0/p.zip",false),"owner/repo","CodexQuota-win-x64.zip","2.1.0");}catch(InvalidDataException){rejected=true;}Check(rejected,"wrong repository rejected");
    Check(UpdateClient.ParseChecksum(new string('B',64)+"  CodexQuota-win-x64.zip\n","CodexQuota-win-x64.zip")==new string('b',64),"matching checksum file");
    rejected=false;try{UpdateClient.ParseChecksum(new string('a',64)+"  other.zip","CodexQuota-win-x64.zip");}catch(InvalidDataException){rejected=true;}Check(rejected,"checksum of another package rejected");
    Check(!new UpdateConfiguration().IsConfigured,"no made-up repository enabled");
    Console.WriteLine("Update client: 7 checks PASS");
  }
}
