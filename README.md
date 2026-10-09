# Abitti2Dashboard

Windows-työkalu usean Abitti2:n KTP-palvelimen hallintaan samasta näkymästä.

Ohjelma näyttää palvelinten yhteyden ja kirjautumisen tilan, kokeet, vastausten määrän ja muut keskeiset tilat. Hallinnasta voi avata valvojan näkymät, tuoda ja käynnistää kokeita, lähettää koesuorituksia sekä avata opiskelijalle selkeän kirjautumisohjeen.

> **Huom.** Tämä on epävirallinen työkalu. Testaa se omassa ympäristössä ennen käyttöä varsinaisessa koetilanteessa.

## Ensikäyttö

1. Lataa projektisivulta **ohjelmapaketti** ja pura se paikalliseen kansioon.
2. Tee mahdolliset koulukohtaiset muutokset **ennen** EXE:n rakentamista. Katso [Koulukohtaiset muutokset](https://ljrant.github.io/Abitti2Dashboard/customointi.html).
3. Käynnistä `Rakenna_ja_kaynnista.cmd`.
4. Skripti rakentaa `Abitti2Dashboard.exe`:n ja käynnistää sen.
5. Jatkossa EXE:n voi käynnistää suoraan.
6. Tarkista **Yhteydet**-välilehdeltä, löytyvätkö KTP-palvelimet ja onnistuuko kirjautuminen.

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

Kaikki tavalliset koulukohtaiset oletukset ovat tiedostossa:

```
src/Program.Core.cs
```

Etsi:

```
[KOULU][ASETUKSET]
```

Siellä ovat:

- `KtpDomainSuffix` – KTP-palvelinten osoitteen loppuosa
- `StudentServerSuffix` – opiskelijalle näytettävä palvelinosoite
- `ReservationUrl` – koulun palvelinvarauslinkki
- `PasswordFileUrl` – koulun linkki ajantasaisiin valvoja-salasanoihin
- `StudentWifi` – koeverkon nimi
- `StudentUsername` – opiskelijan Windows-käyttäjätunnus
- `StudentPassword` – opiskelijan Windows-salasana
- `DefaultDecryptPassword` – Tuo koe -ikkunaan esitäytettävä purkukoodi

Oletussalasanat löytyvät samasta tiedostosta kohdasta:

```
[KOULU][OLETUSSALASANAT]
```

**Älä julkaise oikeita valvoja- tai opiskelijasalasanoja public-repositoryyn.** Jos haluat ne omaan EXE-versioosi oletusarvoiksi, tee muutokset paikallisessa kopiossa ennen kääntämistä.

Käytön aikana **Yhteydet**-välilehdellä tallennetut valvoja-salasanat ohittavat lähdekoodin oletusarvot. Ne tallennetaan Windows-käyttäjäkohtaisesti DPAPI-suojattuina:

```
%LOCALAPPDATA%\Abitti2Dashboard\settings.dat
```

Loki:

```
%LOCALAPPDATA%\Abitti2Dashboard\Abitti2Dashboard.log
```

## Tuo koe

Työnkulku on tarkoituksella lähellä tavallista valvojan näkymää:

1. Valitse palvelimelta **Tuo koe**.
2. Tarkista vahvistusikkunasta oikea KTP-palvelin ja varoitus.
3. Valitse `.mex` tai `.zip`.
4. Dashboard odottaa, että KTP ottaa tiedoston vastaan.
5. Vasta tämän jälkeen näytetään purkukoodi-ikkuna.
6. Tarkista esitäytetty purkukoodi ja valitse **Pura koe**.
7. Väärän purkukoodin voi korjata ilman tiedoston uutta valintaa.

## Palvelinryhmät

Ohjelma vain näyttää KTP:n ilmoittaman palvelinryhmän. Ryhmät muodostetaan koepalvelimelta tai Abitin tavallisesta valvojan näkymästä.

## Testi-HETU

Generaattori tekee muodollisesti oikeanlaisen testihenkilötunnuksen koetilanteen testaamista varten esimerkiksi silloin, kun opiskelija ei muista omaa HETUaan.

Toimintoa ei ole vielä kattavasti testattu varsinaisessa koetilanteessa, eikä generaattori tarkista, osuuko luotu tunnus oikean henkilön tunnukseen.

## Repositoryn rakenne

```
src/Program.Core.cs        asetukset, käynnistys ja elinkaari
src/Program.Http.cs        paikallinen API ja KTP:n HTTP-kutsut
src/Program.WebSocket.cs   KTP-liveyhteydet ja datan suodatus
src/Program.Storage.cs     salasanat, apufunktiot ja WebSocket-kehykset
ui.html                    selaimessa näkyvä käyttöliittymä
Rakenna_ja_kaynnista.cmd   Windows-build ja käynnistys
docs/index.html            projektin GitHub Pages -etusivu
```

## Projektisivu

Projektin lyhyet asennus- ja käyttöohjeet julkaistaan GitHub Pagesissa:

**https://ljrant.github.io/Abitti2Dashboard/**
