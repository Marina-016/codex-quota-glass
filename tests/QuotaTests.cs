using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;
class QuotaTests {
 static int assertions;
 static void Check(bool pass,string label){if(!pass)throw new Exception(label);assertions++;}
 static List<WindowQuota> Parse(string text){return QuotaClient.Parse(new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(text));}
 public static int Main(){try{
  var weekly=Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":36,\"windowDurationMins\":10080},\"secondary\":null}}");
  Check(weekly.Count==1&&weekly[0].Name=="每周"&&weekly[0].Left==64,"weekly-only response must not be labeled 5h");
  Check(weekly[0].Reset==0,"missing reset stays unknown");
  var multi=Parse("{\"rateLimitsByLimitId\":{\"codex\":{\"primary\":{\"usedPercent\":120,\"windowDurationMins\":300}},\"spark\":{\"secondary\":{\"usedPercent\":-5,\"windowDurationMins\":60}}},\"rateLimits\":{\"primary\":{\"usedPercent\":10}}}");
  Check(multi.Count==2&&multi[0].Left==0&&multi[1].Left==100,"clamped quota and no duplicate legacy view");
  Check(multi[1].Name=="spark · 1 小时","multiple buckets retain identifiers");
  Check(Parse("{}").Count==0,"empty response is not full quota");
  Check(Parse("{\"rateLimitsByLimitId\":{},\"rateLimits\":{\"primary\":{\"usedPercent\":25,\"windowDurationMins\":300}}}")[0].Left==75,"empty map falls back to legacy");
  Check(QuotaText.Message(0)=="额度已用完，等重置吧","zero threshold");
  Check(QuotaText.Message(10)=="快到上限，稍作休息"&&QuotaText.Message(10.1)=="留点余量，先做重点","10% boundary");
  Check(QuotaText.Message(30)=="留点余量，先做重点"&&QuotaText.Message(30.1)=="节奏不错，稳步推进","30% boundary");
  Check(QuotaText.Message(60)=="节奏不错，稳步推进"&&QuotaText.Message(60.1)=="余量充足，放心推进","60% boundary");
  Check(QuotaText.ColorFor(10)=="#B85062"&&QuotaText.ColorFor(10.1)=="#AD762E","critical color boundary");
  Check(QuotaText.ColorFor(30)=="#AD762E"&&QuotaText.ColorFor(30.1)=="#5879A5"&&QuotaText.ColorFor(60)=="#5879A5"&&QuotaText.ColorFor(60.1)=="#368B82","low and healthy color boundaries");
  var now=DateTimeOffset.FromUnixTimeSeconds(1800000000);
  Check(QuotaText.Reset(0,now)=="重置时间未知","unknown reset");
  Check(QuotaText.Reset(1799999999,now)=="等待额度刷新","expired data never assumes new balance");
  Check(QuotaText.Reset(1800000061,now)=="2 分钟后重置","countdown rounds minutes up");
  Check(Native.IsCodexProcess("ChatGPT",@"C:\Program Files\WindowsApps\OpenAI.Codex_1_x64\app\ChatGPT.exe"),"packaged Codex client named ChatGPT");
  Check(!Native.IsCodexProcess("ChatGPT",@"C:\Program Files\WindowsApps\OpenAI.ChatGPT_1_x64\ChatGPT.exe"),"unrelated ChatGPT client is not Codex");
  Check(!Native.IsCodexProcess("notepad",@"C:\Windows\notepad.exe"),"other apps stay hidden");
  Console.WriteLine("PASS: "+assertions+" checks");return 0;
 }catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}}
}
