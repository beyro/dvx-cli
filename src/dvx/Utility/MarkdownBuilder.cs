using System.Text;

namespace dvx.Utility
{
    public class MarkdownBuilder
    {
        private readonly StringBuilder _sb = new();

        public MarkdownBuilder Heading(int level, string text)
        {
            _sb.AppendLine($"{new string('#', level)} {text}");
            _sb.AppendLine();
            return this;
        }

        public MarkdownBuilder Paragraph(string text, int indent = 0)
        {
            if (indent > 0) _sb.Append(new string(' ', indent * 2));
            _sb.AppendLine(text);
            _sb.AppendLine();
            return this;
        }

        public MarkdownBuilder Quote(string text)
        {
            _sb.AppendLine($"> {text}");
            _sb.AppendLine();
            return this;
        }

        public MarkdownBuilder HorizontalLine()
        {
            _sb.AppendLine("---");
            _sb.AppendLine();
            return this;
        }

        public MarkdownBuilder Table(string[] headers, IEnumerable<string[]> rows)
        {
            var list = rows.ToList();
            var widths = new int[headers.Length];
            for (int i = 0; i < headers.Length; i++)
            {
                widths[i] = headers[i].Length;
                foreach (var row in list)
                {
                    if (row.Length > i)
                        widths[i] = Math.Max(widths[i], row[i].Length);
                }
            }

            RenderRow(headers, widths);

            _sb.Append("|");
            for (int i = 0; i < widths.Length; i++)
            {
                _sb.Append(" " + new string('-', widths[i]) + " |");
            }
            _sb.AppendLine();

            foreach (var row in list)
            {
                RenderRow(row, widths);
            }
            _sb.AppendLine();

            return this;
        }

        private void RenderRow(string[] cells, int[] widths)
        {
            _sb.Append("|");
            for (int i = 0; i < cells.Length; i++)
            {
                var val = i < cells.Length ? cells[i] : "";
                _sb.Append(" " + val.PadRight(widths[i]) + " |");
            }
            _sb.AppendLine();
        }

        public override string ToString() => _sb.ToString();
    }
}
