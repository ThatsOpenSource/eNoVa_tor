# DocuWare-Integration – Installationsanleitung

## Überblick
Wenn eine Organisation mit `dms.type = docuware` konfiguriert ist, speichert der Server den PDF/A-3-Container in DocuWare statt (oder zusätzlich) im lokalen Upload-Ordner.

## Voraussetzungen
- DocuWare Platform (Cloud oder On-Premises) mit API-Zugriff
- Ein technischer Benutzer mit Rechten auf den Ziel-Aktenschrank
- .NET 10 Runtime auf dem Server

## Schritte
1. Erstellen Sie einen technischen Benutzer in DocuWare und notieren Sie den Organisationsnamen.
2. Ermitteln Sie die File Cabinet ID und die Dialog ID, die Sie verwenden möchten.
3. Kopieren Sie `config/docuware.properties` neben die Server-EXE (oder bearbeiten Sie die DMS-Einstellungen der Organisation in der Datenbank).
4. Füllen Sie aus:
   - `enova.dms.docuware.url`
   - `enova.dms.docuware.username`
   - `enova.dms.docuware.password`
   - `enova.dms.docuware.organization`
   - `enova.dms.docuware.filecabinet.id`
   - `enova.dms.docuware.dialog.id`
5. Setzen Sie `enova.dms.type=docuware` für die Organisation (über die Admin-UI oder direkt in der Tabelle `organisations`).
6. Starten Sie den Server neu.
7. Aktivieren Sie auf dem Client den Upload. Dokumente werden in die Warteschlange gestellt, und der Server versucht die Zustellung an DocuWare. Ist DocuWare nicht erreichbar, bleibt das Element für einen erneuten Versuch in der Warteschlange.

## Hinweise
- Anmeldedaten werden ausschließlich serverseitig gespeichert.
- Der Client sieht niemals die DocuWare-Verbindungsdetails.
- Ein lokaler Fallback bleibt verfügbar, wenn DocuWare nicht konfiguriert oder nicht erreichbar ist.