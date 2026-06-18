using dvx.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace dvx.Services
{
    /// <summary>
    /// Writes [PluginStep] attributes onto the plugin classes in a project's source files,
    /// matching each <see cref="PluginStepDefinition"/> to its class by fully-qualified type name.
    /// Uses Roslyn to locate classes and detect existing attributes; edits are applied as targeted
    /// text insertions so the rest of each file keeps its original formatting.
    /// </summary>
    public class AttributeWriter(ILogger<AttributeWriter> logger)
    {
        private const string AttributesNamespace = "dvx.PluginAttributes";

        private readonly record struct Insertion(int Position, string Text, bool IsUsing);

        public AttributeWriteResult Write(
            string projectPath,
            IReadOnlyList<PluginStepDefinition> definitions,
            bool dryRun,
            bool verbose = false,
            IReadOnlyCollection<string>? customApiTypeNames = null)
        {
            var result     = new AttributeWriteResult();
            var projectDir  = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;

            // ── Phase A: parse every .cs file and index classes by full type name ──────
            var fileText   = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var fileRoot   = new Dictionary<string, CompilationUnitSyntax>(StringComparer.OrdinalIgnoreCase);
            var classIndex = new Dictionary<string, List<ClassDeclarationSyntax>>(StringComparer.Ordinal);

            foreach (var file in EnumerateSourceFiles(projectDir))
            {
                var text = File.ReadAllText(file);
                var root = CSharpSyntaxTree.ParseText(text, path: file).GetCompilationUnitRoot();
                fileText[file] = text;
                fileRoot[file] = root;

                foreach (var cls in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    var fullName = GetFullName(cls);
                    if (!classIndex.TryGetValue(fullName, out var list))
                        classIndex[fullName] = list = new List<ClassDeclarationSyntax>();
                    list.Add(cls);
                }
            }

            // ── Phase B: figure out the attribute insertions per file ──────────────────
            var edits = new Dictionary<string, List<Insertion>>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in definitions.GroupBy(d => d.TypeFullName, StringComparer.Ordinal))
            {
                var typeName = group.Key;
                if (!classIndex.TryGetValue(typeName, out var locations))
                {
                    result.UnmatchedTypes.Add(typeName);
                    continue;
                }

                var target = ChooseTarget(locations);
                var file   = target.SyntaxTree.FilePath;
                var text   = fileText[file];

                // existing (entity, message, stage) keys across all partial parts → idempotency
                var existing = locations
                    .SelectMany(ReadExistingStepKeys)
                    .ToHashSet();

                var attrs = new List<string>();
                foreach (var def in group)
                {
                    var key = (def.Entity.ToLowerInvariant(), def.Message.ToLowerInvariant(), def.Stage);
                    if (!existing.Add(key))
                    {
                        result.SkippedExisting++;
                        continue;
                    }
                    attrs.Add(RenderAttribute(def));
                    result.Added++;
                    result.Planned.Add(
                        $"{Rel(file, projectDir)}: {typeName} ← [PluginStep({Lit(def.Entity)}, {Lit(def.Message)}, {StageExpr(def.Stage)})]");
                }

                if (attrs.Count == 0)
                    continue;

                var nl     = NewLine(text);
                var indent = LineIndent(text, target.SpanStart);
                var insert = string.Concat(attrs.Select(a => $"[{a}]{nl}{indent}"));
                AddEdit(edits, file, new Insertion(target.SpanStart, insert, IsUsing: false));
            }

            // ── Phase B1: [CustomApi] markers for Custom API implementation classes ────
            foreach (var typeName in (customApiTypeNames ?? Array.Empty<string>())
                         .Distinct(StringComparer.Ordinal))
            {
                if (!classIndex.TryGetValue(typeName, out var locations))
                {
                    result.UnmatchedTypes.Add(typeName);
                    continue;
                }

                if (locations.Any(HasCustomApiAttribute))
                    continue; // already marked — idempotent

                var target = ChooseTarget(locations);
                var file   = target.SyntaxTree.FilePath;
                var text   = fileText[file];
                var nl     = NewLine(text);
                var indent = LineIndent(text, target.SpanStart);

                AddEdit(edits, file, new Insertion(target.SpanStart, $"[CustomApi]{nl}{indent}", IsUsing: false));
                result.CustomApisMarked++;
                result.Planned.Add($"{Rel(file, projectDir)}: {typeName} ← [CustomApi]");
            }

            // ── Phase B2: ensure the using directive in each touched file ──────────────
            foreach (var file in edits.Keys.ToList())
            {
                var root = fileRoot[file];
                if (HasAttributesUsing(root))
                    continue;
                var (pos, usingText) = UsingInsertion(root, fileText[file]);
                AddEdit(edits, file, new Insertion(pos, usingText, IsUsing: true));
            }

            // ── Phase C: apply insertions and write ────────────────────────────────────
            foreach (var (file, insertions) in edits)
            {
                var text    = fileText[file];
                var newText = text;
                // Apply right-to-left so earlier offsets stay valid; for a tie, the using goes first.
                foreach (var ins in insertions.OrderByDescending(i => i.Position).ThenBy(i => i.IsUsing ? 1 : 0))
                    newText = newText.Insert(ins.Position, ins.Text);

                if (newText == text)
                    continue;

                if (!dryRun)
                    File.WriteAllText(file, newText);

                result.FilesChanged.Add(Rel(file, projectDir));

                if (verbose)
                    logger.LogInformation("{Verb} {File}", dryRun ? "Would edit" : "Edited", Rel(file, projectDir));
            }

            return result;
        }

        // ── Source enumeration ─────────────────────────────────────────────────────

        private static IEnumerable<string> EnumerateSourceFiles(string projectDir) =>
            Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !IsGenerated(projectDir, f));

        private static bool IsGenerated(string projectDir, string file)
        {
            var rel = Path.GetRelativePath(projectDir, file);
            var segments = rel.Split('/', '\\');
            return segments.Any(s =>
                s.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("obj", StringComparison.OrdinalIgnoreCase));
        }

        // ── Type-name resolution ───────────────────────────────────────────────────

        /// <summary>Builds the reflection-style full name: <c>Namespace.Outer+Inner</c>.</summary>
        internal static string GetFullName(ClassDeclarationSyntax cls)
        {
            var typeNames = new List<string>();
            var ns = "";

            for (SyntaxNode? node = cls; node is not null; node = node.Parent)
            {
                switch (node)
                {
                    case ClassDeclarationSyntax c:  typeNames.Insert(0, c.Identifier.Text); break;
                    case StructDeclarationSyntax s: typeNames.Insert(0, s.Identifier.Text); break;
                    case RecordDeclarationSyntax r: typeNames.Insert(0, r.Identifier.Text); break;
                    case BaseNamespaceDeclarationSyntax n:
                        var name = n.Name.ToString();
                        ns = ns.Length > 0 ? $"{name}.{ns}" : name;
                        break;
                }
            }

            var typePath = string.Join("+", typeNames);
            return ns.Length > 0 ? $"{ns}.{typePath}" : typePath;
        }

        private static ClassDeclarationSyntax ChooseTarget(List<ClassDeclarationSyntax> locations)
            => locations.FirstOrDefault(ImplementsIPlugin) ?? locations[0];

        private static bool ImplementsIPlugin(ClassDeclarationSyntax cls)
            => cls.BaseList?.Types.Any(t =>
            {
                var n = t.Type.ToString();
                return n == "IPlugin" || n.EndsWith(".IPlugin", StringComparison.Ordinal);
            }) ?? false;

        // ── Existing-attribute detection (idempotency) ─────────────────────────────

        private static IEnumerable<(string, string, int)> ReadExistingStepKeys(ClassDeclarationSyntax cls)
        {
            foreach (var list in cls.AttributeLists)
                foreach (var attr in list.Attributes)
                {
                    if (!IsPluginStepName(attr.Name.ToString()))
                        continue;
                    var key = ReadStepKey(attr);
                    if (key is { } k)
                        yield return k;
                }
        }

        private static bool IsPluginStepName(string name) =>
            name is "PluginStep" or "PluginStepAttribute" ||
            name.EndsWith(".PluginStep", StringComparison.Ordinal) ||
            name.EndsWith(".PluginStepAttribute", StringComparison.Ordinal);

        private static bool HasCustomApiAttribute(ClassDeclarationSyntax cls)
            => cls.AttributeLists
                  .SelectMany(l => l.Attributes)
                  .Any(a => IsCustomApiName(a.Name.ToString()));

        private static bool IsCustomApiName(string name) =>
            name is "CustomApi" or "CustomApiAttribute" ||
            name.EndsWith(".CustomApi", StringComparison.Ordinal) ||
            name.EndsWith(".CustomApiAttribute", StringComparison.Ordinal);

        private static (string entity, string message, int stage)? ReadStepKey(AttributeSyntax attr)
        {
            string? entity = null, message = null;
            int? stage = null;
            var positional = 0;

            foreach (var arg in attr.ArgumentList?.Arguments ?? default)
            {
                var named = arg.NameEquals?.Name.Identifier.Text ?? arg.NameColon?.Name.Identifier.Text;
                if (named is null)
                {
                    switch (positional++)
                    {
                        case 0: entity  = StringValue(arg.Expression); break;
                        case 1: message = StringValue(arg.Expression); break;
                        case 2: stage   = StageValue(arg.Expression);  break;
                    }
                }
                else
                {
                    switch (named)
                    {
                        case "Entity":  entity  = StringValue(arg.Expression); break;
                        case "Message": message = StringValue(arg.Expression); break;
                        case "Stage":   stage   = StageValue(arg.Expression);  break;
                    }
                }
            }

            if (entity is null || message is null || stage is null)
                return null;
            return (entity.ToLowerInvariant(), message.ToLowerInvariant(), stage.Value);
        }

        private static string? StringValue(ExpressionSyntax expr) =>
            expr is LiteralExpressionSyntax { Token.Value: string s } ? s : null;

        private static int? StageValue(ExpressionSyntax expr) => expr switch
        {
            MemberAccessExpressionSyntax m => StageFromName(m.Name.Identifier.Text),
            IdentifierNameSyntax i         => StageFromName(i.Identifier.Text),
            LiteralExpressionSyntax { Token.Value: int n } => n,
            _ => null
        };

        private static int? StageFromName(string name) => name switch
        {
            "PreValidation" => 10,
            "PreOperation"  => 20,
            "PostOperation" => 40,
            _ => null
        };

        // ── Attribute rendering ────────────────────────────────────────────────────

        internal static string RenderAttribute(PluginStepDefinition def)
        {
            var args = new List<string>
            {
                Lit(def.Entity),
                Lit(def.Message),
                StageExpr(def.Stage),
            };

            if (def.ExecutionOrder != 1) args.Add($"ExecutionOrder = {def.ExecutionOrder}");
            if (def.Mode == 1)           args.Add("Async = true");
            if (!string.IsNullOrEmpty(def.Description)) args.Add($"Description = {Lit(def.Description!)}");

            if (def.RunAsUser is { } ru)
                args.Add(ru == Guid.Empty ? "RunAsSystem = true" : $"RunAsUser = {Lit(ru.ToString())}");

            if (def.FilteringAttributes.Length > 0)
                args.Add($"FilteringAttributes = {Arr(def.FilteringAttributes)}");

            if (!string.IsNullOrEmpty(def.Configuration)) args.Add($"Configuration = {Lit(def.Configuration!)}");

            var pre = def.Images.FirstOrDefault(i => i.ImageType == ImageType.Pre);
            if (pre is not null)
            {
                args.Add("UsePreImage = true");
                if (pre.Attributes.Length > 0) args.Add($"PreImageAttributes = {Arr(pre.Attributes)}");
                if (pre.Alias != "PreImage")   args.Add($"PreImageAlias = {Lit(pre.Alias)}");
            }

            var post = def.Images.FirstOrDefault(i => i.ImageType == ImageType.Post);
            if (post is not null)
            {
                args.Add("UsePostImage = true");
                if (post.Attributes.Length > 0) args.Add($"PostImageAttributes = {Arr(post.Attributes)}");
                if (post.Alias != "PostImage")  args.Add($"PostImageAlias = {Lit(post.Alias)}");
            }

            return $"PluginStep({string.Join(", ", args)})";
        }

        private static string StageExpr(int stage) => stage switch
        {
            10 => "Stage.PreValidation",
            20 => "Stage.PreOperation",
            40 => "Stage.PostOperation",
            _  => $"(Stage){stage}"
        };

        private static string Lit(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

        private static string Arr(string[] values) =>
            $"[{string.Join(", ", values.Select(Lit))}]";

        // ── using directive handling ───────────────────────────────────────────────

        private static bool HasAttributesUsing(CompilationUnitSyntax root) =>
            root.DescendantNodes().OfType<UsingDirectiveSyntax>()
                .Any(u => u.Alias is null && u.Name?.ToString() == AttributesNamespace);

        private static (int Position, string Text) UsingInsertion(CompilationUnitSyntax root, string text)
        {
            var nl = NewLine(text);
            if (root.Usings.Count > 0)
                return (root.Usings.Last().FullSpan.End, $"using {AttributesNamespace};{nl}");

            var first = root.Members.FirstOrDefault();
            var pos   = first?.SpanStart ?? 0;
            return (pos, $"using {AttributesNamespace};{nl}{nl}");
        }

        // ── Text helpers ───────────────────────────────────────────────────────────

        private static void AddEdit(Dictionary<string, List<Insertion>> edits, string file, Insertion ins)
        {
            if (!edits.TryGetValue(file, out var list))
                edits[file] = list = new List<Insertion>();
            list.Add(ins);
        }

        private static string NewLine(string text) => text.Contains("\r\n") ? "\r\n" : "\n";

        private static string LineIndent(string text, int pos)
        {
            var lineStart = pos;
            while (lineStart > 0 && text[lineStart - 1] != '\n')
                lineStart--;

            var i = lineStart;
            while (i < text.Length && (text[i] == ' ' || text[i] == '\t'))
                i++;

            return text.Substring(lineStart, i - lineStart);
        }

        private static string Rel(string file, string projectDir) =>
            Path.GetRelativePath(projectDir, file).Replace('\\', '/');
    }
}
