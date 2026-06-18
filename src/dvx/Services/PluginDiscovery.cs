using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using dvx.Models;
using dvx.Output;
using Microsoft.Extensions.Logging;

namespace dvx.Services
{
    public class PluginDiscovery(ILogger<PluginDiscovery> logger)
    {
        private const string PluginInterfaceFullName = "Microsoft.Xrm.Sdk.IPlugin";
        private const string PluginStepAttrFullName  = "dvx.PluginAttributes.PluginStepAttribute";
        private const string CustomApiAttrFullName   = "dvx.PluginAttributes.CustomApiAttribute";

        public List<PluginStepDefinition> Discover(string dllPath, bool verbose = false)
        {
            var pluginDir = Path.GetDirectoryName(dllPath)!;

            // MetadataLoadContext lets us inspect a net462 DLL from a net8 host without executing code.
            // We feed it the runtime dir (mscorlib etc.) plus the plugin dir (side-by-side deps).
            var runtimeDlls = Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll");
            var pluginDlls  = Directory.GetFiles(pluginDir, "*.dll");

            var allPaths = new List<string>(runtimeDlls);
            allPaths.AddRange(pluginDlls);
            allPaths.Add(typeof(object).Assembly.Location);  // corlib for net8 host resolution

            var resolver = new PathAssemblyResolver(allPaths);
            using var mlc = new MetadataLoadContext(resolver);

            Assembly asm;
            try
            {
                asm = mlc.LoadFromAssemblyPath(dllPath);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load assembly '{dllPath}': {ex.Message}", ex);
            }

            var results = new List<PluginStepDefinition>();

            foreach (var type in asm.GetTypes())
            {
                if (type.IsAbstract || type.IsInterface)
                    continue;

                if (!ImplementsIPlugin(type))
                    continue;

                var stepAttrs = GetPluginStepAttributes(type);
                // Skip Custom APIs — [CustomApi] takes precedence over any [PluginStep].
                if (HasCustomApiAttribute(type))
                {
                    if (verbose)
                    {
                        Out.Dim($"  Skipping {type.FullName} — marked [CustomApi] (not an event plugin)");
                    }

                    if (stepAttrs.Count > 0)
                    {
                        Out.Warn(
                            $"  {type.FullName} - has [CustomApi] AND [PluginStep] attributes. It should have only one or the other");
                    }

                    continue;
                }
                
                if (stepAttrs.Count == 0)
                {
                    logger.LogWarning(
                        "IPlugin class {Type} has no [PluginStep] attribute — skipping.", type.FullName);
                    continue;
                }

                if (verbose)
                    Out.Dim($"  Reflecting {type.FullName} — {stepAttrs.Count} [PluginStep] attribute(s)");

                foreach (var attr in stepAttrs)
                {
                    if (verbose)
                        DumpAttributeToConsole(type.FullName!, attr);

                    try
                    {
                        results.Add(BuildDefinition(type.FullName!, attr, verbose));
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(
                            $"Failed to read [PluginStep] attribute on '{type.FullName}'.\n" +
                            $"Attribute arguments at time of failure:\n{FormatAttributeDump(attr)}",
                            ex);
                    }
                }
            }

            return results;
        }

        // ── Verbose console helpers ────────────────────────────────────────────

        private static void DumpAttributeToConsole(string typeFullName, CustomAttributeData attr)
        {
            Out.Dim($"    [PluginStep] arguments on {typeFullName}:");

            var ctorParams = attr.Constructor.GetParameters();
            for (var i = 0; i < attr.ConstructorArguments.Count && i < ctorParams.Length; i++)
            {
                var typedValue = attr.ConstructorArguments[i];
                var clrType    = typedValue.Value?.GetType().Name ?? "null";
                Out.Dim($"      {ctorParams[i].Name,-24} = {FormatValue(typedValue)}  (ctor, clr: {clrType})");
            }

            foreach (var arg in attr.NamedArguments)
            {
                var displayValue = FormatValue(arg.TypedValue);
                var clrType      = arg.TypedValue.Value?.GetType().Name ?? "null";
                Out.Dim($"      {arg.MemberName,-24} = {displayValue}  (clr: {clrType})");
            }
        }

        private static string FormatAttributeDump(CustomAttributeData attr)
        {
            var sb = new StringBuilder();

            var ctorParams = attr.Constructor.GetParameters();
            for (var i = 0; i < attr.ConstructorArguments.Count && i < ctorParams.Length; i++)
            {
                var typedValue = attr.ConstructorArguments[i];
                var clrType    = typedValue.Value?.GetType().Name ?? "null";
                sb.AppendLine($"  {ctorParams[i].Name,-24} = {FormatValue(typedValue)}  (ctor, clr: {clrType})");
            }

            foreach (var arg in attr.NamedArguments)
            {
                var displayValue = FormatValue(arg.TypedValue);
                var clrType      = arg.TypedValue.Value?.GetType().Name ?? "null";
                sb.AppendLine($"  {arg.MemberName,-24} = {displayValue}  (clr: {clrType})");
            }
            return sb.ToString();
        }

        private static string FormatValue(CustomAttributeTypedArgument typedValue)
        {
            if (typedValue.Value is null)
                return "null";

            if (typedValue.Value is IReadOnlyList<CustomAttributeTypedArgument> list)
            {
                var items = list.Select(x => $"\"{x.Value?.ToString() ?? "null"}\"");
                return $"[{string.Join(", ", items)}] ({list.Count} item(s))";
            }

            return typedValue.Value.ToString() ?? "null";
        }

        // ── Static helpers ─────────────────────────────────────────────────────

        private static bool ImplementsIPlugin(Type type)
            => type.GetInterfaces().Any(iface => iface.FullName == PluginInterfaceFullName);

        private static List<CustomAttributeData> GetPluginStepAttributes(Type type)
            => type.GetCustomAttributesData()
                   .Where(attr => attr.AttributeType.FullName == PluginStepAttrFullName)
                   .ToList();

        private static bool HasCustomApiAttribute(Type type)
            => type.GetCustomAttributesData()
                   .Any(attr => attr.AttributeType.FullName == CustomApiAttrFullName);

        private static PluginStepDefinition BuildDefinition(
            string typeFullName, CustomAttributeData attr, bool verbose)
        {
            var def = new PluginStepDefinition { TypeFullName = typeFullName };

            // Positional constructor arguments — e.g. [PluginStep("account", "Create", Stage.PostOperation)].
            // CustomAttributeData exposes these separately from named arguments, so map them by the
            // constructor's parameter names. Without this, positional Entity/Message/Stage are lost
            // (the step would be skipped as "Unknown SDK message ''").
            ReadConstructorArguments(attr, def);

            // First pass — scalar fields (named arguments)
            var rawRunAsSystem = false;
            var rawRunAsUser   = (string?)null;

            foreach (var arg in attr.NamedArguments)
            {
                if (verbose)
                    Out.Dim($"      → {arg.MemberName}");

                switch (arg.MemberName)
                {
                    case "Entity":
                        def.Entity = (string)arg.TypedValue.Value!;
                        break;
                    case "Message":
                        def.Message = (string)arg.TypedValue.Value!;
                        break;
                    case "Stage":
                        def.Stage = Convert.ToInt32(arg.TypedValue.Value);
                        break;
                    case "ExecutionOrder":
                        def.ExecutionOrder = Convert.ToInt32(arg.TypedValue.Value);
                        break;
                    case "Async":
                        def.Mode = (bool)arg.TypedValue.Value! ? 1 : 0;
                        break;
                    case "Description":
                        def.Description = (string?)arg.TypedValue.Value;
                        break;
                    case "RunAsSystem":
                        rawRunAsSystem = (bool)arg.TypedValue.Value!;
                        break;
                    case "RunAsUser":
                        rawRunAsUser = arg.TypedValue.Value as string;
                        break;
                    case "FilteringAttributes":
                        def.FilteringAttributes = ReadStringArray(arg.TypedValue, arg.MemberName);
                        break;
                    case "Configuration":
                        def.Configuration = (string?)arg.TypedValue.Value;
                        break;
                }
            }

            def.RunAsUser = ResolveImpersonatingUser(rawRunAsSystem, rawRunAsUser, typeFullName);

            // Second pass — image configuration
            var usePreImage  = false;
            var usePostImage = false;
            var preAttrs  = Array.Empty<string>();
            var postAttrs = Array.Empty<string>();
            var preAlias  = "PreImage";
            var postAlias = "PostImage";

            foreach (var arg in attr.NamedArguments)
            {
                switch (arg.MemberName)
                {
                    case "UsePreImage":
                        usePreImage = (bool)arg.TypedValue.Value!;
                        break;
                    case "UsePostImage":
                        usePostImage = (bool)arg.TypedValue.Value!;
                        break;
                    case "PreImageAttributes":
                        preAttrs = ReadStringArray(arg.TypedValue, arg.MemberName);
                        break;
                    case "PostImageAttributes":
                        postAttrs = ReadStringArray(arg.TypedValue, arg.MemberName);
                        break;
                    case "PreImageAlias":
                        preAlias = (string?)arg.TypedValue.Value is { Length: > 0 } pa ? pa : "PreImage";
                        break;
                    case "PostImageAlias":
                        postAlias = (string?)arg.TypedValue.Value is { Length: > 0 } po ? po : "PostImage";
                        break;
                }
            }

            if (usePreImage)
                def.Images.Add(new ImageDefinition
                {
                    ImageType  = ImageType.Pre,
                    Alias      = preAlias,
                    Attributes = preAttrs
                });

            if (usePostImage)
                def.Images.Add(new ImageDefinition
                {
                    ImageType  = ImageType.Post,
                    Alias      = postAlias,
                    Attributes = postAttrs
                });

            return def;
        }

        /// <summary>
        /// Maps the attribute's positional constructor arguments onto <paramref name="def"/>,
        /// matching by the constructor parameter names <c>entity</c>, <c>message</c> and <c>stage</c>.
        /// Both the parameterless constructor (all values via named arguments) and the
        /// <c>(string entity, string message, Stage stage)</c> constructor are supported.
        /// </summary>
        private static void ReadConstructorArguments(CustomAttributeData attr, PluginStepDefinition def)
        {
            var ctorParams = attr.Constructor.GetParameters();
            for (var i = 0; i < attr.ConstructorArguments.Count && i < ctorParams.Length; i++)
            {
                var value = attr.ConstructorArguments[i].Value;
                switch (ctorParams[i].Name)
                {
                    case "entity":
                        def.Entity = value as string ?? string.Empty;
                        break;
                    case "message":
                        def.Message = value as string ?? string.Empty;
                        break;
                    case "stage":
                        def.Stage = Convert.ToInt32(value);
                        break;
                }
            }
        }

        /// <summary>
        /// Resolves the impersonating user from the raw attribute values.
        /// Returns <c>null</c> (calling user), <c>Guid.Empty</c> (SYSTEM user),
        /// or a specific user Guid.
        /// Throws if both are set, or if RunAsUser is non-empty but not a valid Guid.
        /// </summary>
        internal static Guid? ResolveImpersonatingUser(
            bool runAsSystem, string? runAsUser, string typeFullName)
        {
            var hasSystem = runAsSystem;
            var hasUser   = !string.IsNullOrEmpty(runAsUser);

            if (hasSystem && hasUser)
                throw new InvalidOperationException(
                    $"[PluginStep] on '{typeFullName}': RunAsSystem and RunAsUser cannot both be set. " +
                    "Use RunAsSystem = true to run as the SYSTEM user, or RunAsUser = \"<guid>\" " +
                    "to impersonate a specific user — not both.");

            if (hasUser)
            {
                if (!Guid.TryParse(runAsUser, out var userId))
                    throw new InvalidOperationException(
                        $"[PluginStep] on '{typeFullName}': RunAsUser value \"{runAsUser}\" is not a valid GUID. " +
                        "Provide a properly formatted GUID string, e.g. \"xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx\".");
                return userId;
            }

            if (hasSystem)
                return Guid.Empty; // SYSTEM user (internal model)

            return null; // calling user (internal model)
        }

        private static string[] ReadStringArray(
            CustomAttributeTypedArgument typedValue, string fieldName)
        {
            switch (typedValue.Value)
            {
                case null:
                    return [];

                case IReadOnlyList<CustomAttributeTypedArgument> list:
                {
                    var result = new string[list.Count];
                    for (var i = 0; i < list.Count; i++)
                        result[i] = (string)list[i].Value!;
                    return result;
                }

                default:
                    // Unexpected type — surface it clearly rather than silently returning empty.
                    throw new InvalidOperationException(
                        $"Field '{fieldName}': expected an array (IReadOnlyList<CustomAttributeTypedArgument>) " +
                        $"but got '{typedValue.Value.GetType().FullName}' with value '{typedValue.Value}'. " +
                        "This may indicate an unsupported attribute encoding.");
            }
        }
    }
}
