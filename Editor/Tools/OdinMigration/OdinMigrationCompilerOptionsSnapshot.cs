// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using System.Text;
    using UnityEditor.Compilation;

    internal sealed class OdinMigrationCompilerOptionsSnapshot
    {
        private static readonly PropertyInfo[] Options =
            typeof(ScriptCompilerOptions).GetProperties(
                BindingFlags.Instance | BindingFlags.Public
            );

        internal readonly string signature;
        internal readonly List<string> inputPaths = new List<string>();

        internal OdinMigrationCompilerOptionsSnapshot(ScriptCompilerOptions options)
        {
            SortedDictionary<string, string> values = new SortedDictionary<string, string>(
                StringComparer.Ordinal
            );
            foreach (PropertyInfo property in Options)
            {
                bool pathInput;
                switch (property.Name)
                {
                    case "RoslynAnalyzerRulesetPath":
                    case "RoslynAnalyzerDllPaths":
                    case "RoslynAdditionalFilePaths":
                    case "AnalyzerConfigPath":
                    case "ResponseFiles":
                        pathInput = true;
                        break;
                    case "AllowUnsafeCode":
                    case "EmitReferenceAssembly":
                    case "AdditionalCompilerArguments":
                    case "CodeOptimization":
                    case "ApiCompatibilityLevel":
                    case "EditorAssembliesCompatibilityLevel":
                    case "LanguageVersion":
                        pathInput = false;
                        break;
                    default:
                        throw new InvalidDataException(
                            "An unknown Unity compiler option requires migration compiler support: "
                                + property.Name
                        );
                }
                object value;
                try
                {
                    value = property.GetValue(options);
                }
                catch (TargetInvocationException exception)
                    when (string.Equals(
                            property.Name,
                            "EmitReferenceAssembly",
                            StringComparison.Ordinal
                        )
                        && exception.InnerException is NotImplementedException
                    )
                {
                    value = exception.InnerException.Message;
                }
                StringBuilder encoded = new StringBuilder();
                if (value is string[] paths)
                {
                    foreach (string path in paths)
                    {
                        Append(encoded, path);
                        if (pathInput && !string.IsNullOrWhiteSpace(path))
                        {
                            inputPaths.Add(path);
                        }
                    }
                }
                else
                {
                    string text = value == null ? null : value.ToString();
                    Append(encoded, text);
                    if (pathInput && !string.IsNullOrWhiteSpace(text))
                    {
                        inputPaths.Add(text);
                    }
                }
                values.Add(property.Name, encoded.ToString());
            }
            StringBuilder result = new StringBuilder();
            foreach (KeyValuePair<string, string> value in values)
            {
                Append(result, value.Key);
                Append(result, value.Value);
            }
            signature = result.ToString();
        }

        private static void Append(StringBuilder builder, string value)
        {
            if (value == null)
            {
                builder.Append("null;");
                return;
            }
            builder.Append(value.Length).Append(':').Append(value).Append(';');
        }
    }
}
