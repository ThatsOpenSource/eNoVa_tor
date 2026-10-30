using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Lragrz.Enova.Core.Config;
using Lragrz.Enova.Server.Data;
using Lragrz.Enova.Server.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
var builder = WebApplication.CreateBuilder(args);

// Force the port the client expects (config.properties / default http://localhost:5101)
builder.WebHost.UseUrls("http://localhost:5101");

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 50 * 1024 * 1024; // 50 MB
});

var enovaConfig = ConfigurationLoader.Load(fileName: "eNoVa_tor.server.properties");
builder.Services.AddSingleton(enovaConfig);
builder.Services.AddSingleton<SchemaCatalogService>();
builder.Services.AddSingleton<ReferenceDataService>();

// Simple in-memory rate limiting for login
builder.Services.AddSingleton<DocuWareService>();
builder.Services.AddSingleton<LoginRateLimiter>();
builder.Services.AddHostedService<UploadQueueProcessor>();

// Prefer env ENOVA_DB so you can override without editing files:
//   $env:ENOVA_DB = "Host=localhost;Port=55432;Database=enova;Username=enova;Password=1234"
var connectionString =
    Environment.GetEnvironmentVariable("ENOVA_DB")
    ?? builder.Configuration.GetConnectionString("EnovaDb")
    ?? "Host=localhost;Port=55432;Database=enova;Username=enova;Password=1234";

var masked = string.Join(';', connectionString.Split(';')
    .Select(p => p.Trim().StartsWith("Password=", StringComparison.OrdinalIgnoreCase) ? "Password=***" : p.Trim()));
Console.WriteLine($"[eNoVa_tor] DB: {masked}");
AuditLog.Info("eNoVa_tor [v0.1 // 2026] server started");

builder.Services.AddDbContext<EnovaDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Enova EGOV Server",
        Version = "0.0.1",
        Description = "Intranet configuration, auth, schema distribution & document API"
    });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "JWT Bearer token",
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSection["Key"] ?? "EGOV-Enova-Dev-Key-Change-In-Production-32chars!";
var jwtIssuer = jwtSection["Issuer"] ?? "eNoVa_tor";
var jwtAudience = jwtSection["Audience"] ?? "eNoVa_tor-client";
var jwtExpireHours = int.TryParse(jwtSection["ExpireHours"], out var h) ? h : 8;
var keyBytes = Encoding.UTF8.GetBytes(jwtKey);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(keyBytes)
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<EnovaDbContext>();
    await DbSeeder.SeedAsync(db, enovaConfig.SeedAll);

    // Resolve samples directory: prefer source tree, then output, then cwd
    string? samplesDir = null;
    var candidates = new[]
    {
        Path.Combine(Directory.GetCurrentDirectory(), "samples"),
        Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "samples"),
        Path.Combine(AppContext.BaseDirectory, "samples"),
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "samples")
    };
    foreach (var c in candidates)
    {
        var full = Path.GetFullPath(c);
        if (Directory.Exists(full))
        {
            samplesDir = full;
            break;
        }
    }
    if (samplesDir is not null)
    {
        Console.WriteLine($"[Enova.Server] Signing samples in {samplesDir}");
        SampleSigner.SignSamples(samplesDir);
    }
    else
    {
        Console.WriteLine("[Enova.Server] No samples directory found.");
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

// ---------- Auth ----------
app.MapPost("/api/auth/login", async (LoginRequest req, EnovaDbContext db, HttpContext http, LoginRateLimiter limiter) =>
{
    var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    if (!limiter.IsAllowed(ip))
    {
        AuditLog.Write("ERROR//RATE_LIMIT", "rate limited", req.Username);
        return Results.Json(new { error = "Zu viele Anmeldeversuche. Bitte später erneut versuchen." }, statusCode: 429);
    }

    var user = await db.Users
        .AsNoTracking()
        .FirstOrDefaultAsync(u => u.Username == req.Username && u.IsActive);

    if (user is null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
        return Results.Json(new { error = "Benutzername oder Passwort falsch." }, statusCode: 401);

    var claims = new[]
    {
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Name, user.Username),
        new Claim(ClaimTypes.Role, user.Role),
        new Claim("display_name", user.DisplayName)
    };

    var token = new JwtSecurityToken(
        issuer: jwtIssuer,
        audience: jwtAudience,
        claims: claims,
        expires: DateTime.UtcNow.AddHours(jwtExpireHours),
        signingCredentials: new SigningCredentials(
            new SymmetricSecurityKey(keyBytes),
            SecurityAlgorithms.HmacSha256));

    user.LastLoginAt = DateTime.UtcNow;
    user.IsSessionActive = true;
    await db.SaveChangesAsync();

    AuditLog.Write("AUTH//SUCCESS", $"login: {user.Username}", user.Username);

    return Results.Ok(new
    {
        token = new JwtSecurityTokenHandler().WriteToken(token),
        role = user.Role,
        displayName = user.DisplayName,
        userId = user.Id
    });
}).AllowAnonymous();

app.MapPost("/api/auth/logout", async (EnovaDbContext db, ClaimsPrincipal principal) =>
{
    var username = principal.Identity?.Name;
    if (string.IsNullOrEmpty(username)) return Results.Unauthorized();
    var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
    if (user is not null)
    {
        user.LastLogoutAt = DateTime.UtcNow;
        user.IsSessionActive = false;
        await db.SaveChangesAsync();
        AuditLog.Write("AUTH//LOGOUT", $"logout: {user.Username}", user.Username);
    }
    return Results.Ok();
}).RequireAuthorization();

// ---------- Reference data (caseworkers, decision mappings, codelists) ----------
app.MapGet("/api/caseworkers", (ReferenceDataService data) =>
    Results.Ok(data.GetCaseworkers())).RequireAuthorization();

app.MapGet("/api/decision-mappings", (ReferenceDataService data) =>
    Results.Ok(data.GetDecisionMappings())).RequireAuthorization();

app.MapGet("/api/codelists/{listId}", (string listId, ReferenceDataService data) =>
    Results.Ok(data.GetCodeList(listId))).RequireAuthorization();

// ---------- Account management (admin only) ----------
app.MapGet("/api/users", async (EnovaDbContext db, ClaimsPrincipal principal) =>
{
    if (principal.FindFirst(ClaimTypes.Role)?.Value is not ("admin" or "manager"))
        return Results.Forbid();

    var users = await db.Users.AsNoTracking()
        .Select(u => new { u.Id, u.Username, u.DisplayName, u.Role, u.IsActive, u.AgencyId, u.LastLoginAt, u.LastLogoutAt, u.IsSessionActive, u.CreatedAt })
        .ToListAsync();
    return Results.Ok(users);
}).RequireAuthorization();

app.MapPost("/api/users", async (CreateUserRequest req, EnovaDbContext db, ClaimsPrincipal principal) =>
{
    if (principal.FindFirst(ClaimTypes.Role)?.Value is not ("admin" or "manager"))
        return Results.Forbid();

    if (await db.Users.AnyAsync(u => u.Username == req.Username))
        return Results.Conflict(new { error = "Username already exists" });

    var user = new UserEntity
    {
        Id = Guid.NewGuid(),
        Username = req.Username,
        DisplayName = req.DisplayName,
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
        Role = req.Role is "admin" or "editor" ? req.Role : "editor",
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
    db.Users.Add(user);
    await db.SaveChangesAsync();
    return Results.Ok(new { user.Id, user.Username, user.DisplayName, user.Role });
}).RequireAuthorization();

app.MapPut("/api/users/{id:guid}", async (Guid id, UpdateUserRequest req, EnovaDbContext db, ClaimsPrincipal principal) =>
{
    if (principal.FindFirst(ClaimTypes.Role)?.Value is not ("admin" or "manager"))
        return Results.Forbid();

    var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
    if (user is null) return Results.NotFound();

    user.DisplayName = req.DisplayName ?? user.DisplayName;
    if (!string.IsNullOrEmpty(req.Role) && req.Role is "admin" or "editor")
        user.Role = req.Role;
    user.IsActive = req.IsActive ?? user.IsActive;
    if (!string.IsNullOrEmpty(req.Password))
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password);
    user.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(new { user.Id, user.Username, user.DisplayName, user.Role, user.IsActive });
}).RequireAuthorization();

app.MapDelete("/api/users/{id:guid}", async (Guid id, EnovaDbContext db, ClaimsPrincipal principal) =>
{
    if (principal.FindFirst(ClaimTypes.Role)?.Value is not ("admin" or "manager"))
        return Results.Forbid();

    var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
    if (user is null) return Results.NotFound();

    // Prevent deleting the last admin
    if (user.Role == "admin")
    {
        var adminCount = await db.Users.CountAsync(u => u.Role == "admin" && u.IsActive);
        if (adminCount <= 1)
            return Results.BadRequest(new { error = "Cannot delete the last active admin" });
    }

    db.Users.Remove(user);
    await db.SaveChangesAsync();
    return Results.Ok(new { deleted = id });
}).RequireAuthorization();
app.MapGet("/api/organisation", async (EnovaDbContext db, ClaimsPrincipal principal) =>
{
    if (principal.FindFirst(ClaimTypes.Role)?.Value != "manager")
        return Results.Forbid();
    var org = await db.Organisations.AsNoTracking().FirstOrDefaultAsync();
    if (org is null) return Results.NotFound();
    return Results.Ok(new { org.Id, org.Name, org.DefaultAuthority, org.DefaultSender, org.DefaultUploadEnabled });
}).RequireAuthorization();

app.MapPut("/api/organisation", async (UpdateOrgRequest req, EnovaDbContext db, ClaimsPrincipal principal) =>
{
    if (principal.FindFirst(ClaimTypes.Role)?.Value != "manager")
        return Results.Forbid();
    var org = await db.Organisations.FirstOrDefaultAsync();
    if (org is null) return Results.NotFound();

    if (req.DefaultAuthority is not null) org.DefaultAuthority = req.DefaultAuthority;
    if (req.DefaultSender is not null) org.DefaultSender = req.DefaultSender;
    if (req.DefaultUploadEnabled is not null) org.DefaultUploadEnabled = req.DefaultUploadEnabled.Value;

    // Apply to divisions that still use the previous defaults
    var agencies = await db.Agencies.ToListAsync();
    foreach (var a in agencies)
    {
        if (string.IsNullOrEmpty(a.AuthorityIdentifier) || a.AuthorityIdentifier == "DE-LRAGRZ-01")
            a.AuthorityIdentifier = org.DefaultAuthority ?? a.AuthorityIdentifier;
        if (string.IsNullOrEmpty(a.SenderName) || a.SenderName == "LRA-GRZ")
            a.SenderName = org.DefaultSender ?? a.SenderName;
    }

    await db.SaveChangesAsync();
    AuditLog.Config(principal.Identity?.Name ?? "?", "organisation defaults updated");
    return Results.Ok(new { org.Id, org.DefaultAuthority, org.DefaultSender, org.DefaultUploadEnabled });
}).RequireAuthorization();

app.MapGet("/api/config", async (EnovaDbContext db, ClaimsPrincipal principal, EnovaConfiguration cfg) =>
{
    var username = principal.Identity?.Name;
    var user = await db.Users.Include(u => u.Agency).AsNoTracking()
        .FirstOrDefaultAsync(u => u.Username == username);

    var org = await db.Organisations.AsNoTracking().FirstOrDefaultAsync();
    var agency = user?.Agency;
    return Results.Ok(new
    {
        CryptoProvider = agency?.CryptoProvider ?? cfg.CryptoProvider,
        VdaRestEndpoint = cfg.VdaRestEndpoint,
        AuthorityIdentifier = agency?.AuthorityIdentifier ?? org?.DefaultAuthority ?? cfg.AuthorityIdentifier,
        SenderName = agency?.SenderName ?? org?.DefaultSender ?? cfg.SenderName,
        UploadEnabled = org?.DefaultUploadEnabled ?? cfg.UploadEnabled,
        UploadApiEndpoint = cfg.UploadApiEndpoint,
        LocalOutputDirectory = agency?.LocalOutputDirectory,
        OutputAntworten = agency?.OutputAntworten,
        OutputAblage = agency?.OutputAblage,
        AgencyName = agency?.Name,
        DmsType = agency?.DmsType ?? "none"
    });
}).RequireAuthorization();

app.MapGet("/api/agencies", async (EnovaDbContext db, ClaimsPrincipal principal) =>
{
    if (principal.FindFirst(ClaimTypes.Role)?.Value is not ("manager" or "admin"))
        return Results.Forbid();

    var agencies = await db.Agencies.AsNoTracking()
        .Select(a => new { a.Id, a.Name, a.AuthorityIdentifier, a.SenderName, a.CryptoProvider, a.DmsType, a.LocalOutputDirectory, a.OutputAntworten, a.OutputAblage, a.DmsConfigJson })
        .ToListAsync();
    return Results.Ok(agencies);
}).RequireAuthorization();

app.MapPut("/api/agencies/{id:guid}", async (Guid id, UpdateAgencyRequest req, EnovaDbContext db, ClaimsPrincipal principal) =>
{
    var role = principal.FindFirst(ClaimTypes.Role)?.Value;
    if (role is not ("manager" or "admin"))
        return Results.Forbid();

    var agency = await db.Agencies.FirstOrDefaultAsync(a => a.Id == id);
    if (agency is null) return Results.NotFound();

    if (req.AuthorityIdentifier is not null) agency.AuthorityIdentifier = req.AuthorityIdentifier;
    if (req.SenderName is not null) agency.SenderName = req.SenderName;
    if (req.CryptoProvider is not null) agency.CryptoProvider = req.CryptoProvider;
    if (req.DmsType is not null) agency.DmsType = req.DmsType;
    if (req.LocalOutputDirectory is not null) agency.LocalOutputDirectory = req.LocalOutputDirectory;
    if (req.OutputAntworten is not null) agency.OutputAntworten = req.OutputAntworten;
    if (req.OutputAblage is not null) agency.OutputAblage = req.OutputAblage;
    if (req.DmsConfigJson is not null) agency.DmsConfigJson = req.DmsConfigJson;

    await db.SaveChangesAsync();
    return Results.Ok(new { agency.Id, agency.Name });
}).RequireAuthorization();

app.MapDelete("/api/agencies/{id:guid}", async (Guid id, EnovaDbContext db, ClaimsPrincipal principal) =>
{
    if (principal.FindFirst(ClaimTypes.Role)?.Value != "manager")
        return Results.Forbid();
    var agency = await db.Agencies.FirstOrDefaultAsync(a => a.Id == id);
    if (agency is null) return Results.NotFound();
    db.Agencies.Remove(agency);
    await db.SaveChangesAsync();
    return Results.Ok(new { deleted = id });
}).RequireAuthorization();

app.MapPost("/api/agencies", async (CreateAgencyRequest req, EnovaDbContext db, ClaimsPrincipal principal) =>
{
    if (principal.FindFirst(ClaimTypes.Role)?.Value != "manager")
        return Results.Forbid();

    var org = await db.Organisations.FirstOrDefaultAsync();
    if (org is null) return Results.BadRequest(new { error = "No organisation" });

    var agency = new AgencyEntity
    {
        Id = Guid.NewGuid(),
        OrganisationId = org.Id,
        Name = req.Name,
        AuthorityIdentifier = req.AuthorityIdentifier,
        SenderName = req.SenderName,
        CryptoProvider = req.CryptoProvider ?? "MOCK_FALLBACK",
        DmsType = req.DmsType ?? "none",
        LocalOutputDirectory = req.LocalOutputDirectory,
        OutputAntworten = req.OutputAntworten ?? "antworten",
        OutputAblage = req.OutputAblage ?? "ablage",
        CreatedAt = DateTime.UtcNow
    };
    db.Agencies.Add(agency);
    await db.SaveChangesAsync();
    return Results.Ok(new { agency.Id, agency.Name });
}).RequireAuthorization();

// ---------- Schema distribution ----------
app.MapGet("/api/schemas/manifest", (SchemaCatalogService catalog) =>
{
    var manifest = catalog.GetManifest();
    return Results.Ok(manifest);
}).AllowAnonymous();

app.MapGet("/api/schemas/files/{*relativePath}", (string relativePath, SchemaCatalogService catalog) =>
{
    var stream = catalog.OpenFile(relativePath);
    if (stream is null)
        return Results.NotFound(new { error = "Schema file not found", path = relativePath });

    var contentType = relativePath.EndsWith(".xsd", StringComparison.OrdinalIgnoreCase)
        || relativePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
        ? "application/xml"
        : relativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? "application/json"
            : "application/octet-stream";

    return Results.File(stream, contentType, Path.GetFileName(relativePath));
}).AllowAnonymous();

// ---------- Document upload ----------
app.MapGet("/api/cases/check", async (string caseId, string? sigHash, EnovaDbContext db) =>
{
    // Match by case ID or by fingerprint (signature + UVZ + date + Flurstück)
    var existing = await db.ProcessedCases.AsNoTracking()
        .FirstOrDefaultAsync(c => c.CaseId == caseId || (sigHash != null && c.SignatureHash == sigHash));
    if (existing is null)
        return Results.Ok(new { exists = false });

    var user = await db.Users.AsNoTracking().Include(u => u.Agency)
        .FirstOrDefaultAsync(u => u.Username == existing.ProcessedBy);
    var divLabel = user?.Agency?.Name ?? "";
    return Results.Ok(new
    {
        exists = true,
        processedBy = existing.ProcessedBy,
        processedAt = existing.ProcessedAt,
        divisionLabel = divLabel,
        sameSignature = sigHash is not null && existing.SignatureHash == sigHash
    });
}).RequireAuthorization();

app.MapPost("/api/cases/record", async (RecordCaseRequest req, EnovaDbContext db, ClaimsPrincipal principal) =>
{
    var existing = await db.ProcessedCases.FirstOrDefaultAsync(c => c.CaseId == req.CaseId);
    if (existing is not null)
        return Results.Conflict(new { error = "Vorgang bereits bearbeitet.", processedBy = existing.ProcessedBy, processedAt = existing.ProcessedAt });

    db.ProcessedCases.Add(new ProcessedCaseEntity
    {
        Id = Guid.NewGuid(),
        CaseId = req.CaseId,
        SignatureHash = req.SignatureHash,
        SpecificsHash = req.SpecificsHash,
        ProcessedBy = principal.Identity?.Name,
        ProcessedAt = DateTime.UtcNow
    });
    await db.SaveChangesAsync();
    var dbUser = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == principal.Identity!.Name);
    AuditLog.Write("FILE//GENERATION", $"case recorded: {req.CaseId}", principal.Identity?.Name, dbUser?.AgencyId?.ToString(), dbUser?.OrganisationId?.ToString());
    return Results.Ok(new { recorded = req.CaseId });
}).RequireAuthorization();

app.MapGet("/api/upload-queue", async (EnovaDbContext db, ClaimsPrincipal principal) =>
{
    var role = principal.FindFirst(ClaimTypes.Role)?.Value;
    if (role is not ("admin" or "manager" or "editor"))
        return Results.Forbid();

    var items = await db.UploadQueue.AsNoTracking()
        .OrderByDescending(q => q.CreatedAt)
        .Take(100)
        .Select(q => new { q.Id, q.FileName, q.Status, q.Attempts, q.LastError, q.CreatedAt })
        .ToListAsync();
    return Results.Ok(items);
}).RequireAuthorization();

app.MapPost("/api/documents", async (HttpRequest request, EnovaDbContext db, ClaimsPrincipal principal) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest(new { error = "multipart/form-data expected" });

    var username = principal.Identity?.Name ?? "-";
    var dbUser = await db.Users.AsNoTracking().Include(u => u.Agency)
        .FirstOrDefaultAsync(u => u.Username == username);
    var divId = dbUser?.AgencyId?.ToString() ?? "-";
    var orgId = dbUser?.OrganisationId?.ToString() ?? "-";

    var form = await request.ReadFormAsync();
    var files = form.Files;
    if (files.Count == 0)
        return Results.BadRequest(new { error = "No files supplied" });

    var uploadDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "eNoVa_tor",
        "Uploads");
    Directory.CreateDirectory(uploadDir);

    var stored = new List<object>();
    foreach (var file in files)
    {
        var safeName = Path.GetFileName(file.FileName);
        var target = Path.Combine(uploadDir, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{safeName}");
        await using var fs = File.Create(target);
        await file.CopyToAsync(fs);
        stored.Add(new { originalName = safeName, storedPath = target, size = file.Length });

        // Enqueue for DMS delivery (async, no duplicates by path)
        var exists = await db.UploadQueue.AnyAsync(q => q.FileName == safeName && q.Status != "failed");
        if (exists)
        {
            AuditLog.Action("system", $"duplicate rejected: {safeName}");
            stored.Add(new { file = safeName, status = "duplicate" });
            continue;
        }

        db.UploadQueue.Add(new UploadQueueEntity
        {
            Id = Guid.NewGuid(),
            FileName = safeName,
            LocalPath = target,
            Status = "on-server",
            Attempts = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        AuditLog.Write("FILE//UPLOAD", $"uploaded to server: {safeName}", username, divId, orgId);
    }

    await db.SaveChangesAsync();
    return Results.Ok(new { received = stored.Count, files = stored });
}).RequireAuthorization();

// ---------- Sign credential check ----------
app.MapPost("/api/sign/authorize", async (SignAuthorizeRequest req, EnovaDbContext db, ClaimsPrincipal user) =>
{
    var username = user.Identity?.Name;
    if (string.IsNullOrEmpty(username))
        return Results.Unauthorized();

    var dbUser = await db.Users.AsNoTracking()
        .FirstOrDefaultAsync(u => u.Username == username && u.IsActive);

    if (dbUser is null)
        return Results.Unauthorized();

    if (dbUser.Role is not ("editor" or "admin"))
        return Results.Forbid();

    if (!string.IsNullOrEmpty(req.SignPassword))
    {
        if (!BCrypt.Net.BCrypt.Verify(req.SignPassword, dbUser.PasswordHash))
            return Results.Unauthorized();
    }

    var ticketClaims = new[]
    {
        new Claim(ClaimTypes.Name, username),
        new Claim(ClaimTypes.Role, dbUser.Role),
        new Claim("sign_scope", "vda_production"),
        new Claim("authority", req.AuthorityIdentifier ?? "DE-MUSTERSTADT-08")
    };

    var ticket = new JwtSecurityToken(
        issuer: jwtIssuer,
        audience: "enova-sign",
        claims: ticketClaims,
        expires: DateTime.UtcNow.AddMinutes(5),
        signingCredentials: new SigningCredentials(
            new SymmetricSecurityKey(keyBytes),
            SecurityAlgorithms.HmacSha256));

    return Results.Ok(new
    {
        signTicket = new JwtSecurityTokenHandler().WriteToken(ticket),
        expiresInSeconds = 300,
        role = dbUser.Role
    });
}).RequireAuthorization();

// ---------- Health ----------
app.MapPost("/api/heartbeat", async (EnovaDbContext db, ClaimsPrincipal principal) =>
{
    var username = principal.Identity?.Name;
    if (string.IsNullOrEmpty(username)) return Results.Unauthorized();
    var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
    if (user is null) return Results.Unauthorized();
    user.IsSessionActive = true;
    user.LastLoginAt = DateTime.UtcNow;
    await db.SaveChangesAsync();
    AuditLog.Write("SESSION//HEARTBEAT", $"heartbeat: {username}", username, user.AgencyId?.ToString(), user.OrganisationId?.ToString());
    return Results.Ok(new { ok = true, user = username });
}).RequireAuthorization();

app.MapGet("/health", async (EnovaDbContext db) =>
{
    var canConnect = await db.Database.CanConnectAsync();
    return Results.Ok(new
    {
        status = canConnect ? "healthy" : "degraded",
        version = "0.0.1",
        database = canConnect ? "connected" : "unreachable"
    });
});

app.Run();


public record RecordCaseRequest(string CaseId, string? SignatureHash, string? SpecificsHash);
public record LoginRequest(string Username, string Password);
public record SignAuthorizeRequest(string? SignPassword, string? AuthorityIdentifier);
public record CreateUserRequest(string Username, string Password, string DisplayName, string Role);
public record UpdateOrgRequest(string? DefaultAuthority, string? DefaultSender, bool? DefaultUploadEnabled);
public record CreateAgencyRequest(
    string Name,
    string? AuthorityIdentifier,
    string? SenderName,
    string? CryptoProvider,
    string? DmsType,
    string? LocalOutputDirectory,
    string? OutputAntworten,
    string? OutputAblage);
public record UpdateAgencyRequest(
    string? AuthorityIdentifier,
    string? SenderName,
    string? CryptoProvider,
    string? DmsType,
    string? LocalOutputDirectory,
    string? OutputAntworten,
    string? OutputAblage,
    string? DmsConfigJson);
public record UpdateUserRequest(string? DisplayName, string? Role, string? Password, bool? IsActive);

