# DocuWare Integration – Installation Guide

## Overview
When an organisation is configured with `dms.type = docuware`, the server stores the PDF/A-3 container in DocuWare instead of (or in addition to) the local upload folder.

## Prerequisites
- DocuWare Platform (Cloud or on-premises) with API access
- A technical user with rights to the target file cabinet
- .NET 10 runtime on the server

## Steps
1. Create a technical user in DocuWare and note the organisation name.
2. Identify the File Cabinet ID and Dialog ID you want to use.
3. Copy `config/docuware.properties` next to the server EXE (or edit the organisation's DMS settings in the database).
4. Fill in:
   - `enova.dms.docuware.url`
   - `enova.dms.docuware.username`
   - `enova.dms.docuware.password`
   - `enova.dms.docuware.organization`
   - `enova.dms.docuware.filecabinet.id`
   - `enova.dms.docuware.dialog.id`
5. Set `enova.dms.type=docuware` for the organisation (via the admin UI or directly in the `organisations` table).
6. Restart the server.
7. On the client, enable upload. Documents are queued and the server attempts delivery to DocuWare. If DocuWare is unreachable, the item stays in the queue for retry.

## Notes
- Credentials are stored server-side only.
- The client never sees DocuWare connection details.
- Local fallback remains available when DocuWare is not configured or is down.
