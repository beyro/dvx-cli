using System.Text;
using dvx.Models;

namespace dvx.Services
{
    public class ReportGenerator
    {
        public string GenerateMarkdown(IEnumerable<PluginStepDefinition> definitions)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Plugin Registration Report");
            sb.AppendLine();

            var list = definitions.ToList();

            sb.AppendLine("## By Message then Entity");
            sb.AppendLine();
            RenderGroupedView(sb, list, d => d.Message, d => d.Entity, "Message", "Entity");

            sb.AppendLine("---");
            sb.AppendLine();

            sb.AppendLine("## By Entity then Message");
            sb.AppendLine();
            RenderGroupedView(sb, list, d => d.Entity, d => d.Message, "Entity", "Message");

            return sb.ToString();
        }

        private void RenderGroupedView(
            StringBuilder sb, 
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
                sb.AppendLine($"### {primaryLabel}: {primaryGroup.Key}");
                sb.AppendLine();

                var secondaryGroups = primaryGroup
                    .GroupBy(secondaryKey)
                    .OrderBy(g => g.Key);

                foreach (var secondaryGroup in secondaryGroups)
                {
                    sb.AppendLine($"#### {secondaryLabel}: {secondaryGroup.Key}");
                    sb.AppendLine();

                    var steps = secondaryGroup
                        .OrderBy(d => d.Stage)
                        .ThenBy(d => d.ExecutionOrder)
                        .ToList();

                    var unordered = steps.Where(d => !d.IsExecutionOrderExplicit).ToList();
                    if (unordered.Any())
                    {
                        sb.AppendLine("> **⚠️ Warning:** The following plugins have no explicit execution order set. Their relative execution order within the same stage and mode is non-deterministic:");
                        foreach (var u in unordered.Select(d => d.TypeFullName).Distinct())
                        {
                            sb.AppendLine($"> - {u}");
                        }
                        sb.AppendLine();
                    }

                    sb.AppendLine("| Stage | Order | Mode | Plugin Type | Description |");
                    sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");

                    foreach (var step in steps)
                    {
                        var order = step.IsExecutionOrderExplicit ? step.ExecutionOrder.ToString() : "1*";
                        var mode = step.Mode == 1 ? "Async" : "Sync";
                        sb.AppendLine($"| {PluginStepDefinition.StageName(step.Stage)} | {order} | {mode} | {step.TypeFullName} | {step.Description ?? "-"} |");
                    }
                    sb.AppendLine();
                }
            }
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
