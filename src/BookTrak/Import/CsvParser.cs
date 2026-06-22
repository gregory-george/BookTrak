using System.Text;

namespace BookTrak.Import;

/// <summary>Minimal RFC4180 tokenizer — handles quoted fields (including embedded commas,
/// newlines, and escaped "" quotes), which a naive comma/line split can't. Goodreads/StoryGraph
/// exports routinely have commas and newlines inside quoted review/title fields.</summary>
internal static class CsvParser
{
    public static IReadOnlyList<IReadOnlyList<string>> Parse(string content)
    {
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var rowHasContent = false;

        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"' when field.Length == 0:
                    // Only treat a quote as an RFC4180 quote-opener at the very start of a
                    // field. Goodreads emits ISBN cells as a bare ="12345" (an Excel formula
                    // trick), which has an embedded, non-leading quote that must stay literal.
                    inQuotes = true;
                    rowHasContent = true;
                    break;
                case '"':
                    field.Append(c);
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    rowHasContent = true;
                    break;
                case '\r':
                    break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = [];
                    rowHasContent = false;
                    break;
                default:
                    field.Append(c);
                    rowHasContent = true;
                    break;
            }
        }

        if (field.Length > 0 || row.Count > 0 || rowHasContent)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }
}
