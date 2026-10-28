namespace Chubb.Reporting.Application;

public sealed record ExposureView(string Currency, DateOnly FxAsOf, decimal TotalUsd, IReadOnlyList<ExposureRowView> Rows);
