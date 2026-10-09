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
    static void WsWorker(ServerCfg s,CancellationTokenSourceEx cancel)
    {
        while(!cancel.Cancelled){
            TcpClient tcp=null; SslStream ssl=null;
            try{
                tcp=new TcpClient();
                var ar=tcp.BeginConnect(s.Host,443,null,null);
                if(!ar.AsyncWaitHandle.WaitOne(10000)) throw new Exception("TCP timeout");
                tcp.EndConnect(ar);

                ssl=new SslStream(tcp.GetStream(),false,(a,b,c,d)=>true);
                ssl.ReadTimeout=35000; ssl.WriteTimeout=35000;
                ssl.AuthenticateAsClient(s.Host,null,System.Security.Authentication.SslProtocols.Tls12,false);

                lock(Sync){
                    var l=Live[s.Id];
                    l.TransportConnected=true;
                    l.Authenticated=false;
                    l.AuthStatus="TLS-yhteys muodostettu, kirjautuminen tarkistetaan…";
                    l.UpdatedAt=DateTime.UtcNow;
                }

                string key=Convert.ToBase64String(RandomBytes(16));
                string auth=Convert.ToBase64String(Utf8(Username+":"+s.Password));
                string h="GET /ws/data HTTP/1.1\r\nHost: "+s.Host+"\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: "+key+"\r\nSec-WebSocket-Version: 13\r\nAuthorization: Basic "+auth+"\r\nOrigin: https://"+s.Host+"\r\n\r\n";
                byte[] hb=Encoding.ASCII.GetBytes(h); ssl.Write(hb,0,hb.Length); ssl.Flush();

                string resp=ReadHttpHeader(ssl);
                string statusLine=resp.Split('\n')[0].Trim();
                bool ok101=statusLine.StartsWith("HTTP/1.1 101") || statusLine.StartsWith("HTTP/1.0 101");
                if(!ok101){
                    bool authFail=statusLine.Contains(" 401 ") || statusLine.Contains(" 403 ");
                    lock(Sync){
                        var l=Live[s.Id];
                        l.Authenticated=false;
                        l.AuthStatus=authFail ? "Kirjautuminen epäonnistui ("+statusLine+")" : "WebSocket-kättely epäonnistui ("+statusLine+")";
                        l.Online=false;
                        l.LastError="";
                        l.UpdatedAt=DateTime.UtcNow;
                    }
                    throw new WsHandshakeException(statusLine,authFail);
                }

                lock(Sync){
                    var l=Live[s.Id];
                    l.Online=true;
                    l.TransportConnected=true;
                    l.Authenticated=true;
                    l.AuthStatus="Kirjautuminen onnistui";
                    l.LastError="";
                    l.UpdatedAt=DateTime.UtcNow;
                    l.LastSuccessAt=DateTime.UtcNow;
                }

                var frag=new MemoryStream();
                int fragOp=0;
                while(!cancel.Cancelled){
                    var f=ReadFrame(ssl);
                    if(f.Opcode==8)throw new EndOfStreamException();
                    if(f.Opcode==9){WriteFrame(ssl,10,f.Payload);continue;}
                    if(f.Opcode==10)continue;
                    if(f.Opcode==1 || f.Opcode==2){frag.SetLength(0);fragOp=f.Opcode;frag.Write(f.Payload,0,f.Payload.Length);}
                    else if(f.Opcode==0){frag.Write(f.Payload,0,f.Payload.Length);}
                    else continue;
                    if(!f.Fin)continue;
                    string text=Encoding.UTF8.GetString(frag.ToArray());
                    frag.SetLength(0);
                    if(text=="ping"){WriteFrame(ssl,1,Utf8("pong"));continue;}
                    HandleWsMessage(s.Id,text);
                }
            }
            catch(WsHandshakeException ex){
                Log(s.Id+" WS handshake: "+ex.Message);
            }
            catch(EndOfStreamException){
                lock(Sync){
                    var l=Live[s.Id];
                    l.Online=false;
                    l.TransportConnected=false;
                    l.LastError="";
                    l.UpdatedAt=DateTime.UtcNow;
                }
                Log(s.Id+" WS: yhteys sulkeutui, yhdistetään uudelleen");
            }
            catch(IOException ex){
                lock(Sync){
                    var l=Live[s.Id];
                    l.Online=false;
                    l.TransportConnected=false;
                    bool recent=l.LastSuccessAt!=DateTime.MinValue && l.LastSuccessAt>DateTime.UtcNow.AddSeconds(-60);
                    l.LastError=recent ? "" : "Yhteys katkesi. Yhdistetään uudelleen.";
                    l.UpdatedAt=DateTime.UtcNow;
                }
                Log(s.Id+" WS IO: "+ex.Message);
            }
            catch(Exception ex){
                lock(Sync){
                    var l=Live[s.Id];
                    l.Online=false;
                    l.TransportConnected=false;
                    l.LastError=ex.Message;
                    l.UpdatedAt=DateTime.UtcNow;
                }
                Log(s.Id+" WS: "+ex.Message);
            }
            finally{
                try{if(ssl!=null)ssl.Dispose();}catch{}
                try{if(tcp!=null)tcp.Close();}catch{}
            }
            if(!cancel.Cancelled)Thread.Sleep(2500);
        }
    }

    // [AUDIT][TIETOSUOJA] WebSocket-raakadataa ei välitetä selaimelle.
    static void HandleWsMessage(string sid,string text)
    {
        try{var m=Json.DeserializeObject(text) as Dictionary<string,object>; if(m==null)return; string type=GetString(m,"type"); object data; m.TryGetValue("data",out data); lock(Sync){var l=Live[sid];l.UpdatedAt=DateTime.UtcNow;l.LastSuccessAt=DateTime.UtcNow;
            if(type=="exams") l.Exams=SanitizeExams(data);
            else if(type=="stats") l.Stats=SanitizeStats(data);
            else if(type=="ytl-connection") l.Ytl=SanitizeObject(data,new[]{"status","connectionType"});
            else if(type=="answer-send-state") l.Send=SanitizeObject(data,new[]{"status","sentAt","schoolWithAnswers"});
            else if(type=="security-code") l.SecurityCode=SanitizeSecurityCode(data);
            else if(type=="servers") l.Servers=SanitizeServers(data);
        }}catch{}
    }
    static object SanitizeExams(object data){var a=data as object[]; var outa=new ArrayList(); if(a!=null)foreach(var x in a){var d=x as Dictionary<string,object>;if(d==null)continue;outa.Add(Pick(d,new[]{"examUuid","examTitle","hasStarted","startTime","type"}));}return outa;}
    // [AUDIT][PII] Kokelastiedoista säilytetään vain tilalukumäärät ja kokeen nimi.
    static object SanitizeStats(object data){var d=data as Dictionary<string,object>;if(d==null)return null;var o=new Dictionary<string,object>(); object v;if(d.TryGetValue("examStatus",out v))o["examStatus"]=v;if(d.TryGetValue("answerPaperCount",out v))o["answerPaperCount"]=v;if(d.TryGetValue("audioInSomeExam",out v))o["audioInSomeExam"]=v;var counts=new Dictionary<string,int>{{"total",0},{"waiting",0},{"inExam",0},{"error",0},{"finished",0}};var titles=new HashSet<string>(); if(d.TryGetValue("students",out v)){var arr=v as object[];if(arr!=null)foreach(var x in arr){var s=x as Dictionary<string,object>;if(s==null)continue;counts["total"]++;string st=GetString(s,"studentStatus");if(st=="exam-finished-by-student"||st=="exam-finished-by-supo")counts["finished"]++;else if(st=="waiting-for-auth-browser"||st=="waiting-for-auth"||st=="session-ended")counts["waiting"]++;else if(st=="waiting-for-start"||st=="in-exam-browser"||st=="in-exam")counts["inExam"]++;else counts["error"]++;string et=GetString(s,"examTitle");if(!string.IsNullOrWhiteSpace(et))titles.Add(et);}}o["studentCounts"]=counts;o["studentExamTitles"]=new List<string>(titles);return o;}
    // [AUDIT][SALASANASUOJA] KTP:n servers-viesti voi sisältää password-kentän.
    static object SanitizeServers(object data){var a=data as object[];var outa=new ArrayList();if(a!=null)foreach(var x in a){var d=x as Dictionary<string,object>;if(d!=null)outa.Add(Pick(d,new[]{"address","friendlyName","replication"}));}return outa;}
    static object SanitizeSecurityCode(object data){var d=data as Dictionary<string,object>;if(d==null)return null;return Pick(d,new[]{"keyCode","confirmationCode","securityCode"});}
    static object SanitizeObject(object data,string[] keys){var d=data as Dictionary<string,object>;return d==null?null:Pick(d,keys);} static Dictionary<string,object> Pick(Dictionary<string,object>d,string[]ks){var o=new Dictionary<string,object>();foreach(var k in ks)if(d.ContainsKey(k))o[k]=d[k];return o;}
}
