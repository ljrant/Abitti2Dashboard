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
    static void LoadPasswords(){try{if(!File.Exists(SettingsFile))return;byte[] enc=File.ReadAllBytes(SettingsFile),raw=ProtectedData.Unprotect(enc,null,DataProtectionScope.CurrentUser);var d=Json.Deserialize<Dictionary<string,string>>(Encoding.UTF8.GetString(raw));foreach(var kv in d)if(Servers.ContainsKey(kv.Key)&&!string.IsNullOrWhiteSpace(kv.Value))Servers[kv.Key].Password=kv.Value;}catch(Exception ex){Log("Asetusten luku: "+ex.Message);}}
    // [AUDIT][SALASANAT] Selväkielisiä salasanoja ei kirjoiteta levylle tässä polussa.
    static void SavePasswords(){var d=new Dictionary<string,string>();lock(Sync)foreach(var kv in Servers)d[kv.Key]=kv.Value.Password;byte[] raw=Utf8(Json.Serialize(d)),enc=ProtectedData.Protect(raw,null,DataProtectionScope.CurrentUser);File.WriteAllBytes(SettingsFile,enc);}

    static Dictionary<string,object> ReadJson(HttpListenerContext c){string s=Encoding.UTF8.GetString(ReadAll(c.Request.InputStream)); if(string.IsNullOrWhiteSpace(s))return new Dictionary<string,object>();return Json.DeserializeObject(s) as Dictionary<string,object> ?? new Dictionary<string,object>();}
    static string GetString(Dictionary<string,object>d,string k){object v;return d!=null&&d.TryGetValue(k,out v)&&v!=null?Convert.ToString(v):"";}
    static byte[] ReadAll(Stream s){using(var ms=new MemoryStream()){s.CopyTo(ms);return ms.ToArray();}} static byte[] Utf8(string s){return Encoding.UTF8.GetBytes(s??"");} static byte[] JsonBytes(object o){return Utf8(Json.Serialize(o));}
    static byte[] Multipart(string field,string filename,byte[] payload,out string contentType){string b="----AbittiHallinta"+Guid.NewGuid().ToString("N");contentType="multipart/form-data; boundary="+b;using(var ms=new MemoryStream()){byte[] h=Utf8("--"+b+"\r\nContent-Disposition: form-data; name=\""+field+"\"; filename=\""+filename.Replace("\"","_")+"\"\r\nContent-Type: application/octet-stream\r\n\r\n");ms.Write(h,0,h.Length);ms.Write(payload,0,payload.Length);byte[] t=Utf8("\r\n--"+b+"--\r\n");ms.Write(t,0,t.Length);return ms.ToArray();}}
    static void SendJson(HttpListenerContext c,int code,object o){Send(c,code,"application/json; charset=utf-8",JsonBytes(o));} static void Send(HttpListenerContext c,int code,string ct,byte[] b){c.Response.StatusCode=code;c.Response.ContentType=ct;c.Response.ContentLength64=b.Length;c.Response.OutputStream.Write(b,0,b.Length);c.Response.Close();}
    static byte[] RandomBytes(int n){var b=new byte[n];using(var r=RandomNumberGenerator.Create())r.GetBytes(b);return b;}
    static string ReadHttpHeader(Stream s){var ms=new MemoryStream();int state=0;while(ms.Length<32768){int x=s.ReadByte();if(x<0)throw new EndOfStreamException();ms.WriteByte((byte)x);state=(state==0&&x==13)?1:(state==1&&x==10)?2:(state==2&&x==13)?3:(state==3&&x==10)?4:0;if(state==4)return Encoding.ASCII.GetString(ms.ToArray());}throw new Exception("HTTP header too large");}
    static WsFrame ReadFrame(Stream s){int b0=s.ReadByte(),b1=s.ReadByte();if(b0<0||b1<0)throw new EndOfStreamException();bool fin=(b0&128)!=0;int op=b0&15;bool masked=(b1&128)!=0;ulong len=(ulong)(b1&127);if(len==126){len=(ulong)((ReadByte(s)<<8)|ReadByte(s));}else if(len==127){len=0;for(int i=0;i<8;i++)len=(len<<8)|(byte)ReadByte(s);}if(len>16777216)throw new Exception("WebSocket message too large");byte[] mask=masked?ReadExact(s,4):null,p=ReadExact(s,(int)len);if(masked)for(int i=0;i<p.Length;i++)p[i]^=mask[i%4];return new WsFrame{Fin=fin,Opcode=op,Payload=p};}
    static int ReadByte(Stream s){int x=s.ReadByte();if(x<0)throw new EndOfStreamException();return x;} static byte[] ReadExact(Stream s,int n){var b=new byte[n];int o=0;while(o<n){int x=s.Read(b,o,n-o);if(x<=0)throw new EndOfStreamException();o+=x;}return b;}
    static void WriteFrame(Stream s,int opcode,byte[] payload){if(payload==null)payload=new byte[0];using(var ms=new MemoryStream()){ms.WriteByte((byte)(0x80|(opcode&15)));ulong len=(ulong)payload.Length;if(len<=125)ms.WriteByte((byte)(0x80|(byte)len));else if(len<=65535){ms.WriteByte(0xFE);ms.WriteByte((byte)(len>>8));ms.WriteByte((byte)len);}else{ms.WriteByte(0xFF);for(int i=7;i>=0;i--)ms.WriteByte((byte)(len>>(8*i)));}byte[] mask=RandomBytes(4);ms.Write(mask,0,4);for(int i=0;i<payload.Length;i++)ms.WriteByte((byte)(payload[i]^mask[i%4]));byte[] all=ms.ToArray();s.Write(all,0,all.Length);s.Flush();}}
    static void Log(string s){try{File.AppendAllText(LogFile,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+" "+s+Environment.NewLine);}catch{}}

    class ServerCfg{public string Id,Host,Password;public ServerCfg Clone(){return new ServerCfg{Id=Id,Host=Host,Password=Password};}}
    class UpResult{public int Status;public string ContentType;public byte[] Body;}
    class WsFrame{public bool Fin;public int Opcode;public byte[] Payload;}
    class CancellationTokenSourceEx{volatile bool x;public bool Cancelled{get{return x;}}public void Cancel(){x=true;}}
    class WsHandshakeException : Exception {
        public bool AuthenticationFailure;
        public WsHandshakeException(string message,bool authenticationFailure) : base(message) { AuthenticationFailure=authenticationFailure; }
    }
    class LiveState{
        public bool Online;
        public bool TransportConnected;
        public bool Authenticated;
        public string AuthStatus="Ei yritetty";
        public object Exams=new ArrayList(),Stats,Ytl,Send,Servers=new ArrayList(),SecurityCode;
        public string LastError="";
        public DateTime UpdatedAt=DateTime.UtcNow;
        public DateTime LastSuccessAt=DateTime.MinValue;
        public object ToPublic(){
            bool stableOnline=Authenticated && (Online || (LastSuccessAt!=DateTime.MinValue && LastSuccessAt>DateTime.UtcNow.AddSeconds(-15)));
            return new{
                online=stableOnline,
                rawOnline=Online,
                transportConnected=TransportConnected,
                authenticated=Authenticated,
                authStatus=AuthStatus,
                exams=Exams,
                stats=Stats,
                ytl=Ytl,
                send=Send,
                servers=Servers,
                securityCode=SecurityCode,
                lastError=LastError,
                updatedAt=UpdatedAt.ToString("o"),
                lastSuccessAt=LastSuccessAt==DateTime.MinValue?null:LastSuccessAt.ToString("o")
            };
        }
    }
}
