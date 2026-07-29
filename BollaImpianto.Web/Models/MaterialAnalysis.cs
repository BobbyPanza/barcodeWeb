namespace BollaImpianto.Web.Models;

public sealed class MaterialAnalysisRequest
{
    public string? Bolla { get; set; }
    public int? IdNesting { get; set; }
    public string? CodiceNesting { get; set; }
    public string? Rintracciabilita { get; set; }
    public bool SoloDisponibili { get; set; }
    public List<int>? Lavorazioni { get; set; }
}

public sealed class MaterialAnomaly
{
    public string Code { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Detail { get; set; }
}

public sealed class AttributoRichiesto
{
    public string ColumnName { get; set; } = string.Empty;
    public string? JdeCode { get; set; }
    public string? Description { get; set; }
    public string? RequiredCode { get; set; }
    public decimal? RequiredWeight { get; set; }
    public string? FilterType { get; set; }
    public bool Obbligatorio { get; set; }
}

public sealed class MaterialeMinimo
{
    public int? IdNesting { get; set; }
    public string? Bolla { get; set; }
    public int NumLavorazioni { get; set; }
    public int NumParti { get; set; }
    public decimal? Spessore { get; set; }
    public int NumSpessoriDistinti { get; set; }
    public string? SpessoriRichiesti { get; set; }
    public string? QualitaAmmesse { get; set; }
    public decimal? FormaX { get; set; }
    public decimal? FormaY { get; set; }
    public List<MaterialQuality> Qualita { get; set; } = [];
    public List<AttributoRichiesto> Attributi { get; set; } = [];
}

public sealed class MaterialQuality
{
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public sealed class AvailableSheet
{
    public int TrackingId { get; set; }
    public string TrackingCode { get; set; } = string.Empty;
    public string? PartCode { get; set; }
    public string? PartDescription { get; set; }
    public decimal? Thickness { get; set; }
    public string? StoreCode { get; set; }
    public string? LocationCode { get; set; }
    public decimal? GiacenzaResidua { get; set; }
    public string? QualityCode { get; set; }
    public int Compatible { get; set; }
    public decimal? DimX { get; set; }
    public decimal? DimY { get; set; }
    public bool IsGhost { get; set; }
}

public sealed class SheetValidation
{
    public int TrackingId { get; set; }
    public string TrackingCode { get; set; } = string.Empty;
    public string? PartCode { get; set; }
    public string? PartDescription { get; set; }
    public decimal? Thickness { get; set; }
    public string? StoreCode { get; set; }
    public string? LocationCode { get; set; }
    public decimal? GiacenzaResidua { get; set; }
    public string? QualityCode { get; set; }
    public int Compatible { get; set; }
    public decimal? DimX { get; set; }
    public decimal? DimY { get; set; }
    public bool SpessoreOk { get; set; }
    public bool QualitaOk { get; set; }
    public bool AttributiOk { get; set; }
    public bool DimensioniOk { get; set; }
    public bool EsitoOk => SpessoreOk && QualitaOk && AttributiOk && DimensioniOk;
}

public sealed class AttributeFailure
{
    public string ColumnName { get; set; } = string.Empty;
    public string? JdeCode { get; set; }
    public string? Description { get; set; }
    public string? ValoreLamiera { get; set; }
    public decimal? PesoLamiera { get; set; }
    public string? ValoreRichiesto { get; set; }
    public decimal? PesoRichiesto { get; set; }
    public int Status { get; set; }
    public bool Obbligatorio { get; set; }
}

public sealed class MaterialAnalysisResult
{
    public List<MaterialAnomaly> Anomalie { get; set; } = [];
    public MaterialeMinimo? MaterialeMinimo { get; set; }
    public List<AvailableSheet> Lamiere { get; set; } = [];
    public SheetValidation? Validazione { get; set; }
    public List<AttributeFailure> CaratteristicheNonConformi { get; set; } = [];

    public bool HasError => Anomalie.Any(a =>
        string.Equals(a.Severity, "ERROR", StringComparison.OrdinalIgnoreCase));
}
