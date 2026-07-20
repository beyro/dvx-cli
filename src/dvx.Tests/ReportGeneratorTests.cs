using dvx.Models;
using dvx.Services;
using Shouldly;
using Xunit;

namespace dvx.Tests
{
    public class ReportGeneratorTests
    {
        [Fact]
        public void GenerateMarkdown_ContainsBothViews()
        {
            var definitions = new List<PluginStepDefinition>
            {
                new() { TypeFullName = "P1", Entity = "account", Message = "Create", Stage = 20, ExecutionOrder = 1 },
                new() { TypeFullName = "P2", Entity = "contact", Message = "Update", Stage = 40, ExecutionOrder = 1 }
            };

            var gen = new ReportGenerator();
            var md = gen.GenerateMarkdown(definitions);

            md.ShouldContain("## By Message then Entity");
            md.ShouldContain("## By Entity then Message");
            md.ShouldContain("### Message: Create");
            md.ShouldContain("#### Entity: account");
            md.ShouldContain("### Entity: contact");
            md.ShouldContain("#### Message: Update");
        }

        [Fact]
        public void GenerateMarkdown_UnorderedPlugins_ShowsWarning()
        {
            var definitions = new List<PluginStepDefinition>
            {
                new() { TypeFullName = "P1", Entity = "account", Message = "Create", Stage = 20, ExecutionOrder = 1 },
                // Second one has default order 1, implicit
                new() { TypeFullName = "P2", Entity = "account", Message = "Create", Stage = 20 }
            };

            var gen = new ReportGenerator();
            var md = gen.GenerateMarkdown(definitions);

            md.ShouldContain("⚠️ Warning");
            md.ShouldContain("asterix (*) beside the Order number");
            md.ShouldContain("1*"); 
        }

        [Fact]
        public void GenerateMarkdown_PadsColumnsAndInsertsEmptyRows()
        {
            var definitions = new List<PluginStepDefinition>
            {
                new() { TypeFullName = "Short", Entity = "a", Message = "m", Stage = 10, ExecutionOrder = 1 },
                new() { TypeFullName = "VeryLongPluginNameThatShouldForcePadding", Entity = "a", Message = "m", Stage = 10, ExecutionOrder = 2 },
                new() { TypeFullName = "NextStage", Entity = "a", Message = "m", Stage = 20, ExecutionOrder = 1 }
            };

            var gen = new ReportGenerator();
            var md = gen.GenerateMarkdown(definitions);

            // Check padding
            md.ShouldContain("| PreValidation | 1     | Sync | Short                                    | -           |");
            md.ShouldContain("| PreValidation | 2     | Sync | VeryLongPluginNameThatShouldForcePadding | -           |");
            
            // Check empty row between Stage 10 and 20
            md.ShouldContain("|               |       |      |                                          |             |");
            md.ShouldContain("| PreOperation  | 1     | Sync | NextStage                                | -           |");
        }

        [Fact]
        public void GenerateMarkdown_SortsByStageThenModeThenOrder()
        {
            var definitions = new List<PluginStepDefinition>
            {
                new() { TypeFullName = "P2", Entity = "a", Message = "m", Stage = 40, Mode = 0, ExecutionOrder = 2 },
                new() { TypeFullName = "P3", Entity = "a", Message = "m", Stage = 40, Mode = 1, ExecutionOrder = 1 },
                new() { TypeFullName = "P1", Entity = "a", Message = "m", Stage = 40, Mode = 0, ExecutionOrder = 1 }
            };

            var gen = new ReportGenerator();
            var md = gen.GenerateMarkdown(definitions);

            // Should be P1 (Sync, 1), P2 (Sync, 2), P3 (Async, 1)
            var lines = md.Split('\n').Select(l => l.Trim()).ToList();
            var p1Index = lines.FindIndex(l => l.Contains("P1"));
            var p2Index = lines.FindIndex(l => l.Contains("P2"));
            var p3Index = lines.FindIndex(l => l.Contains("P3"));

            p1Index.ShouldBeLessThan(p2Index);
            p2Index.ShouldBeLessThan(p3Index);
        }

        [Fact]
        public void GenerateCsv_ContainsAllColumns()
        {
            var definitions = new List<PluginStepDefinition>
            {
                new() 
                { 
                    TypeFullName = "MyPlugin", 
                    Entity = "account", 
                    Message = "Create", 
                    Stage = 20, 
                    Mode = 0, 
                    ExecutionOrder = 5,
                    Description = "Some desc",
                    RunAsUser = Guid.Empty
                }
            };

            var gen = new ReportGenerator();
            var csv = gen.GenerateCsv(definitions);

            csv.ShouldContain("Type,Entity,Message,Stage,Mode,ExecutionOrder,IsExplicit,Description,RunsAs");
            csv.ShouldContain("MyPlugin,account,Create,PreOperation,Sync,5,True,Some desc,SYSTEM");
        }
        
        [Fact]
        public void EscapeCsv_HandlesSpecialCharacters()
        {
            var definitions = new List<PluginStepDefinition>
            {
                new() 
                { 
                    TypeFullName = "P", 
                    Entity = "e", 
                    Message = "m", 
                    Stage = 20, 
                    Description = "Comma, \"Quote\", New\nLine"
                }
            };

            var gen = new ReportGenerator();
            var csv = gen.GenerateCsv(definitions);

            csv.ShouldContain("\"Comma, \"\"Quote\"\", New\nLine\"");
        }
    }
}
