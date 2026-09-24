using System;

namespace FeeBilling.Domain.Entities;

public class Firm
{
    public int Id { get; set; }

    public string FirmCode { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Province { get; set; }

    /// <summary>Culture the firm's custodian files and statements use, e.g. "fr-CA".</summary>
    public string Culture { get; set; } = "en-CA";

    public bool IsActive { get; set; } = true;

    public DateTime CreatedOn { get; set; }
}
