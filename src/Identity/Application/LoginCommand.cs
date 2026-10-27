namespace Chubb.Identity.Application;

public sealed record LoginCommand(string Email, string Password);
