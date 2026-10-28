namespace Chubb.Claims.Api;

// Commands without fields still bind a body, so an unknown member is a 400 instead of being ignored.
public sealed record EmptyRequest;
