namespace FixedEditor.Core.Models;

public sealed class FixedRecord
{
    public FixedRecord(int lineNumber, IReadOnlyList<string> values)
    {
        LineNumber = lineNumber;
        Values = values.ToArray();
    }

    public int LineNumber { get; }
    public string[] Values { get; }
}
