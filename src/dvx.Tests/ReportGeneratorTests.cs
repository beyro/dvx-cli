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

            md.ShouldContain("## View 1: Message then Entity");
            md.ShouldContain("## View 2: Entity then Message");
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
            md.ShouldContain("non-deterministic");
            md.ShouldContain("1*"); // My implementation uses 1* for implicit
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
                    Description = "Some desc"
                }
            };

            var gen = new ReportGenerator();
            var csv = gen.GenerateCsv(definitions);

            csv.ShouldContain("Type,Entity,Message,Stage,Mode,ExecutionOrder,IsExplicit,Description");
            csv.ShouldContain("MyPlugin,account,Create,PreOperation,Sync,5,True,Some desc");
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
