namespace ARMpulse.Core;

public static class Csv
{
    public static string Field(string? value)
    {
        value ??= "";
        // Process names are not trusted spreadsheet input. Prevent formula interpretation.
        var trimmed = value.TrimStart();
        if (trimmed.Length > 0 && "=+-@".Contains(trimmed[0]) || value.Length > 0 && value[0] is '\t' or '\r' or '\n') value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
