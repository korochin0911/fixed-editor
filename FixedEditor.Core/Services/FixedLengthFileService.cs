using System.Globalization;
using System.Text;
using FixedEditor.Core.Models;

namespace FixedEditor.Core.Services;

public sealed class FixedLengthFileService
{
    public FixedLengthFileService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public IReadOnlyList<FixedRecord> Read(string path, FixedFileSchema schema)
    {
        schema.Validate();
        var bytes = File.ReadAllBytes(path);
        var separator = ResolveRecordSeparator(schema.RecordSeparator);
        if (separator.Length == 0 && schema.TrimTrailingNewLine)
        {
            bytes = TrimLastNewLine(bytes);
        }

        var encoding = Encoding.GetEncoding(schema.Encoding);
        var records = new List<FixedRecord>();

        if (separator.Length > 0)
        {
            foreach (var recordBytes in SplitRecords(bytes, separator))
            {
                AddRecord(recordBytes);
            }
        }
        else
        {
            if (bytes.Length % schema.RecordLength != 0)
            {
                throw new InvalidDataException(
                    $"File size {bytes.Length:N0} bytes is not divisible by record length {schema.RecordLength:N0}.");
            }

            for (var offset = 0; offset < bytes.Length; offset += schema.RecordLength)
            {
                var recordBytes = bytes[offset..(offset + schema.RecordLength)];
                AddRecord(recordBytes);
            }
        }

        return records;

        void AddRecord(byte[] recordBytes)
        {
            if (recordBytes.Length == 0)
            {
                return;
            }

            if (recordBytes.Length != schema.RecordLength)
            {
                throw new InvalidDataException(
                    $"Record {records.Count + 1:N0} is {recordBytes.Length:N0} bytes, but schema requires {schema.RecordLength:N0} bytes.");
            }

            var values = schema.Fields
                .Select(field => ReadField(recordBytes, 0, field, encoding))
                .ToArray();
            records.Add(new FixedRecord(records.Count + 1, values));
        }
    }

    public void Write(string path, FixedFileSchema schema, IEnumerable<FixedRecord> records)
    {
        schema.Validate();
        var encoding = Encoding.GetEncoding(schema.Encoding);
        var separator = ResolveRecordSeparator(schema.RecordSeparator);
        using var stream = File.Create(path);
        var index = 0;

        foreach (var record in records)
        {
            if (index > 0 && separator.Length > 0)
            {
                stream.Write(separator);
            }

            var buffer = Enumerable.Repeat((byte)0x20, schema.RecordLength).ToArray();

            for (var i = 0; i < schema.Fields.Count; i++)
            {
                var field = schema.Fields[i];
                var value = i < record.Values.Length ? record.Values[i] : "";
                ValidateFieldValue(field, value, encoding);

                var bytes = encoding.GetBytes(FormatForWrite(field, value));
                var targetOffset = GetWriteOffset(field, bytes.Length);
                Array.Copy(bytes, 0, buffer, targetOffset, bytes.Length);
            }

            stream.Write(buffer);
            index++;
        }
    }

    public IReadOnlyList<string> ValidateRecord(FixedFileSchema schema, FixedRecord record)
    {
        var encoding = Encoding.GetEncoding(schema.Encoding);
        var errors = new List<string>();

        for (var i = 0; i < schema.Fields.Count; i++)
        {
            var field = schema.Fields[i];
            var value = i < record.Values.Length ? record.Values[i] : "";

            try
            {
                ValidateFieldValue(field, value, encoding);
            }
            catch (InvalidDataException ex)
            {
                errors.Add($"Row {record.LineNumber}, {field.DisplayName}: {ex.Message}");
            }
        }

        return errors;
    }

    private static string ReadField(byte[] bytes, int recordOffset, FixedFieldDefinition field, Encoding encoding)
    {
        var raw = encoding.GetString(bytes, recordOffset + field.Start - 1, field.Length);
        return raw.TrimEnd();
    }

    private static void ValidateFieldValue(FixedFieldDefinition field, string value, Encoding encoding)
    {
        if (field.Required && string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException("Value is required.");
        }

        if (field.Type == FixedFieldType.Number && !string.IsNullOrWhiteSpace(value) &&
            !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
        {
            throw new InvalidDataException("Value must be a number.");
        }

        if (field.Type == FixedFieldType.Date && !string.IsNullOrWhiteSpace(value))
        {
            var format = string.IsNullOrWhiteSpace(field.Format) ? "yyyyMMdd" : field.Format;
            if (!DateTime.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                throw new InvalidDataException($"Value must match date format '{format}'.");
            }
        }

        var bytes = encoding.GetBytes(FormatForWrite(field, value));
        if (bytes.Length > field.Length)
        {
            throw new InvalidDataException($"Value is {bytes.Length} bytes, but field length is {field.Length} bytes.");
        }
    }

    private static string FormatForWrite(FixedFieldDefinition field, string value)
    {
        var trimmed = value.Trim();
        return field.Type == FixedFieldType.Number ? trimmed : value;
    }

    private static int GetWriteOffset(FixedFieldDefinition field, int byteLength)
    {
        var baseOffset = field.Start - 1;
        return field.Type == FixedFieldType.Number
            ? baseOffset + field.Length - byteLength
            : baseOffset;
    }

    private static byte[] TrimLastNewLine(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[^2] == '\r' && bytes[^1] == '\n')
        {
            return bytes[..^2];
        }

        if (bytes.Length >= 1 && bytes[^1] == '\n')
        {
            return bytes[..^1];
        }

        return bytes;
    }

    private static byte[] ResolveRecordSeparator(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            null or "" or "none" => [],
            "lf" or "\\n" => [(byte)'\n'],
            "crlf" or "\\r\\n" => [(byte)'\r', (byte)'\n'],
            _ => Encoding.ASCII.GetBytes(value)
        };
    }

    private static IEnumerable<byte[]> SplitRecords(byte[] bytes, byte[] separator)
    {
        var start = 0;
        for (var i = 0; i <= bytes.Length - separator.Length; i++)
        {
            if (!MatchesAt(bytes, separator, i))
            {
                continue;
            }

            yield return bytes[start..i];
            start = i + separator.Length;
            i = start - 1;
        }

        yield return bytes[start..];
    }

    private static bool MatchesAt(byte[] bytes, byte[] separator, int index)
    {
        for (var i = 0; i < separator.Length; i++)
        {
            if (bytes[index + i] != separator[i])
            {
                return false;
            }
        }

        return true;
    }
}
