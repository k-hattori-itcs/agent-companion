using System.Text.Json.Serialization;

namespace AgentCompanion.Models;

public record PetLookFrame
{
    [JsonPropertyName("rowIndex")]
    public int RowIndex { get; init; }

    [JsonPropertyName("columnIndex")]
    public int ColumnIndex { get; init; }
}

public record PetSpritesheetLayout
{
    [JsonPropertyName("columns")]
    public int Columns { get; init; }

    [JsonPropertyName("rows")]
    public int Rows { get; init; }

    [JsonPropertyName("cellWidth")]
    public int CellWidth { get; init; }

    [JsonPropertyName("cellHeight")]
    public int CellHeight { get; init; }

    [JsonPropertyName("lookDirectionCount")]
    public int LookDirectionCount { get; init; }

    [JsonPropertyName("neutralLookFrame")]
    public PetLookFrame? NeutralLookFrame { get; init; }
}

public record PetInfo
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Description { get; init; } = "";
    public string SpritesheetPath { get; init; } = "";
    public int? SpriteVersionNumber { get; init; }
    public PetSpritesheetLayout? SpritesheetLayout { get; init; }
    public IReadOnlyDictionary<string, double> AnimationScales { get; init; } = new Dictionary<string, double>();
    public string Directory { get; init; } = "";
}
