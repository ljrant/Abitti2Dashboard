using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;

internal static partial class Program
{
    static bool TryStartListener(int p)
    {
        try {
            var l=new HttpListener(); l.Prefixes.Add("http://127.0.0.1:"+p+"/"); l.Start(); Listener=l; return true;
        } catch { return false; }
    }

    static void ServerLoop()
    {
        while(Listener!=null && Listener.IsListening) {
            HttpListenerContext c=null;
            try { c=Listener.GetContext(); ThreadPool.QueueUserWorkItem(_=>Handle(c)); }
            catch(Exception ex){ Log("Listener: "+ex.Message); }
        }
    }

    static void Handle(HttpListenerContext c)
    {
        try {
            c.Response.Headers["Cache-Control"]="no-store";
            string path=c.Request.Url.AbsolutePath;
            if(path=="/" || path=="/index.html" || path=="/abitti_dashboard.html") { Send(c,200,"text/html; charset=utf-8",Encoding.UTF8.GetBytes(Html)); return; }
            if(!path.StartsWith("/api/")) { SendJson(c,404,new {error="not found"}); return; }
            string[] parts=path.Trim('/').Split('/');
            if(parts.Length<3){SendJson(c,404,new{error="invalid api"});return;}
            string sid=parts[1], action=string.Join("/",parts,2,parts.Length-2), method=c.Request.HttpMethod.ToUpperInvariant();
            if(sid=="local") { HandleLocal(c,action,method); return; }
            if(!Servers.ContainsKey(sid)){SendJson(c,404,new{error="unknown server"});return;}
            if(method=="GET" && action=="server-info") { Proxy(c,Upstream(sid,"/api/server-info","GET",null,null,30)); return; }
            if(method=="POST" && action=="start-exam") { Proxy(c,Upstream(sid,"/api/start-exam","POST",Utf8("{}"),"application/json",30)); return; }
            if(method=="POST" && action=="send-answers") { Proxy(c,Upstream(sid,"/api/send-answers","POST",Utf8("{}"),"application/json",120)); return; }
            if(method=="POST" && action=="single-security-code") { Proxy(c,Upstream(sid,"/api/single-security-code","POST",Utf8("{}"),"application/json",30)); return; }
            if(method=="POST" && action=="ytl-recheck") { Proxy(c,Upstream(sid,"/api/ytl-connection/recheck","POST",new byte[0],null,30)); return; }
            if(method=="POST" && action=="ytl-redeem-pin") {
                var o=ReadJson(c); string pin=GetString(o,"pin"); if(string.IsNullOrWhiteSpace(pin)){SendJson(c,400,new{error="PIN required"});return;}
                Proxy(c,Upstream(sid,"/api/ytl-connection/redeem-pin","POST",JsonBytes(new Dictionary<string,object>{{"pin",pin},{"connectionType","ABITTI"}}),"application/json",30)); return;
            }
            if(method=="POST" && action=="load-exam") {
                string filename=c.Request.QueryString["filename"] ?? "exam.mex"; byte[] raw=ReadAll(c.Request.InputStream); string ct; byte[] mp=Multipart("examZip",filename,raw,out ct);
                Proxy(c,Upstream(sid,"/api/load-exam","POST",mp,ct,180)); return;
            }
            if(method=="POST" && action=="decrypt-exam") {
                var o=ReadJson(c); string dp=GetString(o,"decryptPassword");
                Proxy(c,Upstream(sid,"/api/decrypt-exam","POST",JsonBytes(new Dictionary<string,object>{{"decryptPassword",dp}}),"application/json",180)); return;
            }
            if(method=="DELETE" && action=="ytl-connection") { Proxy(c,Upstream(sid,"/api/ytl-connection","DELETE",new byte[0],null,30)); return; }
            SendJson(c,404,new{error="unsupported action"});
        } catch(Exception ex) { Log("API: "+ex); try{SendJson(c,502,new{error=ex.Message});}catch{} }
    }

    static void HandleLocal(HttpListenerContext c,string action,string method)
    {
        if(method=="GET" && action=="helper-info") { SendJson(c,200,new{version=Version,backend="exe",startedAt=StartedAt.ToString("o"),port=Port,settings=SettingsFile}); return; }
        if(method=="POST" && action=="ui-heartbeat") { var o=ReadJson(c); string id=GetString(o,"session"); if(!string.IsNullOrWhiteSpace(id)){lock(UiSync){UiWasSeen=true;UiSessions[id]=DateTime.UtcNow;}} SendJson(c,200,new{ok=true}); return; }
        if(method=="POST" && action=="ui-closed") { var o=ReadJson(c); string id=GetString(o,"session"); if(!string.IsNullOrWhiteSpace(id)){lock(UiSync)UiSessions.Remove(id);} SendJson(c,200,new{ok=true}); ThreadPool.QueueUserWorkItem(delegate { Thread.Sleep(2500); bool empty; lock(UiSync) empty=UiSessions.Count==0; if(empty) ShutdownProgram("hallintasivu suljettiin"); }); return; }
        if(method=="POST" && action=="shutdown") { SendJson(c,200,new{ok=true}); ThreadPool.QueueUserWorkItem(delegate { Thread.Sleep(200); ShutdownProgram("käyttäjä sulki ohjelman"); }); return; }
        if(method=="GET" && action=="live-state") {
            var d=new Dictionary<string,object>(); lock(Sync) foreach(var kv in Live) d[kv.Key]=kv.Value.ToPublic(); SendJson(c,200,d); return;
        }
        if(method=="GET" && action=="password-status") { var cfg=new Dictionary<string,bool>(); lock(Sync) foreach(var kv in Servers) cfg[kv.Key]=!string.IsNullOrWhiteSpace(kv.Value.Password); SendJson(c,200,new{configured=cfg}); return; }
        // [AUDIT][SALASANAN NÄYTTÖ] Nykyinen tallennettu salasana palautetaan käyttöliittymälle
        // vain käyttäjän painaessa kyseisen Hallinta-kortin "Näytä salasana" -painiketta.
        // Arvoa ei lähetetä live-tilassa, eikä tätä pyyntöä tai vastausta kirjoiteta lokiin.
        if(method=="POST" && action=="password-reveal") { var o=ReadJson(c); string sid=GetString(o,"server"); if(!Servers.ContainsKey(sid)){SendJson(c,400,new{error="Tuntematon palvelin."});return;} string pw; lock(Sync) pw=Servers[sid].Password??""; SendJson(c,200,new{server=sid,password=pw}); return; }
        if(method=="GET" && action=="connection-status") { var en=new Dictionary<string,bool>(); lock(Sync) foreach(var kv in Servers) en[kv.Key]=LiveEnabled.Contains(kv.Key); SendJson(c,200,new{enabled=en,automatic=new[]{"ktp1","ktp2","ktp3","ktp4"}}); return; }
        if(method=="POST" && action=="passwords") { var o=ReadJson(c); object pObj; var changed=new Dictionary<string,string>(); if(o!=null && o.TryGetValue("passwords",out pObj)){var p=pObj as Dictionary<string,object>; if(p!=null) foreach(var kv in p) if(Servers.ContainsKey(kv.Key) && !string.IsNullOrWhiteSpace(Convert.ToString(kv.Value))) changed[kv.Key]=Convert.ToString(kv.Value).Trim();} lock(Sync) foreach(var kv in changed) Servers[kv.Key].Password=kv.Value; SavePasswords(); RestartWs(); SendJson(c,200,new{ok=true}); return; }
        if(method=="POST" && action=="connect-server") { var o=ReadJson(c); string sid=GetString(o,"server"); if(!Servers.ContainsKey(sid)){SendJson(c,400,new{error="Tuntematon palvelin."});return;} lock(Sync) LiveEnabled.Add(sid); RestartWs(); SendJson(c,200,new{ok=true,server=sid}); return; }
        if(method=="POST" && action=="disconnect-server") { var o=ReadJson(c); string sid=GetString(o,"server"); if(!Servers.ContainsKey(sid)){SendJson(c,400,new{error="Tuntematon palvelin."});return;} lock(Sync){LiveEnabled.Remove(sid); Live[sid].Online=false; Live[sid].LastError="Yhteys poistettu käytöstä.";} RestartWs(); SendJson(c,200,new{ok=true,server=sid}); return; }
        if(method=="POST" && action=="restart-live") { RestartWs(); SendJson(c,200,new{ok=true}); return; }
        SendJson(c,404,new{error="unsupported local action"});
    }

    // [AUDIT][VERKKO] Kaikki hallinta-API-kutsut KTP:lle kulkevat tämän funktion kautta.
    // Authorization-header muodostetaan vain muistissa olevasta valvoja-salasanasta eikä sitä lokiteta.
    static UpResult Upstream(string sid,string path,string method,byte[] body,string contentType,int timeoutSec)
    {
        ServerCfg s; lock(Sync)s=Servers[sid].Clone(); if(string.IsNullOrWhiteSpace(s.Password)) throw new Exception("Salasana puuttuu palvelimelta "+sid.ToUpperInvariant()+".");
        var req=(HttpWebRequest)WebRequest.Create("https://"+s.Host+path); req.Method=method; req.Timeout=timeoutSec*1000; req.ReadWriteTimeout=timeoutSec*1000; req.AllowAutoRedirect=false; req.UserAgent="AbittiHallinta/"+Version; req.Accept="application/json, text/plain, */*"; req.Headers[HttpRequestHeader.Authorization]="Basic "+Convert.ToBase64String(Utf8(Username+":"+s.Password));
        if(contentType!=null) req.ContentType=contentType;
        if(body!=null){req.ContentLength=body.Length; using(var st=req.GetRequestStream()) st.Write(body,0,body.Length);}
        try { using(var r=(HttpWebResponse)req.GetResponse()) return ReadResponse(r); }
        catch(WebException ex){var r=ex.Response as HttpWebResponse; if(r!=null) using(r) return ReadResponse(r); throw;}
    }
    static UpResult ReadResponse(HttpWebResponse r){using(var ms=new MemoryStream()){if(r.GetResponseStream()!=null)r.GetResponseStream().CopyTo(ms); return new UpResult{Status=(int)r.StatusCode,ContentType=r.ContentType??"application/octet-stream",Body=ms.ToArray()};}}
    static void Proxy(HttpListenerContext c,UpResult r){Send(c,r.Status,r.ContentType,r.Body);}

    static void RestartWs()
    {
        lock(Sync){WsCancel.Cancel(); WsCancel=new CancellationTokenSourceEx(); foreach(var l in Live.Values){l.Online=false;l.TransportConnected=false;l.Authenticated=false;l.AuthStatus="Yhdistetään…";l.LastError="";}}
        foreach(var s0 in Servers.Values){var s=s0.Clone(); bool enabled; lock(Sync) enabled=LiveEnabled.Contains(s.Id); if(!enabled || string.IsNullOrWhiteSpace(s.Password))continue; var token=WsCancel; var t=new Thread(()=>WsWorker(s,token)){IsBackground=true,Name="WS-"+s.Id}; WsThreads.Add(t); t.Start();}
    }
}
