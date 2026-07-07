using System.Collections.Generic;
using System.IO;
using System.Text;

public static class CsvReader
{
    public static List<string[]> ReadAllRows(string filePath)
    {
        using var reader = new StreamReader(filePath, Encoding.UTF8);
        return ReadAllRows(reader);
    }

    public static List<string[]> ReadAllRowsFromText(string csvText)
    {
        using var reader = new StringReader(csvText);
        return ReadAllRows(reader);
    }

    private static List<string[]> ReadAllRows(TextReader reader)
    {
        var rows = new List<string[]>();
        while (true)
        {
            string[] row = ParseNextRow(reader);
            if (row == null)
                break;
            rows.Add(row);
        }

        return rows;
    }

    private static string[] ParseNextRow(TextReader reader)
    {
        var fields = new List<string>();
        var builder = new StringBuilder();
        bool inQuotes = false;

        while (true)
        {
            int raw = reader.Read();
            if (raw == -1)
            {
                if (builder.Length == 0 && fields.Count == 0)
                    return null;
                fields.Add(builder.ToString());
                break;
            }

            char c = (char)raw;
            if (inQuotes)
            {
                if (c == '"')
                {
                    int peek = reader.Peek();
                    if (peek == '"')
                    {
                        builder.Append('"');
                        reader.Read();
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    builder.Append(c);
                }

                continue;
            }

            if (c == '"')
            {
                inQuotes = true;
                continue;
            }

            if (c == ',')
            {
                fields.Add(builder.ToString());
                builder.Clear();
                continue;
            }

            if (c == '\r')
                continue;

            if (c == '\n')
            {
                fields.Add(builder.ToString());
                break;
            }

            builder.Append(c);
        }

        return fields.ToArray();
    }
}
