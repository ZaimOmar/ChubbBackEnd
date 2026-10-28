namespace Chubb.Reporting.Application;

public sealed record ExposureRowView(string Market, string Type, int ClaimCount, decimal ExposureUsd);
