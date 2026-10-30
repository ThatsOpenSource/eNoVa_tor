using Microsoft.EntityFrameworkCore;

namespace Lragrz.Enova.Server.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(EnovaDbContext db, bool seedAll = true, CancellationToken ct = default)
    {
        // EnsureCreated only creates the DB if it does not exist.
        // If the schema is outdated (missing organisations table), drop and recreate.
        var needsRecreate = false;
        try
        {
            await db.Database.EnsureCreatedAsync(ct).ConfigureAwait(false);
            // Probe for the new table
            _ = await db.Organisations.AnyAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var msg = ex.ToString();
            if (msg.Contains("organisations") || msg.Contains("42P01"))
                needsRecreate = true;
            else
                throw;
        }

        if (needsRecreate)
        {
            Console.WriteLine("[Enova.Server] Schema outdated – dropping and recreating database...");
            await db.Database.EnsureDeletedAsync(ct).ConfigureAwait(false);
            await db.Database.EnsureCreatedAsync(ct).ConfigureAwait(false);
        }

        const string defaultPassword = "ChangeMe!2026";
        var now = DateTime.UtcNow;

        var org = await db.Organisations.FirstOrDefaultAsync(o => o.Name == "LRA-GRZ", ct)
            .ConfigureAwait(false);
        if (org is null)
        {
            org = new OrganisationEntity
            {
                Id = Guid.NewGuid(),
                Name = "LRA-GRZ",
                DefaultAuthority = "DE-LRAGRZ-01",
                DefaultSender = "LRA-GRZ",
                DefaultUploadEnabled = true,
                CreatedAt = now
            };
            db.Organisations.Add(org);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        var agency = await db.Agencies.FirstOrDefaultAsync(a => a.OrganisationId == org.Id && a.Name == "EGOV", ct)
            .ConfigureAwait(false);
        if (agency is null)
        {
            agency = new AgencyEntity
            {
                Id = Guid.NewGuid(),
                OrganisationId = org.Id,
                Name = "EGOV",
                AuthorityIdentifier = "DE-LRAGRZ-01",
                SenderName = "LRA-GRZ",
                CryptoProvider = "MOCK_FALLBACK",
                DmsType = "none",
                LocalOutputDirectory = "%USERPROFILE%\\Documents\\eNoVa_tor",
                OutputAntworten = "antworten",
                OutputAblage = "ablage",
                CreatedAt = now
            };
            db.Agencies.Add(agency);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        // Manager is organisation-level (no agency)
        await EnsureUserAsync(db, "admin", "Organisationsverwalter", "manager", defaultPassword, now, org.Id, null, ct)
            .ConfigureAwait(false);

        if (seedAll)
        {
            await EnsureUserAsync(db, "egov-admin", "EGOV Administrator", "admin", defaultPassword, now, org.Id, agency.Id, ct)
                .ConfigureAwait(false);
            await EnsureUserAsync(db, "editor1", "Sachbearbeiter Antonia", "editor", defaultPassword, now, org.Id, agency.Id, ct)
                .ConfigureAwait(false);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Ensure processed_cases exists even on older databases
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS processed_cases (
                id uuid PRIMARY KEY,
                case_id varchar(128) NOT NULL UNIQUE,
                signature_hash varchar(128),
                specifics_hash varchar(128),
                processed_by varchar(128),
                processed_at timestamp
            )
            """, ct).ConfigureAwait(false);

        // Clear stale sessions on startup
        await db.Users.ExecuteUpdateAsync(s => s.SetProperty(u => u.IsSessionActive, false), ct)
            .ConfigureAwait(false);

        Console.WriteLine("[eNoVa_tor] Seed ready (org LRA-GRZ, agency EGOV, admin=manager).");
    }

    private static async Task EnsureUserAsync(
        EnovaDbContext db,
        string username,
        string displayName,
        string role,
        string password,
        DateTime now,
        Guid organisationId,
        Guid? agencyId,
        CancellationToken ct)
    {
        var existing = await db.Users.FirstOrDefaultAsync(u => u.Username == username, ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            db.Users.Add(new UserEntity
            {
                Id = Guid.NewGuid(),
                Username = username,
                DisplayName = displayName,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                Role = role,
                OrganisationId = organisationId,
                AgencyId = agencyId,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            });
            return;
        }

        existing.DisplayName = displayName;
        existing.Role = role;
        existing.OrganisationId = organisationId;
        existing.AgencyId = agencyId;
        existing.IsActive = true;
        existing.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
        existing.UpdatedAt = now;
    }
}

