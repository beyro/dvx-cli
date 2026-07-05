using System.Text;

namespace dvx.Output
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

        public override string ToString() => _sb.ToString();
    }
}
