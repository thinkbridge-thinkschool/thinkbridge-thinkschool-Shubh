namespace QuotesApi.Modules.Identity.Domain;

public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;

    // Day 31 security hardening: the smallest possible role concept, added specifically so
    // the diagnostics endpoints (QuoteEndpoints.cs) can require something stronger than "any
    // authenticated user" without inventing a full identity/roles system. Every user is
    // "user" by default — nothing in the public API (register, login) can ever set this to
    // "admin"; only a direct, out-of-band database update can.
    public string Role { get; set; } = "user";
}