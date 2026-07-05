using dvx.Output;
using Shouldly;
using Xunit;

namespace dvx.Tests
{
    public class MarkdownBuilderTests
    {
        [Fact]
        public void BasicFormatting_Works()
        {
            var md = new MarkdownBuilder();
            md.Heading(1, "Title")
              .Paragraph("Hello world")
              .Quote("Warning")
              .HorizontalLine();

            var result = md.ToString();
            result.ShouldContain("# Title");
            result.ShouldContain("Hello world");
            result.ShouldContain("> Warning");
            result.ShouldContain("---");
        }
        
        [Fact]
        public void Paragraph_Indentation_Works()
        {
            var md = new MarkdownBuilder();
            md.Paragraph("Indented text", 1);
            
            md.ToString().ShouldBe($"  Indented text{Environment.NewLine}{Environment.NewLine}");
        }

        [Fact]
        public void Table_Alignment_Works()
        {
            var md = new MarkdownBuilder();
            var headers = new[] { "A", "Long Header" };
            var rows = new List<string[]> { new[] { "Short", "Val" } };

            md.Table(headers, rows);
            var result = md.ToString();

            result.ShouldContain("| A     | Long Header |");
            result.ShouldContain("| ----- | ----------- |");
            result.ShouldContain("| Short | Val         |");
        }
    }
}
