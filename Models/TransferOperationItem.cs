namespace VcfEditor.Models;

public sealed record TransferOperationItem(
    string Name,
    string Direction,
    string Status,
    string? Message = null);
