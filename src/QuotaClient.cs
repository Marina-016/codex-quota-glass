using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
class WindowQuota { public string Name; public double Left; public long Reset; }
class QuotaClient {
 static JavaScriptSerializer json = new JavaScriptSerializer();
 public static object Get(Dictionary<string,object> d,string k) { object v; return d!=null && d.TryGetValue(k,out v)?v:null; }
 public static List<WindowQuota> Parse(Dictionary<string,object> result) {
  var rows=new List<WindowQuota>();
  var buckets=Get(result,"rateLimitsByLimitId") as Dictionary<string,object>;
  if(buckets==null || buckets.Count==0) buckets=new Dictionary<string,object>{{"codex",Get(result,"rateLimits")}};
  foreach(var pair in buckets) {
   var bucket=pair.Value as Dictionary<string,object>; if(bucket==null) continue;
   foreach(string key in new[]{"primary","secondary"}) {
    var w=Get(bucket,key) as Dictionary<string,object>; if(w==null || Get(w,"usedPercent")==null) continue;
    double mins=Get(w,"windowDurationMins")==null?0:Convert.ToDouble(w["windowDurationMins"]);
    string label=mins==10080?"每周":mins==300?"5 小时":mins>0?(mins>=1440?(mins/1440).ToString("0.#")+" 天":mins>=60?(mins/60).ToString("0.#")+" 小时":mins+" 分钟"):"周期未知";
    if(pair.Key!="codex") label=pair.Key+" · "+label;
    rows.Add(new WindowQuota{Name=label,Left=Math.Max(0,Math.Min(100,100-Convert.ToDouble(w["usedPercent"]))),Reset=Get(w,"resetsAt")==null?0:Convert.ToInt64(w["resetsAt"])});
   }
  }
  return rows;
 }
 static Dictionary<string,object> Call(Process p,int id,string request) {
  p.StandardInput.WriteLine(request); p.StandardInput.Flush();
  DateTime deadline=DateTime.UtcNow.AddSeconds(20);
  while(DateTime.UtcNow<deadline) {
   var read=p.StandardOutput.ReadLineAsync();
   int wait=(int)Math.Max(1,(deadline-DateTime.UtcNow).TotalMilliseconds);
   if(!read.Wait(wait)) throw new Exception("查询超时，请检查网络");
   if(read.Result==null) throw new Exception("Codex 接口已退出");
   var message=json.DeserializeObject(read.Result) as Dictionary<string,object>;
   if(message==null || Get(message,"id")==null || Convert.ToString(message["id"])!=id.ToString()) continue;
   if(Get(message,"error")!=null) throw new Exception("额度查询失败，请检查 Codex 登录状态");
   return Get(message,"result") as Dictionary<string,object>;
  }
  throw new Exception("查询超时");
 }
 public static string FindCodex() {
  string custom=Environment.GetEnvironmentVariable("CODEX_QUOTA_CLI"); if(!String.IsNullOrEmpty(custom)&&File.Exists(custom)) return custom;
  foreach(string path in (Environment.GetEnvironmentVariable("PATH")??"").Split(';')) { try { string file=Path.Combine(path.Trim('"'),"codex.exe"); if(File.Exists(file)) return file; } catch {} }
  string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenAI\\Codex\\bin");
  if(Directory.Exists(root)) { var files=Directory.GetFiles(root,"codex.exe",SearchOption.AllDirectories); if(files.Length>0) return files.OrderByDescending(File.GetLastWriteTimeUtc).First(); }
  throw new Exception("未找到 Codex，请先安装并登录");
 }
 public static List<WindowQuota> Read() {
  var info=new ProcessStartInfo(FindCodex(),"app-server --stdio"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
  // Some launch environments omit USERPROFILE. The child needs its normal home.
  if(String.IsNullOrEmpty(info.EnvironmentVariables["USERPROFILE"])) info.EnvironmentVariables["USERPROFILE"]=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
  if(String.IsNullOrEmpty(info.EnvironmentVariables["CODEX_HOME"])) info.EnvironmentVariables["CODEX_HOME"]=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex");
  using(var p=new Process{StartInfo=info}) {
   try {
    p.Start(); p.ErrorDataReceived+=(s,e)=>{}; p.BeginErrorReadLine();
    Call(p,1,"{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"codex_quota_float\",\"title\":\"Codex Quota Float\",\"version\":\"0.1.0\"}}}");
    p.StandardInput.WriteLine("{\"method\":\"initialized\",\"params\":{}}");
    var result=Call(p,2,"{\"id\":2,\"method\":\"account/rateLimits/read\"}");
    var rows=Parse(result); if(rows.Count==0) throw new Exception("账户未返回额度，请确认使用 ChatGPT 登录"); return rows;
   } finally { try { if(!p.HasExited) p.Kill(); } catch {} }
  }
 }
}
