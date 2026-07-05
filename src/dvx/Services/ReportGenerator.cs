using System.Text;
using dvx.Models;
using dvx.Output;
using dvx.Utility;

namespace dvx.Services
{
    public class ReportGenerator
    {
        public string GenerateMarkdown(IEnumerable<PluginStepDefinition> definitions)
        {
            var md = new MarkdownBuilder();
            md.Heading(1, "Plugin Registration Report");

            var list = definitions.ToList();

            if (list.Any(d => !d.IsExecutionOrderExplicit))
            {
                md.Quote("**⚠️ Warning:** All steps marked with an asterix (*) beside the Order number have no explicit execution order set. Their relative execution order within the same stage and mode is non-deterministic.");
            }

            md.Heading(2, "By Entity then Message");
            RenderGroupedView(md, list, d => d.Entity, d => d.Message, "Entity", "Message");
            
            md.HorizontalLine();

            md.Heading(2, "By Message then Entity");
            RenderGroupedView(md, list, d => d.Message, d => d.Entity, "Message", "Entity");

            return md.ToString();
        }

        private void RenderGroupedView(
            MarkdownBuilder md, 
            List<PluginStepDefinition> definitions, 
            Func<PluginStepDefinition, string> primaryKey, 
            Func<PluginStepDefinition, string> secondaryKey,
            string primaryLabel,
            string secondaryLabel)
        {
            var groups = definitions
                .GroupBy(primaryKey)
                .OrderBy(g => g.Key);

            foreach (var primaryGroup in groups)
            {
                md.Heading(3, $"{primaryLabel}: {primaryGroup.Key}");

                var secondaryGroups = primaryGroup
                    .GroupBy(secondaryKey)
                    .OrderBy(g => g.Key);

                foreach (var secondaryGroup in secondaryGroups)
                {
                    md.Heading(4, $"{secondaryLabel}: {secondaryGroup.Key}");

                    RenderStepTable(md, secondaryGroup);
                }
            }
        }

        private void RenderStepTable(MarkdownBuilder md, IEnumerable<PluginStepDefinition> steps)
        {
            var orderedSteps = steps
                .OrderBy(d => d.Stage)
                .ThenBy(d => d.Mode)
                .ThenBy(d => d.ExecutionOrder)
                .ToList();

            var headers = new[] { "Stage", "Order", "Mode", "Plugin Type", "Description" };
            var rows = PrepareTableRows(orderedSteps);
            
            md.Table(headers, rows);
        }

        private List<string[]> PrepareTableRows(List<PluginStepDefinition> steps)
        {
            var rows = new List<string[]>();
            int? lastStage = null;
            int? lastMode = null;

            foreach (var step in steps)
            {
                if ((lastStage.HasValue && lastStage.Value != step.Stage) ||
                    (lastMode.HasValue && lastMode != step.Mode))
                {
                    rows.Add(["", "", "", "", ""]); // Add blank row between stages/steps
                }

                var order = step.IsExecutionOrderExplicit ? step.ExecutionOrder.ToString() : $"{step.ExecutionOrder}*";
                var mode = step.Mode == 1 ? "Async" : "Sync";

                rows.Add([
                    PluginStepDefinition.StageName(step.Stage),
                    order,
                    mode,
                    step.TypeFullName,
                    step.Description ?? "-"
                ]);

                lastStage = step.Stage;
                lastMode = step.Mode;
            }

            return rows;
        }

        public string GenerateCsv(IEnumerable<PluginStepDefinition> definitions)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Type,Entity,Message,Stage,Mode,ExecutionOrder,IsExplicit,Description");

            foreach (var def in definitions.OrderBy(d => d.TypeFullName).ThenBy(d => d.Entity).ThenBy(d => d.Message))
            {
                var row = new[]
                {
                    def.TypeFullName,
                    def.Entity,
                    def.Message,
                    PluginStepDefinition.StageName(def.Stage),
                    def.Mode == 1 ? "Async" : "Sync",
                    def.ExecutionOrder.ToString(),
                    def.IsExecutionOrderExplicit.ToString(),
                    def.Description ?? ""
                };
                sb.AppendLine(string.Join(",", row.Select(EscapeCsv)));
            }

            return sb.ToString();
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r"))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }
            return value;
        }
    }
}
