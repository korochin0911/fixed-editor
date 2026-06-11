namespace FixedEditor.Core.Models;

public sealed class FixedFieldDefinition
{
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    public int Start { get; set; }
    public int Length { get; set; }
    public FixedFieldType Type { get; set; } = FixedFieldType.String;
    public string? Format { get; set; }
    public bool Required { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Label) ? Name : Label;
}
