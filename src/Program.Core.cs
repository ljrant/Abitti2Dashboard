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

using System.Web.Script.Serialization;

internal static partial class Program
{
    const string Version = "16.7-exe";

    /*
     * [AUDIT][KOETUONTI][v16.7]
     * Tuo koe -toiminto säilyttää valitun KTP-tunnisteen koko ketjun ajan.
     * Ensin käyttäjä vahvistaa kohdepalvelimen ja varoituksen, sitten tiedosto
     * lähetetään vain kyseisen KTP:n /api/load-exam-rajapintaan. Purkukoodi
     * kysytään vasta onnistuneen load-exam-vastauksen jälkeen ja lähetetään
     * saman KTP:n /api/decrypt-exam-rajapintaan.
     *
     * [AUDIT][OPISKELIJAOHJE][v16.7]
     * Palvelinkortista avattu opiskelijaohje käyttää URL:n server-parametria.
     * Näkymä näyttää vain valitun palvelimen live-kokeet. Kokeen korostaminen
     * on vain paikallinen käyttöliittymävalinta eikä muuta KTP-palvelimen tilaa.
     */

    /*
     * [AUDIT][SALASANAN NÄYTTÖ][v16.7]
     * Hallintakortin päivittyminen ei saa katkaista käyttäjän aloittamaa salasanan näyttöä.
     * Näytetty salasana pidetään vain selainistunnon muistissa ja enintään 60 sekuntia
     * (PASSWORD_REVEAL_MS). Käyttäjä voi kopioida sen erillisellä Kopioi-painikkeella.
     * Salasanaa ei tallenneta HTML:ään, URL:iin tai lokiin.
     */
    /*
     * ==========================================================================
     * AUDITOINTI- JA YLLÄPITOMERKINNÄT (v16)
     * ==========================================================================
     * Tämän tiedoston on tarkoitus olla luettavissa sellaisenaan auditointia varten.
     * Keskeiset turvallisuus- ja koulukohtaiset muokkauskohdat on merkitty [AUDIT]-
     * ja [KOULU]-kommenteilla. Muutokset kannattaa tehdä lähdekoodiin ja rakentaa EXE
     * uudelleen Rakenna_ja_kaynnista.cmd:llä; käytön aikaiset valvoja-salasanojen vaihdot tehdään
     * Yhteydet-välilehdellä eikä niitä tarvitse leipoa uuteen EXE:en.
     *
     * [KOULU] Kaikki tavalliset koulukohtaiset asetukset on koottu heti Username-rivin
     *         jälkeen lohkoihin [KOULU][ASETUKSET] ja [KOULU][OLETUSSALASANAT].
     * [AUDIT] KTP Basic Auth -käyttäjätunnus: Username.
     * [AUDIT] Käytön aikana päivitetyt valvoja-salasanat tallennetaan DPAPI-salattuina
     *         vain nykyiselle Windows-käyttäjälle: %LOCALAPPDATA%\AbittiHallinta\settings.dat
     * [AUDIT] Loki: %LOCALAPPDATA%\AbittiHallinta\AbittiHallinta.log
     * [AUDIT] Opiskelijoiden nimet, HETU:t, UUID:t tai istuntotunnukset eivät siirry UI:lle;
     *         SanitizeStats() tuottaa vain lukumäärät ja kokeiden nimet.
     * [AUDIT] WebSocketin servers-viestin password-kenttää ei välitetä UI:lle;
     *         SanitizeServers() sallii vain address/friendlyName/replication.
     * [AUDIT] security-code -viestistä välitetään vain kirjautumiseen tarvittavat koodikentät.
     * [AUDIT] KTP:n paikallinen TLS-varmenne hyväksytään tarkoituksella suljetussa koeverkossa.
     *         Tämä näkyy Main()- ja WebSocket-koodissa. Jos koulun PKI mahdollistaa normaalin
     *         varmennusketjun, poista hyväksyntäohitus ennen käyttöönottoa.
     * [AUDIT] Yhteystila ja kirjautumistila näytetään erikseen. TCP/TLS-yhteys ei tarkoita
     *         onnistunutta valvoja-kirjautumista; Authenticated asetetaan true vasta HTTP 101
     *         WebSocket-kättelyn jälkeen. HTTP 401/403 näkyy käyttöliittymässä kirjautumisvirheenä.
     * [AUDIT] Selainvälilehtien Basic Auth -popupia ei automaattitäytetä eikä salasanoja upoteta URL:iin.
     *         Valvojan näkymiin kirjaudutaan selaimessa itse käyttäjällä valvoja ja KTP-kohtaisella salasanalla.
     *         Hallinta-kortin "Näytä salasana" näyttää nykyisen tallennetun salasanan vain käyttäjän pyynnöstä.
     *         Salasana piilotetaan automaattisesti yhden minuutin kuluttua. Näkyvyysaikaa voi muuttaa
     *         upotetun UI:n PASSWORD_REVEAL_MS-vakiosta. Salasanaa ei lisätä URL:iin eikä kirjoiteta lokiin.
     * [AUDIT] Palvelinryhmiä EI muodosteta, pureta tai tyhjennetä tästä sovelluksesta.
     *         Sovellus ainoastaan näyttää KTP:n WebSocketissa ilmoittaman ryhmäjäsenyyden.
     * [AUDIT] Prosessin elinkaari: sovellus käyttää vain localhost-porttia 8765. Uusi käynnistys
     *         sulkee aiemmat AbittiHallinta.exe-prosessit. Hallintasivun Sulje ohjelma -painike
     *         pysäyttää HTTP-kuuntelun ja WebSocket-työn. Jos selainvälilehti suljetaan ilman
     *         siistiä ilmoitusta, UI-heartbeat-watchdog sulkee EXE:n viimeistään noin 90 s kuluttua.
     * ==========================================================================
     */
    const string Username = "valvoja";
    // ==========================================================================
    // [KOULU][ASETUKSET] TAVALLISET LUKIOKOHTAISET MUUTOKSET TEHDÄÄN TÄSSÄ
    // ==========================================================================
    // KtpDomainSuffix     = KTP-palvelinten osoitteen loppuosa, esim. ktp1 + tämä arvo.
    // StudentServerSuffix = opiskelijalle näytettävä lyhyt palvelinmuoto, esim. ktp1.1068.
    // ReservationUrl      = linkki, josta opettajat varaavat KTP-palvelimia.
    // PasswordFileUrl     = intranet-linkki ajantasaiseen valvoja-salasanataulukkoon.
    // StudentWifi         = opiskelijoille näytettävä koeverkon nimi.
    // StudentUsername     = opiskelijoille näytettävä Windows-käyttäjätunnus.
    // StudentPassword     = opiskelijoille näytettävä Windows-salasana.
    // DefaultDecryptPassword = Tuo koe -ikkunaan esitäytettävä purkukoodi.
    const string KtpDomainSuffix = ".XXXX.koe.abitti.net";
    const string StudentServerSuffix = ".XXXX";
    const string ReservationUrl = "https://example.invalid/palvelinvaraus";
    const string PasswordFileUrl = "https://example.invalid/valvojan-salasanat";
    const string StudentWifi = "VAIHDA_KOEVERKON_NIMI";
    const string StudentUsername = @".\Abitti";
    const string StudentPassword = "VAIHDA_OPISKELIJASALASANA";
    const string DefaultDecryptPassword = "VAIHDA_PURKUKOODI";

    // [KOULU][OLETUSSALASANAT] Uuden asennuksen lähtöarvot.
    // Yhteydet-välilehdellä myöhemmin tallennetut salasanat ohittavat nämä arvot
    // kyseisen Windows-käyttäjän koneella. KTP5-KTP10 voivat olla tyhjiä.
    static readonly string[] DefaultPasswords = {
        "", "", "", "", "", "", "", "", "", ""
    };
    // ==========================================================================
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };
    static readonly object Sync = new object();
    static readonly Dictionary<string, ServerCfg> Servers = new Dictionary<string, ServerCfg>(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, LiveState> Live = new Dictionary<string, LiveState>(StringComparer.OrdinalIgnoreCase);
    static readonly List<Thread> WsThreads = new List<Thread>();
    // KTP1-KTP4 connect automatically. KTP5-KTP10 are enabled manually from the Connections tab.
    static readonly HashSet<string> LiveEnabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    static CancellationTokenSourceEx WsCancel = new CancellationTokenSourceEx();
    static HttpListener Listener;
    static int Port = 8765;
    static string SettingsDir;
    static string SettingsFile;
    static string LogFile;
    static DateTime StartedAt = DateTime.UtcNow;
    static volatile bool ShuttingDown = false;
    static readonly object UiSync = new object();
    static readonly Dictionary<string,DateTime> UiSessions = new Dictionary<string,DateTime>(StringComparer.OrdinalIgnoreCase);
    static bool UiWasSeen = false;
    static readonly string Html = BuildHtml();

    static string JsEscape(string value)
    {
        if(value==null) return "";
        return value.Replace("\\","\\\\").Replace("'","\\'").Replace("\r","\\r").Replace("\n","\\n");
    }

    static string BuildHtml()
    {
        // [AUDIT][UI] Käyttöliittymä kootaan build-skriptissä ja upotetaan EXE:en resurssina.
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        using (var s = asm.GetManifestResourceStream("AbittiHallinta.ui.html"))
        using (var r = new StreamReader(s, Encoding.UTF8))
        {
            string h = r.ReadToEnd();
            return h
                .Replace("__RESERVATION_URL__", JsEscape(ReservationUrl))
                .Replace("__PASSWORD_FILE_URL__", JsEscape(PasswordFileUrl))
                .Replace("__STUDENT_WIFI__", JsEscape(StudentWifi))
                .Replace("__STUDENT_USERNAME__", JsEscape(StudentUsername))
                .Replace("__STUDENT_PASSWORD__", JsEscape(StudentPassword))
                .Replace("__STUDENT_SERVER_SUFFIX__", JsEscape(StudentServerSuffix))
                .Replace("__KTP_DOMAIN_SUFFIX__", JsEscape(KtpDomainSuffix))
                .Replace("__DEFAULT_DECRYPT_PASSWORD__", JsEscape(DefaultDecryptPassword));
        }
    }

    [STAThread]
    static void Main()
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
        SettingsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AbittiHallinta");
        Directory.CreateDirectory(SettingsDir);
        SettingsFile = Path.Combine(SettingsDir, "settings.dat");
        LogFile = Path.Combine(SettingsDir, "AbittiHallinta.log");
        InitServers(); LoadPasswords();
        CloseOlderInstances();
        Port=8765;
        if (!TryStartListener(Port)) { System.Windows.Forms.MessageBox.Show("Portti 8765 on edelleen käytössä. Sulje mahdollinen muu AbittiHallinta.exe tai porttia käyttävä ohjelma ja yritä uudelleen.", "AbittiHallinta"); return; }
        Log("Käynnistyi v"+Version+" portissa "+Port);
        RestartWs();
        Thread serverThread = new Thread(ServerLoop){IsBackground=true}; serverThread.Start();
        Thread uiWatchdog = new Thread(UiWatchdogLoop){IsBackground=true,Name="UI-watchdog"}; uiWatchdog.Start();
        try { Process.Start(new ProcessStartInfo("http://127.0.0.1:"+Port+"/"){UseShellExecute=true}); } catch {}
        while (!ShuttingDown && Listener != null && Listener.IsListening) Thread.Sleep(500);
    }

    static void CloseOlderInstances()
    {
        try {
            int me=Process.GetCurrentProcess().Id;
            foreach(var p in Process.GetProcessesByName("AbittiHallinta")) {
                try { if(p.Id!=me) { p.Kill(); p.WaitForExit(5000); } } catch {}
                try { p.Dispose(); } catch {}
            }
            Thread.Sleep(250);
        } catch {}
    }

    static void ShutdownProgram(string reason)
    {
        if(ShuttingDown) return;
        ShuttingDown=true;
        try { Log("Suljetaan: "+reason); } catch {}
        try { WsCancel.Cancel(); } catch {}
        try { if(Listener!=null) Listener.Stop(); } catch {}
        try { if(Listener!=null) Listener.Close(); } catch {}
        Listener=null;
    }

    static void UiWatchdogLoop()
    {
        while(!ShuttingDown) {
            Thread.Sleep(5000);
            if(!UiWasSeen) continue;
            DateTime now=DateTime.UtcNow; bool anyFresh=false;
            lock(UiSync) {
                var stale=new List<string>();
                foreach(var kv in UiSessions) { if((now-kv.Value).TotalSeconds<=90) anyFresh=true; else stale.Add(kv.Key); }
                foreach(var k in stale) UiSessions.Remove(k);
            }
            if(!anyFresh) { ShutdownProgram("hallintasivu ei enää ole auki"); break; }
        }
    }

    static void InitServers()
    {
        for(int i=1;i<=10;i++) {
            string id="ktp"+i;
            Servers[id]=new ServerCfg{Id=id,Host=id+KtpDomainSuffix,Password=DefaultPasswords[i-1]};
            Live[id]=new LiveState();
            if(i<=4) LiveEnabled.Add(id);
        }
    }
}
