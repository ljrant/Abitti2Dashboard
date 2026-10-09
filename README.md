# Abitti2Dashboard / AbittiHallinta

Windows-työkalu usean Abitti2:n KTP-palvelimen hallintaan samasta näkymästä.

Ohjelma näyttää palvelinten yhteyden ja kirjautumisen tilan, kokeet, vastausten määrän ja muut keskeiset tilat. Hallinnasta voi avata valvojan näkymät, tuoda ja käynnistää kokeita, lähettää koesuorituksia sekä avata opiskelijalle selkeän kirjautumisohjeen.

> **Huom.** Tämä on epävirallinen työkalu. Testaa se omassa ympäristössä ennen käyttöä varsinaisessa koetilanteessa.

## Ensikäyttö

1. Lataa repository ZIP:nä GitHubista ja pura se paikalliseen kansioon.
2. Avaa halutessasi lähdekoodi tarkistettavaksi. Keskeiset turvallisuus- ja muokkauskohdat on merkitty `[AUDIT]`- ja `[KOULU]`-kommenteilla.
3. Tee tarvittavat koulukohtaiset muutokset ennen kääntämistä. Tarkat kohdat löytyvät alempaa.
4. Käynnistä `Rakenna_ja_kaynnista.cmd`.
5. Skripti rakentaa `AbittiHallinta.exe`:n ja käynnistää sen.
6. Jatkossa EXE:n voi käynnistää suoraan.
7. Tarkista **Yhteydet**-välilehdeltä, löytyvätkö KTP-palvelimet ja onnistuuko kirjautuminen.

KTP1–KTP4 yritetään yhdistää automaattisesti. KTP5–KTP10 otetaan käyttöön tarvittaessa **Yhdistä**-painikkeella.

## Mitä ohjelma tekee

- tukee enintään 10 KTP-palvelinta
- näyttää erikseen verkkoyhteyden ja valvoja-kirjautumisen onnistumisen
- näyttää palvelimella olevat kokeet; useat kokeet avattavana listana
- tuo koetiedoston valitulle palvelimelle ja kysyy purkukoodin vasta tiedoston vastaanoton jälkeen
- käynnistää kokeen ja lähettää koesuoritukset käyttäjän vahvistuksella
- avaa valvojan näkymän
- näyttää hetkellisesti tallennetun valvoja-salasanan manuaalista selainkirjautumista varten
- näyttää opiskelijalle kirjautumisohjeen juuri valitulle palvelimelle
- näyttää kyseisen palvelimen kokeet ohjesivulla ja mahdollistaa oikean kokeen korostamisen
- sisältää testi-HETU-generaattorin tilanteisiin, joissa opiskelija ei muista omaa henkilötunnustaan
- näyttää KTP:n ilmoittaman palvelinryhmän, mutta ei muodosta tai muuta palvelinryhmiä

Valvojan näkymän **ensimmäinen selainkirjautuminen tehdään käsin** käyttäjällä `valvoja` ja kyseisen KTP:n salasanalla. Hallinta-näkymän **Näytä salasana / Kopioi** on tarkoitettu tätä varten.

## Koulukohtaiset asetukset

Avaa `AbittiHallinta.Core.cs` ja etsi:

```
[KOULU][ASETUKSET]
```

Samasta kohdasta löytyvät:

- `KtpDomainSuffix` – KTP-palvelinten osoitteen loppuosa
- `StudentServerSuffix` – opiskelijalle näytettävä palvelinosoite
- `ReservationUrl` – koulun palvelinvarauslinkki
- `PasswordFileUrl` – koulun linkki ajantasaisiin valvoja-salasanoihin
- `StudentWifi` – koeverkon nimi
- `StudentUsername` – opiskelijan Windows-käyttäjätunnus
- `StudentPassword` – opiskelijan Windows-salasana

Oletussalasanat löytyvät samasta tiedostosta kohdasta:

```
[KOULU][OLETUSSALASANAT]
```

GitHub-versiossa oikeat koulukohtaiset salasanat ja sisäiset linkit on **tarkoituksella poistettu**. Älä julkaise niitä public-repositoryyn. Jos haluat tietyt salasanat uuden EXE:n oletusarvoiksi, lisää ne omaan paikalliseen lähdekoodiisi ennen kääntämistä.

Käytön aikana Yhteydet-välilehdellä tallennetut salasanat ohittavat lähdekoodin oletusarvot. Ne tallennetaan Windows-käyttäjäkohtaisesti DPAPI-suojattuina:

```
%LOCALAPPDATA%\AbittiHallinta\settings.dat
```

Loki:

```
%LOCALAPPDATA%\AbittiHallinta\AbittiHallinta.log
```

## Tuo koe

Työnkulku on tarkoituksella vaiheittainen:

1. Valitse palvelimelta **Tuo koe**.
2. Tarkista vahvistuksesta oikea KTP-palvelin.
3. Valitse `.mex` tai `.zip`.
4. Dashboard odottaa, että KTP ottaa tiedoston vastaan.
5. Vasta tämän jälkeen näytetään purkukoodi-ikkuna.
6. Tarkista/esitäytä purkukoodi ja valitse **Pura koe**.
7. Väärän purkukoodin voi korjata ilman tiedoston uutta valintaa.

## Palvelinryhmät

Ohjelma vain näyttää KTP:n ilmoittaman palvelinryhmän. Ryhmät muodostetaan koepalvelimelta tai Abitin tavallisesta valvojan näkymästä.

## Testi-HETU

Generaattori tekee muodollisesti oikeanlaisen testihenkilötunnuksen koetilanteen testaamista varten esimerkiksi silloin, kun opiskelija ei muista omaa HETUaan. Toimintoa ei ole vielä kattavasti testattu varsinaisessa koetilanteessa, eikä generaattori tarkista, osuuko luotu tunnus oikean henkilön tunnukseen.

## Auditointi

Lähdekoodissa on `[AUDIT]`-merkintöjä mm. seuraavista:

- TLS ja KTP-yhteydet
- salasanojen tallennus
- WebSocket-datan suodatus
- henkilötietojen minimointi
- koetuonnin kohdepalvelimen säilyminen
- prosessin ja localhost-portin elinkaari

Ohjelma käyttää paikallista porttia `8765`.

## Julkisen GitHub-version rakenne

- `AbittiHallinta.Core.cs`, `AbittiHallinta.Network.cs`, ... – C#-lähdekoodi
- `ui/` – käyttöliittymän lähdefragmentit
- `Rakenna_ja_kaynnista.cmd` – kokoaa UI:n, kääntää EXE:n ja käynnistää sen
- `docs/` – projektin GitHub Pages -sivu

