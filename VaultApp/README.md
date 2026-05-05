# KeyVault — Secure Password Manager

A .NET 8 MVC password vault with AES-256 encryption, dual-credential login, and entry sharing.

## Features

- **Register / Login / Logout** via ASP.NET Core Identity
- **Dual-factor unlock** — users need both an account password AND a separate encryption key to sign in
- **Encrypted vault entries** — each entry stores the password encrypted with AES-256-CBC + HMAC-SHA256 authentication
- **Per-entry random IVs** — each entry has a unique IV; encrypting the same password twice produces different ciphertext
- **PBKDF2 key derivation** — 600,000 iterations of PBKDF2-SHA256 for the key hash stored in the DB
- **Create / Edit / Delete** vault entries (site name, URL, username, password, notes)
- **Built-in password generator** using `crypto.getRandomValues`
- **Share entries** with other registered users
- **Session-only key storage** — the encryption key is held in server-side session memory and never written to the database

---

## Quick Start

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)

### Run

```bash
cd VaultApp
dotnet restore
dotnet run
```

The app will auto-run migrations on first start and create `vault.db` (SQLite).
Open **https://localhost:5001** (or the URL shown in the terminal).

---

## Project Structure

```
VaultApp/
├── Controllers/
│   ├── AccountController.cs    # Register, Login, Logout
│   ├── HomeController.cs       # Landing page
│   └── VaultController.cs      # Vault CRUD + Share
├── Data/
│   └── ApplicationDbContext.cs # EF Core DbContext
├── Migrations/                 # EF Core migration (pre-generated)
├── Models/
│   └── Models.cs               # ApplicationUser, VaultEntry, SharedEntry, ViewModels
├── Services/
│   ├── EncryptionService.cs    # AES-256-CBC + HMAC-SHA256, PBKDF2
│   └── VaultService.cs         # Vault business logic
├── Views/
│   ├── Account/                # Register, Login
│   ├── Vault/                  # Index, Create, Edit, Share
│   ├── Home/                   # Landing page
│   └── Shared/_Layout.cshtml
├── wwwroot/
│   ├── css/site.css            # Dark vault aesthetic
│   └── js/site.js              # Show/hide passwords, generator
├── Program.cs
└── appsettings.json
```

---

## Security Architecture

| Concern | Approach |
|---|---|
| Password storage | ASP.NET Identity (PBKDF2-SHA256) |
| Encryption key verification | PBKDF2-SHA256, 600k iterations, salted hash stored in DB |
| Vault encryption | AES-256-CBC with unique IV per entry |
| Ciphertext integrity | HMAC-SHA256 (Encrypt-then-MAC) |
| Key in memory | Session only — never written to DB or logs |
| Key derivation for vault | PBKDF2-SHA256 100k iterations from user's key |

### Notes on Sharing
Password sharing across different users is a hard cryptographic problem without a PKI (public-key infrastructure). The current implementation stores the shared copy encrypted with a key derived from `ownerKey + ":shared:" + recipientId`. The recipient sees the metadata (site name, username) but the password is marked as contact-owner to reveal. A production upgrade path would be to generate an RSA/EC keypair per user and encrypt the AES key with the recipient's public key.

---

## Development

To recreate migrations from scratch (if you modify models):

```bash
dotnet ef migrations add InitialCreate
dotnet ef database update
```

To use PostgreSQL instead of SQLite, replace the EF package and connection string.
